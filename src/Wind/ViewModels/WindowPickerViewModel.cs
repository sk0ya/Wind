using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Windows.Data;
using System.Windows.Threading;
using Wind.Models;
using Wind.Services;

namespace Wind.ViewModels;

public partial class WindowPickerViewModel : ObservableObject
{
    private readonly WindowManager _windowManager;
    private readonly SettingsManager _settingsManager;
    private readonly ICollectionView _windowsView;
    private readonly ObservableCollection<WindowInfo> _availableWindows;
    private readonly DispatcherTimer _refreshTimer;
    private CancellationTokenSource? _launchCts;

    public ObservableCollection<WindowInfo> AvailableWindows => _availableWindows;

    public ICollectionView WindowsView => _windowsView;

    [ObservableProperty]
    private WindowInfo? _selectedWindow;

    [ObservableProperty]
    private string _searchText = string.Empty;

    [ObservableProperty]
    private ObservableCollection<QuickLaunchApp> _quickLaunchApps = new();

    [ObservableProperty]
    private bool _hasQuickLaunchApps;

    [ObservableProperty]
    private bool _isLaunching;

    public event EventHandler<WindowInfo>? WindowSelected;
    public event EventHandler? Cancelled;

    public WindowPickerViewModel(WindowManager windowManager, SettingsManager settingsManager)
    {
        _windowManager = windowManager;
        _settingsManager = settingsManager;
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
        LoadQuickLaunchApps();
        _refreshTimer.Start();
    }

    private void LoadQuickLaunchApps()
    {
        QuickLaunchApps.Clear();
        foreach (var app in _settingsManager.Settings.QuickLaunchApps)
        {
            QuickLaunchApps.Add(app);
        }
        HasQuickLaunchApps = QuickLaunchApps.Count > 0;
    }

    public void Stop()
    {
        _refreshTimer.Stop();
        _launchCts?.Cancel();
        _launchCts = null;
        IsLaunching = false;
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

    [RelayCommand]
    private async Task LaunchQuickApp(QuickLaunchApp? app)
    {
        if (app == null) return;

        _launchCts?.Cancel();
        _launchCts = new CancellationTokenSource();
        var ct = _launchCts.Token;

        try
        {
            IsLaunching = true;

            // Snapshot existing window handles before launch
            var existingHandles = new HashSet<IntPtr>(
                _windowManager.EnumerateWindows().Select(w => w.Handle));

            var isFullPath = Path.IsPathFullyQualified(app.Path);
            ProcessStartInfo startInfo;

            if (isFullPath)
            {
                startInfo = new ProcessStartInfo
                {
                    FileName = app.Path,
                    Arguments = app.Arguments,
                    UseShellExecute = true
                };
            }
            else
            {
                // PATH-based command (e.g. "code", "wt") — run via cmd /c to resolve PATH
                // without showing a console window
                var args = string.IsNullOrEmpty(app.Arguments)
                    ? $"/c \"{app.Path}\""
                    : $"/c \"{app.Path}\" {app.Arguments}";
                startInfo = new ProcessStartInfo
                {
                    FileName = "cmd.exe",
                    Arguments = args,
                    UseShellExecute = false,
                    CreateNoWindow = true
                };
            }

            Process.Start(startInfo);

            // Poll for a new window that didn't exist before
            WindowInfo? newWindow = null;
            for (int i = 0; i < 100; i++)
            {
                await Task.Delay(100, ct);
                var current = _windowManager.EnumerateWindows();
                newWindow = current.FirstOrDefault(w => !existingHandles.Contains(w.Handle));
                if (newWindow != null)
                    break;
            }

            IsLaunching = false;

            if (newWindow == null) return;

            RefreshWindowList();
            var windowInfo = _availableWindows.FirstOrDefault(w => w.Handle == newWindow.Handle);
            if (windowInfo != null)
            {
                WindowSelected?.Invoke(this, windowInfo);
            }
        }
        catch (OperationCanceledException)
        {
            // Cancelled by user (e.g. Cancel button or new launch)
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"Failed to launch quick app {app.Name}: {ex.Message}");
        }
        finally
        {
            IsLaunching = false;
        }
    }
}
