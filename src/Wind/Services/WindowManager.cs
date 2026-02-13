using System.Collections.ObjectModel;
using System.Diagnostics;
using Wind.Interop;
using Wind.Models;

namespace Wind.Services;

public class WindowManager
{
    private readonly HashSet<IntPtr> _embeddedWindows = new();

    public ObservableCollection<WindowInfo> AvailableWindows { get; } = new();

    public void RefreshWindowList()
    {
        AvailableWindows.Clear();
        var windows = EnumerateWindows();
        foreach (var window in windows)
        {
            if (!_embeddedWindows.Contains(window.Handle))
            {
                AvailableWindows.Add(window);
            }
        }
    }

    public List<WindowInfo> EnumerateWindows()
    {
        var windows = new List<WindowInfo>();
        var currentProcessId = Environment.ProcessId;

        NativeMethods.EnumWindows((hWnd, _) =>
        {
            if (!IsValidWindow(hWnd, currentProcessId))
                return true;

            var windowInfo = WindowInfo.FromHandle(hWnd);
            if (windowInfo != null)
            {
                windows.Add(windowInfo);
            }

            return true;
        }, IntPtr.Zero);

        return windows.OrderBy(w => w.ProcessName).ThenBy(w => w.Title).ToList();
    }

    private bool IsValidWindow(IntPtr hWnd, int currentProcessId)
    {
        // Must be visible
        if (!NativeMethods.IsWindowVisible(hWnd)) return false;

        // Get window style
        int style = NativeMethods.GetWindowLong(hWnd, NativeMethods.GWL_STYLE);
        int exStyle = NativeMethods.GetWindowLong(hWnd, NativeMethods.GWL_EXSTYLE);

        // Skip child windows
        if ((style & (int)NativeMethods.WS_CHILD) != 0) return false;

        // Skip tool windows
        if ((exStyle & (int)NativeMethods.WS_EX_TOOLWINDOW) != 0) return false;

        // Must have a title
        string title = NativeMethods.GetWindowTitle(hWnd);
        if (string.IsNullOrWhiteSpace(title)) return false;

        // Skip our own window
        NativeMethods.GetWindowThreadProcessId(hWnd, out uint processId);
        if (processId == currentProcessId) return false;

        // Skip certain system windows
        string className = NativeMethods.GetWindowClassName(hWnd);
        if (IsSystemWindow(className)) return false;

        return true;
    }

    private bool IsSystemWindow(string className)
    {
        return className switch
        {
            "Progman" => true,
            "WorkerW" => true,
            "Shell_TrayWnd" => true,
            "Shell_SecondaryTrayWnd" => true,
            "Windows.UI.Core.CoreWindow" => true,
            "ApplicationFrameWindow" => false, // UWP apps - we want these
            _ => false
        };
    }

    public WindowHost? EmbedWindow(IntPtr handle)
    {
        if (handle == IntPtr.Zero || _embeddedWindows.Contains(handle))
            return null;

        // Restore if minimized
        if (NativeMethods.IsIconic(handle))
        {
            NativeMethods.ShowWindow(handle, NativeMethods.SW_RESTORE);
        }

        var host = new WindowHost(handle);
        _embeddedWindows.Add(handle);

        return host;
    }

    public void ReleaseWindow(WindowHost? host)
    {
        if (host == null) return;

        var handle = host.HostedWindowHandle;
        host.ReleaseWindow();

        if (handle != IntPtr.Zero)
        {
            _embeddedWindows.Remove(handle);
        }
    }

    public bool IsEmbedded(IntPtr handle)
    {
        return _embeddedWindows.Contains(handle);
    }

    public bool IsWindowValid(IntPtr handle)
    {
        if (handle == IntPtr.Zero) return false;

        try
        {
            NativeMethods.GetWindowThreadProcessId(handle, out uint processId);
            if (processId == 0) return false;

            // Try to get process - if it throws, the window is gone
            using var process = Process.GetProcessById((int)processId);
            return !process.HasExited;
        }
        catch
        {
            return false;
        }
    }

    public void BringToFront(IntPtr handle)
    {
        if (handle == IntPtr.Zero) return;
        NativeMethods.SetForegroundWindow(handle);
    }
}
