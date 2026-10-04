using System.Windows;
using System.Windows.Input;

namespace Hoboman.Views;

public class DialogWindow : Window
{
    bool _saving;

    public DialogWindow()
    {
        SetResourceReference(StyleProperty, "Dialog");
        Owner = Application.Current.MainWindow;
        // A table in a dialog keeps its divider with the main window's layout.
        if (Owner is { } owner)
        {
            SplitMemory.SetSplits(this, SplitMemory.GetSplits(owner));
        }
        WindowStartupLocation = WindowStartupLocation.CenterOwner;
        CommandBindings.Add(new CommandBinding(SystemCommands.CloseWindowCommand, (_, _) => Close()));
        InputBindings.Add(new KeyBinding(SystemCommands.CloseWindowCommand, Key.Escape, ModifierKeys.None));
    }

    // A second click while saving, or closing the window meanwhile, must not set the result of a window that is gone.
    protected async Task SaveAndCloseAsync(Func<Task<bool>> save)
    {
        if (_saving)
        {
            return;
        }
        _saving = true;
        try
        {
            if (await save() && IsVisible)
            {
                DialogResult = true;
            }
        }
        finally
        {
            _saving = false;
        }
    }

    protected override void OnMouseLeftButtonDown(MouseButtonEventArgs e)
    {
        base.OnMouseLeftButtonDown(e);
        DragMove();
    }
}
