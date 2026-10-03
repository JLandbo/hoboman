using System.Text;
using System.Text.Json;
using Hoboman.Core.Requests;

namespace Hoboman.Cli;

// A --var wins over --vars wherever they stand, and the last of a name wins.
sealed class VariableInput(TextReader input, bool inputRedirected)
{
    public async Task<IReadOnlyList<KeyValue>> ReadAsync(SendInput command, CancellationToken cancellationToken)
    {
        var variables = new Dictionary<string, string>();
        if (command.VariablesFile is { } path)
        {
            // Without redirected input, it would wait for someone to type the JSON.
            if (path == "-" && !inputRedirected)
            {
                throw new FormatException();
            }
            // Reading the console's input does not stop when it is cancelled, so the wait does.
            var json = path == "-" ? await input.ReadToEndAsync(cancellationToken).WaitAsync(cancellationToken) : await File.ReadAllTextAsync(path, Encoding.UTF8, cancellationToken);
            using var document = JsonDocument.Parse(json);
            if (document.RootElement.ValueKind != JsonValueKind.Object)
            {
                throw new FormatException();
            }
            // A value that is not text, such as a number from a response, is put in as its JSON.
            foreach (var property in document.RootElement.EnumerateObject())
            {
                variables[property.Name] = property.Value.ValueKind == JsonValueKind.String ? property.Value.GetString()! : property.Value.GetRawText();
            }
        }
        foreach (var variable in command.Variables)
        {
            var separator = variable.IndexOf('=');
            if (separator < 0)
            {
                throw new FormatException();
            }
            variables[variable[..separator]] = variable[(separator + 1)..];
        }
        return [.. variables.Select(variable => new KeyValue(variable.Key, variable.Value))];
    }
}
