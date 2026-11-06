using System.Collections.Concurrent;
using System.Net.Http;
using System.Text.Json.Nodes;
using FClash.Domain;

namespace FClash.WinUI;

public sealed class ClientSession(LaunchOptions options) : IAsyncDisposable
{
    public LaunchOptions Options { get; } = options;
    public StateStore Store { get; } = new(options.DataDirectory);
    public ClientState State { get; private set; } = new();
    public CoreClient Core { get; } = new();
    public bool Running { get; private set; }
    public bool Suspended { get; private set; }
    public bool Ready => Core.Connected;
    public JsonObject Proxies { get; private set; } = [];
    public List<string> Groups { get; private set; } = [];
    public ConcurrentDictionary<string, int> Delays { get; } = [];
    private readonly SemaphoreSlim lifecycle = new(1);
    private readonly HttpClient http = new() { Timeout = TimeSpan.FromSeconds(30) };
    public SystemProxy Proxy { get; private set; } = null!;
    public event Action<JsonObject>? Message;
    public event Action<Exception>? Failed;
    private bool disposed;
    private long testGeneration;

    public async Task InitializeAsync()
    {
        State = await Store.LoadAsync();
        Proxy = new SystemProxy(Store.DirectoryPath, Options.SafeMode);
        if (!Options.SafeMode)
        {
            if (!File.Exists(Store.StatePath))
            {
                var legacy = StateStore.LegacyDirectories.FirstOrDefault(p => File.Exists(Path.Combine(p, "database.sqlite")));
                if (legacy != null) State = await Store.ImportLegacyAsync(legacy);
            }
            Proxy.Recover();
            foreach (var file in Directory.EnumerateFiles(Path.Combine(AppContext.BaseDirectory, "GeoData")))
            {
                var target = Path.Combine(Store.DirectoryPath, Path.GetFileName(file));
                if (!File.Exists(target)) File.Copy(file, target);
            }
            Core.Message += HandleMessage;
            Core.Disconnected += e => { Running = false; Proxy.Restore(); Failed?.Invoke(e); };
            await Core.StartAsync(Path.Combine(AppContext.BaseDirectory, "FlClashCore.exe"));
            var initialized = await Core.JsonCallAsync("initClash", new Dictionary<string, object> { ["home-dir"] = Store.DirectoryPath, ["version"] = 896 });
            if (initialized?.GetValue<bool>() != true) throw new IOException("Core 初始化失败");
            if (State.Current != null) await ApplyAsync();
            if (State.Settings.Flag("autoRun") && State.Current != null) await SetRunningAsync(true);
        }
        else if (State.Current != null) await ApplyAsync();
        await Store.SaveAsync(State);
    }

    private void HandleMessage(JsonObject message)
    {
        if (message["type"].Text() == "delay" && message["data"] is JsonObject delay)
            Delays[delay["name"].Text()] = delay.Number("value", -1);
        Message?.Invoke(message);
    }

    public Task SaveAsync() => Store.SaveAsync(State);
    public async Task ImportLegacyAsync(string directory)
    {
        if (Running) await SetRunningAsync(false);
        State = await Store.ImportLegacyAsync(directory, replaceEmpty: true);
        if (State.Current != null) await ApplyAsync();
    }
    public async Task RestoreAsync(string backup)
    {
        if (Running) await SetRunningAsync(false);
        State = await Store.RestoreAsync(backup);
        if (State.Current != null) await ApplyAsync();
    }
    public async Task UpdateDueProfilesAsync(CancellationToken token)
    {
        if (Options.SafeMode) return;
        foreach (var profile in State.Profiles.Where(p => p.AutoUpdate && p.Url.Length > 0).ToArray())
            if (DateTimeOffset.UtcNow - (profile.UpdatedAt ?? DateTimeOffset.MinValue) >= TimeSpan.FromMilliseconds(Math.Max(profile.UpdateInterval, 60000)))
            {
                try { await UpdateProfileAsync(profile, token); }
                catch (OperationCanceledException) when (token.IsCancellationRequested) { throw; }
                catch (Exception e) { Failed?.Invoke(new IOException(profile.Label + " 自动更新失败：" + e.Message, e)); }
            }
    }
    public async Task ApplyAsync()
    {
        await lifecycle.WaitAsync();
        try { await ApplyInnerAsync(); }
        finally { lifecycle.Release(); }
    }

