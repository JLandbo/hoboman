using System.ComponentModel;
using System.Reflection;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.Json.Serialization;
using Hoboman.Core.Text;
using Microsoft.Extensions.AI;
using ModelContextProtocol.Protocol;
using ModelContextProtocol.Server;

namespace Hoboman.Cli;

// Each tool runs its command as the CLI does, from input made directly, so no value is ever read as an option, and the rules and errors are the CLI's own.
// Calls can come at the same time, so each gets an application of its own, with its output kept in memory and its JSON object as stdin.
// Clients such as Claude Code cut the instructions and each description after 2048 characters, so they hold the essentials, and guide holds the rest.
// A file a call saves goes into downloads, the one folder an AI can write to.
sealed class McpTools(Func<Stream, Stream, TextReader, CliApplication> create, string downloads)
{
    public const string Instructions =
        """
        Hoboman is the user's local API tool on Windows. These tools are your only access to it.
        Rules:
        1. Never list, read, search, write or delete anything in Hoboman's folder, and never run hoboman-cli.exe yourself, with a shell, a file tool or anything else. If the tools cannot do what you need, stop and tell the user.
        2. Passwords and client secrets are written only by the user, under Auth in the Hoboman app. Never send or save one as text: not in a URL, header, body, variables, parameters, environment or script. A login that needs a password uses a saved request the user has set up. Other values, such as a token from a response, may be passed as variables; put a token in a header as {{token}} and give it in variables, so history keeps {{token}} and not the token. Never repeat a token or password to the user.
        3. Change nothing (new_request, new_folder, new_workflow, new_environment, update, rename, move, delete) unless the user asks. Delete with confirm only when the user asked for exactly that deletion.
        4. Ask the user before you send a call or run a workflow that changes data; judge by what it does, not only its method, so a login or a search with POST does not count. show tells a workflow's steps. check checks a workflow without sending anything.
        5. Do not guess. When a request, workflow, environment or parameter is not found, tell the user and ask.
        6. Tell the user when you have changed something, as unsaved edits in the open app can be lost or overwrite yours.
        Find things with list, and use their ids; paths and names work too when only one thing has them. environment is the exception: it takes the environment's name, never its id.
        Call guide before you build or change a request, workflow or environment, and when a result or an error is unclear. It holds the JSON formats, the workflow events, and every error with what to do.
        """;

    const string Target = "Its id from list, or its path or name when only one thing has it.";
    const string Workflow = "The workflow's id from list with kind workflows, or its name when only one workflow has it.";
    const string Environment = "The environment's name, in any case, never its id. The one the user has chosen in the app when not given.";
    const string Parameters = "The workflow's parameters as a JSON object, such as {\"orderId\":\"o-17\",\"pageSize\":50}. Values keep their type. A parameter with no default in show is required.";
    const string Variables = "Temporary {{variables}} as a JSON object, such as {\"id\":\"42\"}. They win over the environment's for this call only and are not saved as variables, though a value in the URL's host or path shows in the address history keeps. Values that are not text are put in as their JSON.";
    const string Out = "A file name only, such as invoice.pdf, for a new file in the user's Downloads\\Hoboman folder that the response body is saved in, byte for byte; use it for a PDF, an image or another binary or large response. The response then has file, the file's full path, instead of body, and the file is yours to use. It is written whatever the status, so check status. If the name is not a plain file name or the file already exists, nothing is sent; pick another name. If the file then cannot be written, the call was still sent.";
    const string Folder = "The folder's id or path from list with kind folders, or . for the top.";
    const string Name = "The name. Not empty or only spaces, and no line breaks or other control characters. Names need not be unique.";
    const string EnvironmentName = "The name. Not empty or only spaces, and no line breaks or other control characters. Unique among the environments, whatever the case.";
    const string NewName = "The new name. Not empty or only spaces, and no line breaks or other control characters. An environment's name must be unique among the environments, whatever the case; other names need not be unique.";
    const string NoSecrets = "Never put a password or client secret in it; a login that needs one uses send_saved with a request the user has set up.";
    const string Headers = "Headers, each as 'Name: Value', such as 'Accept: application/json'. Put a token as 'Authorization: Bearer {{token}}' and give it in variables, so history keeps {{token}} and not the token.";
    const string Json = "A JSON body as a string, such as {\"id\":1}. Sent with Content-Type application/json unless a header gives another. Never read from a file. Not with text.";
    const string Text = "A text body. Sent with Content-Type text/plain unless a header gives another, such as application/xml. Never read from a file. Not with json.";
    const string Response = "Gives the response as JSON: {\"status\",\"reason\",\"elapsedMs\",\"size\",\"headers\",\"body\"}, where body is the response as text, so JSON in it must be parsed again. A status other than 2xx is a response, not a tool error. The call is saved in Hoboman's history.";

