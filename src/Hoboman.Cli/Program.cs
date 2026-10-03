using System.Text;
using Hoboman.Cli;
using Hoboman.Core.Auth;
using Hoboman.Core.Environments;
using Hoboman.Core.History;
using Hoboman.Core.Languages;
using Hoboman.Core.Requests;
using Hoboman.Core.Sending;
using Hoboman.Core.Settings;
using Hoboman.Core.Storage;
using Hoboman.Core.Workflows;
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
    var settings = new SettingsStore(folder, NullLogger<SettingsStore>.Instance);
    var secrets = new SecretStore(folder, NullLogger<SecretStore>.Instance);
    var environments = new EnvironmentStore(folder, secrets, NullLogger<EnvironmentStore>.Instance);
    var library = new RequestLibrary(folder, NullLogger<RequestLibrary>.Instance);
    var history = new HistoryStore(folder, NullLogger<HistoryStore>.Instance);
    using var clients = new HttpClients(settings);
    var sender = new HttpRequestSender(secrets, clients, TimeProvider.System, NullLogger<HttpRequestSender>.Instance);
    var runner = new RequestRunner(sender, library, history, NullLogger<RequestRunner>.Instance);
    var workflows = new WorkflowLibrary(folder, NullLogger<WorkflowLibrary>.Instance);
    var check = new WorkflowCheck(library, secrets, NullLogger<WorkflowCheck>.Instance);
    var workflowRunner = new WorkflowRunner(runner, folder, NullLogger<WorkflowRunner>.Instance);
    var oauth = new OAuthClient(clients, new NoBrowser(), new Translator(Translation.English), TimeProvider.System, NullLogger<OAuthClient>.Instance);
    var tokens = new UnaskedTokens(oauth, secrets, NullLogger<UnaskedTokens>.Instance);
    var output = new CliOutput(Console.OpenStandardOutput(), Console.OpenStandardError());
    using var input = new StreamReader(Console.OpenStandardInput(), Encoding.UTF8);
    return await new CliApplication(library, settings, environments, runner, workflows, check, workflowRunner, tokens, output, new(input, Console.IsInputRedirected)).RunAsync(args, cancellation.Token);
}
finally
{
    Console.CancelKeyPress -= cancel;
}
