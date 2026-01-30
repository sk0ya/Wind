using System.Windows.Controls;
using Wind.ViewModels;

namespace Wind.Views;

public partial class SettingsPage : UserControl
{
    public SettingsPage(SettingsViewModel viewModel)
    {
        InitializeComponent();
        DataContext = viewModel;
    }
}
