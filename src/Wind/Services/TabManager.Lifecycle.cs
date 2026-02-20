using System.Diagnostics;
using Wind.Interop;
using Wind.Models;

namespace Wind.Services;

public partial class TabManager
{
    /// <summary>
    /// Timeout in milliseconds to wait for embedded processes to exit after sending WM_CLOSE.
    /// </summary>
    private const int ForceKillTimeoutMs = 3000;

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
        var processIdsToKill = new List<int>();

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
                        processIdsToKill.Add(host.HostedProcessId);
                        NativeMethods.PostMessage(tab.Window!.Handle, NativeMethods.WM_CLOSE, IntPtr.Zero, IntPtr.Zero);
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

        ForceKillRemainingProcesses(processIdsToKill);
        _processTracker.Clear();
    }

    /// <summary>
    /// Returns (Handle, ProcessId) pairs for all managed embedded apps that should be closed.
    /// </summary>
    public List<(IntPtr Handle, int ProcessId)> GetManagedAppsWithHandles(bool startupOnly)
    {
        var result = new List<(IntPtr Handle, int ProcessId)>();
        foreach (var tab in Tabs.ToList())
        {
            if (tab.IsContentTab || tab.IsWebTab) continue;
            if (startupOnly && !tab.IsLaunchedAtStartup) continue;

            if (_windowHosts.TryGetValue(tab.Id, out var host) &&
                tab.Window?.Handle != IntPtr.Zero &&
                host.HostedProcessId != 0)
            {
                result.Add((tab.Window!.Handle, host.HostedProcessId));
            }
        }
        return result;
    }

    public void CloseAllTabs()
    {
        var processIdsToKill = new List<int>();

        foreach (var tab in Tabs.ToList())
        {
            if (tab.IsContentTab) continue;
            if (tab.IsWebTab) { RemoveWebTabControl(tab.Id); continue; }

            if (_windowHosts.TryGetValue(tab.Id, out var host))
            {
                if (tab.Window?.Handle != IntPtr.Zero)
                {
                    processIdsToKill.Add(host.HostedProcessId);
                    NativeMethods.PostMessage(tab.Window!.Handle, NativeMethods.WM_CLOSE, IntPtr.Zero, IntPtr.Zero);
                }
                _windowHosts.Remove(tab.Id);
            }
        }
        Tabs.Clear();
        ActiveTab = null;

        ForceKillRemainingProcesses(processIdsToKill);
        _processTracker.Clear();
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

        _processTracker.Clear();
    }

    /// <summary>
    /// Returns the process IDs of all currently hosted windows.
    /// Used by the abnormal exit handler to force-kill orphaned processes.
    /// </summary>
    public List<int> GetTrackedProcessIds()
    {
        return _windowHosts.Values
            .Where(h => h.HostedProcessId != 0)
            .Select(h => h.HostedProcessId)
            .Distinct()
            .ToList();
    }

    private static void ForceKillRemainingProcesses(List<int> processIds)
    {
        if (processIds.Count == 0) return;

        // Wait for processes to exit gracefully
        var sw = Stopwatch.StartNew();
        while (sw.ElapsedMilliseconds < ForceKillTimeoutMs)
        {
            bool allExited = true;
            foreach (var pid in processIds)
            {
                try
                {
                    using var proc = Process.GetProcessById(pid);
                    if (!proc.HasExited) { allExited = false; break; }
                }
                catch
                {
                    // Process already exited
                }
            }

            if (allExited) return;
            Thread.Sleep(100);
        }

        // Force kill any remaining processes
        foreach (var pid in processIds)
        {
            try
            {
                using var proc = Process.GetProcessById(pid);
                if (!proc.HasExited)
                {
                    proc.Kill();
                }
            }
            catch
            {
                // Process already exited or access denied
            }
        }
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
