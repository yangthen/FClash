using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using Jint;
using YamlDotNet.Serialization;

namespace FClash.Domain;

public static class ConfigCompiler
{
    public static JsonObject DefaultDns() => Parse("""
        enable: true
        listen: 127.0.0.1:1053
        use-hosts: true
        use-system-hosts: true
        default-nameserver: [223.5.5.5]
        enhanced-mode: fake-ip
        fake-ip-range: 198.18.0.1/16
        fake-ip-filter: ['*.lan', localhost.ptlogin2.qq.com]
        nameserver: [https://doh.pub/dns-query, https://dns.alidns.com/dns-query]
        fallback: [tls://8.8.4.4, tls://1.1.1.1]
        proxy-server-nameserver: [https://doh.pub/dns-query]
        fallback-filter:
          geoip: true
          geoip-code: CN
        """);
    public static JsonObject Parse(string yaml)
    {
        var value = new DeserializerBuilder().WithAttemptingUnquotedStringTypeDeserialization().Build().Deserialize<object>(yaml);
        var json = new SerializerBuilder().JsonCompatible().Build().Serialize(value);
        return JsonNode.Parse(json) as JsonObject ?? throw new InvalidDataException("配置必须是 YAML 映射");
    }
    public static string Yaml(JsonObject config) => new SerializerBuilder().DisableAliases().Build().Serialize(ToValue(config));
    private static object? ToValue(JsonNode? node) => node switch
    {
        JsonObject map => map.ToDictionary(e => e.Key, e => ToValue(e.Value)),
        JsonArray array => array.Select(ToValue).ToList(),
        null => null,
        _ => JsonSerializer.Deserialize<JsonElement>(node.ToJsonString()) is var e ? e.ValueKind switch
        {
            JsonValueKind.True => true,
            JsonValueKind.False => false,
            JsonValueKind.Number => e.TryGetInt64(out var n) ? (object)n : e.GetDouble(),
            _ => e.GetString(),
        } : null,
    };