    public IReadOnlyList<McpServerTool> All =>
    [
        Reads(ListAsync, "list", "List saved items"),
        Reads(ShowAsync, "show", "Show a saved item"),
        Reads(HistoryAsync, "history", "Call history"),
        Reads(LogAsync, "log", "Workflow runs"),
        Reads(CheckAsync, "check", "Check a workflow"),
        Sends(RunAsync, "run", "Run a workflow"),
        Sends(SendSavedAsync, "send_saved", "Send a saved request"),
        Sends(SendAsync, "send", "Send a direct call"),
        Changes(NewRequestAsync, "new_request", "New request"),
        Changes(NewFolderAsync, "new_folder", "New folder"),
        Changes(NewWorkflowAsync, "new_workflow", "New workflow"),
        Changes(NewEnvironmentAsync, "new_environment", "New environment"),
        Changes(UpdateAsync, "update", "Update a saved item", destructive: true),
        Changes(RenameAsync, "rename", "Rename"),
        Changes(MoveAsync, "move", "Move"),
        Changes(DeleteAsync, "delete", "Delete", destructive: true),
        Reads(Guide, "guide", "Guide to the Hoboman tools"),
    ];

    // Nothing but the protocol is written to stdout, so the server has no logger.
    public async Task ServeAsync(CancellationToken cancellationToken)
    {
        var options = new McpServerOptions
        {
            ServerInfo = new() { Name = "hoboman", Version = Assembly.GetEntryAssembly()?.GetName().Version?.ToString() ?? "1.0.0" },
            ServerInstructions = Instructions,
            ToolCollection = [.. All],
        };
        await using var server = McpServer.Create(new StdioServerTransport(options), options);
        await server.RunAsync(cancellationToken);
    }

    [Description("Lists what Hoboman has saved, one line each: the id, a tab, and the path through the folders (requests and folders) or the name (workflows and environments). Split at the first tab, as a name can hold /. The environment the user has chosen ends with a tab and selected. A line with no path is a file that cannot be read, or a request with no name at the top; show with its id tells more. Use the ids with the other tools, as names are not unique.")]
    internal Task<CallToolResult> ListAsync([Description("What to list: requests (the default), workflows, folders or environments.")] ListKind kind = ListKind.Requests,
        CancellationToken cancellationToken = default) =>
        Enum.IsDefined(kind)
            ? ExecuteAsync(new(IsList: true, ListsWorkflows: kind == ListKind.Workflows, ListsFolders: kind == ListKind.Folders, ListsEnvironments: kind == ListKind.Environments), null, cancellationToken)
            : Invalid("kind is requests, workflows, folders or environments.");

    [Description("Shows a saved request, folder, workflow or environment as JSON, without secrets: {\"request\":…}, {\"folder\":…}, {\"workflow\":…,\"scripts\":{\"name.js\":\"code\"}} or {\"environment\":…}. A request, workflow or environment shows in the exact form update takes, so use it as the template for a change. To make something new, make it with new_request, new_workflow or new_environment, and update it from show of something like it. A request has its folderId, and a folder its auth and parentId; a folder cannot be updated.")]
    internal Task<CallToolResult> ShowAsync([Description(Target)] string target, CancellationToken cancellationToken = default) =>
        ExecuteAsync(new(Show: target), null, cancellationToken);

