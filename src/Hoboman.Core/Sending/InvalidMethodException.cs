namespace Hoboman.Core.Sending;

public sealed class InvalidMethodException(string method) : Exception($"'{method}' is not a valid method")
{
    public string Method => method;
}
