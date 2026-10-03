using System.Text;
using System.Text.Json;
using Hoboman.Core.Requests;

namespace Hoboman.Cli;

// A --var wins over --vars wherever they stand, and the last of a name wins. --param and --params follow the same rule.
sealed class VariableInput(TextReader input, bool inputRedirected)
{
    // A value that is not text, such as a number from a response, is put in as its JSON.
    public async Task<IReadOnlyList<KeyValue>> ReadAsync(SendInput command, CancellationToken cancellationToken) =>
        [.. (await ReadAsync(command.Variables, command.VariablesFile, cancellationToken)).Select(variable =>
            new KeyValue(variable.Key, variable.Value.ValueKind == JsonValueKind.String ? variable.Value.GetString()! : variable.Value.GetRawText()))];

    // A parameter keeps its JSON type, so a number from --params is a number in the run.
    public Task<IReadOnlyDictionary<string, JsonElement>> ReadAsync(RunInput command, CancellationToken cancellationToken) =>
        ReadAsync(command.Parameters, command.ParametersFile, cancellationToken);

    async Task<IReadOnlyDictionary<string, JsonElement>> ReadAsync(string[] options, string? file, CancellationToken cancellationToken)
    {
        var values = new Dictionary<string, JsonElement>();
        if (file is { } path)
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
            foreach (var property in document.RootElement.EnumerateObject())
            {
                values[property.Name] = property.Value.Clone();
            }
        }
        foreach (var option in options)
        {
            var separator = option.IndexOf('=');
            if (separator < 0)
            {
                throw new FormatException();
            }
            values[option[..separator]] = JsonSerializer.SerializeToElement(option[(separator + 1)..]);
        }
        return values;
    }
}
