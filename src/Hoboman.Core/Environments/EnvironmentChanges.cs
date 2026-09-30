namespace Hoboman.Core.Environments;

// What saving the environments did to their names: the new name, or null when the environment was removed. Names left out are unchanged.
public static class EnvironmentChanges
{
    public static string? NameAfter(this IReadOnlyDictionary<string, string?> changes, string name) => changes.TryGetValue(name, out var renamed) ? renamed : name;
}
