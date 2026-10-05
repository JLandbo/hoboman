using System.Diagnostics;
using System.Text;
using System.Text.Json;
using Hoboman.Tests.Sending;

namespace Hoboman.Tests.Cli;

// Runs the built CLI from a folder of its own, as its data lies next to it, with a working directory of its own for relative input files.
sealed class CliProcess : IDisposable
{
    readonly TemporaryFolder _programFolder = new();
    readonly TemporaryFolder _workingFolder = new();

    public CliProcess()
    {
        Directory.CreateDirectory(Folder.Root);
        Directory.CreateDirectory(WorkingDirectory);
        var build = File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "cli-build.txt")).Trim();
        foreach (var file in ProgramFiles(build))
        {
            File.Copy(Path.Combine(build, file), Path.Combine(Folder.Root, file));
        }
    }

    public AppFolder Folder => new(_programFolder.Path);

    public string WorkingDirectory => _workingFolder.Path;

    public async Task<CliProcessResult> RunAsync(string[] arguments, string? input = null)
    {
        using var cancellation = CancellationTokenSource.CreateLinkedTokenSource(TestContext.Current.CancellationToken);
        cancellation.CancelAfter(TimeSpan.FromSeconds(30));
        var start = new ProcessStartInfo(Path.Combine(Folder.Root, "hoboman-cli.exe"))
        {
            WorkingDirectory = WorkingDirectory,
            UseShellExecute = false,
            CreateNoWindow = true,
            RedirectStandardInput = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            StandardInputEncoding = new UTF8Encoding(false),
            StandardOutputEncoding = Encoding.UTF8,
            StandardErrorEncoding = Encoding.UTF8,
        };
        foreach (var argument in arguments)
        {
            start.ArgumentList.Add(argument);
        }
        using var process = Process.Start(start)!;
        var output = process.StandardOutput.ReadToEndAsync(cancellation.Token);
        var error = process.StandardError.ReadToEndAsync(cancellation.Token);
        try
        {
            if (input is not null)
            {
                await process.StandardInput.WriteAsync(input.AsMemory(), cancellation.Token);
            }
            process.StandardInput.Close();
            await process.WaitForExitAsync(cancellation.Token);
            return new(process.ExitCode, await output, await error);
        }
        finally
        {
            if (!process.HasExited)
            {
                process.Kill(entireProcessTree: true);
            }
            await process.WaitForExitAsync().WaitAsync(TimeSpan.FromSeconds(5));
        }
    }

    // A client keeps stdin open while it waits, as the server stops answering once it closes, so the answer to the last message is read first.
    public async Task<JsonElement> ServeMcpAsync(string[] messages, int answerId)
    {
        using var cancellation = CancellationTokenSource.CreateLinkedTokenSource(TestContext.Current.CancellationToken);
        cancellation.CancelAfter(TimeSpan.FromSeconds(30));
        var start = new ProcessStartInfo(Path.Combine(Folder.Root, "hoboman-cli.exe"), "mcp")
        {
            WorkingDirectory = WorkingDirectory,
            UseShellExecute = false,
            CreateNoWindow = true,
            RedirectStandardInput = true,
            RedirectStandardOutput = true,
            StandardInputEncoding = new UTF8Encoding(false),
            StandardOutputEncoding = Encoding.UTF8,
        };
        using var process = Process.Start(start)!;
        try
        {
            foreach (var message in messages)
            {
                await process.StandardInput.WriteLineAsync(message.AsMemory(), cancellation.Token);
            }
            await process.StandardInput.FlushAsync(cancellation.Token);
            while (await process.StandardOutput.ReadLineAsync(cancellation.Token) is { } line)
            {
                var answer = JsonDocument.Parse(line).RootElement;
                if (answer.TryGetProperty("id", out var id) && id.GetInt32() == answerId)
                {
                    return answer;
                }
            }
            throw new InvalidOperationException("The server stopped without an answer.");
        }
        finally
        {
            process.StandardInput.Close();
            if (!await process.WaitForExitAsync(cancellation.Token).ContinueWith(task => task.IsCompletedSuccessfully, TaskScheduler.Default))
            {
                process.Kill(entireProcessTree: true);
            }
        }
    }

    // The history and the runs are what the CLI writes, so they are left out unless asked for.
    public IReadOnlyDictionary<string, byte[]> Snapshot(bool includeOutput = false) =>
        Directory.EnumerateFileSystemEntries(Folder.Root, "*", SearchOption.AllDirectories)
            .Where(path => includeOutput || (!path.StartsWith(Folder.History, StringComparison.OrdinalIgnoreCase) && !path.StartsWith(Folder.Runs, StringComparison.OrdinalIgnoreCase)))
            .ToDictionary(path => path, path => Directory.Exists(path) ? [] : File.ReadAllBytes(path));

    public void AssertUnchanged(IReadOnlyDictionary<string, byte[]> original, bool includeOutput = false)
    {
        var current = Snapshot(includeOutput);
        Assert.Equal(original.Keys.Order(StringComparer.Ordinal), current.Keys.Order(StringComparer.Ordinal));
        Assert.All(original, file => Assert.Equal(file.Value, current[file.Key]));
    }

    public async Task<IReadOnlyList<HistoryFile>> CallsAsync()
    {
        var history = new HistoryStore(Folder, NullLogger<HistoryStore>.Instance);
        return await history.ReadAsync(await history.LatestAsync(100, TestContext.Current.CancellationToken), TestContext.Current.CancellationToken);
    }

    public void Dispose()
    {
        _workingFolder.Dispose();
        _programFolder.Dispose();
    }

    // The deps file names all the CLI needs, so nothing else in its build folder, such as data left by a run, comes along.
    static IEnumerable<string> ProgramFiles(string build)
    {
        using var deps = JsonDocument.Parse(File.ReadAllBytes(Path.Combine(build, "hoboman-cli.deps.json")));
        var libraries = deps.RootElement.GetProperty("targets").EnumerateObject().First().Value.EnumerateObject();
        string[] assemblies = [.. libraries.SelectMany(library => library.Value.TryGetProperty("runtime", out var runtime) ? runtime.EnumerateObject().Select(file => Path.GetFileName(file.Name)) : [])];
        return ["hoboman-cli.exe", "hoboman-cli.deps.json", "hoboman-cli.runtimeconfig.json", .. assemblies];
    }
}

sealed record CliProcessResult(int ExitCode, string Output, string Error)
{
    public string Body()
    {
        using var response = JsonDocument.Parse(Output);
        return response.RootElement.GetProperty("body").GetString()!;
    }

    public Echo Echo() => JsonSerializer.Deserialize<Echo>(Body(), JsonSerializerOptions.Web)!;

    public string? Problem()
    {
        using var error = JsonDocument.Parse(Error);
        return error.RootElement.GetProperty("error").GetString();
    }
}
