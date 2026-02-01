using System.Windows.Controls;
using System.Windows.Input;
using Wind.ViewModels;

namespace Wind.Views;

public partial class SettingsPage : UserControl
{
    public SettingsPage(SettingsViewModel viewModel)
    {
        InitializeComponent();
        DataContext = viewModel;
    }

    private void QuickLaunchPath_PreviewKeyDown(object sender, KeyEventArgs e)
    {
        if (DataContext is not SettingsViewModel vm) return;

        if (vm.IsSuggestionsOpen)
        {
            if (e.Key == Key.Down)
            {
                SuggestionsList.SelectedIndex = Math.Min(
                    SuggestionsList.SelectedIndex + 1,
                    SuggestionsList.Items.Count - 1);
                SuggestionsList.ScrollIntoView(SuggestionsList.SelectedItem);
                e.Handled = true;
                return;
            }
            if (e.Key == Key.Up)
            {
                SuggestionsList.SelectedIndex = Math.Max(
                    SuggestionsList.SelectedIndex - 1, 0);
                SuggestionsList.ScrollIntoView(SuggestionsList.SelectedItem);
                e.Handled = true;
                return;
            }
            if (e.Key == Key.Tab && SuggestionsList.SelectedItem is string selected)
            {
                vm.ApplySuggestion(selected);
                QuickLaunchPathBox.CaretIndex = vm.NewQuickLaunchPath.Length;
                e.Handled = true;
                return;
            }
            if (e.Key == Key.Escape)
            {
                vm.IsSuggestionsOpen = false;
                e.Handled = true;
                return;
            }
        }

        if (e.Key == Key.Enter)
        {
            vm.AddQuickLaunchAppCommand.Execute(null);
            e.Handled = true;
        }
    }

    private void Suggestions_PreviewMouseLeftButtonUp(object sender, MouseButtonEventArgs e)
    {
        if (SuggestionsList.SelectedItem is string selected
            && DataContext is SettingsViewModel vm)
        {
            vm.ApplySuggestion(selected);
            QuickLaunchPathBox.Focus();
            QuickLaunchPathBox.CaretIndex = vm.NewQuickLaunchPath.Length;
        }
    }

    private void HotkeyButton_PreviewKeyDown(object sender, KeyEventArgs e)
    {
        if (DataContext is not SettingsViewModel vm) return;
        if (vm.RecordingHotkey is null) return;

        // System キー（Alt 押下中）の場合は SystemKey を使用
        var key = e.Key == Key.System ? e.SystemKey : e.Key;
        var modifiers = Keyboard.Modifiers;

        if (vm.ApplyRecordedKey(modifiers, key))
        {
            e.Handled = true;
        }
    }
}
