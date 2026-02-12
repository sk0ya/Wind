using Wind.Interop;
using Wind.Models;

namespace Wind.Services;

public partial class TabManager
{
    public void CleanupInvalidTabs()
    {
        var invalidTabs = Tabs.Where(t =>
            !t.IsContentTab &&
            !t.IsWebTab &&
            (t.Window?.Handle == IntPtr.Zero ||
            !_windowManager.IsWindowValid(t.Window!.Handle))).ToList();

        foreach (var tab in invalidTabs)
        {
            // ウィンドウは既に無効なので ReleaseWindow せずに追跡だけ解除する
            OnHostedWindowClosed(tab);
        }
    }

    public void StopCleanupTimer()
    {
        _cleanupTimer.Stop();
    }

    public void CloseStartupTabs()
    {
        foreach (var tab in Tabs.ToList())
        {
            if (tab.IsContentTab) continue;
            if (tab.IsWebTab) { RemoveWebTabControl(tab.Id); continue; }

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
            if (tab.IsWebTab) { RemoveWebTabControl(tab.Id); continue; }

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
            if (tab.IsWebTab) { RemoveWebTabControl(tab.Id); continue; }

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
