using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Windows.Data;
using Wind.Models;
using Wind.Services;

namespace Wind.ViewModels;

public partial class WindowPickerViewModel : ObservableObject
{
    private readonly WindowManager _windowManager;
    private readonly ICollectionView _windowsView;

    public ObservableCollection<WindowInfo> AvailableWindows => _windowManager.AvailableWindows;

    public ICollectionView WindowsView => _windowsView;

    [ObservableProperty]
    private WindowInfo? _selectedWindow;

    [ObservableProperty]
    private string _searchText = string.Empty;

    public event EventHandler<WindowInfo>? WindowSelected;
    public event EventHandler? Cancelled;

    public WindowPickerViewModel(WindowManager windowManager)
    {
        _windowManager = windowManager;
        _windowsView = CollectionViewSource.GetDefaultView(AvailableWindows);
        _windowsView.Filter = FilterWindows;
    }

    partial void OnSearchTextChanged(string value)
    {
        _windowsView.Refresh();
    }

    private bool FilterWindows(object obj)
    {
        if (obj is not WindowInfo window) return false;
        if (string.IsNullOrWhiteSpace(SearchText)) return true;

        return window.Title.Contains(SearchText, StringComparison.OrdinalIgnoreCase) ||
               window.ProcessName.Contains(SearchText, StringComparison.OrdinalIgnoreCase);
    }

    [RelayCommand]
    private void Refresh()
    {
        _windowManager.RefreshWindowList();
        _windowsView.Refresh();
    }

    [RelayCommand]
    private void Select()
    {
        if (SelectedWindow != null)
        {
            WindowSelected?.Invoke(this, SelectedWindow);
        }
    }

    [RelayCommand]
    private void Cancel()
    {
        Cancelled?.Invoke(this, EventArgs.Empty);
    }

    [RelayCommand]
    private void SelectWindow(WindowInfo? window)
    {
        if (window != null)
        {
            WindowSelected?.Invoke(this, window);
        }
    }
}
