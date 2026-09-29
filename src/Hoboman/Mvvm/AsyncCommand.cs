using System.Windows.Input;

namespace Hoboman.Mvvm;

public sealed class AsyncCommand(Func<Task> execute) : ICommand
{
    bool _running;

    public event EventHandler? CanExecuteChanged;

    public bool CanExecute(object? parameter) => !_running;

    public async void Execute(object? parameter)
    {
        _running = true;
        RaiseCanExecuteChanged();
        try
        {
            await execute();
        }
        finally
        {
            _running = false;
            RaiseCanExecuteChanged();
        }
    }

    void RaiseCanExecuteChanged() => CanExecuteChanged?.Invoke(this, EventArgs.Empty);
}
