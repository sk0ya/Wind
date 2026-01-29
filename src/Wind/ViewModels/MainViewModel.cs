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
    private readonly HotkeyManager _hotkeyManager;
    private readonly WindowPickerViewModel _windowPickerViewModel;
    private readonly SettingsManager _settingsManager;

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

    [ObservableProperty]
    private TileLayout? _currentTileLayout;

    /// <summary>
    /// True when a tile layout exists (may or may not be visible).
    /// </summary>
    [ObservableProperty]
    private bool _isTiled;

    /// <summary>
    /// True when the tile view is currently shown (active tab is a tiled tab).
    /// </summary>
    [ObservableProperty]
    private bool _isTileVisible;

    public MainViewModel(
        WindowManager windowManager,
        TabManager tabManager,
        HotkeyManager hotkeyManager,
        WindowPickerViewModel windowPickerViewModel,
        SettingsManager settingsManager)
    {
        _windowManager = windowManager;
        _tabManager = tabManager;
        _hotkeyManager = hotkeyManager;
        _windowPickerViewModel = windowPickerViewModel;
        _settingsManager = settingsManager;

        _tabManager.ActiveTabChanged += OnActiveTabChanged;
        _tabManager.TileLayoutChanged += OnTileLayoutChanged;
        _hotkeyManager.HotkeyPressed += OnHotkeyPressed;
    }

    private void OnActiveTabChanged(object? sender, TabItem? tab)
    {
        SelectedTab = tab;

        if (IsTiled && tab != null && tab.IsTiled)
        {
            // Clicked a tiled tab — show tile view
            IsTileVisible = true;
            CurrentWindowHost = null;
        }
        else
        {
            // Clicked a non-tiled tab or no tile layout — show single view
            IsTileVisible = false;
            CurrentWindowHost = tab != null ? _tabManager.GetWindowHost(tab) : null;
        }
    }

    private void OnTileLayoutChanged(object? sender, TileLayout? layout)
    {
        CurrentTileLayout = layout;
        IsTiled = layout?.IsActive == true;
        IsTileVisible = layout?.IsActive == true;
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

    [RelayCommand]
    private void ToggleMultiSelect(TabItem tab)
    {
        _tabManager.ToggleMultiSelect(tab);
    }

    [RelayCommand]
    private void TileSelectedTabs()
    {
        var selectedTabs = _tabManager.GetMultiSelectedTabs();
        if (selectedTabs.Count < 2)
        {
            StatusMessage = "Select 2 or more tabs to tile";
            return;
        }

        _tabManager.StartTile(selectedTabs);
        StatusMessage = $"Tiled {selectedTabs.Count} tabs";
    }

    [RelayCommand]
    private void StopTile()
    {
        _tabManager.StopTile();
        IsTileVisible = false;
        // Restore single tab view
        if (SelectedTab != null)
        {
            CurrentWindowHost = _tabManager.GetWindowHost(SelectedTab);
        }
        StatusMessage = "Tile layout stopped";
    }

    // Non-command methods for multi-parameter operations
    public WindowHost? GetWindowHost(TabItem tab)
    {
        return _tabManager.GetWindowHost(tab);
    }

    public void AddTabToGroup(TabItem tab, TabGroup group)
    {
        _tabManager.AddTabToGroup(tab, group);
    }

    public void RemoveTabFromGroup(TabItem tab)
    {
        _tabManager.RemoveTabFromGroup(tab);
    }

    [RelayCommand]
    private void ReleaseAllWindows()
    {
        _tabManager.ReleaseAllTabs();
        StatusMessage = "All windows released";
    }

    public void Cleanup()
    {
        // Stop the window picker timer first to prevent UI updates during cleanup
        _windowPickerViewModel.Stop();

        _tabManager.ActiveTabChanged -= OnActiveTabChanged;
        _tabManager.TileLayoutChanged -= OnTileLayoutChanged;
        _hotkeyManager.HotkeyPressed -= OnHotkeyPressed;

        switch (_settingsManager.Settings.CloseWindowsOnExit)
        {
            case "All":
                _tabManager.CloseAllTabs();
                break;
            case "StartupOnly":
                _tabManager.CloseStartupTabs();
                break;
            default:
                _tabManager.ReleaseAllTabs();
                break;
        }

        _hotkeyManager.Dispose();
    }

    public async Task EmbedStartupProcessesAsync(
        List<(Process Process, StartupApplication Config)> processConfigs,
        AppSettings settings)
    {
        if (processConfigs.Count == 0) return;

        // Wait for windows to be created
        await Task.Delay(1500);

        // Embed each process and track the mapping from config to tab
        var configTabPairs = new List<(StartupApplication Config, TabItem Tab)>();

        foreach (var (process, config) in processConfigs)
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
                        tab.IsLaunchedAtStartup = true;
                        StatusMessage = $"Added: {tab.Title}";
                        configTabPairs.Add((config, tab));
                    }
                }
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"Failed to embed process: {ex.Message}");
            }
        }

        // Apply groups from settings
        ApplyStartupGroups(configTabPairs, settings);

        // Apply tile layout from settings
        ApplyStartupTile(configTabPairs);
    }

    private void ApplyStartupGroups(
        List<(StartupApplication Config, TabItem Tab)> configTabPairs,
        AppSettings settings)
    {
        // Build a lookup of group definitions
        var groupDefs = settings.StartupGroups
            .Where(g => !string.IsNullOrEmpty(g.Name))
            .ToDictionary(g => g.Name, StringComparer.OrdinalIgnoreCase);

        // Collect which group names are actually used
        var usedGroupNames = configTabPairs
            .Where(p => !string.IsNullOrEmpty(p.Config.Group))
            .Select(p => p.Config.Group!)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();

        // Create TabGroup objects for each used group name
        var createdGroups = new Dictionary<string, TabGroup>(StringComparer.OrdinalIgnoreCase);
        foreach (var groupName in usedGroupNames)
        {
            var color = Colors.CornflowerBlue;
            if (groupDefs.TryGetValue(groupName, out var def))
            {
                color = TryParseColor(def.Color) ?? Colors.CornflowerBlue;
            }

            var group = _tabManager.CreateGroup(groupName, color);
            createdGroups[groupName] = group;
        }

        // Assign tabs to groups
        foreach (var (config, tab) in configTabPairs)
        {
            if (!string.IsNullOrEmpty(config.Group) && createdGroups.TryGetValue(config.Group, out var group))
            {
                _tabManager.AddTabToGroup(tab, group);
            }
        }
    }

    private void ApplyStartupTile(List<(StartupApplication Config, TabItem Tab)> configTabPairs)
    {
        // Group tabs by tile name
        var tileGroups = configTabPairs
            .Where(p => !string.IsNullOrEmpty(p.Config.Tile))
            .GroupBy(p => p.Config.Tile!, StringComparer.OrdinalIgnoreCase)
            .Where(g => g.Count() >= 2);

        foreach (var tileGroup in tileGroups)
        {
            // Sort by TilePosition, then by original order
            var orderedTabs = tileGroup
                .OrderBy(p => p.Config.TilePosition ?? int.MaxValue)
                .Select(p => p.Tab)
                .ToList();

            _tabManager.StartTile(orderedTabs);
            StatusMessage = $"Tiled {orderedTabs.Count} tabs";
            break; // Only one tile layout can be active at a time
        }
    }

    private static Color? TryParseColor(string colorString)
    {
        try
        {
            return (Color)ColorConverter.ConvertFromString(colorString);
        }
        catch
        {
            return null;
        }
    }
}
