using Hoboman.Core.Auth;

namespace Hoboman.Core.Requests;

public sealed record FolderSettings
{
    public Guid Id { get; init; }

    public AuthSettings Auth { get; init; } = AuthSettings.Inherit;
}
