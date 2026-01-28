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

    private const string HostClassName = "WindWindowHost";
    private static bool _classRegistered;

    public IntPtr HostedWindowHandle => _hostedWindowHandle;

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

    public WindowHost(IntPtr windowHandle)
    {
        _hostedWindowHandle = windowHandle;
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

        // Store original state for restoration
        _originalStyle = NativeMethods.GetWindowLong(_hostedWindowHandle, NativeMethods.GWL_STYLE);
        _originalExStyle = NativeMethods.GetWindowLong(_hostedWindowHandle, NativeMethods.GWL_EXSTYLE);
        NativeMethods.GetWindowRect(_hostedWindowHandle, out _originalRect);

        // Remove window decorations and make it a child window
        int newStyle = _originalStyle;
        newStyle &= ~(int)(NativeMethods.WS_CAPTION | NativeMethods.WS_THICKFRAME |
                          NativeMethods.WS_MINIMIZEBOX | NativeMethods.WS_MAXIMIZEBOX |
                          NativeMethods.WS_SYSMENU | NativeMethods.WS_BORDER | NativeMethods.WS_DLGFRAME);
        newStyle |= (int)(NativeMethods.WS_CHILD | NativeMethods.WS_VISIBLE);

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

        return new HandleRef(this, _hwndHost);
    }

    protected override void DestroyWindowCore(HandleRef hwnd)
    {
        ReleaseWindow();

        if (_hwndHost != IntPtr.Zero)
        {
            DestroyWindow(_hwndHost);
            _hwndHost = IntPtr.Zero;
        }
    }

    public void ReleaseWindow()
    {
        if (_hostedWindowHandle == IntPtr.Zero) return;

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
        if (_hwndHost == IntPtr.Zero || _hostedWindowHandle == IntPtr.Zero) return;

        // Resize host window
        NativeMethods.MoveWindow(_hwndHost, 0, 0, width, height, true);

        // Resize hosted window to fill the host
        NativeMethods.MoveWindow(_hostedWindowHandle, 0, 0, width, height, true);
    }

    protected override void OnWindowPositionChanged(Rect rcBoundingBox)
    {
        base.OnWindowPositionChanged(rcBoundingBox);

        if (_hwndHost != IntPtr.Zero && _hostedWindowHandle != IntPtr.Zero)
        {
            int width = (int)rcBoundingBox.Width;
            int height = (int)rcBoundingBox.Height;

            if (width > 0 && height > 0)
            {
                ResizeHostedWindow(width, height);
            }
        }
    }
}
