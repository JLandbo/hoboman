namespace Hoboman.Core.Base64;

public sealed class MissingBase64PathException(string path) : Exception($"The chosen path {path} leads to no value in the body")
{
    public string Path => path;
}
