using System.Globalization;
using Hoboman.Core.History;

namespace Hoboman.ViewModels;

public sealed class HistoryItemViewModel(HistoryFile file, string day)
{
    HistoryEntry Entry => file.Entry;

    public HistoryFile File => file;

    public string Day => day;

    public string Method => Entry.Request.Method;

    public string Address => Entry.Address;

    public string? Status => Entry.Response?.StatusCode.ToString(CultureInfo.InvariantCulture);

    public bool IsSuccess => Entry.Response?.IsSuccess == true;

    public bool IsCli => Entry.Source == HistorySource.Cli;
}
