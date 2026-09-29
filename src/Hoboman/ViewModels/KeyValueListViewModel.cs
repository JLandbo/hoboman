using System.Collections.ObjectModel;
using System.ComponentModel;
using Hoboman.Core.Requests;
using Hoboman.Mvvm;

namespace Hoboman.ViewModels;

public sealed class KeyValueListViewModel : ObservableObject
{
    public KeyValueListViewModel() => Load([]);

    public ObservableCollection<KeyValueRowViewModel> Rows { get; } = [];

    public int Count => Rows.Count(row => row.Enabled && !row.IsBlank);

    public event Action? Changed;

    public void Load(IEnumerable<KeyValue> values)
    {
        foreach (var row in Rows)
        {
            row.PropertyChanged -= Row_PropertyChanged;
        }
        Rows.Clear();
        foreach (var value in values)
        {
            Add(new() { Enabled = value.Enabled, Name = value.Name, Value = value.Value });
        }
        Add(new());
        OnPropertyChanged(nameof(Count));
    }

    public void Remove(KeyValueRowViewModel row)
    {
        if (row == Rows[^1])
        {
            return;
        }
        row.PropertyChanged -= Row_PropertyChanged;
        Rows.Remove(row);
        OnChanged();
    }

    public IReadOnlyList<KeyValue> ToList() => [.. Rows.Where(row => !row.IsBlank).Select(row => new KeyValue(row.Name, row.Value, row.Enabled))];

    void Add(KeyValueRowViewModel row)
    {
        row.PropertyChanged += Row_PropertyChanged;
        Rows.Add(row);
    }

    // There is always one blank row at the end to type into, like in Postman.
    void Row_PropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(KeyValueRowViewModel.IsBlank))
        {
            return;
        }
        if (!Rows[^1].IsBlank)
        {
            Add(new());
        }
        OnChanged();
    }

    void OnChanged()
    {
        OnPropertyChanged(nameof(Count));
        Changed?.Invoke();
    }
}

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
