using System.Collections.ObjectModel;
using Hoboman.Core.History;
using Hoboman.Core.Languages;
using Hoboman.Core.Storage;
using Hoboman.Mvvm;
using Microsoft.Extensions.Logging;

namespace Hoboman.ViewModels;

public sealed class HistoryViewModel(HistoryStore store, Translator translator, TimeProvider clock, ILogger<HistoryViewModel> logger) : ObservableObject
{
    public const int LatestCount = 100;

    string? _newest;
    bool _relabel = true;
    DateTime _labelledDay;
    ITimer? _midnight;

    public ObservableCollection<HistoryItemViewModel> Items { get; } = [];

    public event Action? DayChanged;

    public bool IsFull => Items.Count >= LatestCount;

    // The day labels are texts in the chosen language, so the next refresh builds the list again.
    public void Relabel() => _relabel = true;

    public async Task RefreshAsync(CancellationToken cancellationToken)
    {
        // "Today" and "Yesterday" move at midnight, so the labels are built again on the first refresh of a new day,
        // and the view is told when midnight passes, so that refresh also happens when nothing else does.
        var now = clock.GetLocalNow();
        var today = now.Date;
        var relabel = _relabel || today != _labelledDay;
        (_relabel, _labelledDay) = (false, today);
        _midnight?.Dispose();
        _midnight = clock.CreateTimer(_ => DayChanged?.Invoke(), null, today.AddDays(1) - now.DateTime, Timeout.InfiniteTimeSpan);
        try
        {
            var files = await store.LatestAsync(LatestCount, relabel ? null : _newest, cancellationToken);
            if (relabel)
            {
                Items.Clear();
            }
            else
            {
                // Only newer calls are read, so a call whose file was deleted is taken out by name.
                var names = await store.NamesAsync(cancellationToken);
                foreach (var gone in Items.Where(item => !names.Contains(item.File.Name)).ToList())
                {
                    Items.Remove(gone);
                }
            }
            foreach (var file in files.Reverse())
            {
                Items.Insert(0, new(file, DayOf(file.Entry.At.LocalDateTime.Date, today)));
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

    // The call leaves the list at once, instead of when the folder watcher notices.
    public async Task DeleteAsync(HistoryItemViewModel item, CancellationToken cancellationToken)
    {
        await store.DeleteAsync(item.File.Name, cancellationToken);
        Items.Remove(item);
        OnPropertyChanged(nameof(IsFull));
    }

    string DayOf(DateTime day, DateTime today) =>
        day == today ? translator.Of("History.Today")
        : day == today.AddDays(-1) ? translator.Of("History.Yesterday")
        : day.Year == today.Year ? day.ToString("M", translator.Current.Culture)
        : $"{day.ToString("M", translator.Current.Culture)} {day.Year}";
}