    [Description("Lists the newest calls from the app, the CLI and MCP, newest first, one per line: name, time (UTC), source (App, or Cli for the CLI and MCP), method, status and address, separated by tabs. Status is the kind of error when no response came. With name, shows that one call as {\"call\":…} with its request, and its response or error. Responses can hold tokens: never repeat them.")]
    internal Task<CallToolResult> HistoryAsync([Description("A call's name, the first column of the list, to show that call. Not with count.")] string? name = null,
        [Description("How many calls to list, at least 1. 20 when not given. Not with name.")] int? count = null, CancellationToken cancellationToken = default) =>
        HistoryInput.IsValid(name, count ?? HistoryInput.DefaultCount, count is not null)
            ? ExecuteAsync(new(History: new(name, count ?? HistoryInput.DefaultCount)), null, cancellationToken)
            : Invalid("give name or count, not both, and count is at least 1.");

    [Description("Lists the newest runs of a workflow, also those started in the app, newest first, one per line: run id, start (UTC) and outcome (Succeeded, Failed, Cancelled, or - while it runs or when it was stopped). With run or last, gives that run's events as JSON lines, as far as it has come, as run gives them: without the steps' headers and body. With step as well, gives that step's step.finished with its headers and body. An unknown workflow is an error. While a run still runs, tell the user, and ask again when they want to know.")]
    internal Task<CallToolResult> LogAsync([Description(Workflow)] string workflow, [Description("A runId from log's list or from run.started. Not with last.")] string? run = null,
        [Description("true gives the newest run. Not with run.")] bool last = false,
        [Description("A step's index, the first is 0, to get its whole response. Only with run or last.")] int? step = null,
        [Description("How many runs to list, at least 1. 20 when not given. Not with run or last.")] int? count = null, CancellationToken cancellationToken = default)
    {
        var log = new LogInput(workflow, run, last, Follow: false, count);
        return !log.IsValid ? Invalid("give run or last, not both, and count, at least 1, only without them.")
            : step is not null && run is null && !last ? Invalid("step needs run or last.")
            : ExecuteAsync(new(Log: log), null, cancellationToken, step is { } index ? events => StepOf(events, index) : run is not null || last ? WithoutBodies : null);
    }

    [Description("Checks a workflow exactly as run does, but sends nothing: that the parameters are known and the required ones given, that every {{name}} has a value, and that the steps and scripts are right. It does not check that auth secrets and tokens are there; a missing one fails its step at run. Gives {\"id\",\"name\"} when it can run, or the error 'Workflow cannot run.' with problems, each with kind, step and detail; guide tells what to do with each kind.")]
    internal Task<CallToolResult> CheckAsync([Description(Workflow)] string workflow, [Description(Environment)] string? environment = null, [Description(Parameters)] JsonObject? parameters = null,
        CancellationToken cancellationToken = default) =>
        ExecuteAsync(new(Run: new(workflow, environment, [], parameters is null ? null : "-", CheckOnly: true)), parameters?.ToJsonString(), cancellationToken);

    [Description("Runs a saved workflow: it is checked, and then its steps are sent one after another, right away. Ask the user first when the steps change data; show tells their methods, and check only checks. Waits until the run ends, then gives its events as JSON lines: run.started, step.started, step.finished (index, outcome, status, reason, size, error, saved), step.retrying, step.skipped, step.cancelled, and last run.finished with outcome (Succeeded, Failed or Cancelled) and the variables. The steps' headers and body are left out, so the result stays small; log with last (or run, the runId) and step gives one step's whole response. A failed step is not a tool error: read outcome. If it could not start, nothing was sent. If your client cancels the call, the run stops as Cancelled; log with last tells. Parameters and saved values are in clear text in the result and the run's log.")]
    internal Task<CallToolResult> RunAsync([Description(Workflow)] string workflow, [Description(Environment)] string? environment = null, [Description(Parameters)] JsonObject? parameters = null,
        CancellationToken cancellationToken = default) =>
        ExecuteAsync(new(Run: new(workflow, environment, [], parameters is null ? null : "-")), parameters?.ToJsonString(), cancellationToken, WithoutBodies);

    [Description($"Sends a saved request once, with its own method, URL, headers, body and auth, or its folder's auth. Only its {{{{variables}}}} can be changed, with variables, so a token reaches it only where it has a {{{{token}}}}; otherwise use send. In its body they are filled in only when its useEnvironmentVariablesInBody is true (see show). Use it for a login that needs a password, which the user sets up in the app. Ask the user first when it changes data. {Response}")]
    internal Task<CallToolResult> SendSavedAsync([Description("The request's id from list, or its path when only one request has it.")] string request, [Description(Environment)] string? environment = null,
        [Description(Variables)] JsonObject? variables = null, [Description(Out)] string? @out = null, CancellationToken cancellationToken = default) =>
        ExecuteSendAsync(new([request], environment, [], null, null, @out, [], variables is null ? null : "-"), variables, cancellationToken);

