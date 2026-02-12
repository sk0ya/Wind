using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Interop;

namespace Wind.Interop;

public partial class WindowHost
{
    [DllImport("user32.dll")]
    private static extern IntPtr SetWinEventHook(
        uint eventMin, uint eventMax, IntPtr hmodWinEventProc,
        WinEventDelegate lpfnWinEventProc, uint idProcess, uint idThread, uint dwFlags);

    [DllImport("user32.dll")]
    private static extern bool UnhookWinEvent(IntPtr hWinEventHook);

    [DllImport("user32.dll")]
    private static extern uint GetWindowThreadProcessId(IntPtr hWnd, out uint lpdwProcessId);

    [DllImport("user32.dll")]
    private static extern bool IsZoomed(IntPtr hWnd);

    [DllImport("user32.dll")]
    private static extern bool IsIconic(IntPtr hWnd);

    private delegate void WinEventDelegate(
        IntPtr hWinEventHook, uint eventType, IntPtr hwnd,
        int idObject, int idChild, uint dwEventThread, uint dwmsEventTime);

    private const uint EVENT_OBJECT_DESTROY = 0x8001;
    private const uint EVENT_SYSTEM_MOVESIZESTART = 0x000A;
    private const uint EVENT_SYSTEM_MOVESIZEEND = 0x000B;
    private const uint EVENT_SYSTEM_MINIMIZESTART = 0x0016;
    private const uint EVENT_OBJECT_LOCATIONCHANGE = 0x800B;
    private const uint WINEVENT_OUTOFCONTEXT = 0x0000;
    private const int OBJID_WINDOW = 0;

    private IntPtr _winEventHookMoveSize;
    private IntPtr _winEventHookMinimize;
    private IntPtr _winEventHookLocation;
    private IntPtr _winEventHookDestroy;
    private WinEventDelegate? _winEventProc;
    private bool _wasMaximized;

    // Move tracking state
    private bool _isHostedMoving;

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

        if (_isChromium || _isOffice)
        {
            // Chromium and Office apps break when made WS_CHILD — their rendering
            // pipelines and input handling assume a top-level window.
            // Use WS_POPUP + SetParent so the window is clipped to the host
            // but still receives keyboard input normally.
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

    protected override void DestroyWindowCore(HandleRef hwnd)
    {
        // Clear current instance reference
        if (_currentInstance == this)
        {
            _currentInstance = null;
        }

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

    private void SetupWinEventHook()
    {
        if (_hostedWindowHandle == IntPtr.Zero) return;

        GetWindowThreadProcessId(_hostedWindowHandle, out uint processId);

        _winEventProc = WinEventCallback;

        // Hook for move/size start and end
        _winEventHookMoveSize = SetWinEventHook(
            EVENT_SYSTEM_MOVESIZESTART, EVENT_SYSTEM_MOVESIZEEND,
            IntPtr.Zero, _winEventProc, processId, 0, WINEVENT_OUTOFCONTEXT);

        // Hook for minimize events
        _winEventHookMinimize = SetWinEventHook(
            EVENT_SYSTEM_MINIMIZESTART, EVENT_SYSTEM_MINIMIZESTART,
            IntPtr.Zero, _winEventProc, processId, 0, WINEVENT_OUTOFCONTEXT);

        // Hook for location/size changes to detect maximize and move
        _winEventHookLocation = SetWinEventHook(
            EVENT_OBJECT_LOCATIONCHANGE, EVENT_OBJECT_LOCATIONCHANGE,
            IntPtr.Zero, _winEventProc, processId, 0, WINEVENT_OUTOFCONTEXT);

        // Hook for window destruction (WS_POPUP ウィンドウは WM_PARENTNOTIFY が送信されないため必要)
        _winEventHookDestroy = SetWinEventHook(
            EVENT_OBJECT_DESTROY, EVENT_OBJECT_DESTROY,
            IntPtr.Zero, _winEventProc, processId, 0, WINEVENT_OUTOFCONTEXT);
    }

    private void RemoveWinEventHook()
    {
        if (_winEventHookMoveSize != IntPtr.Zero)
        {
            UnhookWinEvent(_winEventHookMoveSize);
            _winEventHookMoveSize = IntPtr.Zero;
        }
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
        if (_winEventHookDestroy != IntPtr.Zero)
        {
            UnhookWinEvent(_winEventHookDestroy);
            _winEventHookDestroy = IntPtr.Zero;
        }
        _winEventProc = null;
    }

    private void WinEventCallback(
        IntPtr hWinEventHook, uint eventType, IntPtr hwnd,
        int idObject, int idChild, uint dwEventThread, uint dwmsEventTime)
    {
        if (hwnd != _hostedWindowHandle) return;

        if (eventType == EVENT_OBJECT_DESTROY && idObject == OBJID_WINDOW)
        {
            // ホストされたウィンドウが破棄された
            // WS_POPUP ウィンドウでは WM_PARENTNOTIFY が送信されないため、ここで検出する
            if (!_isHostedWindowClosed)
            {
                _isHostedWindowClosed = true;
                RemoveWinEventHook();
                _hostedWindowHandle = IntPtr.Zero;
                HostedWindowClosed?.Invoke(this, EventArgs.Empty);
            }
            return;
        }

        if (eventType == EVENT_SYSTEM_MOVESIZESTART)
        {
            _isHostedMoving = true;
        }
        else if (eventType == EVENT_SYSTEM_MOVESIZEEND)
        {
            if (_isHostedMoving)
            {
                _isHostedMoving = false;
                // Reset hosted window to fill host
                if (_hwndHost != IntPtr.Zero)
                {
                    NativeMethods.GetWindowRect(_hwndHost, out var hostRect);
                    NativeMethods.MoveWindow(_hostedWindowHandle, 0, 0, hostRect.Width, hostRect.Height, true);
                }
            }
        }
        else if (eventType == EVENT_SYSTEM_MINIMIZESTART)
        {
            // The hosted window is trying to minimize
            // Restore it immediately and minimize Wind instead
            NativeMethods.ShowWindow(_hostedWindowHandle, NativeMethods.SW_RESTORE);
            MinimizeRequested?.Invoke(this, EventArgs.Empty);
        }
        else if (eventType == EVENT_OBJECT_LOCATIONCHANGE && idObject == OBJID_WINDOW)
        {
            // Check if window became maximized
            if (!_isHostedMoving)
            {
                bool isMaximized = IsZoomed(_hostedWindowHandle);
                if (isMaximized && !_wasMaximized)
                {
                    _wasMaximized = true;
                    NativeMethods.ShowWindow(_hostedWindowHandle, NativeMethods.SW_RESTORE);
                    MaximizeRequested?.Invoke(this, EventArgs.Empty);
                }
                else if (!isMaximized)
                {
                    _wasMaximized = false;
                }
            }

            // During a hosted window move, detect displacement from host
            // and translate it to Wind movement, then reset position.
            if (_isHostedMoving && _hwndHost != IntPtr.Zero &&
                NativeMethods.GetWindowRect(_hostedWindowHandle, out var hostedRect) &&
                NativeMethods.GetWindowRect(_hwndHost, out var hostRect))
            {
                int dx = hostedRect.Left - hostRect.Left;
                int dy = hostedRect.Top - hostRect.Top;

                if (dx != 0 || dy != 0)
                {
                    MoveRequested?.Invoke(dx, dy);
                    NativeMethods.MoveWindow(_hostedWindowHandle, 0, 0, hostRect.Width, hostRect.Height, true);
                }
            }
        }
    }
}
