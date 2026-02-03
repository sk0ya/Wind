using System.Windows.Controls;
using Wind.ViewModels;

namespace Wind.Views;

public partial class StartupSettingsPage : UserControl
{
    public StartupSettingsPage(StartupSettingsViewModel viewModel)
    {
        InitializeComponent();
        DataContext = viewModel;
    }
}
