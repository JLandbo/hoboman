namespace Hoboman.Core.Requests;

public sealed record KeyValue(string Name, string Value = "", bool Enabled = true);
