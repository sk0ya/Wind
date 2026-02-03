using System.Windows.Controls;
using Wind.ViewModels;

namespace Wind.Views;

public partial class ProcessInfoPage : UserControl
{
    public ProcessInfoPage(ProcessInfoViewModel viewModel)
    {
        InitializeComponent();
        DataContext = viewModel;
    }
}
