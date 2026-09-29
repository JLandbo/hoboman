using Hoboman.Mvvm;

namespace Hoboman.ViewModels;

public sealed class KeyValueRowViewModel : ObservableObject
{
    public bool Enabled { get; set => Set(ref field, value); } = true;

    public string Name
    {
        get;
        set
        {
            if (Set(ref field, value))
            {
                OnPropertyChanged(nameof(IsBlank));
            }
        }
    } = "";

    public string Value
    {
        get;
        set
        {
            if (Set(ref field, value))
            {
                OnPropertyChanged(nameof(IsBlank));
            }
        }
    } = "";

    public bool IsBlank => Name.Length == 0 && Value.Length == 0;
}
