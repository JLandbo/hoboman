namespace Hoboman.Core.Languages;

public sealed class Translator(Translation current)
{
    public Translation Current { get; private set; } = current;

    public event Action? Changed;

    public void Use(Translation translation)
    {
        Current = translation;
        Changed?.Invoke();
    }

    public string Of(string key) => Current.Of(key);

    public string Format(string key, params object?[] values) => Current.Format(key, values);
}
