namespace Hoboman.Core.Base64;

// The body with the chosen values decoded, and the places whose value was not Base64 and was left as it came.
public sealed record Base64Decoded(string Body, IReadOnlySet<string> Failed);
