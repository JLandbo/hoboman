namespace Hoboman.ViewModels;

// A checkbox beside a property, on the line the property starts on, and what is written at the end of that line.
public sealed record Base64Mark(int Line, string Path, Base64MarkState State, string? Badge, string? Note);
