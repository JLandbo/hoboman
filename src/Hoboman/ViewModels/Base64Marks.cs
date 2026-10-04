using Hoboman.Core.Base64;
using Hoboman.Core.Languages;

namespace Hoboman.ViewModels;

// Turns the properties of a body into checkboxes: chosen, inside a chosen one and so taken along, or chosen but not decodable.
public static class Base64Marks
{
    public static IReadOnlyList<Base64Mark> ForRequest(IReadOnlyList<JsonProperty> outline, IReadOnlyList<string> encode, Translator translator) =>
    [
        .. outline.Select(property => StateOf(property.Path, encode) switch
        {
            Base64MarkState.Checked => new Base64Mark(property.Line, property.Path, Base64MarkState.Checked, translator.Of("Base64.Encoded"), NoteOf(property, translator)),
            var state => new Base64Mark(property.Line, property.Path, state, null, null),
        }),
    ];

    // Saved holds the variables a workflow step saved, by the place in the body they were saved from.
    public static IReadOnlyList<Base64Mark> ForResponse(IReadOnlyList<JsonProperty> outline, IReadOnlyList<string> decode, IReadOnlySet<string> failed, Translator translator,
        IReadOnlyDictionary<string, string>? saved = null) =>
    [
        .. outline.Select(property => (StateOf(property.Path, decode) switch
        {
            Base64MarkState.Checked when failed.Contains(property.Place) =>
                new Base64Mark(property.Line, property.Path, Base64MarkState.Failed, translator.Of("Base64.Invalid"), translator.Of("Base64.AsItCame")),
            Base64MarkState.Checked =>
                new Base64Mark(property.Line, property.Path, Base64MarkState.Decoded, translator.Of("Base64.Decoded"), property.HoldsMore ? translator.Of("Base64.AsJson") : null),
            var state => new Base64Mark(property.Line, property.Path, state, null, null),
        }) with { Saved = saved?.GetValueOrDefault(property.Place) is { } variables ? translator.Format("Workflow.SavedAs", variables) : null }),
    ];

    // An object or a list goes whole, and a property in a list goes in every element.
    static string? NoteOf(JsonProperty property, Translator translator) =>
        property.HoldsMore ? translator.Of("Base64.Whole")
        : property.Path.Contains("[*]", StringComparison.Ordinal) ? translator.Of("Base64.Each")
        : null;

    static Base64MarkState StateOf(string path, IReadOnlyList<string> chosen) =>
        chosen.Contains(path) ? Base64MarkState.Checked
        : chosen.Any(container => JsonPath.IsInside(path, container)) ? Base64MarkState.Inside
        : Base64MarkState.Unchecked;
}
