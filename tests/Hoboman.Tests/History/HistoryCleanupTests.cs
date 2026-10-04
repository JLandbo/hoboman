namespace Hoboman.Tests.History;

public sealed class HistoryCleanupTests : IDisposable
{
    readonly TemporaryFolder _temporary = new();

    AppFolder Folder => new(_temporary.Path);

    SettingsStore Settings => new(Folder, NullLogger<SettingsStore>.Instance);

    CancellationToken Cancellation => TestContext.Current.CancellationToken;

    public void Dispose() => _temporary.Dispose();

    string FileOf(string folder, string name, int daysOld)
    {
        var path = Path.Combine(folder, name);
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllText(path, "{}");
        File.SetLastWriteTimeUtc(path, DateTime.UtcNow.AddDays(-daysOld));
        return path;
    }

    Task DeleteOldAsync() => new HistoryCleanup(Folder, Settings, TimeProvider.System, NullLogger<HistoryCleanup>.Instance).DeleteOldAsync(Cancellation);

    [Fact]
    public async Task DeleteOldAsync_WhenDaysAreSet_ThenDeletesOnlyOlderCallsAndRuns()
    {
        // Arrange
        await Settings.UpdateAsync(settings => settings with { DeleteHistoryAfterDays = 30 }, Cancellation);
        string[] files = [FileOf(Folder.History, "old.json", 31), FileOf(Folder.History, "new.json", 29), FileOf(Folder.Runs, @"flow\old.jsonl", 31), FileOf(Folder.Runs, @"flow\new.jsonl", 29)];

        // Act
        await DeleteOldAsync();

        // Assert
        Assert.Equal([false, true, false, true], files.Select(File.Exists));
    }

    [Fact]
    public async Task DeleteOldAsync_WhenNoDaysAreSet_ThenKeepsEverything()
    {
        // Arrange
        var file = FileOf(Folder.History, "old.json", 3650);

        // Act
        await DeleteOldAsync();

        // Assert
        Assert.True(File.Exists(file));
    }
}
