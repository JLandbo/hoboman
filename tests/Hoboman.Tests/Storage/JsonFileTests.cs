namespace Hoboman.Tests.Storage;

public sealed class JsonFileTests : IDisposable
{
    readonly string _directory = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString());

    string FilePath => Path.Combine(_directory, "settings.json");

    JsonFile<AppSettings> Store() => new(FilePath, AppSettings.Default);

    public void Dispose()
    {
        if (Directory.Exists(_directory))
        {
            Directory.Delete(_directory, recursive: true);
        }
    }

    [Fact]
    public void Load_WhenTheFileIsMissing_ThenReturnsTheEmptyValue()
    {
        // Act
        var settings = Store().Load();

        // Assert
        Assert.Same(AppSettings.Default, settings);
    }

    [Fact]
    public void Load_WhenTheFileIsCorrupt_ThenReturnsTheEmptyValue()
    {
        // Arrange
        Directory.CreateDirectory(_directory);
        File.WriteAllText(FilePath, "{");

        // Act
        var settings = Store().Load();

        // Assert
        Assert.Same(AppSettings.Default, settings);
    }

    [Fact]
    public void Load_WhenSaved_ThenReturnsTheSameValue()
    {
        // Arrange
        Store().Save(new AppSettings("Test", IgnoreCertificateErrors: true));

        // Act
        var settings = Store().Load();

        // Assert
        Assert.Equal(new AppSettings("Test", IgnoreCertificateErrors: true), settings);
    }

    [Fact]
    public void Save_WhenTheFileIsLockedForAMoment_ThenSavesOnceItIsReleased()
    {
        // Arrange
        Store().Save(AppSettings.Default);
        var locked = new FileStream(FilePath, FileMode.Open, FileAccess.Read, FileShare.None);
        new Thread(() =>
        {
            Thread.Sleep(20);
            locked.Dispose();
        }).Start();

        // Act
        Store().Save(new AppSettings("Test"));

        // Assert
        Assert.Equal("Test", Store().Load().EnvironmentName);
    }
}
