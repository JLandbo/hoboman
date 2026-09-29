using System.Collections.ObjectModel;
using Hoboman.Core.History;
using Hoboman.Core.Languages;
using Hoboman.Core.Storage;
using Hoboman.Mvvm;
using Microsoft.Extensions.Logging;

namespace Hoboman.ViewModels;

public sealed class HistoryViewModel(HistoryStore store, Translator translator, ILogger<HistoryViewModel> logger) : ObservableObject
{
    public const int LatestCount = 100;

    string? _newest;
    bool _relabel = true;

    public ObservableCollection<HistoryItemViewModel> Items { get; } = [];

    public bool IsFull => Items.Count >= LatestCount;

    // The day labels are texts in the chosen language, so the next refresh builds the list again.
    public void Relabel() => _relabel = true;

    public async Task RefreshAsync(CancellationToken cancellationToken)
    {
        var relabel = _relabel;
        _relabel = false;
        try
        {
            var files = await store.LatestAsync(LatestCount, relabel ? null : _newest, cancellationToken);
            if (relabel)
            {
                Items.Clear();
            }
            foreach (var file in files.Reverse())
            {
                Items.Insert(0, new(file.Entry, DayOf(file.Entry.At.LocalDateTime.Date, DateTime.Today)));
            }
            while (Items.Count > LatestCount)
            {
                Items.RemoveAt(Items.Count - 1);
            }
            _newest = files.Count > 0 ? files[0].Name : _newest;
            OnPropertyChanged(nameof(IsFull));
        }
        catch (Exception exception) when (FileProblem.Is(exception))
        {
            logger.LogError(exception, "Could not load the history");
        }
    }

    string DayOf(DateTime day, DateTime today) =>
        day == today ? translator.Of("History.Today")
        : day == today.AddDays(-1) ? translator.Of("History.Yesterday")
        : day.Year == today.Year ? day.ToString("M", translator.Current.Culture)
        : $"{day.ToString("M", translator.Current.Culture)} {day.Year}";
}
