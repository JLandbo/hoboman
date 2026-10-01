namespace Hoboman.ViewModels;

public interface IClipboard
{
    // Null when it holds no text.
    string? Text();

    void Put(string text);
}
