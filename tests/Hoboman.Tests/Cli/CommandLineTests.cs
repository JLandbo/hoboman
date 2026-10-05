using Hoboman.Cli;

namespace Hoboman.Tests.Cli;

public sealed class CommandLineTests
{
    [Fact]
    public void Parse_WhenListing_ThenSelectsList()
    {
        // Act
        var input = new CommandLine().Parse(["list"]);

        // Assert
        Assert.True(input.IsList);
    }

    [Theory]
    [InlineData("GET")]
    [InlineData("Folder/My request")]
    public void Parse_WhenThereIsOneTarget_ThenSelectsASavedRequest(string name)
    {
        // Act
        var input = new CommandLine().Parse(["send", name]);

        // Assert
        Assert.Equal((false, name), (input.Send?.IsDirect, Assert.Single(input.Send!.Target)));
    }

    [Fact]
    public void Parse_WhenThereAreTwoTargets_ThenSelectsADirectRequest()
    {
        // Act
        var input = new CommandLine().Parse(["send", "CUSTOM", "https://localhost/"]);

        // Assert
        Assert.Equal((true, "CUSTOM https://localhost/"), (input.Send?.IsDirect, string.Join(' ', input.Send!.Target)));
    }

    [Fact]
    public void Parse_WhenOptionsAreGiven_ThenKeepsTheirValues()
    {
        // Act
        var input = new CommandLine().Parse(["send", "POST", "https://localhost/", "--env", "Dev", "-H", "A: 1", "-H", "A: 2", "--json", "{}", "--var", "a=1", "--var", "a=2", "--vars", "-"]);

        // Assert
        var send = input.Send!;
        Assert.Equal(("Dev", "A: 1|A: 2", "{}", "a=1|a=2", "-"), (send.EnvironmentName, string.Join('|', send.Headers), send.JsonBody, string.Join('|', send.Variables), send.VariablesFile));
    }

    [Fact]
    public void Parse_WhenRunning_ThenKeepsTheWorkflowAndItsOptions()
    {
        // Act
        var input = new CommandLine().Parse(["run", "Ordre-sync", "--env", "Dev", "--param", "a=1", "--param", "a=2", "--params", "-"]);

        // Assert
        var run = input.Run!;
        Assert.Equal(("Ordre-sync", "Dev", "a=1|a=2", "-"), (run.Workflow, run.EnvironmentName, string.Join('|', run.Parameters), run.ParametersFile));
    }

    [Theory]
    [InlineData("--json", "@file")]
    [InlineData("--text", "@@literal")]
    [InlineData("--json", "")]
    public void Parse_WhenABodyIsGiven_ThenKeepsItsText(string option, string body)
    {
        // Act
        var input = new CommandLine().Parse(["send", "POST", "https://localhost/", option, body]);

        // Assert
        Assert.Equal(body, input.Send!.JsonBody ?? input.Send.TextBody);
    }

    [Fact]
    public async Task Parse_WhenTheBodyFileHoldsArguments_ThenKeepsItAsABody()
    {
        // Arrange
        using var folder = new TemporaryFolder();
        Directory.CreateDirectory(folder.Path);
        var path = Path.Combine(folder.Path, "body.json");
        await File.WriteAllTextAsync(path, "--help", TestContext.Current.CancellationToken);

        // Act
        var input = new CommandLine().Parse(["send", "POST", "https://localhost/", "--json", $"@{path}"]);

        // Assert
        Assert.Equal($"@{path}", input.Send?.JsonBody);
    }

