namespace Hoboman.Tests.Requests;

public sealed class RequestDeletionTests : IDisposable
{
    readonly TemporaryFolder _temporary = new();

    CancellationToken Cancellation => TestContext.Current.CancellationToken;

    AppFolder Folder => field ??= new(_temporary.Path);

    RequestLibrary Library => field ??= new(Folder, NullLogger<RequestLibrary>.Instance);

    SecretStore Secrets => field ??= new(Folder, NullLogger<SecretStore>.Instance);

    RequestDeletion Service() => new(Library, Secrets, Folder, NullLogger<RequestDeletion>.Instance);

    string FileOf(ApiRequest request) => Path.Combine(Folder.Requests, $"{request.Id}.json");

    public void Dispose() => _temporary.Dispose();

    [Fact]
    public async Task DeleteAsync_WhenAFolderIsDeleted_ThenDeletesTheFoldersAndRequestsInItAndNothingElse()
    {
        await Library.SaveAtAsync("Users/Get", ApiRequest.New(), Cancellation);
        await Library.SaveAtAsync("Users/Admin/List", ApiRequest.New(), Cancellation);
        await Library.SaveAtAsync("Ping", ApiRequest.New(), Cancellation);
        await Library.FolderAtAsync("Other", Cancellation);

        await Service().DeleteAsync((await Library.FolderAtAsync("Users", Cancellation))!.Value, true, Cancellation);

        Assert.Equal(["Ping"], await Library.PathsAsync(Cancellation));
        Assert.Equal(["Other"], (await Library.LoadAllAsync(Cancellation)).Folders.Select(folder => folder.Name));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task DeleteAsync_WhenSecretsAreLocked_ThenRemembersAllOwnersAndRetriesAfterRestart(bool folder)
    {
        var request = await Library.SaveAtAsync("Folder/Request", ApiRequest.New(), Cancellation);
        var owner = (await Library.FolderAtAsync("Folder", Cancellation))!.Value;
        var outside = await Library.SaveAtAsync("Outside", ApiRequest.New(), Cancellation);
        var prod = Guid.NewGuid();
        foreach (var id in new[] { request.Id, owner, outside.Id })
        {
            await Secrets.SaveAsync(id, SecretKind.Token, "secret", Cancellation);
            await Secrets.SaveAsync(id, SecretKind.OAuthToken, prod, "oauth-secret", Cancellation);
        }
        using (var locked = new FileStream(Folder.Secrets, FileMode.Open, FileAccess.Read, FileShare.Read))
        {
            Assert.True(await Record.ExceptionAsync(() => Service().DeleteAsync(folder ? owner : request.Id, folder, Cancellation)) is IOException or UnauthorizedAccessException);
        }
        Assert.False(Library.Exists(request.Id));
        Assert.Equal("secret", await Secrets.OfAsync(request.Id, SecretKind.Token, Cancellation));
        Assert.DoesNotContain("oauth-secret", await File.ReadAllTextAsync(Folder.PendingSecretCleanup, Cancellation));

        await Service().CleanupAsync(Cancellation);

        Assert.Null(await Secrets.OfAsync(request.Id, SecretKind.Token, Cancellation));
        Assert.Null(await Secrets.OfAsync(request.Id, SecretKind.OAuthToken, prod, Cancellation));
        Assert.Equal(folder ? null : "secret", await Secrets.OfAsync(owner, SecretKind.Token, Cancellation));
        Assert.Equal("secret", await Secrets.OfAsync(outside.Id, SecretKind.Token, Cancellation));
        await Service().CleanupAsync(Cancellation);
        Assert.Equal("secret", await Secrets.OfAsync(outside.Id, SecretKind.Token, Cancellation));
    }

    [Fact]
    public async Task CleanupAsync_WhenAnOwnerCannotBeRead_ThenKeepsItsSecrets()
    {
        var request = await Library.SaveAtAsync("Request", ApiRequest.New(), Cancellation);
        await Secrets.SaveAsync(request.Id, SecretKind.Token, "kept", Cancellation);
        await File.WriteAllTextAsync(Folder.PendingSecretCleanup, System.Text.Json.JsonSerializer.Serialize(new[] { request.Id }), Cancellation);
        using var locked = new FileStream(FileOf(request), FileMode.Open, FileAccess.Read, FileShare.None);

        await Service().CleanupAsync(Cancellation);

        Assert.Equal("kept", await Secrets.OfAsync(request.Id, SecretKind.Token, Cancellation));
    }

    [Fact]
    public async Task DeleteAsync_WhenThePendingListCannotBeWritten_ThenDoesNotDeleteTheOwner()
    {
        var request = await Library.SaveAtAsync("Request", ApiRequest.New(), Cancellation);
        await Secrets.SaveAsync(request.Id, SecretKind.Token, "secret", Cancellation);
        await File.WriteAllTextAsync(Folder.PendingSecretCleanup, "[]", Cancellation);
        using var locked = new FileStream(Folder.PendingSecretCleanup, FileMode.Open, FileAccess.Read, FileShare.Read);

        Assert.True(await Record.ExceptionAsync(() => Service().DeleteAsync(request.Id, false, Cancellation)) is IOException or UnauthorizedAccessException);

        Assert.True(Library.Exists(request.Id));
        Assert.Equal("secret", await Secrets.OfAsync(request.Id, SecretKind.Token, Cancellation));
    }

    [Fact]
    public async Task DeleteAsync_WhenAFileCannotBeDeleted_ThenDeletesTheRestAndKeepsItsFolderAndSecrets()
    {
        var removable = await Library.SaveAtAsync("Folder/A", ApiRequest.New(), Cancellation);
        var remaining = await Library.SaveAtAsync("Folder/B", ApiRequest.New(), Cancellation);
        await Secrets.SaveAsync(removable.Id, SecretKind.Token, "removable", Cancellation);
        await Secrets.SaveAsync(remaining.Id, SecretKind.Token, "remaining", Cancellation);
        var folder = (await Library.FolderAtAsync("Folder", Cancellation))!.Value;
        await Secrets.SaveAsync(folder, SecretKind.Token, "folder", Cancellation);
        using var locked = new FileStream(FileOf(remaining), FileMode.Open, FileAccess.Read, FileShare.Read);

        await Assert.ThrowsAnyAsync<IOException>(() => Service().DeleteAsync(folder, true, Cancellation));

        Assert.Equal((false, true, true), (Library.Exists(removable.Id), Library.Exists(remaining.Id), Library.FolderExists(folder)));
        Assert.Null(await Secrets.OfAsync(removable.Id, SecretKind.Token, Cancellation));
        Assert.Equal(("remaining", "folder"), (await Secrets.OfAsync(remaining.Id, SecretKind.Token, Cancellation), await Secrets.OfAsync(folder, SecretKind.Token, Cancellation)));
    }

    [Fact]
    public async Task DeleteAsync_WhenASubfolderCannotBeDeleted_ThenKeepsTheFoldersAboveIt()
    {
        // Arrange
        // The parent's file comes first on disk, so it would be deleted first if the deepest folders did not go first.
        var (users, admin) = (Guid.Parse("00000000-0000-0000-0000-000000000001"), Guid.Parse("00000000-0000-0000-0000-000000000002"));
        await Library.SaveFolderAsync(new() { Id = users, Name = "Users" }, Cancellation);
        await Library.SaveFolderAsync(new() { Id = admin, Name = "Admin", ParentId = users }, Cancellation);
        using var locked = new FileStream(Path.Combine(Folder.Folders, $"{admin}.json"), FileMode.Open, FileAccess.Read, FileShare.Read);

        // Act
        var failure = await Record.ExceptionAsync(() => Service().DeleteAsync(users, true, Cancellation));

        // Assert
        Assert.IsType<IOException>(failure, exactMatch: false);
        Assert.Equal((true, true), (Library.FolderExists(admin), Library.FolderExists(users)));
    }

    [Fact]
    public async Task DeleteAsync_WhenTheFoldersLeadBackToEachOther_ThenDeletesOnlyTheChosenOne()
    {
        // Arrange
        var (shop, admin) = (Guid.NewGuid(), Guid.NewGuid());
        await Library.SaveFolderAsync(new() { Id = shop, Name = "Shop", ParentId = admin }, Cancellation);
        await Library.SaveFolderAsync(new() { Id = admin, Name = "Admin", ParentId = shop }, Cancellation);
        await Library.SaveAsync(ApiRequest.New() with { Name = "Login", FolderId = shop }, Cancellation);

        // Act
        await Service().DeleteAsync(admin, true, Cancellation);

        // Assert
        Assert.Equal(["Shop/Login"], await Library.PathsAsync(Cancellation));
    }

    [Fact]
    public async Task CleanupAsync_WhenThePendingListCannotBeCleared_ThenCanSafelyRepeatCleanup()
    {
        var request = await Library.SaveAtAsync("Request", ApiRequest.New(), Cancellation);
        var outside = await Library.SaveAtAsync("Outside", ApiRequest.New(), Cancellation);
        await Secrets.SaveAsync(request.Id, SecretKind.Token, "deleted", Cancellation);
        await Secrets.SaveAsync(outside.Id, SecretKind.Token, "kept", Cancellation);
        using (var locked = new FileStream(Folder.Secrets, FileMode.Open, FileAccess.Read, FileShare.Read))
        {
            Assert.True(await Record.ExceptionAsync(() => Service().DeleteAsync(request.Id, false, Cancellation)) is IOException or UnauthorizedAccessException);
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
        var request = await Library.SaveAtAsync("Request", ApiRequest.New(), Cancellation);
        await Secrets.SaveAsync(request.Id, SecretKind.Token, "kept", Cancellation);
        await File.WriteAllTextAsync(Folder.PendingSecretCleanup, System.Text.Json.JsonSerializer.Serialize(new[] { request.Id }), Cancellation);

        await Service().CleanupAsync(Cancellation);

        Assert.True(Library.Exists(request.Id));
        Assert.Equal("kept", await Secrets.OfAsync(request.Id, SecretKind.Token, Cancellation));
    }
}
