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
    public void Parse_WhenTheArgumentsAreInvalid_ThenSaysSo(params string[] arguments)
    {
        // Act
        var input = new CommandLine().Parse(arguments);

        // Assert
        Assert.Equal("Invalid command arguments. Use --help for usage.", input.Problem);
    }
}
