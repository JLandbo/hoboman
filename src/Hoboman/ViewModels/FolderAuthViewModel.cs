using Hoboman.Core.Auth;
using Hoboman.Core.Languages;
using Hoboman.Core.Requests;
using Hoboman.Core.Storage;
using Hoboman.Mvvm;
using Microsoft.Extensions.Logging;

namespace Hoboman.ViewModels;

public sealed class FolderAuthViewModel(RequestLibrary library, SecretStore secrets, Translator translator, ILogger<FolderAuthViewModel> logger) : ObservableObject, IAuthFields
{
    string _folder = "";
    FolderSettings _settings = new();
    string _savedPassword = "";
    string _savedToken = "";

    public string Title { get; private set => Set(ref field, value); } = "";

    public AuthKind AuthKind { get; set => Set(ref field, value); }

    public string UserName { get; set => Set(ref field, value); } = "";

    public string Password { get; set => Set(ref field, value); } = "";

    public string Token { get; set => Set(ref field, value); } = "";

    public string? Problem { get; private set => Set(ref field, value); }

    // Saving what could not be read would replace the folder's auth with nothing.
    public bool CanSave { get; private set => Set(ref field, value); }

    public async Task LoadAsync(string folder, CancellationToken cancellationToken)
    {
        _folder = folder;
        Title = translator.Format("FolderAuth.Title", folder.Split('/')[^1]);
        Problem = null;
        CanSave = false;
        try
        {
            _settings = await library.LoadFolderAsync(folder, cancellationToken) ?? new();
            AuthKind = _settings.Auth.Kind;
            UserName = _settings.Auth.UserName;
            Password = _savedPassword = await SecretOfAsync(SecretKind.Password, cancellationToken);
            Token = _savedToken = await SecretOfAsync(SecretKind.Token, cancellationToken);
            CanSave = true;
        }
        catch (Exception exception) when (FileProblem.Is(exception))
        {
            logger.LogError(exception, "Could not load the auth of the folder {Folder}", folder);
            Problem = translator.Format("FolderAuth.LoadFailed", exception.Message);
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
            // The disk wins: a change made while the window was open is not overwritten, and a moved folder is not made again.
            if (!library.FolderExists(_folder) || (await library.LoadFolderAsync(_folder, CancellationToken.None) ?? new()) != _settings)
            {
                Problem = translator.Of("FolderAuth.ChangedOnDisk");
                return false;
            }
            var shared = _settings.Id != Guid.Empty && await library.SharesFolderIdAsync(_folder, _settings.Id, CancellationToken.None);
            if (shared)
            {
                logger.LogWarning("{Folder} shares its id with another folder, so it gets its own", _folder);
            }
            var settings = _settings with { Id = _settings.Id == Guid.Empty || shared ? Guid.NewGuid() : _settings.Id, Auth = _settings.Auth with { Kind = AuthKind, UserName = UserName } };
            // A new id has no secrets yet, so the ones shown are saved under it.
            var (savedPassword, savedToken) = settings.Id == _settings.Id ? (_savedPassword, _savedToken) : ("", "");
            // The secrets go first, so a failure leaves no folder file behind that points at secrets that were never saved.
            if (Password != savedPassword)
            {
                await secrets.SaveAsync(settings.Id, SecretKind.Password, Password, CancellationToken.None);
            }
            if (Token != savedToken)
            {
                await secrets.SaveAsync(settings.Id, SecretKind.Token, Token, CancellationToken.None);
            }
            await library.SaveFolderAsync(_folder, settings, CancellationToken.None);
            (_settings, _savedPassword, _savedToken) = (settings, Password, Token);
            logger.LogInformation("Saved the auth of the folder {Folder}", _folder);
            return true;
        }
        catch (Exception exception) when (FileProblem.Is(exception))
        {
            logger.LogError(exception, "Could not save the auth of the folder {Folder}", _folder);
            Problem = translator.Format("FolderAuth.SaveFailed", exception.Message);
            return false;
        }
    }

    async Task<string> SecretOfAsync(SecretKind kind, CancellationToken cancellationToken) =>
        _settings.Id == Guid.Empty ? "" : await secrets.OfAsync(_settings.Id, kind, cancellationToken) ?? "";
}
