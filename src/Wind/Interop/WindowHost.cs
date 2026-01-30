using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Interop;

namespace Wind.Interop;

public class WindowHost : HwndHost
{
    private IntPtr _hostedWindowHandle;
    private IntPtr _hwndHost;
    private IntPtr _originalParent;
    private int _originalStyle;
    private int _originalExStyle;
    private NativeMethods.RECT _originalRect;
    private bool _isHostedWindowClosed;

    private const string HostClassName = "WindWindowHost";
    private static bool _classRegistered;

    private const int WM_PARENTNOTIFY = 0x0210;
    private const int WM_DESTROY = 0x0002;

    public IntPtr HostedWindowHandle => _hostedWindowHandle;

    public event EventHandler? HostedWindowClosed;
    public event EventHandler? MinimizeRequested;
    public event EventHandler? MaximizeRequested;

    [DllImport("user32.dll", SetLastError = true, CharSet = CharSet.Unicode)]
    private static extern IntPtr CreateWindowEx(
        uint dwExStyle, string lpClassName, string lpWindowName, uint dwStyle,
        int x, int y, int nWidth, int nHeight,
        IntPtr hWndParent, IntPtr hMenu, IntPtr hInstance, IntPtr lpParam);

    [DllImport("user32.dll", SetLastError = true)]
    private static extern bool DestroyWindow(IntPtr hWnd);

    [DllImport("user32.dll", SetLastError = true, CharSet = CharSet.Unicode)]
    private static extern ushort RegisterClass(ref WNDCLASS lpWndClass);

    [DllImport("user32.dll")]
    private static extern IntPtr DefWindowProc(IntPtr hWnd, uint uMsg, IntPtr wParam, IntPtr lParam);

    [DllImport("kernel32.dll", CharSet = CharSet.Unicode)]
    private static extern IntPtr GetModuleHandle(string? lpModuleName);

    [DllImport("user32.dll")]
    private static extern IntPtr SetWinEventHook(
        uint eventMin, uint eventMax, IntPtr hmodWinEventProc,
        WinEventDelegate lpfnWinEventProc, uint idProcess, uint idThread, uint dwFlags);

    [DllImport("user32.dll")]
    private static extern bool UnhookWinEvent(IntPtr hWinEventHook);

    [DllImport("user32.dll")]
    private static extern uint GetWindowThreadProcessId(IntPtr hWnd, out uint lpdwProcessId);

    private delegate void WinEventDelegate(
        IntPtr hWinEventHook, uint eventType, IntPtr hwnd,
        int idObject, int idChild, uint dwEventThread, uint dwmsEventTime);

    private const uint EVENT_SYSTEM_MINIMIZESTART = 0x0016;
    private const uint EVENT_OBJECT_LOCATIONCHANGE = 0x800B;
    private const uint WINEVENT_OUTOFCONTEXT = 0x0000;
    private const int OBJID_WINDOW = 0;

    private IntPtr _winEventHookMinimize;
    private IntPtr _winEventHookLocation;
    private WinEventDelegate? _winEventProc;
    private bool _wasMaximized;

    [DllImport("user32.dll")]
    private static extern bool IsZoomed(IntPtr hWnd);

    [DllImport("user32.dll")]
    private static extern bool IsIconic(IntPtr hWnd);

    private delegate IntPtr WndProcDelegate(IntPtr hWnd, uint msg, IntPtr wParam, IntPtr lParam);
    private static WndProcDelegate? _wndProcDelegate;

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private struct WNDCLASS
    {
        public uint style;
        public IntPtr lpfnWndProc;
        public int cbClsExtra;
        public int cbWndExtra;
        public IntPtr hInstance;
        public IntPtr hIcon;
        public IntPtr hCursor;
        public IntPtr hbrBackground;
        public string? lpszMenuName;
        public string lpszClassName;
    }

    private bool _isChromium;

