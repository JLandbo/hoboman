using System.Text;
using System.Text.Json;
using Hoboman.Cli;

namespace Hoboman.Tests.Cli;

public sealed class VariableInputTests
{
    CancellationToken Cancellation => TestContext.Current.CancellationToken;

    static SendInput Command(string[] variables, string? file = null) => new(["GET", "https://localhost/"], null, [], null, null, null, variables, file);

    static Task<IReadOnlyList<KeyValue>> ReadAsync(string[] variables, string? json = null) =>
        new VariableInput(new StringReader(json ?? ""), json is not null).ReadAsync(Command(variables, json is null ? null : "-"), TestContext.Current.CancellationToken);

    [Fact]
    public async Task ReadAsync_WhenANameIsGivenTwice_ThenTheLastWins()
    {
        // Act
        var variables = await ReadAsync(["a=first", "a=last"]);

        // Assert
        Assert.Equal("last", Assert.Single(variables).Value);
    }

    [Theory]
    [InlineData("a=b=c", "b=c")]
    [InlineData("a=", "")]
    public async Task ReadAsync_WhenAValueIsGiven_ThenKeepsAllAfterTheFirstEqualsSign(string variable, string expected)
    {
        // Act
        var variables = await ReadAsync([variable]);

        // Assert
        Assert.Equal(expected, Assert.Single(variables).Value);
    }

    [Fact]
    public async Task ReadAsync_WhenTheJsonAndAnOptionGiveTheSameName_ThenTheOptionWins()
    {
        // Act
        var variables = await ReadAsync(["a=option"], """{"a":"json"}""");

        // Assert
        Assert.Equal("option", Assert.Single(variables).Value);
    }

    [Theory]
    [InlineData("{\"a\":42}", "42")]
    [InlineData("{\"a\":true}", "true")]
    [InlineData("{\"a\":null}", "null")]
    public async Task ReadAsync_WhenAValueIsNotText_ThenUsesItsJson(string json, string expected)
    {
        // Act
        var variables = await ReadAsync([], json);

        // Assert
        Assert.Equal(expected, Assert.Single(variables).Value);
    }

    [Fact]
    public async Task ReadAsync_WhenReadingParameters_ThenTheOptionWinsAsTextAndTheJsonKeepsItsTypes()
    {
        // Act
        var parameters = await new VariableInput(new StringReader("""{"a":"json","b":42}"""), true).ReadAsync(new RunInput("Flow", null, ["a=1"], "-"), Cancellation);

        // Assert
        Assert.Equal("""{"a":"1","b":42}""", JsonSerializer.Serialize(parameters));
    }

    [Fact]
    public async Task ReadAsync_WhenReadingAUtf8File_ThenKeepsTheText()
    {
        // Arrange
        using var folder = new TemporaryFolder();
        Directory.CreateDirectory(folder.Path);
        var path = Path.Combine(folder.Path, "variables.json");
        await File.WriteAllTextAsync(path, """{"city":"Ærø 🚀"}""", new UTF8Encoding(true), Cancellation);

        // Act
        var variables = await new VariableInput(TextReader.Null, false).ReadAsync(Command([], path), Cancellation);

        // Assert
        Assert.Equal("Ærø 🚀", Assert.Single(variables).Value);
    }

    [Theory]
    [InlineData("null")]
    [InlineData("[]")]
    [InlineData("\"text\"")]
    [InlineData("1")]
    public async Task ReadAsync_WhenTheJsonIsNotAnObject_ThenThrows(string json)
    {
        // Act
        var reading = ReadAsync([], json);

        // Assert
        await Assert.ThrowsAsync<FormatException>(() => reading);
    }

    [Theory]
    [InlineData("")]
    [InlineData("{")]
    [InlineData("{}{}")]
    public async Task ReadAsync_WhenTheJsonIsMalformed_ThenThrows(string json)
    {
        // Act
        var reading = ReadAsync([], json);

        // Assert
        await Assert.ThrowsAnyAsync<JsonException>(() => reading);
    }

    [Fact]
    public async Task ReadAsync_WhenAnOptionHasNoEqualsSign_ThenThrows()
    {
        // Act
        var reading = ReadAsync(["missing-equals"]);

        // Assert
        await Assert.ThrowsAsync<FormatException>(() => reading);
    }

    [Fact]
    public async Task ReadAsync_WhenTheInputIsNotRedirected_ThenThrowsWithoutWaitingForIt()
    {
        // Act
        var reading = new VariableInput(new PendingReader(), false).ReadAsync(Command([], "-"), Cancellation);

        // Assert
        await Assert.ThrowsAsync<FormatException>(() => reading);
    }

    [Fact]
    public async Task ReadAsync_WhenCancelledWhileReadingTheInput_ThenStops()
    {
        // Arrange
        var input = new PendingReader();
        using var cancellation = CancellationTokenSource.CreateLinkedTokenSource(Cancellation);
        var reading = new VariableInput(input, true).ReadAsync(Command([], "-"), cancellation.Token);
        await input.Started.Task.WaitAsync(Cancellation);

        // Act
        await cancellation.CancelAsync();

        // Assert
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => reading);
    }
}
