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
}
