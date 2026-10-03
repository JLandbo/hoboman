namespace Hoboman.Tests.Environments;

public sealed class ApiEnvironmentTests
{
    [Fact]
    public void Resolve_WhenTheVariableIsKnown_ThenReplacesIt()
    {
        // Arrange
        var environment = new ApiEnvironment("Dev", [new("base", "https://dev.local")]);

        // Act
        var text = environment.Resolve("{{base}}/users");

        // Assert
        Assert.Equal("https://dev.local/users", text);
    }

    [Fact]
    public void Resolve_WhenTheVariableIsUnknown_ThenLeavesIt()
    {
        // Arrange
        var environment = new ApiEnvironment("Dev", []);

        // Act
        var text = environment.Resolve("{{base}}/users");

        // Assert
        Assert.Equal("{{base}}/users", text);
    }

    [Fact]
    public void Resolve_WhenTheVariableIsDisabled_ThenLeavesIt()
    {
        // Arrange
        var environment = new ApiEnvironment("Dev", [new("base", "https://dev.local", Enabled: false)]);

        // Act
        var text = environment.Resolve("{{base}}/users");

        // Assert
        Assert.Equal("{{base}}/users", text);
    }

    [Fact]
    public void WithVariables_WhenANameIsSaved_ThenTheTemporaryValueWins()
    {
        // Act
        var environment = new ApiEnvironment("Dev", [new("name", "saved")]).WithVariables([new("name", "temporary")]);

        // Assert
        Assert.Equal("temporary", environment.Resolve("{{name}}"));
    }

    [Fact]
    public void WithVariables_WhenTheSavedValueIsDisabled_ThenUsesTheTemporaryValue()
    {
        // Act
        var environment = new ApiEnvironment("Dev", [new("name", "saved", Enabled: false)]).WithVariables([new("name", "temporary")]);

        // Assert
        Assert.Equal("temporary", environment.Resolve("{{name}}"));
    }

    [Fact]
    public void WithVariables_WhenGivenTemporaryValues_ThenLeavesTheOriginalUnchanged()
    {
        // Arrange
        var original = new ApiEnvironment("Dev", [new("name", "saved")]);

        // Act
        original.WithVariables([new("name", "temporary")]);

        // Assert
        Assert.Equal("saved", original.Resolve("{{name}}"));
    }
}
