using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace Hoboman.Tests.Workflows;

public sealed class RunLogTests : IDisposable
{
    readonly TemporaryFolder _temporary = new();

    public void Dispose() => _temporary.Dispose();

    [Fact]
    public async Task AddAsync_WhenAnEventIsWritten_ThenAnotherReaderSeesTheLine()
    {
        // Arrange
        await using var log = RunLog.Create(new AppFolder(_temporary.Path), Guid.NewGuid(), NullLogger.Instance);
        using var reader = new StreamReader(new FileStream(log.FilePath, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete));

        // Act
        await log.AddAsync(new StepSkipped(2));

        // Assert
        Assert.Equal("""{"type":"step.skipped","index":2}""", await reader.ReadLineAsync(TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task DisposeAsync_WhenALineCouldNotBeWritten_ThenDoesNotThrow()
    {
        // Arrange
        var log = RunLog.Create(new AppFolder(_temporary.Path), Guid.NewGuid(), NullLogger.Instance);
        using var locker = new FileStream(log.FilePath, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
        locker.Lock(0, 1_000_000);
        await log.AddAsync(new StepSkipped(2));

        // Act
        var exception = await Record.ExceptionAsync(async () => await log.DisposeAsync());

        // Assert
        Assert.Null(exception);
    }

    [Fact]
    public async Task Create_WhenCalled_ThenNamesTheFileByTimeInTheFolderOfTheWorkflow()
    {
        // Arrange
        var workflowId = Guid.NewGuid();

        // Act
        await using var log = RunLog.Create(new AppFolder(_temporary.Path), workflowId, NullLogger.Instance);

        // Assert
        Assert.Matches($@"^{Regex.Escape(Path.Combine(_temporary.Path, "runs", $"{workflowId}"))}\\\d{{8}}-\d{{6}}-\d{{3}}-[0-9a-f]{{4}}\.jsonl$", log.FilePath);
    }

    [Fact]
    public void LineOf_WhenAStepFinishes_ThenWritesTheSmallFieldsFirstAndTheBodyLast()
    {
        // Arrange
        var finished = StepFinished.Of(0, new(200, "OK", 5, 2, [new("Content-Type", "application/json")], "{}"), new Dictionary<string, JsonElement> { ["navn"] = JsonSerializer.SerializeToElement("Æble") }, null);

        // Act
        var line = Encoding.UTF8.GetString(RunLog.LineOf(finished).Span);

        // Assert
        Assert.Equal("""{"type":"step.finished","index":0,"outcome":"Succeeded","status":200,"reason":"OK","elapsedMs":5,"size":2,"saved":{"navn":"Æble"},"headers":[{"name":"Content-Type","value":"application/json"}],"body":"{}"}""" + "\n", line);
    }

    [Fact]
    public void LineOf_WhenARunStarts_ThenWritesTheFieldsInOrder()
    {
        // Arrange
        var started = new RunStarted("r1", Guid.Parse("6f1c2a3b-0000-4000-8000-000000000001"), "Ordre-sync", "Dev", "r1.jsonl", new Dictionary<string, JsonElement> { ["orderId"] = JsonSerializer.SerializeToElement("o-17") });

        // Act
        var line = Encoding.UTF8.GetString(RunLog.LineOf(started).Span);

        // Assert
        Assert.Equal("""{"type":"run.started","runId":"r1","workflowId":"6f1c2a3b-0000-4000-8000-000000000001","workflow":"Ordre-sync","environment":"Dev","runFile":"r1.jsonl","parameters":{"orderId":"o-17"}}""" + "\n", line);
    }

    [Fact]
    public void LineOf_WhenAStepStarts_ThenWritesTheFieldsInOrder()
    {
        // Arrange
        var started = new StepStarted(0, "Shop/Login", "POST", "https://dev.local/login");

        // Act
        var line = Encoding.UTF8.GetString(RunLog.LineOf(started).Span);

        // Assert
        Assert.Equal("""{"type":"step.started","index":0,"request":"Shop/Login","method":"POST","address":"https://dev.local/login"}""" + "\n", line);
    }

    [Fact]
    public void LineOf_WhenARunFinishes_ThenWritesTheFieldsInOrder()
    {
        // Arrange
        var finished = new RunFinished(RunOutcome.Failed, 7, [new(0, StepOutcome.Failed, 500), new(1, StepOutcome.Skipped)], new Dictionary<string, JsonElement> { ["token"] = JsonSerializer.SerializeToElement("abc") });

        // Act
        var line = Encoding.UTF8.GetString(RunLog.LineOf(finished).Span);

        // Assert
        Assert.Equal("""{"type":"run.finished","outcome":"Failed","elapsedMs":7,"steps":[{"index":0,"outcome":"Failed","status":500},{"index":1,"outcome":"Skipped"}],"variables":{"token":"abc"}}""" + "\n", line);
    }
}