    public static async Task<string> CompileAsync(ClientState state, Profile profile, StateStore store)
    {
        var raw = Parse(await File.ReadAllTextAsync(store.ProfilePath(profile.Id)));
        if (profile.OverwriteType == "script")
        {
            if (profile.ScriptId is not { } scriptId) throw new InvalidDataException("此配置缺少脚本");
            var engine = new Engine(options => options.TimeoutInterval(TimeSpan.FromSeconds(5)).LimitMemory(32 * 1024 * 1024).MaxStatements(1000000));
            raw["proxy-providers"] ??= new JsonObject();
            engine.Execute(await File.ReadAllTextAsync(store.ScriptPath(scriptId)));
            var result = engine.Evaluate($"JSON.stringify(main({raw.ToJsonString()}))").AsString();
            raw = JsonNode.Parse(result)!.AsObject();
        }
        var patch = state.Patch;
        var defaults = Parse("mixed-port: 7890\nmode: rule\nallow-lan: false\nlog-level: error\nipv6: false\nunified-delay: true\ntcp-concurrent: true\nfind-process-mode: always\ngeodata-loader: memconservative\n");
        foreach (var (key, value) in defaults) raw[key] = patch[key].Copy() ?? value.Copy();
        foreach (var (key, value) in patch)
            if (key is not ("dns" or "hosts" or "external-controller" or "global-ua")) raw[key] = value.Copy();
        raw["global-ua"] = patch["global-ua"].Copy() ?? raw["global-ua"].Copy() ?? JsonValue.Create("FClash");
        raw["port"] = patch["port"].Copy() ?? JsonValue.Create(0);
        raw["socks-port"] = patch["socks-port"].Copy() ?? JsonValue.Create(0);
        raw["external-controller"] = patch["external-controller"].Text();
        raw["external-ui"] = ""; raw["external-ui-url"] = ""; raw["interface-name"] = "";
        var realTun = Parse("enable: false\ndevice: FClash\nstack: mixed\nauto-route: false\ndns-hijack: ['any:53']\nroute-address: []\n");
        if (patch["tun"] is JsonObject tun) foreach (var (key, value) in tun) realTun[key] = value.Copy();
        raw["tun"] = realTun;
        raw.Object("profile")["store-selected"] = false;
        foreach (var (kind, folder) in new[] { ("proxy-providers", "proxies"), ("rule-providers", "rules") })
            if (raw[kind] is JsonObject providers)
                foreach (var provider in providers.Select(p => p.Value).OfType<JsonObject>())
                    if (provider["type"].Text() == "http" && provider["url"] is { } url)
                        provider["path"] = Path.Combine(store.DirectoryPath, "profiles", "providers", profile.Id.ToString(), folder,
                            Convert.ToHexStringLower(MD5.HashData(Encoding.UTF8.GetBytes(url.ToString()))));
        if (patch["hosts"] is JsonObject hosts && hosts.Count > 0)
        {
            var output = raw.Object("hosts");
            foreach (var (key, value) in hosts)
                output[key] = new JsonArray(value.Text().Split(new[] { ',', '\n' }, StringSplitOptions.RemoveEmptyEntries).Select(x => JsonValue.Create(x.Trim())).ToArray());
        }
        var enabledDns = raw["dns"] is JsonObject dns && dns.Flag("enable");
        if (state.Config.Flag("overrideDns") || !enabledDns)
        {
            var realDns = DefaultDns();
            if (patch["dns"] is JsonObject customDns) foreach (var (key, value) in customDns) realDns[key] = value.Copy();
            if (!enabledDns)
            {
                if (realDns["nameserver"] is not JsonArray servers) realDns["nameserver"] = servers = [];
                if (!servers.Any(x => x.Text() == "system://")) servers.Add("system://");
            }
            raw["dns"] = realDns;
        }
        var finalDns = raw.Object("dns");
        if (finalDns["nameserver-policy"] is JsonArray policies)
        {
            var map = new JsonObject();
            foreach (var item in policies.OfType<JsonObject>()) map[item["domain"].Text()] = Servers(item["server"].Text());
            finalDns["nameserver-policy"] = map;
        }
        else if (finalDns["nameserver-policy"] is JsonObject policyMap)
            foreach (var (domain, server) in policyMap.ToArray()) if (server is JsonValue && server.Text().Contains(',')) policyMap[domain] = Servers(server.Text());
        if (raw["sniffer"]?["sniff"] is JsonObject sniff)
            foreach (var entry in sniff.Select(x => x.Value).OfType<JsonObject>())
                if (entry["ports"] is JsonArray ports) entry["ports"] = new JsonArray(ports.Select(x => JsonValue.Create(x.Text())).ToArray());
        if (state.Network.Flag("appendSystemDns"))
        {
            if (finalDns["nameserver"] is not JsonArray servers) finalDns["nameserver"] = servers = [];
            if (!servers.Any(x => x.Text() == "system://")) servers.Add("system://");
        }
        var rules = (raw["rules"] as JsonArray ?? []).Select(x => x.Text()).ToList();
        if (profile.OverwriteType == "custom" && profile.CustomRules.Count > 0) rules = [.. profile.CustomRules];
        else
        {
            var target = rules.LastOrDefault(x => x.StartsWith("MATCH,", StringComparison.Ordinal))?.Split(',')[1];
            var extra = state.AddedRules.Concat(profile.AddedRules).Where(x => !profile.DisabledRules.Contains(x))
                .Select(x => target == null ? x : ReplaceMatchTarget(x, target));
            rules = [.. extra, .. rules];
        }
        raw["rules"] = new JsonArray(rules.Select(x => JsonValue.Create(x)).ToArray());
        if (profile.Groups.Count > 0) raw["proxy-groups"] = profile.Groups.Copy();
        return Yaml(raw);
    }
    private static JsonArray Servers(string servers) => new(servers.Split(new[] { ',', '\n' }, StringSplitOptions.RemoveEmptyEntries).Select(x => JsonValue.Create(x.Trim())).ToArray());

    private static string ReplaceMatchTarget(string rule, string target)
    {
        var parts = rule.Split(',');
        var i = parts.Length - 1;
        while (i > 0 && parts[i] is "src" or "no-resolve") i--;
        if (i > 0 && parts[i] == "MATCH") parts[i] = target;
        return string.Join(',', parts);
    }
}
