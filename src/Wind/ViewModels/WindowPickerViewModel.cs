using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Windows.Data;
using System.Windows.Threading;
using Wind.Models;
using Wind.Services;

namespace Wind.ViewModels;

public partial class WindowPickerViewModel : ObservableObject
{
    private readonly WindowManager _windowManager;
    private readonly ICollectionView _windowsView;
    private readonly ObservableCollection<WindowInfo> _availableWindows;
    private readonly DispatcherTimer _refreshTimer;

    public ObservableCollection<WindowInfo> AvailableWindows => _availableWindows;

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
        _availableWindows = new ObservableCollection<WindowInfo>();
        _windowsView = CollectionViewSource.GetDefaultView(_availableWindows);
        _windowsView.Filter = FilterWindows;

        _refreshTimer = new DispatcherTimer
        {
            Interval = TimeSpan.FromMilliseconds(500)
        };
        _refreshTimer.Tick += (s, e) => RefreshWindowList();
    }

    public void Start()
    {
        SearchText = string.Empty;
        SelectedWindow = null;
        RefreshWindowList();
        _refreshTimer.Start();
    }

    public void Stop()
    {
        _refreshTimer.Stop();
    }

    private void RefreshWindowList()
    {
        var currentSelection = SelectedWindow?.Handle;
        var windows = _windowManager.EnumerateWindows();

        // 追加されたウィンドウを追加
        foreach (var window in windows)
        {
            if (!_availableWindows.Any(w => w.Handle == window.Handle))
            {
                _availableWindows.Add(window);
            }
        }

        // 削除されたウィンドウを削除
        for (int i = _availableWindows.Count - 1; i >= 0; i--)
        {
            if (!windows.Any(w => w.Handle == _availableWindows[i].Handle))
            {
                _availableWindows.RemoveAt(i);
            }
        }

        // 選択を復元
        if (currentSelection != null)
        {
            SelectedWindow = _availableWindows.FirstOrDefault(w => w.Handle == currentSelection);
        }
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
