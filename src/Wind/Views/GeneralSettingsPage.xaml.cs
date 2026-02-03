using System.Windows.Controls;
using Wind.ViewModels;

namespace Wind.Views;

public partial class GeneralSettingsPage : UserControl
{
    public GeneralSettingsPage(GeneralSettingsViewModel viewModel)
    {
        InitializeComponent();
        DataContext = viewModel;
    }
}
