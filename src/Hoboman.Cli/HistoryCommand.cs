using System.Globalization;
using Hoboman.Core.History;

namespace Hoboman.Cli;

// Reads the calls in the history, from the app and the CLI, as the app shows them. It only reads.
sealed class HistoryCommand(HistoryStore history, CliOutput output)
{
    const string _ending = ".json";

    public async Task<int> RunAsync(HistoryInput input, CancellationToken cancellationToken)
    {
        if (input.Call is null)
        {
            var files = await history.ReadAsync(await history.LatestAsync(input.Count, cancellationToken), cancellationToken);
            await output.WriteNamesAsync(files.Select(file => string.Join('\t', NameOf(file.Name), file.Entry.At.UtcDateTime.ToString("yyyy-MM-ddTHH:mm:ssZ", CultureInfo.InvariantCulture),
                file.Entry.Source, file.Entry.Request.Method, file.Entry.Response?.StatusCode.ToString(CultureInfo.InvariantCulture) ?? $"{file.Entry.Problem}", file.Entry.Address)), cancellationToken);
            return 0;
        }
        // Only a call the history has is read, so a name cannot lead to another file.
        var name = $"{input.Call}{_ending}";
        var found = (await history.LatestAsync(int.MaxValue, cancellationToken)).Contains(name) ? await history.ReadAsync([name], cancellationToken) : [];
        return found is [var call] ? await output.WriteResultAsync(new { call = call.Entry }) : await output.WriteErrorAsync("Call could not be found.");
    }

    static string NameOf(string file) => file.EndsWith(_ending, StringComparison.Ordinal) ? file[..^_ending.Length] : file;
}
