using Hoboman.Core.Auth;

namespace Hoboman.ViewModels;

// A saved credential with its secrets, so picking it fills in the auth at once.
public sealed record CredentialChoice(Credential Credential, string Password, string Token, string ClientSecret);