    [Description($"Sends one direct call, not saved as a request, with no auth but the headers given. {{{{variables}}}} from the environment and variables are filled in the URL and the headers, but never in the body. {NoSecrets} Ask the user first when it changes data. {Response}")]
    internal Task<CallToolResult> SendAsync([Description("The HTTP method, such as GET, POST, PUT, PATCH or DELETE.")] string method,
        [Description("An absolute URL once its {{variables}} are filled in, such as https://api.example.com/orders/{{id}}.")] string url, [Description(Environment)] string? environment = null,
        [Description(Headers)] string[]? headers = null, [Description(Json)] string? json = null, [Description(Text)] string? text = null, [Description(Variables)] JsonObject? variables = null,
        [Description(Out)] string? @out = null, CancellationToken cancellationToken = default) =>
        CheckBody(headers, json, text) is { } problem
            ? Invalid(problem)
            : ExecuteSendAsync(new([method, url], environment, headers ?? [], RequestInput.Literal(json), RequestInput.Literal(text), @out, [], variables is null ? null : "-"), variables, cancellationToken);

    [Description($"Makes a saved request in a folder. It inherits its folder's auth, like a request made in the app. {{{{variables}}}} in its body are only filled in when useEnvironmentVariablesInBody is true, which update can set. {NoSecrets} Only when the user asks for it. Gives {{\"id\",\"path\"}}.")]
    internal Task<CallToolResult> NewRequestAsync([Description(Folder)] string folder, [Description(Name)] string name, [Description("The URL, such as {{baseUrl}}/orders/{{id}}.")] string url,
        [Description("The HTTP method. GET when not given.")] string? method = null, [Description(Headers)] string[]? headers = null, [Description(Json)] string? json = null,
        [Description(Text)] string? text = null, CancellationToken cancellationToken = default) =>
        CheckBody(headers, json, text) is { } problem
            ? Invalid(problem)
            : ExecuteAsync(new(New: new(SavedKind.Request, folder, name, method ?? "GET", url, headers ?? [], RequestInput.Literal(json), RequestInput.Literal(text))), null, cancellationToken);

    [Description("Makes a folder in a folder, or at the top. Only when the user asks for it. Gives {\"id\",\"path\"}.")]
    internal Task<CallToolResult> NewFolderAsync([Description(Folder)] string folder, [Description(Name)] string name, CancellationToken cancellationToken = default) =>
        ExecuteAsync(new(New: new(SavedKind.Folder, folder, name, "GET", "", [], null, null)), null, cancellationToken);

    [Description("Makes an empty workflow. Fill it with show and update; guide has the format. Only when the user asks for it. Gives {\"id\",\"path\"}.")]
    internal Task<CallToolResult> NewWorkflowAsync([Description(Name)] string name, CancellationToken cancellationToken = default) =>
        ExecuteAsync(new(New: new(SavedKind.Workflow, null, name, "GET", "", [], null, null)), null, cancellationToken);

    [Description("Makes an empty environment. Give it variables with show and update. Environment names are unique, whatever the case. Only when the user asks for it. Gives {\"id\",\"path\"}.")]
    internal Task<CallToolResult> NewEnvironmentAsync([Description(EnvironmentName)] string name, CancellationToken cancellationToken = default) =>
        ExecuteAsync(new(New: new(SavedKind.Environment, null, name, "GET", "", [], null, null)), null, cancellationToken);

