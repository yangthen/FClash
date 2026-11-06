using System.IO.Compression;
using System.Text.Json;
using System.Text.Json.Nodes;
using Microsoft.Data.Sqlite;

namespace FClash.Domain;

public sealed class StateStore(string directory)
{
    public string DirectoryPath { get; } = Path.GetFullPath(directory);
    public string ProfilePath(long id) => Path.Combine(DirectoryPath, "profiles", $"{id}.yaml");
    public string ScriptPath(long id) => Path.Combine(DirectoryPath, "scripts", $"{id}.js");
    public string StatePath => Path.Combine(DirectoryPath, "native-state.json");
    private readonly SemaphoreSlim writes = new(1);
    private static readonly JsonSerializerOptions JsonOptions = new() { WriteIndented = true };

    public async Task<ClientState> LoadAsync()
    {
        Directory.CreateDirectory(Path.Combine(DirectoryPath, "profiles"));
        Directory.CreateDirectory(Path.Combine(DirectoryPath, "scripts"));
        if (!File.Exists(StatePath)) return new();
        var state = JsonSerializer.Deserialize<ClientState>(await File.ReadAllTextAsync(StatePath)) ?? throw new InvalidDataException("配置文件为空");
        if (state.FormatVersion != 1) throw new InvalidDataException("不支持此原生数据版本，请更新 FClash");
        return state;
    }

    public async Task SaveAsync(ClientState state)
    {
        await writes.WaitAsync();
        try { await AtomicWriteAsync(StatePath, JsonSerializer.Serialize(state, JsonOptions)); }
        finally { writes.Release(); }
    }

