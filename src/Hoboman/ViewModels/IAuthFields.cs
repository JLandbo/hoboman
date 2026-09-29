using System.ComponentModel;
using Hoboman.Core.Auth;

namespace Hoboman.ViewModels;

// What the auth editor edits, so requests and folders share one editor.
public interface IAuthFields : INotifyPropertyChanged
{
    AuthKind AuthKind { get; set; }

    string UserName { get; set; }

    string Password { get; set; }

    string Token { get; set; }
}
