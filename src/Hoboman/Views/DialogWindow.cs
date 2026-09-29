using System.Windows;
using System.Windows.Input;

namespace Hoboman.Views;

// Every dialog shares the look, the title bar and the close button from the Dialog style, and can be dragged anywhere.
public class DialogWindow : Window
{
    public DialogWindow()
    {
        SetResourceReference(StyleProperty, "Dialog");
        Owner = Application.Current.MainWindow;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;
        CommandBindings.Add(new CommandBinding(SystemCommands.CloseWindowCommand, (_, _) => Close()));
        InputBindings.Add(new KeyBinding(SystemCommands.CloseWindowCommand, Key.Escape, ModifierKeys.None));
    }

    protected override void OnMouseLeftButtonDown(MouseButtonEventArgs e)
    {
        base.OnMouseLeftButtonDown(e);
        DragMove();
    }
}