    [Theory]
    [InlineData]
    [InlineData("unknown")]
    [InlineData("send")]
    [InlineData("send", "GET", "https://localhost/", "extra")]
    [InlineData("send", "--", "GET", "https://localhost/", "extra")]
    [InlineData("list", "--env", "Dev")]
    [InlineData("send", "saved", "-H", "A: 1")]
    [InlineData("send", "saved", "--json", "{}")]
    [InlineData("send", "saved", "--text", "text")]
    [InlineData("send", "POST", "https://localhost/", "--json", "{}", "--text", "text")]
    [InlineData("send", "POST", "https://localhost/", "--env")]
    [InlineData("send", "POST", "https://localhost/", "--env", "a", "--env", "b")]
    [InlineData("send", "GET", "https://localhost/", "-H")]
    [InlineData("send", "GET", "https://localhost/", "--var")]
    [InlineData("run")]
    [InlineData("run", "a", "b")]
    [InlineData("run", "a", "--var", "a=1")]
    [InlineData("run", "a", "--param")]
    [InlineData("send", "saved", "--param", "a=1")]
    [InlineData("show")]
    [InlineData("history", "--count", "0")]
    [InlineData("history", "20261005-120000-000-abc", "--count", "5")]
    [InlineData("update", "Ping")]
    [InlineData("log", "Flow", "--follow")]
    [InlineData("log", "Flow", "20261005-120000-000-abcd", "--last")]
    public void Parse_WhenTheArgumentsAreInvalid_ThenSaysSo(params string[] arguments)
    {
        // Act
        var input = new CommandLine().Parse(arguments);

        // Assert
        Assert.Equal("Invalid command arguments. Use --help for usage.", input.Problem);
    }

    [Fact]
    public void Parse_WhenUpdating_ThenTakesTheTargetAndTheFile()
    {
        // Act
        var input = new CommandLine().Parse(["update", "Ping", "--file", "-"]);

        // Assert
        Assert.Equal(new UpdateInput("Ping", "-"), input.Update);
    }

    [Fact]
    public void Parse_WhenTheLastRunIsFollowed_ThenTakesIt()
    {
        // Act
        var input = new CommandLine().Parse(["log", "Flow", "--last", "--follow"]);

        // Assert
        Assert.Equal(new LogInput("Flow", null, true, true), input.Log);
    }

    [Fact]
    public void Parse_WhenChecking_ThenIsARunThatOnlyChecks()
    {
        // Act
        var input = new CommandLine().Parse(["check", "Flow", "--env", "Dev"]);

        // Assert
        Assert.Equal(("Flow", "Dev", true), (input.Run!.Workflow, input.Run.EnvironmentName, input.Run.CheckOnly));
    }

    [Fact]
    public void Parse_WhenTheHistoryIsListed_ThenTakesTwentyCallsWhenNoCountIsGiven()
    {
        // Act
        var input = new CommandLine().Parse(["history"]);

        // Assert
        Assert.Equal(new HistoryInput(null, 20), input.History);
    }

    [Fact]
    public void Parse_WhenAnEnvironmentIsMade_ThenTakesItsName()
    {
        // Act
        var input = new CommandLine().Parse(["new", "environment", "Dev"]);

        // Assert
        Assert.Equal((SavedKind.Environment, "Dev"), (input.New!.Kind, input.New.Name));
    }

    [Fact]
    public void Parse_WhenShowing_ThenTakesTheTarget()
    {
        // Act
        var input = new CommandLine().Parse(["show", "Shop/Ping"]);

        // Assert
        Assert.Equal("Shop/Ping", input.Show);
    }

    [Fact]
    public void Parse_WhenANewRequestIsGiven_ThenTakesItsFolderNameAndRequest()
    {
        // Act
        var input = new CommandLine().Parse(["new", "request", ".", "Ping", "--method", "POST", "--url", "https://dev.local", "-H", "A: 1", "--json", "{}"]);

        // Assert
        var created = input.New!;
        Assert.Equal((SavedKind.Request, ".", "Ping", "POST", "https://dev.local", "A: 1", "{}"), (created.Kind, created.Folder, created.Name, created.Method, created.Url, created.Headers.Single(), created.JsonBody));
    }

    [Theory]
    [InlineData("rename", "Pong", false)]
    [InlineData("move", ".", false)]
    [InlineData("delete", null, true)]
    public void Parse_WhenRenamingMovingOrDeleting_ThenSelectsTheChange(string command, string? value, bool yes)
    {
        // Act
        var input = new CommandLine().Parse(value is not null ? [command, "Ping", value] : [command, "Ping", "--yes"]);

        // Assert
        Assert.Equal(new ChangeInput(Enum.Parse<ChangeKind>(command, ignoreCase: true), "Ping", value, yes), input.Change);
    }
}