    public static async Task AtomicWriteAsync(string path, string text)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        var temporary = path + "." + Guid.NewGuid().ToString("N") + ".tmp";
        try
        {
            await File.WriteAllTextAsync(temporary, text);
            File.Move(temporary, path, overwrite: true);
        }
        finally { if (File.Exists(temporary)) File.Delete(temporary); }
    }

    /// <summary>Import a read-only SQLite snapshot. Never alter a Flutter database.</summary>
    public async Task<ClientState> ImportLegacyAsync(string source, bool replaceEmpty = false)
    {
        source = Path.GetFullPath(source);
        if (File.Exists(StatePath) && (!replaceEmpty || (await LoadAsync()).Profiles.Count != 0))
            throw new InvalidOperationException("已有原生配置，请先备份，不能覆盖导入");
        if (source.Equals(DirectoryPath, StringComparison.OrdinalIgnoreCase)) throw new InvalidOperationException("源目录与目标目录相同");
        var state = new ClientState { ImportedFrom = source };
        var preferencesPath = Path.Combine(source, "shared_preferences.json");
        if (File.Exists(preferencesPath))
        {
            var prefs = JsonNode.Parse(await File.ReadAllTextAsync(preferencesPath))!.AsObject();
            var config = prefs["flutter.config"] ?? prefs["config"];
            if (config != null) state.Config = config.Decode().AsObject();
            var countries = prefs["flutter.proxyCountries"] ?? prefs["proxyCountries"];
            if (countries != null) state.Countries = countries.Decode().Deserialize<Dictionary<string, string>>() ?? [];
        }
        var database = Path.Combine(source, "database.sqlite");
        if (!File.Exists(database)) throw new FileNotFoundException("未找到旧版 database.sqlite", database);
        Directory.CreateDirectory(DirectoryPath);
        var snapshot = Path.Combine(DirectoryPath, "legacy-snapshot.sqlite");
        await using (var from = new SqliteConnection(new SqliteConnectionStringBuilder { DataSource = database, Mode = SqliteOpenMode.ReadOnly, Pooling = false }.ToString()))
        await using (var to = new SqliteConnection(new SqliteConnectionStringBuilder { DataSource = snapshot, Pooling = false }.ToString()))
        {
            await from.OpenAsync(); await to.OpenAsync(); from.BackupDatabase(to);
        }
        await using var db = new SqliteConnection(new SqliteConnectionStringBuilder { DataSource = snapshot, Mode = SqliteOpenMode.ReadOnly, Pooling = false }.ToString());
        await db.OpenAsync();
        using (var cmd = db.CreateCommand())
        {
            cmd.CommandText = "SELECT * FROM profiles ORDER BY \"order\" IS NULL, \"order\", id";
            await using var rows = await cmd.ExecuteReaderAsync();
            while (await rows.ReadAsync())
            {
                string Text(string field) => rows[field] is DBNull ? "" : rows[field].ToString()!;
                var p = new Profile
                {
                    Id = (long)rows["id"],
                    Label = Text("label"),
                    Url = Text("url"),
                    Selected = JsonSerializer.Deserialize<Dictionary<string, string>>(Text("selected_map")) ?? [],
                    OverwriteType = Text("overwrite_type"),
                    ScriptId = rows["script_id"] is DBNull ? null : (long)rows["script_id"],
                    AutoUpdate = (long)rows["auto_update"] != 0,
                    UpdateInterval = (long)rows["auto_update_duration_millis"],
                    SubscriptionInfo = Text("subscription_info"),
                    UpdatedAt = rows["last_update_date"] is DBNull ? null : DateTimeOffset.FromUnixTimeSeconds((long)rows["last_update_date"]),
                };
                state.Profiles.Add(p);
            }
        }
        using (var cmd = db.CreateCommand())
        {
            cmd.CommandText = "SELECT r.*, m.profile_id, m.scene FROM rules r JOIN profile_rule_mapping m ON m.rule_id = r.id ORDER BY m.\"order\"";
            await using var rows = await cmd.ExecuteReaderAsync();
            while (await rows.ReadAsync())
            {
                string Text(string field) => rows[field] is DBNull ? "" : rows[field].ToString()!;
                var action = Text("rule_action").Replace('_', '-');
                var parts = new List<string> { action };
                if (action != "MATCH") parts.Add(action == "RULE-SET" ? Text("rule_provider") : Text("content"));
                parts.Add(action == "SUB-RULE" ? Text("sub_rule") : Text("rule_target"));
                if ((long)rows["src"] != 0) parts.Add("src");
                if ((long)rows["no_resolve"] != 0) parts.Add("no-resolve");
                var rule = string.Join(',', parts);
                if (rows["profile_id"] is DBNull) state.AddedRules.Add(rule);
                else if (state.Profiles.FirstOrDefault(p => p.Id == (long)rows["profile_id"]) is { } profile)
                {
                    var list = Text("scene") switch { "custom" => profile.CustomRules, "disabled" => profile.DisabledRules, _ => profile.AddedRules };
                    list.Add(rule);
                }
            }
        }
        using (var cmd = db.CreateCommand())
        {
            cmd.CommandText = "SELECT * FROM proxy_groups ORDER BY \"order\"";
            await using var rows = await cmd.ExecuteReaderAsync();
            while (await rows.ReadAsync())
            {
                if (rows["profile_id"] is DBNull || state.Profiles.FirstOrDefault(p => p.Id == (long)rows["profile_id"]) is not { } p) continue;
                var group = new JsonObject();
                for (var i = 0; i < rows.FieldCount; i++)
                {
                    var key = rows.GetName(i);
                    if (key is "id" or "profile_id" or "order" || rows.IsDBNull(i)) continue;
                    var name = key == "disable_u_d_p" ? "disable-udp" : key.Replace('_', '-');
                    group[name] = key is "proxies" or "use" ? JsonNode.Parse(rows.GetString(i)) :
                        key is "lazy" or "disable_udp" or "disable_u_d_p" or "include_all" or "include_all_proxies" or "include_all_providers" or "hidden"
                            ? JsonValue.Create(rows.GetInt64(i) != 0) : JsonSerializer.SerializeToNode(rows.GetValue(i));
                }
                p.Groups.Add(group);
            }
        }
        foreach (var folder in new[] { "profiles", "scripts" })
        {
            var root = Path.Combine(source, folder);
            if (!Directory.Exists(root)) continue;
            foreach (var file in Directory.EnumerateFiles(root, "*", SearchOption.AllDirectories))
            {
                if ((File.GetAttributes(file) & FileAttributes.ReparsePoint) != 0) continue;
                var target = Path.Combine(DirectoryPath, folder, Path.GetRelativePath(root, file));
                Directory.CreateDirectory(Path.GetDirectoryName(target)!);
                File.Copy(file, target, overwrite: true);
            }
        }
        // This last atomic write commits the migration; the legacy source remains usable.
        await SaveAsync(state);
        return state;
    }

    public async Task BackupAsync(string destination, ClientState state)
    {
        await SaveAsync(state);
        var full = Path.GetFullPath(destination);
        if (full.StartsWith(DirectoryPath + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase))
            throw new InvalidOperationException("备份文件必须位于数据目录外");
        using var archive = ZipFile.Open(full, ZipArchiveMode.Create);
        foreach (var file in Directory.EnumerateFiles(DirectoryPath, "*", SearchOption.AllDirectories))
        {
            var relative = Path.GetRelativePath(DirectoryPath, file);
            if (relative != "native-state.json" && !relative.StartsWith("profiles" + Path.DirectorySeparatorChar, StringComparison.Ordinal)
                && !relative.StartsWith("scripts" + Path.DirectorySeparatorChar, StringComparison.Ordinal)) continue;
            archive.CreateEntryFromFile(file, relative.Replace('\\', '/'), CompressionLevel.Fastest);
        }
    }

    public async Task<ClientState> RestoreAsync(string backup)
    {
        var staging = Path.Combine(DirectoryPath, "restore-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(staging);
        try
        {
            using (var zip = ZipFile.OpenRead(backup))
            {
                if (zip.Entries.Sum(x => x.Length) > 512L * 1024 * 1024) throw new InvalidDataException("备份超过 512 MiB");
                foreach (var entry in zip.Entries)
                {
                    var destination = Path.GetFullPath(Path.Combine(staging, entry.FullName));
                    if (!destination.StartsWith(staging + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase)) throw new InvalidDataException("备份包含不安全路径");
                    if (entry.FullName.EndsWith('/')) continue;
                    if (entry.FullName != "native-state.json" && !entry.FullName.StartsWith("profiles/", StringComparison.Ordinal)
                        && !entry.FullName.StartsWith("scripts/", StringComparison.Ordinal)) continue;
                    Directory.CreateDirectory(Path.GetDirectoryName(destination)!);
                    entry.ExtractToFile(destination, overwrite: false);
                }
            }
            var statePath = Path.Combine(staging, "native-state.json");
            if (!File.Exists(statePath)) throw new InvalidDataException("不是 FClash WinUI 备份；旧版数据请使用导入");
            var state = JsonSerializer.Deserialize<ClientState>(await File.ReadAllTextAsync(statePath)) ?? throw new InvalidDataException("备份内容为空");
            if (state.FormatVersion != 1) throw new InvalidDataException("不支持此备份数据版本");
            foreach (var profile in state.Profiles)
            {
                var path = Path.Combine(staging, "profiles", $"{profile.Id}.yaml");
                if (!File.Exists(path)) throw new InvalidDataException("备份缺少配置：" + profile.Label);
                _ = ConfigCompiler.Parse(await File.ReadAllTextAsync(path));
            }
            // Keep a complete recovery archive before touching the current state/files.
            var recovery = Path.Combine(Path.GetDirectoryName(DirectoryPath)!, "FClash-before-restore-" + Guid.NewGuid().ToString("N") + ".zip");
            if (File.Exists(StatePath)) await BackupAsync(recovery, await LoadAsync());
            foreach (var file in Directory.EnumerateFiles(staging, "*", SearchOption.AllDirectories))
            {
                var relative = Path.GetRelativePath(staging, file);
                if (relative == "native-state.json") continue;
                var target = Path.Combine(DirectoryPath, relative);
                Directory.CreateDirectory(Path.GetDirectoryName(target)!);
                var temporary = target + ".restore-" + Guid.NewGuid().ToString("N");
                try { File.Copy(file, temporary); File.Move(temporary, target, true); }
                finally { if (File.Exists(temporary)) File.Delete(temporary); }
            }
            await SaveAsync(state);
            return state;
        }
        finally { Directory.Delete(staging, recursive: true); }
    }

    public static string DefaultDirectory => Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "com.follow", "FClash", "WinUI");
    public static IEnumerable<string> LegacyDirectories => new[]
    {
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "com.follow", "clash"),
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "com.follow", "FlClash"),
    };
}
