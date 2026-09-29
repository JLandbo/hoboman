using System.Globalization;
using Hoboman.Core.History;

namespace Hoboman.ViewModels;

public sealed class HistoryItemViewModel(HistoryEntry entry, string day)
{
    public HistoryEntry Entry => entry;

    public string Day => day;

    public string Method => entry.Request.Method;

    public string Address => entry.Address;

    public string? Status => entry.Response?.StatusCode.ToString(CultureInfo.InvariantCulture);

    public bool IsSuccess => entry.Response?.IsSuccess == true;

    public bool IsCli => entry.Source == HistorySource.Cli;
}
