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
            Base64MarkState.Checked => new Base64Mark(property.Line, property.Path, Base64MarkState.Checked, translator.Of("Base64.Encoded")),
            var state => new Base64Mark(property.Line, property.Path, state, null),
        }),
    ];

    // Saved holds the variables a workflow step saved, by the place in the body they were saved from.
    public static IReadOnlyList<Base64Mark> ForResponse(IReadOnlyList<JsonProperty> outline, IReadOnlyList<string> decode, IReadOnlySet<string> failed, Translator translator,
        IReadOnlyDictionary<string, string>? saved = null) =>
    [
        .. outline.Select(property => (StateOf(property.Path, decode) switch
        {
            Base64MarkState.Checked when failed.Contains(property.Place) =>
                new Base64Mark(property.Line, property.Path, Base64MarkState.Failed, translator.Of("Base64.Invalid")),
            Base64MarkState.Checked =>
                new Base64Mark(property.Line, property.Path, Base64MarkState.Decoded, translator.Of("Base64.Decoded")),
            var state => new Base64Mark(property.Line, property.Path, state, null),
        }) with { Saved = saved?.GetValueOrDefault(property.Place) is { } variables ? translator.Format("Workflow.SavedAs", variables) : null }),
    ];

    static Base64MarkState StateOf(string path, IReadOnlyList<string> chosen) =>
        chosen.Contains(path) ? Base64MarkState.Checked
        : chosen.Any(container => JsonPath.IsInside(path, container)) ? Base64MarkState.Inside
        : Base64MarkState.Unchecked;
}
