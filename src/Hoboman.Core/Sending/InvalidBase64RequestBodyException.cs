namespace Hoboman.Core.Sending;

public sealed class InvalidBase64RequestBodyException(Exception inner) : Exception("The request body must be JSON when properties are chosen for Base64", inner);
