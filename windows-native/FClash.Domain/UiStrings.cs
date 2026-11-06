using System.Globalization;
using System.Text.Json.Nodes;

namespace FClash.Domain;

/// <summary>Load shared native JSON translation catalogs.</summary>
public sealed class UiStrings
{
    public static UiStrings Current { get; private set; } = new();
    private readonly Dictionary<string, string> translations = [];
    private UiStrings() { }
    public static void Load(string directory, string? locale)
    {
        var result = new UiStrings();
        var language = (string.IsNullOrWhiteSpace(locale) || locale == "system" ? CultureInfo.CurrentUICulture.Name : locale).Split('-', '_')[0];
        if (language == "zh") { Current = result; return; }
        if (language is not ("en" or "ja" or "ru")) language = "en";
        var chinese = Read(Path.Combine(directory, "intl_zh_CN.json"));
        var selected = Read(Path.Combine(directory, "intl_" + language + ".json"));
        foreach (var (key, value) in chinese)
            if (!key.StartsWith('@') && value is JsonValue && selected[key] is JsonValue target && !value.ToString().Contains('{'))
                result.translations.TryAdd(value.ToString(), target.ToString());
        foreach (var (key, value) in Read(Path.Combine(directory, "native.en.json")))
            result.translations.TryAdd(key, value.Text());
        Current = result;
    }
    private static JsonObject Read(string path) => File.Exists(path) ? JsonNode.Parse(File.ReadAllText(path))!.AsObject() : [];
    public string T(string text) => translations.GetValueOrDefault(text, text);
}
