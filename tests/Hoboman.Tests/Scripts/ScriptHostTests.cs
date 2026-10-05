using System.Text.Json;
using Hoboman.Core.Scripts;

namespace Hoboman.Tests.Scripts;

public sealed class ScriptHostTests
{
    static CancellationToken Cancellation => TestContext.Current.CancellationToken;

    static Dictionary<string, JsonElement> Values => new() { ["count"] = JsonSerializer.SerializeToElement(1), ["name"] = JsonSerializer.SerializeToElement("Søren") };

    [Fact]
    public void Run_WhenTheScriptReturnsAValue_ThenGivesItsJson()
    {
        // Act
        var output = ScriptHost.Run("map.js", "return { count: vars.count + 1, name: vars.name };", Values, ScriptOutput.Json, Cancellation);

        // Assert
        Assert.Equal("""{"count":2,"name":"Søren"}""", output);
    }

    [Fact]
    public void Run_WhenTheOutputIsTextAndTheScriptReturnsText_ThenGivesItAsItIs()
    {
        // Act
        var output = ScriptHost.Run("html.js", "return `<p class=\"name\">${vars.name}</p>`;", Values, ScriptOutput.Html, Cancellation);

        // Assert
        Assert.Equal("""<p class="name">Søren</p>""", output);
    }

    [Fact]
    public void Run_WhenTheOutputIsTextAndTheScriptReturnsNoText_ThenFails()
    {
        // Act
        var exception = Assert.Throws<ScriptException>(() => ScriptHost.Run("html.js", "return { name: vars.name };", Values, ScriptOutput.Html, Cancellation));

        // Assert
        Assert.Equal("html.js must return text when its output is Html.", exception.Message);
    }

    [Fact]
    public void Run_WhenTheOutputIsTextAndTheScriptReturnsNothing_ThenGivesNull()
    {
        // Act
        var output = ScriptHost.Run("check.js", "if (!vars.name) { throw new Error('No name'); }", Values, ScriptOutput.Text, Cancellation);

        // Assert
        Assert.Null(output);
    }

    [Theory]
    [InlineData("vars.count = 2;\nreturn vars;")]
    [InlineData("return eval('1');")]
    [InlineData("function deeper() { return deeper(); }\nreturn deeper();")]
    [InlineData("let nested = [];\nfor (let i = 0; i < 100000; i++) { nested = [nested]; }\nreturn nested;")]
    public void Run_WhenTheScriptBreaksALimit_ThenFailsWithoutStoppingTheApp(string code)
    {
        // Act
        var exception = Record.Exception(() => ScriptHost.Run("map.js", code, Values, ScriptOutput.Json, Cancellation));

        // Assert
        Assert.IsType<ScriptException>(exception);
    }

    [Fact]
    public void Run_WhenTheScriptRunsForever_ThenTimesOut()
    {
        // Act
        var exception = Record.Exception(() => ScriptHost.Run("map.js", "while (true) {}", Values, ScriptOutput.Json, Cancellation));

        // Assert
        Assert.IsType<ScriptException>(exception);
    }

    [Fact]
    public void Run_WhenTheScriptThrows_ThenTellsItsFileAndLine()
    {
        // Act
        var exception = Assert.Throws<ScriptException>(() => ScriptHost.Run("map.js", "const order = vars;\nthrow new Error('No order');", Values, ScriptOutput.Json, Cancellation));

        // Assert
        Assert.Equal("map.js:2: No order", exception.Message);
    }

    [Fact]
    public void Run_WhenTheScriptUsesTooMuchMemory_ThenStopsBeforeTheTimeout()
    {
        // Act
        var exception = Assert.Throws<ScriptException>(() => ScriptHost.Run("map.js", "const all = [];\nfor (;;) { all.push('x'.repeat(1000000) + all.length); }", Values, ScriptOutput.Json, Cancellation));

        // Assert
        Assert.Contains("memory", exception.Message);
    }

    [Fact]
    public void Run_WhenTheScriptMakesMuchGarbage_ThenRuns()
    {
        // Act
        var output = ScriptHost.Run("map.js", "const orders = Array.from({ length: 2000 }, (_, id) => ({ id, total: id }));\nreturn Object.keys(orders.reduce((all, order) => ({ ...all, [order.id]: order.total }), {})).length;", Values, ScriptOutput.Json, Cancellation);

        // Assert
        Assert.Equal("2000", output);
    }

    [Fact]
    public void Run_WhenCancelled_ThenStops()
    {
        // Arrange
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();

        // Act
        var exception = Record.Exception(() => ScriptHost.Run("map.js", "while (true) {}", Values, ScriptOutput.Json, cancellation.Token));

        // Assert
        Assert.IsAssignableFrom<OperationCanceledException>(exception);
    }

    [Fact]
    public void Run_WhenTheScriptReturnsNothing_ThenGivesNull()
    {
        // Act
        var output = ScriptHost.Run("check.js", "if (!vars.name) { throw new Error('No name'); }", Values, ScriptOutput.Json, Cancellation);

        // Assert
        Assert.Null(output);
    }

    [Fact]
    public void SyntaxErrorIn_WhenTheCodeIsValid_ThenGivesNull()
    {
        // Act
        var error = ScriptHost.SyntaxErrorIn("map.js", "return 1;");

        // Assert
        Assert.Null(error);
    }

    [Fact]
    public void SyntaxErrorIn_WhenTheCodeHasASyntaxError_ThenTellsWhatAndWhere()
    {
        // Act
        var error = ScriptHost.SyntaxErrorIn("map.js", "let a = 1;\nreturn a +;");

        // Assert
        Assert.Equal("Unexpected token ';' (map.js:2:11)", error);
    }

    [Fact]
    public void SyntaxErrorIn_WhenTheCodeIsNestedTooDeeply_ThenTellsIt()
    {
        // Act
        var error = ScriptHost.SyntaxErrorIn("map.js", $"return {string.Join(" + ", Enumerable.Range(0, 300).Select(index => $"vars.a{index}"))};");

        // Assert
        Assert.NotNull(error);
    }
}
