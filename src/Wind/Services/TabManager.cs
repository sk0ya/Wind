using System.Collections.ObjectModel;
using System.Windows.Media;
using System.Windows.Threading;
using Wind.Interop;
using Wind.Models;

namespace Wind.Services;

public class TabManager
{
    private readonly WindowManager _windowManager;
    private readonly Dictionary<Guid, WindowHost> _windowHosts = new();
    private readonly Dispatcher _dispatcher;

    public ObservableCollection<TabItem> Tabs { get; } = new();
    public ObservableCollection<TabGroup> Groups { get; } = new();

    private TabItem? _activeTab;
    public TabItem? ActiveTab
    {
        get => _activeTab;
        set
        {
            if (_activeTab != value)
            {
                if (_activeTab != null)
                    _activeTab.IsSelected = false;

                _activeTab = value;

                if (_activeTab != null)
                    _activeTab.IsSelected = true;

                ActiveTabChanged?.Invoke(this, _activeTab);
            }
        }
    }

    private TileLayout? _currentTileLayout;
    public TileLayout? CurrentTileLayout
    {
        get => _currentTileLayout;
        private set
        {
            _currentTileLayout = value;
            TileLayoutChanged?.Invoke(this, value);
        }
    }

    public event EventHandler<TabItem?>? ActiveTabChanged;
    public event EventHandler<TabItem>? TabAdded;
    public event EventHandler<TabItem>? TabRemoved;
    public event EventHandler<TileLayout?>? TileLayoutChanged;
    public event EventHandler? TileLayoutUpdated;
    public event EventHandler? MinimizeRequested;
    public event EventHandler? MaximizeRequested;

    public TabManager(WindowManager windowManager)
    {
        _windowManager = windowManager;
        _dispatcher = Dispatcher.CurrentDispatcher;
    }

    public TabItem? AddTab(WindowInfo windowInfo, bool activate = true)
    {
        if (windowInfo.Handle == IntPtr.Zero) return null;

        // Check if already added
        var existingTab = Tabs.FirstOrDefault(t => t.Window?.Handle == windowInfo.Handle);
        if (existingTab != null)
        {
            if (activate)
                ActiveTab = existingTab;
            return existingTab;
        }

        var host = _windowManager.EmbedWindow(windowInfo.Handle);
        if (host == null) return null;

        var tab = new TabItem(windowInfo);
        _windowHosts[tab.Id] = host;

        // Subscribe to hosted window events
        host.HostedWindowClosed += (s, e) => OnHostedWindowClosed(tab);
        host.MinimizeRequested += (s, e) => MinimizeRequested?.Invoke(this, EventArgs.Empty);
        host.MaximizeRequested += (s, e) => MaximizeRequested?.Invoke(this, EventArgs.Empty);

        Tabs.Add(tab);

        TabAdded?.Invoke(this, tab);

        if (activate)
            ActiveTab = tab;

        return tab;
    }

    public TabItem AddContentTab(string title, string contentKey, bool activate = true)
    {
        // Check if already added
        var existingTab = Tabs.FirstOrDefault(t => t.ContentKey == contentKey);
        if (existingTab != null)
        {
            if (activate)
                ActiveTab = existingTab;
            return existingTab;
        }

        var tab = new TabItem { ContentKey = contentKey };
        tab.Title = title;

        Tabs.Add(tab);
        TabAdded?.Invoke(this, tab);

        if (activate)
            ActiveTab = tab;

        return tab;
    }

    private void OnHostedWindowClosed(TabItem tab)
    {
        // Ensure we're on the UI thread
        if (!_dispatcher.CheckAccess())
        {
            _dispatcher.BeginInvoke(() => OnHostedWindowClosed(tab));
            return;
        }

        // Update tile layout if this tab was tiled
        UpdateTileForRemovedTab(tab);

        // Remove the tab without trying to release the window (it's already closed)
        if (_windowHosts.TryGetValue(tab.Id, out var host))
        {
            _windowHosts.Remove(tab.Id);
        }

        tab.Group?.RemoveTab(tab);

        var index = Tabs.IndexOf(tab);
        if (index < 0) return;

        Tabs.Remove(tab);
        TabRemoved?.Invoke(this, tab);

        // Select adjacent tab
        if (ActiveTab == tab)
        {
            if (Tabs.Count > 0)
            {
                var newIndex = Math.Min(index, Tabs.Count - 1);
                ActiveTab = Tabs[newIndex];
            }
            else
            {
                ActiveTab = null;
            }
        }
    }

    public void RemoveTab(TabItem tab)
    {
        // Update tile layout if this tab was tiled
        UpdateTileForRemovedTab(tab);

        if (!tab.IsContentTab && _windowHosts.TryGetValue(tab.Id, out var host))
        {
            _windowManager.ReleaseWindow(host);
            _windowHosts.Remove(tab.Id);
        }

        // Remove from group if in one
        tab.Group?.RemoveTab(tab);

        var index = Tabs.IndexOf(tab);
        Tabs.Remove(tab);

        TabRemoved?.Invoke(this, tab);

        // Select adjacent tab
        if (ActiveTab == tab)
        {
            if (Tabs.Count > 0)
            {
                var newIndex = Math.Min(index, Tabs.Count - 1);
                ActiveTab = Tabs[newIndex];
            }
            else
            {
                ActiveTab = null;
            }
        }
    }

    public void CloseTab(TabItem tab)
    {
        if (tab.Window?.Handle != IntPtr.Zero)
        {
            // Send close message to the original window
            NativeMethods.SendMessage(tab.Window!.Handle, NativeMethods.WM_CLOSE, IntPtr.Zero, IntPtr.Zero);
        }
        RemoveTab(tab);
    }