    private async Task ApplyInnerAsync()
    {
        var profile = State.Current ?? throw new InvalidOperationException("请先选择配置");
        var yaml = await ConfigCompiler.CompileAsync(State, profile, Store);
        if (Options.SafeMode)
        {
            await StateStore.AtomicWriteAsync(Path.Combine(Store.DirectoryPath, "config.yaml"), yaml);
            var raw = ConfigCompiler.Parse(yaml);
            Proxies = new JsonObject { ["DIRECT"] = new JsonObject { ["type"] = "Direct" }, ["REJECT"] = new JsonObject { ["type"] = "Reject" } };
            foreach (var proxy in (raw["proxies"] as JsonArray ?? []).OfType<JsonObject>()) Proxies[proxy["name"].Text()] = proxy.Copy();
            Groups = [];
            foreach (var item in (raw["proxy-groups"] as JsonArray ?? []).OfType<JsonObject>())
            {
                var name = item["name"].Text(); Groups.Add(name);
                Proxies[name] = new JsonObject { ["type"] = item["type"].Copy(), ["all"] = item["proxies"].Copy() ?? new JsonArray(), ["now"] = profile.Selected.GetValueOrDefault(name) };
            }
            await SaveAsync(); return;
        }
        var target = Path.Combine(Store.DirectoryPath, "config.yaml");
        try
        {
            await ConfigTransaction.ApplyAsync(target, yaml, async path =>
            {
                var validation = (await Core.CallAsync("validateConfig", path)).Text();
                if (validation.Length > 0) throw new InvalidDataException(validation);
            }, async () =>
            {
                var error = (await Core.JsonCallAsync("setupConfig", new Dictionary<string, object>
                {
                    ["selected-map"] = profile.Selected,
                    ["test-url"] = State.Settings["testUrl"].Text("https://www.gstatic.com/generate_204"),
                })).Text();
                if (error.Length > 0) throw new InvalidDataException(error);
            });
            await RefreshProxiesAsync();
            await SaveAsync();
        }
        catch (AggregateException)
        {
            Running = false; Proxy.Restore(); await Core.CallAsync("stopListener");
            throw;
        }
    }

    public async Task SetRunningAsync(bool value)
    {
        if (Options.SafeMode) throw new InvalidOperationException("安全模式不会启动 Core 或修改系统网络");
        await lifecycle.WaitAsync();
        try
        {
            if (value)
            {
                await ApplyInnerAsync();
                if ((await Core.CallAsync("startListener"))?.GetValue<bool>() != true) throw new IOException("Core 启动失败");
                try
                {
                    if (State.Network.Flag("systemProxy", true)) Proxy.Enable(State.Patch.Number("mixed-port", 7890), State.Network["bypassDomain"] as JsonArray);
                    Running = true;
                }
                catch { await Core.CallAsync("stopListener"); throw; }
            }
            else
            {
                Proxy.Restore(); await Core.CallAsync("stopListener"); Running = false; Suspended = false;
            }
        }
        finally { lifecycle.Release(); }
    }

    public async Task UpdateOnDemandAsync()
    {
        if (Options.SafeMode || !Ready || !Running) return;
        var excluded = (State.Config["excludeSSIDs"] as JsonArray ?? []).Select(x => x.Text()).ToArray();
        var ssid = excluded.Length == 0 ? null : await Task.Run(WlanNetwork.GetSsid);
        var suspend = ssid != null && excluded.Contains(ssid, StringComparer.Ordinal);
        await lifecycle.WaitAsync();
        try
        {
            if (!Running || suspend == Suspended) return;
            if (suspend) { Proxy.Restore(); await Core.CallAsync("stopListener"); }
            else
            {
                await Core.CallAsync("startListener");
                if (State.Network.Flag("systemProxy", true)) Proxy.Enable(State.Patch.Number("mixed-port", 7890), State.Network["bypassDomain"] as JsonArray);
            }
            Suspended = suspend;
        }
        finally { lifecycle.Release(); }
    }

    public async Task RefreshProxiesAsync()
    {
        if (!Ready) return;
        var data = (await Core.CallAsync("getProxies")).Decode();
        Proxies = data["proxies"]?.AsObject() ?? [];
        Groups = (data["all"] as JsonArray ?? []).Select(x => x.Text()).ToList();
    }

