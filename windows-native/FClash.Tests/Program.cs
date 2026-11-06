using System.Buffers.Binary;
using System.IO.Pipes;
using System.Text;
using System.Text.Json.Nodes;
using FClash.Domain;
using Microsoft.Data.Sqlite;

if (args.Length == 1 && args[0].StartsWith(@"\\.\pipe\", StringComparison.Ordinal))
{
    await FakeCore(args[0][9..]);
    return 0;
}
if (args.Length == 2 && args[0] == "--prepare-safe-data")
{
    await PrepareSafeData(args[1]);
    return 0;
}

var tests = new (string Name, Func<Task> Run)[]
{
    ("legacy UI: compact rail, original dashboard order and masonry dimensions", LegacyLayout),
    ("framing: split UTF-8 payload, truncation, size limit", Framing),
    ("compiler: YAML types, ordered policies/rules, target resolution", Compiler),
    ("compiler: bounded scripts, custom rules, provider cache paths", Scripts),
    ("store: legacy SQLite snapshot, selections, order and immutable source", Import),
    ("store: atomic persistence and transient property exclusion", Persistence),
    ("RPC: concurrent replies, cancellation, disconnect and owned process exit", Rpc),
    ("configuration transaction: validation, rollback and first-apply failure", Transaction),
    ("backup: round trip and traversal rejection", Backup),
    ("WebDAV: authenticated stream upload/download and URL validation", WebDav),
    ("localization: shared JSON lookup and locale switching", Localization),
};
var failed = 0;
foreach (var test in tests)
{
    try { await test.Run(); Console.WriteLine("PASS " + test.Name); }
    catch (Exception e) { failed++; Console.Error.WriteLine("FAIL " + test.Name + "\n" + e); }
}
return failed == 0 ? 0 : 1;

static void Check(bool result, string message) { if (!result) throw new Exception(message); }
static Task LegacyLayout()
{
    Check(LegacyUiLayout.RailWidth == 80 && LegacyUiLayout.AppBarHeight == 56, "navigation proportions changed");
    Check(LegacyUiLayout.ProfileColumns(600) == 2 && LegacyUiLayout.ProfileColumns(1000) == 3, "profile columns differ from Flutter utils");
    Check(LegacyUiLayout.ProxyColumns(600) == 3 && LegacyUiLayout.ProxyColumns(600, "tight") == 4 && LegacyUiLayout.ProxyColumns(600, "loose") == 2, "proxy density settings differ from Flutter utils");
    Check(!LegacyUiLayout.DesktopNavigation.Contains("resources") && !LegacyUiLayout.DesktopNavigation.Contains("settings"), "tools-only destinations leaked into the rail");
    var tiles = LegacyUiLayout.Dashboard(600, LegacyUiLayout.DashboardDefaults);
    Check(tiles.Select(x => x.Key).SequenceEqual(new[] { "networkSpeed", "systemProxyButton", "tunButton", "outboundMode", "networkDetection", "trafficUsage", "intranetIp" }), "default dashboard was redesigned");
    Check(tiles[0].Width == 374 && tiles[0].Height == 174, "speed chart must span two cards");
    Check(tiles[1].X == 388 && tiles[1].Y == 0 && tiles[1].Height == 80, "system proxy card location changed");
    Check(tiles[2].X == 388 && tiles[2].Y == 94, "TUN should pack below system proxy");
    Check(tiles[3].X == 0 && tiles[3].Y == 188 && tiles[3].Height == 174, "radio-mode card height changed");
    foreach (var width in new[] { 320d, 600d, 1000d, 1280d })
    {
        var layout = LegacyUiLayout.Dashboard(width, LegacyUiLayout.DashboardDefaults);
        foreach (var tile in layout) Check(tile.X >= 0 && tile.X + tile.Width <= width - 32 + .001, "card overflows viewport");
        for (var i = 0; i < layout.Count; i++) for (var j = i + 1; j < layout.Count; j++)
        { var a = layout[i]; var b = layout[j]; Check(a.X + a.Width <= b.X || b.X + b.Width <= a.X || a.Y + a.Height <= b.Y || b.Y + b.Height <= a.Y, "dashboard cards overlap"); }
    }
    return Task.CompletedTask;
}
static async Task Throws<T>(Func<Task> call) where T : Exception
{
    try { await call(); } catch (T) { return; }
    throw new Exception("Expected " + typeof(T).Name);
}
static string Temp() { var dir = Path.Combine(Environment.GetEnvironmentVariable("FCLASH_TEST_ROOT") ?? Path.GetTempPath(), "FClash-native-test-" + Guid.NewGuid().ToString("N")); Directory.CreateDirectory(dir); return dir; }
static async Task Framing()
{
    var bytes = Encoding.UTF8.GetBytes("{\"id\":\"中文\"}");
    using var stream = new MemoryStream();
    await CoreClient.WriteFrameAsync(stream, bytes); stream.Position = 0;
    Check((await CoreClient.ReadFrameAsync(stream)).SequenceEqual(bytes), "UTF-8 frame damaged");
    using var shortFrame = new MemoryStream(new byte[] { 2, 0, 0, 0, 1 });
    await Throws<EndOfStreamException>(() => CoreClient.ReadFrameAsync(shortFrame));
    var header = new byte[4]; BinaryPrimitives.WriteUInt32LittleEndian(header, CoreClient.MaxFrame + 1);
    await Throws<InvalidDataException>(() => CoreClient.ReadFrameAsync(new MemoryStream(header)));
    var pipeName = "FClash-rpc-test-" + Guid.NewGuid().ToString("N");
    await using var server = new NamedPipeServerStream(pipeName, PipeDirection.InOut, 1, PipeTransmissionMode.Byte, PipeOptions.Asynchronous);
    await using var client = new NamedPipeClientStream(".", pipeName, PipeDirection.InOut, PipeOptions.Asynchronous);
    var connect = server.WaitForConnectionAsync(); await client.ConnectAsync(); await connect;
    var frame = stream.ToArray();
    var read = CoreClient.ReadFrameAsync(server);
    foreach (var b in frame) await client.WriteAsync(new[] { b });
    Check((await read.WaitAsync(TimeSpan.FromSeconds(3))).SequenceEqual(bytes), "partial reads not supported");
}
static async Task Compiler()
{
    var dir = Temp();
    try
    {
        var store = new StateStore(dir); var state = await store.LoadAsync(); var profile = new Profile { Id = 42 };
        state.Profiles.Add(profile); state.CurrentId = profile.Id;
        await File.WriteAllTextAsync(store.ProfilePath(42), "mixed-port: 9999\ndns:\n  enable: true\n  nameserver: [system://]\nrules:\n  - DOMAIN,example.com,Proxy\n  - MATCH,Proxy\n");
        state.Config["overrideDns"] = true;
        state.Patch["mixed-port"] = 9876;
        state.Patch["dns"] = ConfigCompiler.Parse("enable: true\nnameserver: [system://]\nnameserver-policy:\n  '+.first.cn': system://\n  '+.second.cn': 223.5.5.5\n");
        state.AddedRules = ["DOMAIN-SUFFIX,dev.sijingkeji.cn,DIRECT", "GEOSITE,cn,DIRECT", "DOMAIN,github.com,MATCH", "GEOIP,CN,DIRECT,no-resolve"];
        profile.AddedRules = ["DOMAIN,example.org,DIRECT"];
        var yaml = await ConfigCompiler.CompileAsync(state, profile, store);
        var result = ConfigCompiler.Parse(yaml);
        Check(result["mixed-port"]!.GetValue<int>() == 9876, "port scalar type");
        Check(result["allow-lan"]!.GetValue<bool>() == false, "boolean scalar type");
        Check(result["dns"]!["nameserver-policy"]!.AsObject().Select(x => x.Key).SequenceEqual(new[] { "+.first.cn", "+.second.cn" }), "policy order reversed");
        Check(result["rules"]!.AsArray().Select(x => x.Text()).SequenceEqual(new[] { "DOMAIN-SUFFIX,dev.sijingkeji.cn,DIRECT", "GEOSITE,cn,DIRECT", "DOMAIN,github.com,Proxy", "GEOIP,CN,DIRECT,no-resolve", "DOMAIN,example.org,DIRECT", "DOMAIN,example.com,Proxy", "MATCH,Proxy" }), "rule ordering or MATCH target");
    }
    finally { Directory.Delete(dir, true); }
}
static async Task Scripts()
{
    var dir = Temp();
    try
    {
        var store = new StateStore(dir); var state = await store.LoadAsync(); var profile = new Profile { Id = 1, OverwriteType = "script", ScriptId = 10 };
        await File.WriteAllTextAsync(store.ProfilePath(1), "proxy-providers:\n  remote:\n    type: http\n    url: https://example.invalid/proxies\nrules: [MATCH,DIRECT]\n");
        await File.WriteAllTextAsync(store.ScriptPath(10), "function main(c) { c.rules = ['MATCH,REJECT']; return c; }");
        var result = ConfigCompiler.Parse(await ConfigCompiler.CompileAsync(state, profile, store));
        Check(result["rules"]![0].Text() == "MATCH,REJECT", "script not executed");
        Check(result["proxy-providers"]!["remote"]!["path"].Text().StartsWith(Path.Combine(dir, "profiles", "providers", "1", "proxies")), "provider cache path not isolated");
        profile.OverwriteType = "custom"; profile.CustomRules = ["GEOSITE,cn,DIRECT", "MATCH,REJECT"];
        state.AddedRules = ["MATCH,DIRECT"];
        result = ConfigCompiler.Parse(await ConfigCompiler.CompileAsync(state, profile, store));
        Check(result["rules"]!.AsArray().Count == 2 && result["rules"]![1].Text() == "MATCH,REJECT", "custom rules were prepended");
        profile.OverwriteType = "script";
        await File.WriteAllTextAsync(store.ScriptPath(10), "function main(c) { while (true) {} }");
        await Throws<Jint.Runtime.StatementsCountOverflowException>(() => ConfigCompiler.CompileAsync(state, profile, store));
    }
    finally { Directory.Delete(dir, true); }
}
static async Task Import()
{
    var source = Temp(); var target = Temp();
    try
    {
        var dbPath = Path.Combine(source, "database.sqlite");
        await using (var db = new SqliteConnection("Data Source=" + dbPath))
        {
            await db.OpenAsync(); using var cmd = db.CreateCommand();
            cmd.CommandText = """
                CREATE TABLE profiles(id INTEGER, label TEXT, url TEXT, selected_map TEXT, overwrite_type TEXT, script_id INTEGER, auto_update INTEGER, auto_update_duration_millis INTEGER, subscription_info TEXT, last_update_date INTEGER, "order" INTEGER);
                CREATE TABLE rules(id INTEGER, rule_action TEXT, content TEXT, rule_target TEXT, rule_provider TEXT, sub_rule TEXT, no_resolve INTEGER, src INTEGER);
                CREATE TABLE profile_rule_mapping(id TEXT, profile_id INTEGER, rule_id INTEGER, scene TEXT, "order" TEXT);
                CREATE TABLE proxy_groups(id INTEGER, profile_id INTEGER, name TEXT, type TEXT, proxies TEXT, "order" TEXT);
                INSERT INTO profiles VALUES(5,'Legacy','https://example.invalid','{"JMS":"node"}','standard',NULL,1,86400000,NULL,1700000000,1);
                INSERT INTO rules VALUES(1,'GEOSITE','cn','DIRECT',NULL,NULL,0,0),(2,'DOMAIN_SUFFIX','dev.sijingkeji.cn','DIRECT',NULL,NULL,0,0);
                INSERT INTO profile_rule_mapping VALUES('a',NULL,1,NULL,'b'),('b',NULL,2,NULL,'a');
                INSERT INTO proxy_groups VALUES(1,5,'JMS','select','["node"]','a');
                """;
            await cmd.ExecuteNonQueryAsync();
        }
        Directory.CreateDirectory(Path.Combine(source, "profiles"));
        await File.WriteAllTextAsync(Path.Combine(source, "profiles", "5.yaml"), "rules: ['MATCH,DIRECT']");
        await File.WriteAllTextAsync(Path.Combine(source, "shared_preferences.json"), "{\"flutter.config\":\"{\\\"currentProfileId\\\":5}\"}");
        SqliteConnection.ClearAllPools();
        var before = await File.ReadAllBytesAsync(dbPath);
        var store = new StateStore(target); var state = await store.ImportLegacyAsync(source);
        Check(state.CurrentId == 5 && state.Current!.Selected["JMS"] == "node", "selection lost");
        Check(state.AddedRules.SequenceEqual(new[] { "DOMAIN-SUFFIX,dev.sijingkeji.cn,DIRECT", "GEOSITE,cn,DIRECT" }), "legacy ordering");
        Check(state.Current!.Groups.Count == 1, "groups lost");
        Check((await File.ReadAllBytesAsync(dbPath)).SequenceEqual(before), "legacy database changed");
        Check(File.Exists(store.ProfilePath(5)), "profile not copied");
    }
    finally { SqliteConnection.ClearAllPools(); Directory.Delete(source, true); Directory.Delete(target, true); }
}
static async Task Persistence()
{
    var dir = Temp();
    try
    {
        var store = new StateStore(dir); var state = await store.LoadAsync(); state.Profiles.Add(new() { Id = 17 }); state.CurrentId = 17;
        state.Countries[state.CountryKey(17, "node")] = "JP";
        await Task.WhenAll(Enumerable.Range(0, 10).Select(_ => store.SaveAsync(state)));
        var raw = await File.ReadAllTextAsync(store.StatePath);
        Check(!raw.Contains("\"Current\"") && !raw.Contains("\"Patch\""), "transient properties serialized");
        var read = await store.LoadAsync(); Check(read.CurrentId == 17 && read.Countries[read.CountryKey(17, "node")] == "JP", "cache not persisted");
        Check(!Directory.EnumerateFiles(dir, "*.tmp").Any(), "temporary write left behind");
    }
    finally { Directory.Delete(dir, true); }
}

static async Task FakeCore(string name)
{
    await using var pipe = new NamedPipeClientStream(".", name, PipeDirection.InOut, PipeOptions.Asynchronous);
    await pipe.ConnectAsync(5000);
    while (true)
    {
        JsonNode request;
        try { request = JsonNode.Parse(await CoreClient.ReadFrameAsync(pipe))!; }
        catch (EndOfStreamException) { return; }
        var method = request["method"].Text();
        if (method == "drop") return;
        if (method == "hang") continue;
        var response = new JsonObject { ["id"] = request["id"].Copy(), ["method"] = method, ["data"] = request["data"].Copy(), ["code"] = 0 };
        await CoreClient.WriteFrameAsync(pipe, Encoding.UTF8.GetBytes(response.ToJsonString()));
        if (method == "terminateCore") return;
    }
}

static async Task Rpc()
{
    await using var core = new CoreClient();
    await core.StartAsync(Environment.ProcessPath!);
    var pid = core.ProcessId!.Value;
    var replies = await Task.WhenAll(Enumerable.Range(0, 30).Select(n => core.CallAsync("echo", n)));
    Check(replies.Select(n => n!.GetValue<int>()).SequenceEqual(Enumerable.Range(0, 30)), "concurrent replies mixed up");
    using var timeout = new CancellationTokenSource(30);
    await Throws<OperationCanceledException>(() => core.CallAsync("hang", cancellationToken: timeout.Token));
    Check((await core.CallAsync("echo", "after timeout")).Text() == "after timeout", "cancellation poisoned transport");
    var hang = core.CallAsync("hang"); var drop = core.CallAsync("drop");
    await Throws<EndOfStreamException>(() => hang.WaitAsync(TimeSpan.FromSeconds(3)));
    await Throws<EndOfStreamException>(() => drop.WaitAsync(TimeSpan.FromSeconds(3)));
    await core.DisposeAsync();
    try { using var process = System.Diagnostics.Process.GetProcessById(pid); Check(process.HasExited, "owned Core leaked"); }
    catch (ArgumentException) { }
}

static async Task Transaction()
{
    var dir = Temp();
    try
    {
        var target = Path.Combine(dir, "config.yaml"); await File.WriteAllTextAsync(target, "old");
        await Throws<InvalidDataException>(() => ConfigTransaction.ApplyAsync(target, "bad", _ => throw new InvalidDataException("bad"), () => Task.CompletedTask));
        Check(await File.ReadAllTextAsync(target) == "old", "validation replaced live YAML");
        var applied = new List<string>();
        await Throws<IOException>(() => ConfigTransaction.ApplyAsync(target, "new", _ => Task.CompletedTask, async () =>
        {
            var value = await File.ReadAllTextAsync(target); applied.Add(value); if (value == "new") throw new IOException("provider failed");
        }));
        Check(applied.SequenceEqual(new[] { "new", "old" }), "previous YAML not reapplied");
        Check(await File.ReadAllTextAsync(target) == "old", "rollback lost previous YAML");
        File.Delete(target);
        await Throws<IOException>(() => ConfigTransaction.ApplyAsync(target, "new", _ => Task.CompletedTask, () => throw new IOException("bad")));
        Check(!File.Exists(target), "first failed configuration left live file");
        Check(!Directory.EnumerateFiles(dir, "*.candidate").Any(), "candidate leaked");
    }
    finally { Directory.Delete(dir, true); }
}
static async Task Backup()
{
    var dir = Temp(); var target = Temp(); var zip = Path.Combine(Temp(), "backup.zip");
    try
    {
        var store = new StateStore(dir); var state = await store.LoadAsync(); state.Profiles.Add(new() { Id = 10 }); state.CurrentId = 10;
        await File.WriteAllTextAsync(store.ProfilePath(10), "rules: ['MATCH,DIRECT']\n");
        var binary = Path.Combine(dir, "profiles", "providers", "10", "rules", "cache.mrs"); Directory.CreateDirectory(Path.GetDirectoryName(binary)!);
        await File.WriteAllBytesAsync(binary, new byte[] { 0xff, 0, 1, 0x80 });
        await File.WriteAllTextAsync(Path.Combine(dir, "proxy-recovery.json"), "must never be restored");
        await store.BackupAsync(zip, state);
        var restored = new StateStore(target); var result = await restored.RestoreAsync(zip);
        Check(result.CurrentId == 10 && File.Exists(restored.ProfilePath(10)), "backup lost profile");
        Check(!File.Exists(Path.Combine(target, "proxy-recovery.json")), "proxy journal included in backup");
        Check((await File.ReadAllBytesAsync(Path.Combine(target, "profiles", "providers", "10", "rules", "cache.mrs"))).SequenceEqual(new byte[] { 0xff, 0, 1, 0x80 }), "provider binary corrupted during restore");
        var hostile = Path.Combine(Path.GetDirectoryName(zip)!, "hostile.zip");
        using (var archive = System.IO.Compression.ZipFile.Open(hostile, System.IO.Compression.ZipArchiveMode.Create))
        { using var writer = new StreamWriter(archive.CreateEntry("../outside.txt").Open()); writer.Write("bad"); }
        await Throws<InvalidDataException>(() => restored.RestoreAsync(hostile));
        Check(!File.Exists(Path.Combine(target, "outside.txt")), "zip traversal escaped staging");
    }
    finally { Directory.Delete(dir, true); Directory.Delete(target, true); Directory.Delete(Path.GetDirectoryName(zip)!, true); }
}

static async Task PrepareSafeData(string directory)
{
    var store = new StateStore(directory);
    if (File.Exists(store.StatePath)) throw new InvalidOperationException("Fixture requires an empty directory");
    var state = await store.LoadAsync();
    var profile = new Profile { Id = 1, Label = "原生界面验收配置", Selected = new() { ["代理选择"] = "日本 · 验收节点" } };
    state.Profiles.Add(profile); state.CurrentId = 1;
    state.Countries[state.CountryKey(1, "日本 · 验收节点")] = "JP";
    state.Countries[state.CountryKey(1, "香港 · 验收节点")] = "HK";
    state.AddedRules = ["DOMAIN-SUFFIX,dev.sijingkeji.cn,DIRECT", "GEOSITE,cn,DIRECT", "GEOIP,CN,DIRECT,no-resolve"];
    state.Config["overrideDns"] = true;
    state.Config.Object("themeProps")["themeMode"] = "light";
    state.Patch["dns"] = ConfigCompiler.Parse("enable: true\nlisten: 127.0.0.1:1053\nenhanced-mode: fake-ip\nnameserver: [system://, 223.5.5.5]\nnameserver-policy:\n  '+.dev.sijingkeji.cn': system://\n  'geosite:cn': 223.5.5.5\nfake-ip-filter: ['+.dev.sijingkeji.cn']\n");
    await File.WriteAllTextAsync(store.ProfilePath(1), """
        proxies:
          - name: 日本 · 验收节点
            type: ss
            server: example.invalid
            port: 443
            cipher: aes-128-gcm
            password: fixture-only
          - name: 香港 · 验收节点
            type: ss
            server: example.invalid
            port: 443
            cipher: aes-128-gcm
            password: fixture-only
        proxy-groups:
          - name: 代理选择
            type: select
            proxies: [日本 · 验收节点, 香港 · 验收节点, DIRECT]
        rules: ['MATCH,代理选择']
        """);
    await store.SaveAsync(state);
    Console.WriteLine(store.DirectoryPath);
}

static async Task WebDav()
{
    var dir = Temp();
    try
    {
        var source = Path.Combine(dir, "source.zip"); var target = Path.Combine(dir, "download.zip");
        var bytes = new byte[] { 0, 1, 0xff, 0x80 }; await File.WriteAllBytesAsync(source, bytes);
        using var handler = new FakeWebDavHandler(); using var http = new System.Net.Http.HttpClient(handler);
        var dav = new WebDavBackup(http);
        await dav.UploadAsync("https://example.invalid/backups", "native.zip", "user", "secret", source);
        await dav.DownloadAsync("https://example.invalid/backups", "native.zip", "user", "secret", target);
        Check((await File.ReadAllBytesAsync(target)).SequenceEqual(bytes), "WebDAV binary damaged");
        Check(handler.Seen.All(x => x == "https://example.invalid/backups/native.zip"), "WebDAV URI changed");
        await Throws<InvalidDataException>(() => dav.UploadAsync("http://example.invalid", "native.zip", "user", "secret", source));
        await Throws<InvalidDataException>(() => dav.UploadAsync("https://example.invalid", "../bad.zip", "user", "secret", source));
    }
    finally { Directory.Delete(dir, true); }
}
static async Task Localization()
{
    var dir = Temp();
    try
    {
        await File.WriteAllTextAsync(Path.Combine(dir, "intl_zh_CN.json"), "{\"dashboard\":\"仪表盘\",\"@dashboard\":{}} ");
        await File.WriteAllTextAsync(Path.Combine(dir, "intl_en.json"), "{\"dashboard\":\"Dashboard\"}");
        UiStrings.Load(dir, "en"); Check(UiStrings.Current.T("仪表盘") == "Dashboard", "JSON translation failed");
        UiStrings.Load(dir, "zh_CN"); Check(UiStrings.Current.T("仪表盘") == "仪表盘", "locale switch retained stale translation");
    }
    finally { Directory.Delete(dir, true); }
}

sealed class FakeWebDavHandler : System.Net.Http.HttpMessageHandler
{
    public List<string> Seen { get; } = [];
    private byte[] bytes = [];
    protected override async Task<System.Net.Http.HttpResponseMessage> SendAsync(System.Net.Http.HttpRequestMessage request, CancellationToken token)
    {
        Seen.Add(request.RequestUri!.AbsoluteUri);
        if (request.Headers.Authorization?.Scheme != "Basic" || Encoding.UTF8.GetString(Convert.FromBase64String(request.Headers.Authorization.Parameter!)) != "user:secret")
            throw new InvalidOperationException("WebDAV authorization missing");
        if (request.Method == System.Net.Http.HttpMethod.Put) bytes = await request.Content!.ReadAsByteArrayAsync(token);
        return new(System.Net.HttpStatusCode.OK) { Content = new System.Net.Http.ByteArrayContent(bytes) };
    }
}
