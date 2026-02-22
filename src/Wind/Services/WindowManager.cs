using System.Collections.ObjectModel;
using System.Diagnostics;
using Wind.Interop;
using Wind.Models;

namespace Wind.Services;

public class WindowManager
{
    private readonly SettingsManager _settingsManager;
    private readonly HashSet<IntPtr> _embeddedWindows = new();
    private readonly Dictionary<IntPtr, WindowHost> _embeddedHosts = new();

    public ObservableCollection<WindowInfo> AvailableWindows { get; } = new();

    public WindowManager(SettingsManager settingsManager)
    {
        _settingsManager = settingsManager;
        _settingsManager.HideEmbeddedFromTaskbarChanged += OnHideEmbeddedFromTaskbarChanged;
    }

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

        var host = new WindowHost(handle, _settingsManager.Settings.HideEmbeddedFromTaskbar);
        _embeddedWindows.Add(handle);
        _embeddedHosts[handle] = host;
        host.HostedWindowClosed += (_, _) =>
        {
            _embeddedWindows.Remove(handle);
            _embeddedHosts.Remove(handle);
        };

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
            _embeddedHosts.Remove(handle);
        }
    }

    public bool IsEmbedded(IntPtr handle)
    {
        return _embeddedWindows.Contains(handle);
    }

    private void OnHideEmbeddedFromTaskbarChanged(bool hideFromTaskbar)
    {
        foreach (var host in _embeddedHosts.Values.ToList())
        {
            host.SetHideFromTaskbar(hideFromTaskbar);
        }
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

    public void ArrangeTopmostWindows()
    {
        var currentProcessId = Environment.ProcessId;
        var topmostWindows = new List<(IntPtr Handle, NativeMethods.RECT Rect, string Title)>();

        NativeMethods.EnumWindows((hWnd, _) =>
        {
            if (!NativeMethods.IsWindowVisible(hWnd)) return true;

            int style = NativeMethods.GetWindowLong(hWnd, NativeMethods.GWL_STYLE);
            int exStyle = NativeMethods.GetWindowLong(hWnd, NativeMethods.GWL_EXSTYLE);

            if ((style & (int)NativeMethods.WS_CHILD) != 0) return true;
            if ((exStyle & (int)NativeMethods.WS_EX_TOPMOST) == 0) return true;

            NativeMethods.GetWindowThreadProcessId(hWnd, out uint processId);
            if (processId == currentProcessId) return true;

            string className = NativeMethods.GetWindowClassName(hWnd);
            if (IsSystemWindow(className)) return true;

            NativeMethods.GetWindowRect(hWnd, out var rect);
            // Skip tiny helper windows (e.g. WPF internal 1x1 windows)
            if (rect.Width < 10 || rect.Height < 10) return true;

            string windowTitle = NativeMethods.GetWindowTitle(hWnd);
            if (string.IsNullOrWhiteSpace(windowTitle)) windowTitle = $"(0x{hWnd:X})";
            topmostWindows.Add((hWnd, rect, windowTitle));

            return true;
        }, IntPtr.Zero);

        if (topmostWindows.Count == 0)
        {
            Debug.WriteLine("[ArrangeTopmost] No topmost windows found.");
            return;
        }

        var workArea = new NativeMethods.RECT();
        NativeMethods.SystemParametersInfo(NativeMethods.SPI_GETWORKAREA, 0, ref workArea, 0);
        Debug.WriteLine($"[ArrangeTopmost] WorkArea: L={workArea.Left} T={workArea.Top} R={workArea.Right} B={workArea.Bottom}");

        int x = workArea.Right;
        int y = workArea.Bottom;
        int columnMaxWidth = 0;

        foreach (var (handle, rect, windowTitle) in topmostWindows)
        {
            int w = rect.Width;
            int h = rect.Height;

            if (y - h < workArea.Top)
            {
                x -= columnMaxWidth;
                y = workArea.Bottom;
                columnMaxWidth = 0;
            }

            int posX = x - w;
            int posY = y - h;

            bool result = NativeMethods.SetWindowPos(handle, NativeMethods.HWND_TOPMOST,
                posX, posY, w, h, NativeMethods.SWP_NOACTIVATE);
            if (!result)
            {
                int error = System.Runtime.InteropServices.Marshal.GetLastWin32Error();
                Debug.WriteLine($"[ArrangeTopmost] {windowTitle} (0x{handle:X}) -> ({posX},{posY}) {w}x{h} SetWindowPos FAILED error={error}");
                result = NativeMethods.MoveWindow(handle, posX, posY, w, h, true);
                if (!result)
                {
                    error = System.Runtime.InteropServices.Marshal.GetLastWin32Error();
                    Debug.WriteLine($"[ArrangeTopmost] {windowTitle} (0x{handle:X}) MoveWindow FAILED error={error}");
                }
            }
            else
            {
                Debug.WriteLine($"[ArrangeTopmost] {windowTitle} (0x{handle:X}) -> ({posX},{posY}) {w}x{h} OK");
            }

            y -= h;
            if (w > columnMaxWidth) columnMaxWidth = w;
        }
    }
}
