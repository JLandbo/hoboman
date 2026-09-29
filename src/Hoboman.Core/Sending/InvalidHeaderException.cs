namespace Hoboman.Core.Sending;

public sealed class InvalidHeaderException(string name) : Exception($"'{name}' is not a valid header name")
{
    public string Name => name;
}
