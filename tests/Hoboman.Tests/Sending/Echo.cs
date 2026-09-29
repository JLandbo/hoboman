namespace Hoboman.Tests.Sending;

public sealed record Echo(string Method, string Target, Dictionary<string, string> Headers, string Body);
