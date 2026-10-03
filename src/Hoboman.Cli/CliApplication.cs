using System.Text.Json;
using Hoboman.Core.Auth;
using Hoboman.Core.Environments;
using Hoboman.Core.History;
using Hoboman.Core.Requests;
using Hoboman.Core.Sending;
using Hoboman.Core.Settings;
using Hoboman.Core.Storage;

namespace Hoboman.Cli;

sealed class CliApplication(RequestLibrary library, SettingsStore settings, EnvironmentStore environments, RequestRunner runner, UnaskedTokens tokens, CliOutput output, VariableInput variables)
{
    public async Task<int> RunAsync(string[] arguments, CancellationToken cancellationToken)
    {
        try
        {
            var input = new CommandLine().Parse(arguments);
            if (input.Problem is not null)
            {
                return await output.WriteErrorAsync(input.Problem);
            }
            if (input.StandardOutput is not null)
            {
                return output.WriteStandardText(input.StandardOutput);
            }
            return input.IsList ? await ListAsync(cancellationToken) : await SendAsync(input.Send!, cancellationToken);
        }
        catch (Exception exception)
        {
            return await output.WriteErrorAsync(ProblemOf(exception, cancellationToken));
        }
    }

    async Task<int> ListAsync(CancellationToken cancellationToken)
    {
        var names = await library.NamesAsync(cancellationToken);
        await output.WriteNamesAsync(names.Order(StringComparer.OrdinalIgnoreCase).ThenBy(name => name, StringComparer.Ordinal), cancellationToken);
        return 0;
    }

    async Task<int> SendAsync(SendInput input, CancellationToken cancellationToken)
    {
        IReadOnlyList<KeyValue> overrides;
        try
        {
            overrides = await variables.ReadAsync(input, cancellationToken);
        }
        catch (Exception exception) when (exception is JsonException or FormatException)
        {
            return await output.WriteErrorAsync("Invalid variable input.");
        }
        var name = input.IsDirect ? null : input.Target[0];
        ApiRequest? request;
        try
        {
            request = input.IsDirect ? await RequestInput.CreateAsync(input, cancellationToken) : await library.LoadAsync(name!, cancellationToken);
        }
        catch (Exception exception) when (!input.IsDirect && (FileProblem.Is(exception) || exception is ArgumentException))
        {
            request = null;
        }
        if (request is null)
        {
            return await output.WriteErrorAsync("Saved request could not be loaded.");
        }
        ApiEnvironment? environment;
        try
        {
            var environmentName = input.EnvironmentName ?? (await settings.LoadAsync(cancellationToken)).EnvironmentName;
            environment = environmentName is null ? ApiEnvironment.None : await environments.FindAsync(environmentName, cancellationToken);
        }
        catch (Exception exception) when (FileProblem.Is(exception))
        {
            return await output.WriteErrorAsync("Environment settings could not be read.");
        }
        if (environment is null)
        {
            return await output.WriteErrorAsync("Selected environment was not found.");
        }
        // The temporary values only live in this call, and a token is saved for the environment, so it is fetched without them.
        var used = overrides.Count > 0 ? environment.WithVariables(overrides) : environment;
        var response = await runner.RunAsync(request, name, used, HistorySource.Cli, auth => tokens.FetchAsync(auth, environment, cancellationToken), cancellationToken);
        return await output.WriteResponseAsync(response, cancellationToken);
    }

    // Told by the kind of problem and never copied from the exception, as its message can hold values such as a token in an address.
    static string ProblemOf(Exception exception, CancellationToken cancellationToken) => exception switch
    {
        OperationCanceledException => cancellationToken.IsCancellationRequested ? "Request was cancelled." : "Request timed out.",
        MissingSecretException { Kind: SecretKind.OAuthToken } or ExpiredTokenException => "Fetch a new OAuth token in Hoboman before sending this request.",
        MissingSecretException => "Required authentication secret is missing.",
        UriFormatException => "Invalid request URL.",
        HttpRequestException => "Network request failed.",
        InvalidMethodException or InvalidHeaderException or FormatException or ArgumentException => "Invalid request input.",
        InvalidBase64RequestBodyException => "Request body could not be encoded.",
        IOException or UnauthorizedAccessException => "Input could not be read.",
        _ => "Request failed.",
    };
}