    [Description("Replaces what a saved request, workflow or environment holds. Take the JSON from show, change it, and give all of it as content, with its \"request\", \"workflow\" or \"environment\" wrapper; guide has the formats. id, name and folderId must stay as show gave them: use rename and move. In a workflow, keep each step's request.id and leave it out on new steps; a step's bodyKind is Json when left out. A workflow's scripts go in \"scripts\" next to \"workflow\", as {\"name.js\":\"code\"}: a script given as null is deleted, and scripts not given stay. Passwords and client secrets are never in it; the user writes them under Auth in the app. A folder cannot be updated. Only when the user asks for it. Gives {\"id\",\"path\"}.")]
    internal Task<CallToolResult> UpdateAsync([Description(Target)] string target, [Description("The whole JSON object from show, with your changes. An object, not a string.")] JsonObject? content,
        CancellationToken cancellationToken = default) =>
        content is null ? Invalid("content is the JSON object from show.") : ExecuteAsync(new(Update: new(target, "-")), content.ToJsonString(), cancellationToken);

    [Description("Renames a saved request, folder, workflow or environment. Its id, place, secrets and history stay. Only when the user asks for it. Gives {\"id\",\"path\"}.")]
    internal Task<CallToolResult> RenameAsync([Description(Target)] string target, [Description(NewName)] string name, CancellationToken cancellationToken = default) =>
        ExecuteAsync(new(Change: new(ChangeKind.Rename, target, name, false)), null, cancellationToken);

    [Description("Moves a saved request or folder into another folder, or to the top. A folder cannot go into itself, and workflows and environments lie in no folder. Only when the user asks for it. Gives {\"id\",\"path\"}.")]
    internal Task<CallToolResult> MoveAsync([Description(Target)] string target, [Description(Folder)] string folder, CancellationToken cancellationToken = default) =>
        ExecuteAsync(new(Change: new(ChangeKind.Move, target, folder, false)), null, cancellationToken);

    [Description("Deletes a saved request, a folder with all in it, a workflow or an environment for good, with their secrets. Without confirm nothing is deleted, and the error 'Deleting needs confirmation.' is the expected preview of what would go: path, folders (the folder itself counted) and requests. Tell the user, and only call again with confirm when the user has asked for exactly this deletion. Gives {\"id\",\"path\"}.")]
    internal Task<CallToolResult> DeleteAsync([Description(Target)] string target, [Description("true deletes. Only when the user has asked for exactly this deletion.")] bool confirm = false,
        CancellationToken cancellationToken = default) =>
        ExecuteAsync(new(Change: new(ChangeKind.Delete, target, null, confirm)), null, cancellationToken);

    [Description("The full guide to these tools, in Danish: the rules, every tool, the JSON formats for update (request, auth, workflow, steps, saves, retry, environment), the workflow events, and every error with what to do. Read it before you build or change anything, and when a result or an error is unclear.")]
    internal static CallToolResult Guide()
    {
        using var stream = typeof(McpTools).Assembly.GetManifestResourceStream("McpGuide.md")!;
        using var reader = new StreamReader(stream, Encoding.UTF8);
        return Result(reader.ReadToEnd(), isError: false);
    }

    // An item in a list, such as a header, is never null, though the schema would otherwise allow it.
    static readonly AIJsonSchemaCreateOptions _schema = new()
    {
        TransformSchemaNode = (context, node) =>
        {
            if (context.Path is [.., "items"] && node is JsonObject item && item["type"] is JsonArray types && types.Any(type => type?.GetValue<string>() == "null"))
            {
                item["type"] = types.Single(type => type?.GetValue<string>() != "null")!.GetValue<string>();
            }
            return node;
        },
    };

    static McpServerTool Reads(Delegate method, string name, string title) =>
        McpServerTool.Create(method, new() { Name = name, Title = title, ReadOnly = true, OpenWorld = false, SchemaCreateOptions = _schema });

    static McpServerTool Changes(Delegate method, string name, string title, bool destructive = false) =>
        McpServerTool.Create(method, new() { Name = name, Title = title, ReadOnly = false, Destructive = destructive, OpenWorld = false, SchemaCreateOptions = _schema });

    // A call can do anything to the API it reaches, so it is neither read-only nor known to be harmless.
    static McpServerTool Sends(Delegate method, string name, string title) =>
        McpServerTool.Create(method, new() { Name = name, Title = title, ReadOnly = false, Destructive = true, OpenWorld = true, SchemaCreateOptions = _schema });

    static string? CheckBody(string[]? headers, string? json, string? text) =>
        !RequestInput.IsValidBody(json, text) ? "give json or text, not both."
        : headers?.Any(header => header is null) == true ? "a header cannot be null."
        : null;