    public WindowHost? GetWindowHost(TabItem tab)
    {
        return _windowHosts.TryGetValue(tab.Id, out var host) ? host : null;
    }

    public void SelectTab(int index)
    {
        if (index >= 0 && index < Tabs.Count)
        {
            ActiveTab = Tabs[index];
        }
    }

    public void SelectNextTab()
    {
        if (Tabs.Count == 0 || ActiveTab == null) return;

        var index = Tabs.IndexOf(ActiveTab);
        var nextIndex = (index + 1) % Tabs.Count;
        ActiveTab = Tabs[nextIndex];
    }

    public void SelectPreviousTab()
    {
        if (Tabs.Count == 0 || ActiveTab == null) return;

        var index = Tabs.IndexOf(ActiveTab);
        var prevIndex = (index - 1 + Tabs.Count) % Tabs.Count;
        ActiveTab = Tabs[prevIndex];
    }

    public void MoveTab(TabItem tab, int newIndex)
    {
        var oldIndex = Tabs.IndexOf(tab);
        if (oldIndex < 0 || oldIndex == newIndex) return;

        Tabs.Move(oldIndex, newIndex);
    }

    public TabGroup CreateGroup(string name, Color color)
    {
        var group = new TabGroup(name, color);
        Groups.Add(group);
        return group;
    }

    public void AddTabToGroup(TabItem tab, TabGroup group)
    {
        // Remove from existing group if any
        tab.Group?.RemoveTab(tab);

        group.AddTab(tab);
    }

    public void RemoveTabFromGroup(TabItem tab)
    {
        tab.Group?.RemoveTab(tab);
    }

    public void DeleteGroup(TabGroup group)
    {
        // Move all tabs out of the group
        foreach (var tab in group.Tabs.ToList())
        {
            tab.Group = null;
        }
        group.Tabs.Clear();
        Groups.Remove(group);
    }

    public void CleanupInvalidTabs()
    {
        var invalidTabs = Tabs.Where(t =>
            !t.IsContentTab &&
            (t.Window?.Handle == IntPtr.Zero ||
            !_windowManager.IsWindowValid(t.Window!.Handle))).ToList();

        foreach (var tab in invalidTabs)
        {
            RemoveTab(tab);
        }
    }

    public void CloseStartupTabs()
    {
        foreach (var tab in Tabs.ToList())
        {
            if (tab.IsContentTab) continue;

            if (_windowHosts.TryGetValue(tab.Id, out var host))
            {
                if (tab.IsLaunchedAtStartup)
                {
                    if (tab.Window?.Handle != IntPtr.Zero)
                    {
                        NativeMethods.SendMessage(tab.Window!.Handle, NativeMethods.WM_CLOSE, IntPtr.Zero, IntPtr.Zero);
                    }
                }
                else
                {
                    _windowManager.ReleaseWindow(host);
                }
                _windowHosts.Remove(tab.Id);
            }
        }
        Tabs.Clear();
        ActiveTab = null;
    }

    public void CloseAllTabs()
    {
        foreach (var tab in Tabs.ToList())
        {
            if (tab.IsContentTab) continue;

            if (tab.Window?.Handle != IntPtr.Zero)
            {
                NativeMethods.SendMessage(tab.Window!.Handle, NativeMethods.WM_CLOSE, IntPtr.Zero, IntPtr.Zero);
            }
            if (_windowHosts.TryGetValue(tab.Id, out var host))
            {
                _windowHosts.Remove(tab.Id);
            }
        }
        Tabs.Clear();
        ActiveTab = null;
    }

    public void ReleaseAllTabs()
    {
        // First, release all window hosts before modifying the collection.
        // This prevents UI binding updates from triggering DestroyWindowCore
        // on WindowHost objects before SetParent has detached the hosted windows.
        foreach (var tab in Tabs.ToList())
        {
            if (tab.IsContentTab) continue;

            if (_windowHosts.TryGetValue(tab.Id, out var host))
            {
                _windowManager.ReleaseWindow(host);
                _windowHosts.Remove(tab.Id);
            }
        }

        // Now safe to clear the collection and update UI
        Tabs.Clear();
        ActiveTab = null;
    }

    public void ToggleMultiSelect(TabItem tab)
    {
        tab.IsMultiSelected = !tab.IsMultiSelected;
    }

    public void ClearMultiSelection()
    {
        foreach (var tab in Tabs)
        {
            tab.IsMultiSelected = false;
        }
    }

    public IReadOnlyList<TabItem> GetMultiSelectedTabs()
    {
        return Tabs.Where(t => t.IsMultiSelected).ToList();
    }

    public void StartTile(IEnumerable<TabItem> tabs)
    {
        // Stop existing tile if any
        StopTile();

        var tabList = tabs.ToList();
        if (tabList.Count < 2) return;

        CurrentTileLayout = new TileLayout(tabList);
        ClearMultiSelection();
    }

    public void StopTile()
    {
        if (CurrentTileLayout == null) return;

        CurrentTileLayout.Deactivate();
        CurrentTileLayout = null;
    }

    private void UpdateTileForRemovedTab(TabItem tab)
    {
        if (CurrentTileLayout == null || !tab.IsTiled) return;

        var hasEnoughTabs = CurrentTileLayout.RemoveTab(tab);
        if (hasEnoughTabs)
        {
            // Rebuild the tile layout with remaining tabs
            TileLayoutUpdated?.Invoke(this, EventArgs.Empty);
        }
        else
        {
            // Not enough tabs to tile, stop tiling
            StopTile();
        }
    }
}