    public WindowHost(IntPtr windowHandle)
    {
        _hostedWindowHandle = windowHandle;
        _isChromium = IsChromiumWindow(windowHandle);

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

    private static bool IsChromiumWindow(IntPtr hwnd)
    {
        var className = NativeMethods.GetWindowClassName(hwnd);
        // Chrome, Edge, and other Chromium-based browsers use Chrome_WidgetWin_*
        return className.StartsWith("Chrome_WidgetWin");
    }

    private static void EnsureClassRegistered()
    {
        if (_classRegistered) return;

        _wndProcDelegate = WndProc;

        var wndClass = new WNDCLASS
        {
            style = 0,
            lpfnWndProc = Marshal.GetFunctionPointerForDelegate(_wndProcDelegate),
            cbClsExtra = 0,
            cbWndExtra = 0,
            hInstance = GetModuleHandle(null),
            hIcon = IntPtr.Zero,
            hCursor = IntPtr.Zero,
            hbrBackground = IntPtr.Zero,
            lpszMenuName = null,
            lpszClassName = HostClassName
        };

        RegisterClass(ref wndClass);
        _classRegistered = true;
    }

    private static IntPtr WndProc(IntPtr hWnd, uint msg, IntPtr wParam, IntPtr lParam)
    {
        return DefWindowProc(hWnd, msg, wParam, lParam);
    }

    protected override HandleRef BuildWindowCore(HandleRef hwndParent)
    {
        if (_hostedWindowHandle == IntPtr.Zero)
        {
            return new HandleRef(this, IntPtr.Zero);
        }

        EnsureClassRegistered();

        const uint WS_CLIPCHILDREN = 0x02000000;
        const uint WS_CLIPSIBLINGS = 0x04000000;

        // Create a host window
        _hwndHost = CreateWindowEx(
            0,
            HostClassName,
            "WindHost",
            NativeMethods.WS_CHILD | NativeMethods.WS_VISIBLE | WS_CLIPCHILDREN | WS_CLIPSIBLINGS,
            0, 0, 100, 100,
            hwndParent.Handle,
            IntPtr.Zero,
            GetModuleHandle(null),
            IntPtr.Zero);

        if (_hwndHost == IntPtr.Zero)
        {
            return new HandleRef(this, IntPtr.Zero);
        }

        // Remove window decorations (original state was saved in constructor)
        int newStyle = _originalStyle;
        newStyle &= ~(int)(NativeMethods.WS_CAPTION | NativeMethods.WS_THICKFRAME |
                          NativeMethods.WS_MINIMIZEBOX | NativeMethods.WS_MAXIMIZEBOX |
                          NativeMethods.WS_SYSMENU | NativeMethods.WS_BORDER | NativeMethods.WS_DLGFRAME);

        if (_isChromium)
        {
            // Chromium apps break when made WS_CHILD — their multi-process input
            // pipeline assumes a top-level window.  Use WS_POPUP + SetParent so the
            // window is clipped to the host but still receives keyboard input normally.
            newStyle &= ~(int)NativeMethods.WS_CHILD;
            newStyle |= unchecked((int)NativeMethods.WS_POPUP) | (int)NativeMethods.WS_VISIBLE;
        }
        else
        {
            newStyle |= (int)(NativeMethods.WS_CHILD | NativeMethods.WS_VISIBLE);
        }

        NativeMethods.SetWindowLong(_hostedWindowHandle, NativeMethods.GWL_STYLE, newStyle);

        // Remove taskbar button
        int newExStyle = _originalExStyle;
        newExStyle &= ~(int)NativeMethods.WS_EX_APPWINDOW;
        newExStyle |= (int)NativeMethods.WS_EX_TOOLWINDOW;
        NativeMethods.SetWindowLong(_hostedWindowHandle, NativeMethods.GWL_EXSTYLE, newExStyle);

        // Set parent to our host window
        _originalParent = NativeMethods.SetParent(_hostedWindowHandle, _hwndHost);

        // Position the window at 0,0 within the host
        NativeMethods.SetWindowPos(_hostedWindowHandle, IntPtr.Zero, 0, 0, 100, 100,
            NativeMethods.SWP_NOZORDER | NativeMethods.SWP_FRAMECHANGED | NativeMethods.SWP_SHOWWINDOW);

        // Make sure it's visible
        NativeMethods.ShowWindow(_hostedWindowHandle, NativeMethods.SW_SHOW);

        // Set up event hook to monitor hosted window events
        SetupWinEventHook();

        return new HandleRef(this, _hwndHost);
    }

    private void SetupWinEventHook()
    {
        if (_hostedWindowHandle == IntPtr.Zero) return;

        GetWindowThreadProcessId(_hostedWindowHandle, out uint processId);

        _winEventProc = WinEventCallback;

        // Hook for minimize events
        _winEventHookMinimize = SetWinEventHook(
            EVENT_SYSTEM_MINIMIZESTART, EVENT_SYSTEM_MINIMIZESTART,
            IntPtr.Zero, _winEventProc, processId, 0, WINEVENT_OUTOFCONTEXT);

        // Hook for location/size changes to detect maximize
        _winEventHookLocation = SetWinEventHook(
            EVENT_OBJECT_LOCATIONCHANGE, EVENT_OBJECT_LOCATIONCHANGE,
            IntPtr.Zero, _winEventProc, processId, 0, WINEVENT_OUTOFCONTEXT);
    }

    private void RemoveWinEventHook()
    {
        if (_winEventHookMinimize != IntPtr.Zero)
        {
            UnhookWinEvent(_winEventHookMinimize);
            _winEventHookMinimize = IntPtr.Zero;
        }
        if (_winEventHookLocation != IntPtr.Zero)
        {
            UnhookWinEvent(_winEventHookLocation);
            _winEventHookLocation = IntPtr.Zero;
        }
        _winEventProc = null;
    }

    private void WinEventCallback(
        IntPtr hWinEventHook, uint eventType, IntPtr hwnd,
        int idObject, int idChild, uint dwEventThread, uint dwmsEventTime)
    {
        if (hwnd != _hostedWindowHandle) return;

        if (eventType == EVENT_SYSTEM_MINIMIZESTART)
        {
            // The hosted window is trying to minimize
            // Restore it immediately and minimize Wind instead
            NativeMethods.ShowWindow(_hostedWindowHandle, NativeMethods.SW_RESTORE);
            MinimizeRequested?.Invoke(this, EventArgs.Empty);
        }
        else if (eventType == EVENT_OBJECT_LOCATIONCHANGE && idObject == OBJID_WINDOW)
        {
            // Check if window became maximized
            bool isMaximized = IsZoomed(_hostedWindowHandle);
            if (isMaximized && !_wasMaximized)
            {
                // Window just became maximized - restore it and maximize Wind instead
                _wasMaximized = true;
                NativeMethods.ShowWindow(_hostedWindowHandle, NativeMethods.SW_RESTORE);
                MaximizeRequested?.Invoke(this, EventArgs.Empty);
            }
            else if (!isMaximized)
            {
                _wasMaximized = false;
            }
        }
    }

    protected override void DestroyWindowCore(HandleRef hwnd)
    {
        // Detach the hosted window before destroying the host HWND.
        // If the hosted window is still a child of _hwndHost when DestroyWindow
        // is called, Windows will cascade-destroy it, killing the hosted process's window.
        if (_hostedWindowHandle != IntPtr.Zero && !_isHostedWindowClosed)
        {
            RemoveWinEventHook();
            NativeMethods.SetParent(_hostedWindowHandle, IntPtr.Zero);
            NativeMethods.SetWindowLong(_hostedWindowHandle, NativeMethods.GWL_STYLE, _originalStyle);
            NativeMethods.SetWindowLong(_hostedWindowHandle, NativeMethods.GWL_EXSTYLE, _originalExStyle);
            NativeMethods.SetWindowPos(_hostedWindowHandle, IntPtr.Zero,
                _originalRect.Left, _originalRect.Top, _originalRect.Width, _originalRect.Height,
                NativeMethods.SWP_NOZORDER | NativeMethods.SWP_FRAMECHANGED | NativeMethods.SWP_SHOWWINDOW);
            NativeMethods.ShowWindow(_hostedWindowHandle, NativeMethods.SW_RESTORE);
            _hostedWindowHandle = IntPtr.Zero;
        }

        if (_hwndHost != IntPtr.Zero)
        {
            DestroyWindow(_hwndHost);
            _hwndHost = IntPtr.Zero;
        }
    }

    public void ReleaseWindow()
    {
        if (_hostedWindowHandle == IntPtr.Zero || _isHostedWindowClosed) return;

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

        // Resize host window
        NativeMethods.MoveWindow(_hwndHost, 0, 0, width, height, true);

        // Resize hosted window to fill the host
        NativeMethods.MoveWindow(_hostedWindowHandle, 0, 0, width, height, true);
    }

    protected override void OnWindowPositionChanged(Rect rcBoundingBox)
    {
        base.OnWindowPositionChanged(rcBoundingBox);

        if (_hwndHost != IntPtr.Zero && _hostedWindowHandle != IntPtr.Zero && !_isHostedWindowClosed)
        {
            int width = (int)rcBoundingBox.Width;
            int height = (int)rcBoundingBox.Height;

            if (width > 0 && height > 0)
            {
                ResizeHostedWindow(width, height);
            }
        }
    }

    public void FocusHostedWindow()
    {
        if (_hostedWindowHandle == IntPtr.Zero || _isHostedWindowClosed) return;
        NativeMethods.SetForegroundWindow(_hostedWindowHandle);
        NativeMethods.SetFocus(_hostedWindowHandle);
    }

    protected override IntPtr WndProc(IntPtr hwnd, int msg, IntPtr wParam, IntPtr lParam, ref bool handled)
    {
        if (msg == WM_PARENTNOTIFY)
        {
            int eventCode = wParam.ToInt32() & 0xFFFF;
            if (eventCode == WM_DESTROY)
            {
                // Hosted window is being destroyed
                _isHostedWindowClosed = true;
                _hostedWindowHandle = IntPtr.Zero;
                HostedWindowClosed?.Invoke(this, EventArgs.Empty);
                handled = true;
                return IntPtr.Zero;
            }
        }

        return base.WndProc(hwnd, msg, wParam, lParam, ref handled);
    }
}