    // A prompt can choose the name, so the file only ever goes into the one folder, as a new file, and can neither reach another folder, such as Startup, nor overwrite anything.
    Task<CallToolResult> ExecuteSendAsync(SendInput input, JsonObject? variables, CancellationToken cancellationToken)
    {
        if (input.OutFile is { } name)
        {
            if (!IsPlainFileName(name))
            {
                return Task.FromResult(Error("Output file must be a plain file name."));
            }
            Directory.CreateDirectory(downloads);
            input = input with { OutFile = Path.Combine(downloads, name) };
        }
        return ExecuteAsync(new(Send: input), variables?.ToJsonString(), cancellationToken);
    }

    // No folder, drive, stream (:), . or .., and no name Windows keeps for a device, such as NUL, CONOUT$ or COM1.txt.
    static bool IsPlainFileName(string name) =>
        name.Trim().Length > 0 && name.IndexOfAny(Path.GetInvalidFileNameChars()) < 0 && !name.EndsWith('.') && !name.EndsWith(' ') && !name.StartsWith(' ')
        && !_devices.Contains(name.Split('.')[0].TrimEnd());

    static readonly HashSet<string> _devices = new(["CON", "PRN", "AUX", "NUL", "CONIN$", "CONOUT$",
        .. "0123456789¹²³".SelectMany(digit => new[] { $"COM{digit}", $"LPT{digit}" })], StringComparer.OrdinalIgnoreCase);

    // A response with another status than 2xx and a run with a failed step are answers too, so only a command that failed is an error.
    async Task<CallToolResult> ExecuteAsync(CommandInput input, string? json, CancellationToken cancellationToken, Func<string, CallToolResult>? answer = null)
    {
        using var output = new MemoryStream();
        using var error = new MemoryStream();
        var exitCode = await create(output, error, new StringReader(json ?? "")).RunAsync(input, cancellationToken);
        if (exitCode == 2)
        {
            return Result(Encoding.UTF8.GetString(error.ToArray()), isError: true);
        }
        try
        {
            return answer is null ? Result(Encoding.UTF8.GetString(output.ToArray()), isError: false) : answer(Encoding.UTF8.GetString(output.ToArray()));
        }
        // Only a run log that was changed by hand holds a line that is not JSON.
        catch (JsonException)
        {
            return Error("Run could not be read.");
        }
    }

    // Bodies make a run too large for a client to keep, and run.finished comes last, so the steps' headers and body are left out, and one step is asked for on its own.
    // The run log's path is left out too, as it leads into Hoboman's folder.
    static CallToolResult WithoutBodies(string events) => Result(string.Join('\n', Lines(events).Select(line =>
    {
        line.Remove("runFile");
        if (line["type"]?.GetValue<string>() == "step.finished")
        {
            line.Remove("headers");
            line.Remove("body");
        }
        return line.ToJsonString(CompactJson.Options);
    })), isError: false);

    static CallToolResult StepOf(string events, int index) =>
        Lines(events).FirstOrDefault(line => line["type"]?.GetValue<string>() == "step.finished" && line["index"]?.GetValue<int>() == index) is { } step
            ? Result(step.ToJsonString(CompactJson.Options), isError: false)
            : Error("Step could not be found.");

    static IEnumerable<JsonObject> Lines(string events) => events.Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries).Select(line => JsonNode.Parse(line)!.AsObject());

    static Task<CallToolResult> Invalid(string problem) => Task.FromResult(Error($"Invalid tool arguments: {problem}"));

    static CallToolResult Error(string problem) => Result(JsonSerializer.Serialize(new { error = problem }), isError: true);

    static CallToolResult Result(string text, bool isError) => new() { IsError = isError, Content = [new TextContentBlock { Text = text.TrimEnd() }] };
}

// The kinds list can give, named as the tool takes them, so a client offers only these.
[JsonConverter(typeof(JsonStringEnumConverter<ListKind>))]
enum ListKind
{
    [JsonStringEnumMemberName("requests")] Requests,
    [JsonStringEnumMemberName("workflows")] Workflows,
    [JsonStringEnumMemberName("folders")] Folders,
    [JsonStringEnumMemberName("environments")] Environments,
}
