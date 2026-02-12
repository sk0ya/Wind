using System.Windows.Interop;
using System.Windows.Media;

namespace Wind.Interop;

public partial class WindowHost : HwndHost
{
    private IntPtr _hostedWindowHandle;
    private IntPtr _hwndHost;
    private IntPtr _originalParent;
    private int _originalStyle;
    private int _originalExStyle;
    private NativeMethods.RECT _originalRect;
    private bool _isHostedWindowClosed;
    private bool _isChromium;
    private bool _isOffice;

    public IntPtr HostedWindowHandle => _hostedWindowHandle;

    public event EventHandler? HostedWindowClosed;
    public event EventHandler? MinimizeRequested;
    public event EventHandler? MaximizeRequested;
    /// <summary>
    /// Fired when the hosted window is being dragged. Parameters are (dx, dy) in physical pixels.
    /// </summary>
    public event Action<int, int>? MoveRequested;

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
        _isChromium = IsChromiumWindow(windowHandle);
        _isOffice = IsOfficeWindow(windowHandle);
        _currentInstance = this; // Store current instance for WndProc access

        // Save original state immediately for later restoration.
        _originalStyle = NativeMethods.GetWindowLong(_hostedWindowHandle, NativeMethods.GWL_STYLE);
        _originalExStyle = NativeMethods.GetWindowLong(_hostedWindowHandle, NativeMethods.GWL_EXSTYLE);
        NativeMethods.GetWindowRect(_hostedWindowHandle, out _originalRect);

        // Remove taskbar button and hide the window right away.
        // The window will be shown inside Wind when BuildWindowCore runs
        // (i.e. when this host enters the WPF visual tree).
        int newExStyle = _originalExStyle;
        newExStyle &= ~(int)NativeMethods.WS_EX_APPWINDOW;
        newExStyle |= (int)NativeMethods.WS_EX_TOOLWINDOW;
        NativeMethods.SetWindowLong(_hostedWindowHandle, NativeMethods.GWL_EXSTYLE, newExStyle);
        NativeMethods.ShowWindow(_hostedWindowHandle, 0); // SW_HIDE = 0
    }

    public void ReleaseWindow()
    {
        if (_hostedWindowHandle == IntPtr.Zero || _isHostedWindowClosed) return;

        // Clear current instance reference
        if (_currentInstance == this)
        {
            _currentInstance = null;
        }

        // Remove event hook before releasing
        RemoveWinEventHook();

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
            NativeMethods.MoveWindow(_hostedWindowHandle, 0, 0, width, height, true);
        }
    }

    public void FocusHostedWindow()
    {
        if (_hostedWindowHandle == IntPtr.Zero || _isHostedWindowClosed) return;
        NativeMethods.SetForegroundWindow(_hostedWindowHandle);
        NativeMethods.SetFocus(_hostedWindowHandle);
    }
}
