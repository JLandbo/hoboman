using Hoboman.Core.Auth;

namespace Hoboman.Core.Requests;

// A folder in the collections. Its file is named by its id, so a name can be anything, and moving it only changes ParentId.
public sealed record RequestFolder
{
    public Guid Id { get; init; }

    public string Name { get; init; } = "";

    public Guid? ParentId { get; init; }

    public AuthSettings Auth { get; init; } = AuthSettings.Inherit;
}
