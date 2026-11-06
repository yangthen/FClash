using System.Text.Json.Nodes;
using System.Text.Json.Serialization;

namespace FClash.Domain;

public sealed class Profile
{
    public long Id { get; set; } = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
    public string Label { get; set; } = "新配置";
    public string Url { get; set; } = "";
    public DateTimeOffset? UpdatedAt { get; set; }
    public bool AutoUpdate { get; set; }
    public long UpdateInterval { get; set; } = 86400000;
    public string OverwriteType { get; set; } = "standard";
    public long? ScriptId { get; set; }
    public Dictionary<string, string> Selected { get; set; } = [];
    public List<string> AddedRules { get; set; } = [];
    public List<string> CustomRules { get; set; } = [];
    public List<string> DisabledRules { get; set; } = [];
    public JsonArray Groups { get; set; } = [];
    public string SubscriptionInfo { get; set; } = "";
    public override string ToString() => Label;
}

public sealed class ClientState
{
    public int FormatVersion { get; set; } = 1;
    public JsonObject Config { get; set; } = [];
    public List<Profile> Profiles { get; set; } = [];
    public List<string> AddedRules { get; set; } = [];
    public Dictionary<string, string> Countries { get; set; } = [];
    public string? ImportedFrom { get; set; }
    [JsonIgnore]
    public long? CurrentId
    {
        get => Config["currentProfileId"]?.GetValue<long>();
        set => Config["currentProfileId"] = value;
    }
    [JsonIgnore] public Profile? Current => Profiles.FirstOrDefault(p => p.Id == CurrentId);
    [JsonIgnore] public JsonObject Patch => Config.Object("patchClashConfig");
    [JsonIgnore] public JsonObject Settings => Config.Object("appSettingProps");
    [JsonIgnore] public JsonObject Network => Config.Object("networkProps");
    public string CountryKey(long id, string name) => System.Text.Json.JsonSerializer.Serialize(new object[] { id, name });
}

public static class JsonExtensions
{
    public static JsonObject Object(this JsonObject parent, string name)
    {
        if (parent[name] is not JsonObject value) parent[name] = value = [];
        return value;
    }
    public static string Text(this JsonNode? node, string fallback = "") => node?.ToString() ?? fallback;
    public static bool Flag(this JsonObject node, string name, bool fallback = false) => node[name]?.GetValue<bool>() ?? fallback;
    public static int Number(this JsonObject node, string name, int fallback = 0) => node[name]?.GetValue<int>() ?? fallback;
    public static JsonNode? Copy(this JsonNode? node) => node?.DeepClone();
    public static JsonNode Decode(this JsonNode? node) => node is JsonValue v && v.TryGetValue<string>(out var s)
        ? JsonNode.Parse(s) ?? new JsonObject() : node ?? new JsonObject();
}
