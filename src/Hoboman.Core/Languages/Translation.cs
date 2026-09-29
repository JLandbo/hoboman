using System.Globalization;
using System.Text.Json;

namespace Hoboman.Core.Languages;

public sealed record Translation(string Name, IReadOnlyDictionary<string, string> Texts)
{
    static readonly JsonSerializerOptions _options = new(JsonSerializerDefaults.Web) { ReadCommentHandling = JsonCommentHandling.Skip, AllowTrailingCommas = true };

    public static Translation Danish { get; } = BuiltIn("Dansk");

    public static Translation English { get; } = BuiltIn("English");

    public static IReadOnlyList<Translation> All { get; } = [Danish, English];

    // Dates and numbers follow the chosen language, not the language of Windows.
    public CultureInfo Culture { get; init; } = CultureInfo.InvariantCulture;

    public static Translation Find(string? name) => All.FirstOrDefault(translation => translation.Name == name) ?? Danish;

    public static Translation Parse(string name, string json)
    {
        var saved = JsonSerializer.Deserialize<SavedTranslation>(json, _options);
        return new(name, saved?.Texts ?? []) { Culture = saved?.Culture is { } culture ? CultureInfo.GetCultureInfo(culture) : CultureInfo.InvariantCulture };
    }

    public string Of(string key) => Texts.GetValueOrDefault(key) ?? Danish.Texts.GetValueOrDefault(key) ?? key;

    public string Format(string key, params object?[] values)
    {
        try
        {
            return string.Format(Culture, Of(key), values);
        }
        catch (FormatException)
        {
            return string.Format(Culture, Danish.Of(key), values);
        }
    }

    static Translation BuiltIn(string name)
    {
        using var reader = new StreamReader(typeof(Translation).Assembly.GetManifestResourceStream($"Languages/{name}.json")!);
        return Parse(name, reader.ReadToEnd());
    }

    sealed record SavedTranslation(Dictionary<string, string>? Texts, string? Culture);
}
