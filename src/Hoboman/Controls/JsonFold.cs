namespace Hoboman.Controls;

// An object or a list that spans lines: where its inside starts and ends, and how many properties or elements it holds.
public readonly record struct JsonFold(int Start, int End, int Count, bool IsList);
