using System.Text;
using Hoboman.Cli;
using Hoboman.Core.Auth;
using Hoboman.Core.Languages;
using Hoboman.Core.Sending;
using Hoboman.Core.Settings;
using Hoboman.Core.Storage;
using Microsoft.Extensions.Logging.Abstractions;

Console.OutputEncoding = new UTF8Encoding(false);
using var cancellation = new CancellationTokenSource();
// Ctrl+C stops the call or run instead of the process, so it ends like any other failure, and a run still tells how it ended.
ConsoleCancelEventHandler cancel = (_, eventArguments) =>
{
    eventArguments.Cancel = true;
    cancellation.Cancel();
};
Console.CancelKeyPress += cancel;
try
{
    // The data lies next to the program, as for the app, so both use the same requests, environments and secrets.
    var folder = new AppFolder(AppContext.BaseDirectory);
    var secrets = new SecretStore(folder, NullLogger<SecretStore>.Instance);
    using var clients = new HttpClients(new SettingsStore(folder, NullLogger<SettingsStore>.Instance));
    var factory = new CliFactory(folder, secrets, new HttpRequestSender(secrets, clients, TimeProvider.System, NullLogger<HttpRequestSender>.Instance),
        new OAuthClient(clients, new NoBrowser(), new Translator(Translation.English), TimeProvider.System, NullLogger<OAuthClient>.Instance));
    var command = new CommandLine().Parse(args);
    // MCP talks over stdin and stdout itself, so each call gets its own output and input instead of the console's.
    if (command.IsMcp)
    {
        await new McpTools((output, error, input) => factory.Create(output, error, input, inputRedirected: true)).ServeAsync(cancellation.Token);
        return 0;
    }
    using var input = new StreamReader(Console.OpenStandardInput(), Encoding.UTF8);
    return await factory.Create(Console.OpenStandardOutput(), Console.OpenStandardError(), input, Console.IsInputRedirected).RunAsync(command, cancellation.Token);
}
finally
{
    Console.CancelKeyPress -= cancel;
}
