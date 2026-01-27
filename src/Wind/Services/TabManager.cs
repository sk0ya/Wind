using System.Collections.ObjectModel;
using System.Windows.Media;
using Wind.Interop;
using Wind.Models;

namespace Wind.Services;

public class TabManager
{
    private readonly WindowManager _windowManager;
    private readonly Dictionary<Guid, WindowHost> _windowHosts = new();

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

    public event EventHandler<TabItem?>? ActiveTabChanged;
    public event EventHandler<TabItem>? TabAdded;
    public event EventHandler<TabItem>? TabRemoved;

    public TabManager(WindowManager windowManager)
    {
        _windowManager = windowManager;
    }

    public TabItem? AddTab(WindowInfo windowInfo)
    {
        if (windowInfo.Handle == IntPtr.Zero) return null;

        // Check if already added
        var existingTab = Tabs.FirstOrDefault(t => t.Window?.Handle == windowInfo.Handle);
        if (existingTab != null)
        {
            ActiveTab = existingTab;
            return existingTab;
        }

        var host = _windowManager.EmbedWindow(windowInfo.Handle);
        if (host == null) return null;

        var tab = new TabItem(windowInfo);
        _windowHosts[tab.Id] = host;
        Tabs.Add(tab);

        TabAdded?.Invoke(this, tab);

        if (ActiveTab == null)
        {
            ActiveTab = tab;
        }

        return tab;
    }

    public void RemoveTab(TabItem tab)
    {
        if (_windowHosts.TryGetValue(tab.Id, out var host))
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
            t.Window?.Handle == IntPtr.Zero ||
            !_windowManager.IsWindowValid(t.Window!.Handle)).ToList();

        foreach (var tab in invalidTabs)
        {
            RemoveTab(tab);
        }
    }

    public void ReleaseAllTabs()
    {
        foreach (var tab in Tabs.ToList())
        {
            RemoveTab(tab);
        }
    }
}
