using System.Windows.Interop;
using System.Windows.Media;

namespace Wind.Interop;

public partial class WindowHost : HwndHost
{
    private IntPtr _hostedWindowHandle;
    private IntPtr _hwndHost;
    private int _originalStyle;
    private int _originalExStyle;
    private NativeMethods.RECT _originalRect;
    private bool _isHostedWindowClosed;

    public IntPtr HostedWindowHandle => _hostedWindowHandle;
    public int HostedProcessId { get; }

    public event EventHandler? HostedWindowClosed;
    public event EventHandler? MinimizeRequested;
    public event EventHandler? MaximizeRequested;
    public event EventHandler? BringToFrontRequested;

    /// <summary>
    /// Fired when the hosted window is being dragged. Parameters are (dx, dy) in physical pixels.
    /// </summary>
    public event Action<int, int>? MoveRequested;

    /// <summary>
    /// Fired when the hosted process creates a new top-level window.
    /// The parameter is the HWND of the new window.
    /// </summary>
    public event Action<IntPtr>? NewWindowDetected;

    // Background color property
    private Color _backgroundColor = Colors.Black;

    public Color BackgroundColor
    {
        get => _backgroundColor;
        set
        {
            if (_backgroundColor != value)
            {
                _backgroundColor = value;
                // Trigger repaint if host window exists
                if (_hwndHost != IntPtr.Zero)
                {
                    InvalidateRect(_hwndHost, IntPtr.Zero, true);
                }
            }
        }
    }

    public WindowHost(IntPtr windowHandle)
    {
        _hostedWindowHandle = windowHandle;

        // Store the process ID so we can force-kill if needed at shutdown.
        NativeMethods.GetWindowThreadProcessId(windowHandle, out uint pid);
        HostedProcessId = (int)pid;

        // Save original state immediately for later restoration.
        _originalStyle = NativeMethods.GetWindowLong(_hostedWindowHandle, NativeMethods.GWL_STYLE);
        _originalExStyle = NativeMethods.GetWindowLong(_hostedWindowHandle, NativeMethods.GWL_EXSTYLE);
        NativeMethods.GetWindowRect(_hostedWindowHandle, out _originalRect);

        // Remove taskbar button and hide the window right away.
        // The window will be shown inside Wind when BuildWindowCore runs
        // (i.e. when this host enters the WPF visual tree).
        int newExStyle = _originalExStyle;
        newExStyle &= ~(int) NativeMethods.WS_EX_APPWINDOW;
        newExStyle |= (int) NativeMethods.WS_EX_TOOLWINDOW;
        NativeMethods.SetWindowLong(_hostedWindowHandle, NativeMethods.GWL_EXSTYLE, newExStyle);
        NativeMethods.ShowWindow(_hostedWindowHandle, 0); // SW_HIDE = 0
    }

    public void ReleaseWindow()
    {
        if (_hostedWindowHandle == IntPtr.Zero || _isHostedWindowClosed) return;

        // Remove from instance mapping
        if (_hwndHost != IntPtr.Zero)
            _instances.Remove(_hwndHost);

        // Remove event hook before releasing
        RemoveWinEventHook();

        // Remove clipping region before restoring
        NativeMethods.SetWindowRgn(_hostedWindowHandle, IntPtr.Zero, false);

        // Restore parent (to desktop)
        NativeMethods.SetParent(_hostedWindowHandle, IntPtr.Zero);

        // Restore original styles
        NativeMethods.SetWindowLong(_hostedWindowHandle, NativeMethods.GWL_STYLE, _originalStyle);
        NativeMethods.SetWindowLong(_hostedWindowHandle, NativeMethods.GWL_EXSTYLE, _originalExStyle);

        // Apply style changes and restore to original position and size
        NativeMethods.SetWindowPos(_hostedWindowHandle, IntPtr.Zero,
            _originalRect.Left, _originalRect.Top, _originalRect.Width, _originalRect.Height,
            NativeMethods.SWP_NOZORDER | NativeMethods.SWP_FRAMECHANGED | NativeMethods.SWP_SHOWWINDOW);

        NativeMethods.ShowWindow(_hostedWindowHandle, NativeMethods.SW_RESTORE);

        _hostedWindowHandle = IntPtr.Zero;
    }

    public void ResizeHostedWindow(int width, int height)
    {
        if (_hwndHost == IntPtr.Zero || _hostedWindowHandle == IntPtr.Zero || _isHostedWindowClosed) return;

        // Only resize the hosted window within the host.
        // _hwndHost position is managed by HwndHost base class — do not move it.
        if (!_isHostedMoving)
        {
            // Use SetWindowPos with SWP_NOCOPYBITS to prevent Windows from copying
            // old pixel content during resize, which causes ghost artifacts.
            NativeMethods.SetWindowPos(_hostedWindowHandle, IntPtr.Zero,
                0, 0, width, height,
                NativeMethods.SWP_NOZORDER | NativeMethods.SWP_NOACTIVATE | NativeMethods.SWP_NOCOPYBITS);

            // Clip the WS_POPUP window to the host's client area.
            // Unlike WS_CHILD, WS_POPUP windows are not automatically clipped by
            // their parent, so we enforce it with a window region.
            // SetWindowRgn takes ownership of the region handle — do not delete it.
            IntPtr rgn = NativeMethods.CreateRectRgn(0, 0, width, height);
            NativeMethods.SetWindowRgn(_hostedWindowHandle, rgn, true);
        }
    }

    public void FocusHostedWindow()
    {
        if (_hostedWindowHandle == IntPtr.Zero || _isHostedWindowClosed) return;

        // SetForegroundWindow を使うと EVENT_SYSTEM_FOREGROUND が発火して
        // BringToFrontRequested との誤発火ループが起きる。
        // AttachThreadInput + SetFocus でフォーカスのみ移す（フォアグラウンドは変えない）。
        var currentThread = NativeMethods.GetCurrentThreadId();
        var hostedThread = NativeMethods.GetWindowThreadProcessId(_hostedWindowHandle, out _);

        if (hostedThread != 0 && hostedThread != currentThread)
            NativeMethods.AttachThreadInput(currentThread, hostedThread, true);

        NativeMethods.SetFocus(_hostedWindowHandle);

        if (hostedThread != 0 && hostedThread != currentThread)
            NativeMethods.AttachThreadInput(currentThread, hostedThread, false);
    }

    public void ForceRedraw()
    {
        if (_hostedWindowHandle == IntPtr.Zero || _isHostedWindowClosed) return;

        NativeMethods.ShowWindow(_hostedWindowHandle, NativeMethods.SW_SHOW);
        NativeMethods.RedrawWindow(
            _hostedWindowHandle,
            IntPtr.Zero,
            IntPtr.Zero,
            NativeMethods.RDW_INVALIDATE |
            NativeMethods.RDW_ERASE |
            NativeMethods.RDW_FRAME |
            NativeMethods.RDW_ALLCHILDREN |
            NativeMethods.RDW_UPDATENOW);
        NativeMethods.UpdateWindow(_hostedWindowHandle);

        if (_hwndHost != IntPtr.Zero)
        {
            NativeMethods.RedrawWindow(
                _hwndHost,
                IntPtr.Zero,
                IntPtr.Zero,
                NativeMethods.RDW_INVALIDATE |
                NativeMethods.RDW_ERASE |
                NativeMethods.RDW_UPDATENOW);
            NativeMethods.UpdateWindow(_hwndHost);
        }
    }
}
