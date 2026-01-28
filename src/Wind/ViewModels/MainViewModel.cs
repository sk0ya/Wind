using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using System.Collections.ObjectModel;
using System.Diagnostics;
using System.Windows.Media;
using Wind.Interop;
using Wind.Models;
using Wind.Services;

namespace Wind.ViewModels;

public partial class MainViewModel : ObservableObject
{
    private readonly WindowManager _windowManager;
    private readonly TabManager _tabManager;
    private readonly SessionManager _sessionManager;
    private readonly HotkeyManager _hotkeyManager;
    private readonly WindowPickerViewModel _windowPickerViewModel;

    public ObservableCollection<TabItem> Tabs => _tabManager.Tabs;
    public ObservableCollection<TabGroup> Groups => _tabManager.Groups;
    public ObservableCollection<WindowInfo> AvailableWindows => _windowManager.AvailableWindows;

    [ObservableProperty]
    private TabItem? _selectedTab;

    [ObservableProperty]
    private WindowHost? _currentWindowHost;

    [ObservableProperty]
    private bool _isWindowPickerOpen;

    [ObservableProperty]
    private string _statusMessage = "Ready";

    public MainViewModel(
        WindowManager windowManager,
        TabManager tabManager,
        SessionManager sessionManager,
        HotkeyManager hotkeyManager,
        WindowPickerViewModel windowPickerViewModel)
    {
        _windowManager = windowManager;
        _tabManager = tabManager;
        _sessionManager = sessionManager;
        _hotkeyManager = hotkeyManager;
        _windowPickerViewModel = windowPickerViewModel;

        _tabManager.ActiveTabChanged += OnActiveTabChanged;
        _hotkeyManager.HotkeyPressed += OnHotkeyPressed;
    }

    private void OnActiveTabChanged(object? sender, TabItem? tab)
    {
        SelectedTab = tab;
        CurrentWindowHost = tab != null ? _tabManager.GetWindowHost(tab) : null;
    }

    private void OnHotkeyPressed(object? sender, HotkeyBinding binding)
    {
        switch (binding.Action)
        {
            case HotkeyAction.NextTab:
                _tabManager.SelectNextTab();
                break;
            case HotkeyAction.PreviousTab:
                _tabManager.SelectPreviousTab();
                break;
            case HotkeyAction.CloseTab:
                if (SelectedTab != null)
                    CloseTab(SelectedTab);
                break;
            case HotkeyAction.NewTab:
                OpenWindowPicker();
                break;
            case HotkeyAction.SwitchToTab1:
            case HotkeyAction.SwitchToTab2:
            case HotkeyAction.SwitchToTab3:
            case HotkeyAction.SwitchToTab4:
            case HotkeyAction.SwitchToTab5:
            case HotkeyAction.SwitchToTab6:
            case HotkeyAction.SwitchToTab7:
            case HotkeyAction.SwitchToTab8:
            case HotkeyAction.SwitchToTab9:
                int index = binding.Action - HotkeyAction.SwitchToTab1;
                _tabManager.SelectTab(index);
                break;
        }
    }

    [RelayCommand]
    private void SelectTab(TabItem tab)
    {
        _tabManager.ActiveTab = tab;
    }

    [RelayCommand]
    private void CloseTab(TabItem tab)
    {
        _tabManager.RemoveTab(tab);
        StatusMessage = $"Closed: {tab.Title}";
    }

    [RelayCommand]
    private void OpenWindowPicker()
    {
        _windowPickerViewModel.Start();
        IsWindowPickerOpen = true;
    }

    [RelayCommand]
    private void CloseWindowPicker()
    {
        _windowPickerViewModel.Stop();
        IsWindowPickerOpen = false;
    }

    [RelayCommand]
    private void AddWindow(WindowInfo? windowInfo)
    {
        if (windowInfo == null) return;

        var tab = _tabManager.AddTab(windowInfo);
        if (tab != null)
        {
            StatusMessage = $"Added: {tab.Title}";
            _windowPickerViewModel.Stop();
            IsWindowPickerOpen = false;
        }
        else
        {
            StatusMessage = "Failed to add window";
        }
    }

    [RelayCommand]
    private void RefreshWindows()
    {
        _windowManager.RefreshWindowList();
    }

    [RelayCommand]
    private void CreateGroup()
    {
        var colors = new[] { Colors.CornflowerBlue, Colors.Coral, Colors.MediumSeaGreen, Colors.MediumPurple, Colors.Goldenrod };
        var color = colors[Groups.Count % colors.Length];
        var group = _tabManager.CreateGroup($"Group {Groups.Count + 1}", color);
        StatusMessage = $"Created: {group.Name}";
    }

    [RelayCommand]
    private void DeleteGroup(TabGroup group)
    {
        _tabManager.DeleteGroup(group);
        StatusMessage = $"Deleted: {group.Name}";
    }

    // Non-command methods for multi-parameter operations
    public void AddTabToGroup(TabItem tab, TabGroup group)
    {
        _tabManager.AddTabToGroup(tab, group);
    }

    public void RemoveTabFromGroup(TabItem tab)
    {
        _tabManager.RemoveTabFromGroup(tab);
    }

    [RelayCommand]
    private async Task SaveSession()
    {
        await _sessionManager.SaveSessionAsync(_tabManager);
        StatusMessage = "Session saved";
    }

    [RelayCommand]
    private async Task RestoreSession()
    {
        await _sessionManager.RestoreSessionAsync(_tabManager, _windowManager);
        StatusMessage = "Session restored";
    }

    [RelayCommand]
    private void ReleaseAllWindows()
    {
        _tabManager.ReleaseAllTabs();
        StatusMessage = "All windows released";
    }

    public void Cleanup()
    {
        _tabManager.ActiveTabChanged -= OnActiveTabChanged;
        _hotkeyManager.HotkeyPressed -= OnHotkeyPressed;
        _tabManager.ReleaseAllTabs();
        _hotkeyManager.Dispose();
    }

    public async Task EmbedStartupProcessesAsync(List<Process> processes)
    {
        if (processes.Count == 0) return;

        // Wait for windows to be created
        await Task.Delay(1500);

        foreach (var process in processes)
        {
            try
            {
                if (process.HasExited) continue;

                // Wait for main window handle
                for (int i = 0; i < 20; i++)
                {
                    process.Refresh();
                    if (process.MainWindowHandle != IntPtr.Zero)
                        break;
                    await Task.Delay(250);
                }

                if (process.MainWindowHandle == IntPtr.Zero) continue;

                // Find the window info
                _windowManager.RefreshWindowList();
                var windowInfo = _windowManager.AvailableWindows
                    .FirstOrDefault(w => w.Handle == process.MainWindowHandle);

                if (windowInfo != null)
                {
                    var tab = _tabManager.AddTab(windowInfo);
                    if (tab != null)
                    {
                        StatusMessage = $"Added: {tab.Title}";
                    }
                }
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"Failed to embed process: {ex.Message}");
            }
        }
    }
}