    public async Task SelectAsync(string group, string name)
    {
        if (Options.SafeMode)
        {
            if (State.Current is { } preview) { preview.Selected[group] = name; await ApplyAsync(); }
            return;
        }
        var error = (await Core.JsonCallAsync("changeProxy", new Dictionary<string, string> { ["group-name"] = group, ["proxy-name"] = name })).Text();
        if (error.Length > 0) throw new IOException(error);
        if (State.Current is { } profile) profile.Selected[group] = name;
        await SaveAsync(); await RefreshProxiesAsync();
    }

    public async Task TestAsync(IEnumerable<string> names, CancellationToken token)
    {
        if (!Ready) throw new IOException("Core 未连接");
        var generation = Interlocked.Increment(ref testGeneration);
        var profileId = State.Current?.Id;
        using var concurrency = new SemaphoreSlim(4);
        await Task.WhenAll(names.Distinct().Select(async name =>
        {
            await concurrency.WaitAsync(token);
            try
            {
                var result = (await Core.JsonCallAsync("asyncTestDelay", new Dictionary<string, object>
                {
                    ["proxy-name"] = name,
                    ["timeout"] = 5000,
                    ["test-url"] = State.Settings["testUrl"].Text("https://www.gstatic.com/generate_204"),
                }, token)).Decode().AsObject();
                if (generation != testGeneration) return;
                Delays[name] = result.Number("value", -1);
                if (profileId.HasValue && name is not ("DIRECT" or "REJECT"))
                {
                    var country = (await Core.CallAsync("getProxyCountry", name, token)).Text().ToUpperInvariant();
                    if (generation == testGeneration && country.Length == 2 && country.All(c => c is >= 'A' and <= 'Z'))
                        lock (State.Countries) State.Countries[State.CountryKey(profileId.Value, name)] = country;
                }
            }
            finally { concurrency.Release(); }
        }));
        await SaveAsync();
    }

    public async Task UpdateProfileAsync(Profile profile, CancellationToken token = default)
    {
        if (!Uri.TryCreate(profile.Url, UriKind.Absolute, out var uri) || uri.Scheme is not ("http" or "https")) throw new InvalidDataException("请输入有效订阅地址");
        using var request = new HttpRequestMessage(HttpMethod.Get, uri);
        request.Headers.UserAgent.ParseAdd(ClientVersion.UserAgent);
        using var response = await http.SendAsync(request, token);
        response.EnsureSuccessStatusCode();
        var yaml = await response.Content.ReadAsStringAsync(token);
        _ = ConfigCompiler.Parse(yaml);
        var path = Store.ProfilePath(profile.Id);
        var temp = path + ".candidate";
        await StateStore.AtomicWriteAsync(temp, yaml);
        try
        {
            if (Ready)
            {
                var error = (await Core.CallAsync("validateConfig", temp)).Text();
                if (error.Length > 0) throw new InvalidDataException(error);
            }
            File.Move(temp, path, true);
        }
        finally { if (File.Exists(temp)) File.Delete(temp); }
        profile.UpdatedAt = DateTimeOffset.Now;
        if (response.Headers.TryGetValues("subscription-userinfo", out var info)) profile.SubscriptionInfo = string.Join(';', info);
        await SaveAsync();
        if (State.CurrentId == profile.Id) await ApplyAsync();
    }

    public async Task<string> CheckUpdateAsync()
    {
        using var request = new HttpRequestMessage(HttpMethod.Get, "https://api.github.com/repos/yangthen/FClash/releases/latest");
        request.Headers.UserAgent.ParseAdd(ClientVersion.UserAgent);
        using var response = await http.SendAsync(request); response.EnsureSuccessStatusCode();
        var release = JsonNode.Parse(await response.Content.ReadAsStringAsync())!;
        return release["tag_name"].Text() + "\n\n" + release["body"].Text();
    }

    public async ValueTask DisposeAsync()
    {
        if (disposed) return; disposed = true;
        try { Proxy?.Restore(); }
        finally
        {
            try { await Core.DisposeAsync(); }
            finally { http.Dispose(); }
        }
        await SaveAsync();
    }
}
