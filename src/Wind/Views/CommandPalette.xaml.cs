using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Threading;
using Wind.ViewModels;

namespace Wind.Views;

public partial class CommandPalette : UserControl
{
    public CommandPalette()
    {
        InitializeComponent();
    }

    public void FocusSearch()
    {
        Dispatcher.BeginInvoke(DispatcherPriority.Input, () =>
        {
            SearchBox.Focus();
            Keyboard.Focus(SearchBox);

            // Wpf.Ui TextBox wraps an inner TextBox; try to focus it directly
            var inner = FindVisualChild<System.Windows.Controls.TextBox>(SearchBox);
            if (inner != null)
            {
                inner.Focus();
                Keyboard.Focus(inner);
                inner.SelectAll();
            }
        });
    }

    private void SearchBox_PreviewKeyDown(object sender, KeyEventArgs e)
    {
        if (DataContext is not CommandPaletteViewModel vm) return;

        switch (e.Key)
        {
            case Key.Down:
                vm.MoveSelectionDown();
                ScrollToSelected();
                e.Handled = true;
                break;
            case Key.Up:
                vm.MoveSelectionUp();
                ScrollToSelected();
                e.Handled = true;
                break;
            case Key.Enter:
                vm.ExecuteCommand.Execute(null);
                e.Handled = true;
                break;
            case Key.Escape:
                vm.CancelCommand.Execute(null);
                e.Handled = true;
                break;
        }
    }

    private void ScrollToSelected()
    {
        if (ResultsList.SelectedItem != null)
            ResultsList.ScrollIntoView(ResultsList.SelectedItem);
    }

    private void ListBox_MouseUp(object sender, MouseButtonEventArgs e)
    {
        if (DataContext is CommandPaletteViewModel vm && ResultsList.SelectedItem != null)
            vm.ExecuteCommand.Execute(null);
    }

    private static T? FindVisualChild<T>(DependencyObject parent) where T : DependencyObject
    {
        for (int i = 0; i < VisualTreeHelper.GetChildrenCount(parent); i++)
        {
            var child = VisualTreeHelper.GetChild(parent, i);
            if (child is T found)
                return found;
            var result = FindVisualChild<T>(child);
            if (result != null)
                return result;
        }
        return null;
    }
}
