namespace Hoboman.Core.Base64;

// A property in a JSON text: the line it starts on, the path that picks it in every element of the lists it is in, and its own place.
public sealed record JsonProperty(int Line, string Path, string Place);
