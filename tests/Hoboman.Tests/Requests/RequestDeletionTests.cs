namespace Hoboman.Tests.Requests;

public sealed class RequestDeletionTests : IDisposable
{
    readonly TemporaryFolder _temporary = new();

    CancellationToken Cancellation => TestContext.Current.CancellationToken;

    AppFolder Folder => field ??= new(_temporary.Path);

    RequestLibrary Library => field ??= new(Folder, NullLogger<RequestLibrary>.Instance);

    SecretStore Secrets => field ??= new(Folder, NullLogger<SecretStore>.Instance);

    RequestDeletion Service() => new(Library, Secrets, Folder, NullLogger<RequestDeletion>.Instance);

    public void Dispose() => _temporary.Dispose();

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task DeleteAsync_WhenSecretsAreLocked_ThenRemembersAllOwnersAndRetriesAfterRestart(bool folder)
    {
        var request = ApiRequest.New();
        var owner = new FolderSettings { Id = Guid.NewGuid() };
        var outside = ApiRequest.New();
        await Library.SaveAsync("Folder/Request", request, Cancellation);
        await Library.SaveFolderAsync("Folder", owner, Cancellation);
        await Library.SaveAsync("Outside", outside, Cancellation);
        foreach (var id in new[] { request.Id, owner.Id, outside.Id })
        {
            await Secrets.SaveAsync(id, SecretKind.Token, "secret", Cancellation);
            await Secrets.SaveAsync(id, SecretKind.OAuthToken, "Prod", "oauth-secret", Cancellation);
        }
        using (var locked = new FileStream(Folder.Secrets, FileMode.Open, FileAccess.Read, FileShare.Read))
        {
            Assert.True(await Record.ExceptionAsync(() => Service().DeleteAsync(folder ? "Folder" : "Folder/Request", folder, Cancellation)) is IOException or UnauthorizedAccessException);
        }
        Assert.False(Library.Exists("Folder/Request"));
        Assert.Equal("secret", await Secrets.OfAsync(request.Id, SecretKind.Token, Cancellation));
        Assert.DoesNotContain("oauth-secret", await File.ReadAllTextAsync(Folder.PendingSecretCleanup, Cancellation));

        await Service().CleanupAsync(Cancellation);

        Assert.Null(await Secrets.OfAsync(request.Id, SecretKind.Token, Cancellation));
        Assert.Null(await Secrets.OfAsync(request.Id, SecretKind.OAuthToken, "Prod", Cancellation));
        Assert.Equal(folder ? null : "secret", await Secrets.OfAsync(owner.Id, SecretKind.Token, Cancellation));
        Assert.Equal("secret", await Secrets.OfAsync(outside.Id, SecretKind.Token, Cancellation));
        await Service().CleanupAsync(Cancellation);
        Assert.Equal("secret", await Secrets.OfAsync(outside.Id, SecretKind.Token, Cancellation));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task DeleteAsync_WhenASharedOwnerCannotBeRead_ThenNeverDeletesItsSecrets(bool folder)
    {
        var request = ApiRequest.New();
        await Library.SaveAsync("Original", request, Cancellation);
        if (folder)
        {
            await Library.SaveFolderAsync("Copy", new() { Id = request.Id }, Cancellation);
        }
        else
        {
            await Library.SaveAsync("Copy", request, Cancellation);
        }
        await Secrets.SaveAsync(request.Id, SecretKind.Token, "shared", Cancellation);
        var copy = folder ? Path.Combine(Folder.Requests, "Copy", ".folder.json") : Path.Combine(Folder.Requests, "Copy.json");
        using (var locked = new FileStream(copy, FileMode.Open, FileAccess.Read, FileShare.None))
        {
            await Assert.ThrowsAnyAsync<IOException>(() => Service().DeleteAsync("Original", false, Cancellation));
        }
        Assert.False(Library.Exists("Original"));
        Assert.Equal("shared", await Secrets.OfAsync(request.Id, SecretKind.Token, Cancellation));

        await Service().CleanupAsync(Cancellation);

        Assert.Equal("shared", await Secrets.OfAsync(request.Id, SecretKind.Token, Cancellation));
        await Service().DeleteAsync("Copy", folder, Cancellation);
        Assert.Null(await Secrets.OfAsync(request.Id, SecretKind.Token, Cancellation));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task DeleteAsync_WhenTheOwnersIdentityCannotBeRead_ThenKeepsTheFilesAndSecrets(bool folder)
    {
        var request = ApiRequest.New();
        await Library.SaveAsync("Folder/Request", request, Cancellation);
        await Library.SaveFolderAsync("Folder", new() { Id = request.Id }, Cancellation);
        await Secrets.SaveAsync(request.Id, SecretKind.Token, "secret", Cancellation);
        var path = Path.Combine(Folder.Requests, "Folder", folder ? ".folder.json" : "Request.json");
        using var locked = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Delete);

        await Assert.ThrowsAnyAsync<IOException>(() => Service().DeleteAsync(folder ? "Folder" : "Folder/Request", folder, Cancellation));

        Assert.True(Library.Exists("Folder/Request"));
        Assert.True(Library.FolderExists("Folder"));
        Assert.Equal("secret", await Secrets.OfAsync(request.Id, SecretKind.Token, Cancellation));
    }

    [Fact]
    public async Task DeleteAsync_WhenThePendingListCannotBeWritten_ThenDoesNotDeleteTheOwner()
    {
        var request = ApiRequest.New();
        await Library.SaveAsync("Request", request, Cancellation);
        await Secrets.SaveAsync(request.Id, SecretKind.Token, "secret", Cancellation);
        await File.WriteAllTextAsync(Folder.PendingSecretCleanup, "[]", Cancellation);
        using var locked = new FileStream(Folder.PendingSecretCleanup, FileMode.Open, FileAccess.Read, FileShare.Read);

        Assert.True(await Record.ExceptionAsync(() => Service().DeleteAsync("Request", false, Cancellation)) is IOException or UnauthorizedAccessException);

        Assert.True(Library.Exists("Request"));
        Assert.Equal("secret", await Secrets.OfAsync(request.Id, SecretKind.Token, Cancellation));
    }

    [Fact]
    public async Task DeleteAsync_WhenAFileCannotBeDeleted_ThenKeepsTheRemainingOwnersSecrets()
    {
        var removable = ApiRequest.New();
        var remaining = ApiRequest.New();
        await Library.SaveAsync("Folder/A", removable, Cancellation);
        await Library.SaveAsync("Folder/B", remaining, Cancellation);
        await Secrets.SaveAsync(removable.Id, SecretKind.Token, "removable", Cancellation);
        await Secrets.SaveAsync(remaining.Id, SecretKind.Token, "remaining", Cancellation);
        using var locked = new FileStream(Path.Combine(Folder.Requests, "Folder", "B.json"), FileMode.Open, FileAccess.Read, FileShare.Read);

        await Assert.ThrowsAnyAsync<IOException>(() => Service().DeleteAsync("Folder", true, Cancellation));

        Assert.False(Library.Exists("Folder/A"));
        Assert.True(Library.Exists("Folder/B"));
        Assert.Null(await Secrets.OfAsync(removable.Id, SecretKind.Token, Cancellation));
        Assert.Equal("remaining", await Secrets.OfAsync(remaining.Id, SecretKind.Token, Cancellation));
    }

    [Fact]
    public async Task CleanupAsync_WhenThePendingListCannotBeCleared_ThenCanSafelyRepeatCleanup()
    {
        var request = ApiRequest.New();
        var outside = ApiRequest.New();
        await Library.SaveAsync("Request", request, Cancellation);
        await Library.SaveAsync("Outside", outside, Cancellation);
        await Secrets.SaveAsync(request.Id, SecretKind.Token, "deleted", Cancellation);
        await Secrets.SaveAsync(outside.Id, SecretKind.Token, "kept", Cancellation);
        using (var locked = new FileStream(Folder.Secrets, FileMode.Open, FileAccess.Read, FileShare.Read))
        {
            Assert.True(await Record.ExceptionAsync(() => Service().DeleteAsync("Request", false, Cancellation)) is IOException or UnauthorizedAccessException);
        }
        using (var locked = new FileStream(Folder.PendingSecretCleanup, FileMode.Open, FileAccess.Read, FileShare.Read))
        {
            Assert.True(await Record.ExceptionAsync(() => Service().CleanupAsync(Cancellation)) is IOException or UnauthorizedAccessException);
            Assert.Null(await Secrets.OfAsync(request.Id, SecretKind.Token, Cancellation));
        }

        await Service().CleanupAsync(Cancellation);

        Assert.Null(await Secrets.OfAsync(request.Id, SecretKind.Token, Cancellation));
        Assert.Equal("kept", await Secrets.OfAsync(outside.Id, SecretKind.Token, Cancellation));
        Assert.Equal("[]", (await File.ReadAllTextAsync(Folder.PendingSecretCleanup, Cancellation)).Trim());
    }

    [Fact]
    public async Task CleanupAsync_WhenInterruptedBeforeDeletingTheFile_ThenKeepsTheOwnersSecrets()
    {
        var request = ApiRequest.New();
        await Library.SaveAsync("Request", request, Cancellation);
        await Secrets.SaveAsync(request.Id, SecretKind.Token, "kept", Cancellation);
        await File.WriteAllTextAsync(Folder.PendingSecretCleanup, System.Text.Json.JsonSerializer.Serialize(new[] { request.Id }), Cancellation);

        await Service().CleanupAsync(Cancellation);

        Assert.True(Library.Exists("Request"));
        Assert.Equal("kept", await Secrets.OfAsync(request.Id, SecretKind.Token, Cancellation));
    }
}
