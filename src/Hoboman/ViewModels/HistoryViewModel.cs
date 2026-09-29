using System.Collections.ObjectModel;
using System.Globalization;
using Hoboman.Core.History;
using Hoboman.Core.Languages;
using Microsoft.Extensions.Logging;

namespace Hoboman.ViewModels;

public sealed class HistoryViewModel(HistoryStore store, Translator translator, ILogger<HistoryViewModel> logger)
{
    const int _shown = 100;

    public ObservableCollection<HistoryItemViewModel> Items { get; } = [];

    public async Task LoadAsync(CancellationToken cancellationToken)
    {
        try
        {
            var entries = await store.LatestAsync(_shown, cancellationToken);
            Items.Clear();
            foreach (var entry in entries)
            {
                Items.Add(new(entry, DayOf(entry.At.LocalDateTime.Date, DateTime.Today)));
            }
        }
        catch (Exception exception) when (FileProblem.Is(exception))
        {
            logger.LogError(exception, "Could not load the history");
        }
    }

    internal string DayOf(DateTime day, DateTime today) =>
        day == today ? translator.Of("History.Today")
        : day == today.AddDays(-1) ? translator.Of("History.Yesterday")
        : day.ToString("d. MMMM yyyy", CultureInfo.CurrentCulture);
}

public sealed class HistoryItemViewModel(HistoryEntry entry, string day)
{
    public HistoryEntry Entry => entry;

    public string Day => day;

    public string Method => entry.Request.Method;

    public string Address => entry.Address;

    public string? Status => entry.Response?.StatusCode.ToString(CultureInfo.InvariantCulture);

    public bool IsSuccess => entry.Response?.StatusCode is >= 200 and < 300;

    public bool IsCli => entry.Source == HistorySource.Cli;
}
