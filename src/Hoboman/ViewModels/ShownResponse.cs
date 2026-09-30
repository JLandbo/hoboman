namespace Hoboman.ViewModels;

// A response as it is shown: with the chosen values decoded, the checkboxes beside its properties, and why the whole body could not be decoded.
public sealed record ShownResponse(ResponseDisplay Display, IReadOnlyList<Base64Mark> Marks, string? Problem);
