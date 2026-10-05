using Hoboman.Core.Auth;
using Hoboman.Core.Languages;
using Hoboman.Core.Requests;
using Hoboman.Core.Storage;
using Hoboman.Mvvm;
using Hoboman.Services;
using Microsoft.Extensions.Logging;

namespace Hoboman.ViewModels;

public sealed class FolderAuthViewModel(RequestLibrary library, SecretStore secrets, AuthRefreshService refreshes, EnvironmentsViewModel environments, CredentialsViewModel credentials, Translator translator, TimeProvider clock, ILogger<FolderAuthViewModel> logger) : ObservableObject
{
    Guid _id;
    string _path = "";
    RequestFolder _folder = new();

    public string Title { get; private set => Set(ref field, value); } = "";

    public AuthViewModel Auth { get; } = new(secrets, refreshes, environments, credentials, translator, clock, logger);

    public string? Problem { get; private set => Set(ref field, value); }

    // Saving what could not be read would replace the folder's auth with nothing.
    public bool CanSave { get; private set => Set(ref field, value); }

    public async Task LoadAsync(Guid id, string path, CancellationToken cancellationToken)
    {
        _id = id;
        _path = path;
        Title = translator.Format("FolderAuth.Title", path);
        Problem = null;
        CanSave = false;
        try
        {
            if (await library.LoadFolderAsync(id, cancellationToken) is not { } folder)
            {
                Problem = translator.Of("FolderAuth.Gone");
                return;
            }
            _folder = folder;
            Auth.UseOwner(id);
            Auth.Load(_folder.Auth);
            await Auth.LoadSecretsAsync(id, cancellationToken);
            CanSave = true;
        }
        catch (Exception exception) when (FileProblem.Is(exception))
        {
            logger.LogError(exception, "Could not load the auth of the folder {Folder}", path);
            Problem = translator.Format("FolderAuth.LoadFailed", translator.DetailsOf(exception));
        }
    }

    public async Task<bool> SaveAsync()
    {
        if (!CanSave)
        {
            return false;
        }
        try
        {
            return await refreshes.SaveAsync(SaveUnderLockAsync, CancellationToken.None);
        }
        catch (Exception exception) when (FileProblem.Is(exception))
        {
            logger.LogError(exception, "Could not save the auth of the folder {Folder}", _path);
            Problem = translator.Format("FolderAuth.SaveFailed", translator.DetailsOf(exception));
            return false;
        }
    }

    async Task<bool> SaveUnderLockAsync()
    {
        // The disk wins: auth changed while the window was open is not overwritten. A name or place changed meanwhile is kept.
        if (await library.LoadFolderAsync(_id, CancellationToken.None) is not { } saved || saved.Auth != _folder.Auth)
        {
            Problem = translator.Of("FolderAuth.ChangedOnDisk");
            return false;
        }
        // The secrets go first, so a failure leaves no folder file behind that points at secrets that were never saved.
        await Auth.SaveSecretsUnderLockAsync(_id, CancellationToken.None);
        var folder = saved with { Auth = Auth.ToSettings() };
        if (folder != saved)
        {
            await library.SaveFolderAsync(folder, CancellationToken.None);
        }
        _folder = folder;
        logger.LogInformation("Saved the auth of the folder {Folder}", _path);
        return true;
    }
}
