using System.Collections.ObjectModel;
using System.ComponentModel;
using Hoboman.Core.Requests;
using Hoboman.Mvvm;

namespace Hoboman.ViewModels;

public sealed class KeyValueListViewModel : ObservableObject
{
    public KeyValueListViewModel() => Load([]);

    public ObservableCollection<KeyValueRowViewModel> Rows { get; } = [];

    public int EnabledCount => Rows.Count(row => row.Enabled && !row.IsBlank);

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
        OnPropertyChanged(nameof(EnabledCount));
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

    // A blank last row lets the user type a new entry without an Add button, like in Postman.
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
        OnPropertyChanged(nameof(EnabledCount));
        Changed?.Invoke();
    }
}
