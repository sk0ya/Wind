using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Threading;
using Wind.Interop;
using Wind.Services;

namespace Wind.Views;

public partial class MainWindow
{
    // ── Fields owned by the managed-sync subsystem ───────────────────────────

    private readonly WindowManager _windowManager;
    private IntPtr _activeManagedWindowHandle;

    // ── WinEvent hook infrastructure ─────────────────────────────────────────

    private delegate void ManagedWinEventDelegate(
        IntPtr hWinEventHook,
        uint eventType,
        IntPtr hwnd,
        int idObject,
        int idChild,
        uint dwEventThread,
        uint dwmsEventTime);

    [DllImport("user32.dll")]
    private static extern IntPtr SetWinEventHook(
        uint eventMin,
        uint eventMax,
        IntPtr hmodWinEventProc,
        ManagedWinEventDelegate lpfnWinEventProc,
        uint idProcess,
        uint idThread,
        uint dwFlags);

    [DllImport("user32.dll")]
    private static extern bool UnhookWinEvent(IntPtr hWinEventHook);

    private const uint EVENT_SYSTEM_MOVESIZESTART_M = 0x000A;
    private const uint EVENT_SYSTEM_MOVESIZEEND_M = 0x000B;
    private const uint EVENT_SYSTEM_MINIMIZESTART_M = 0x0016;
    private const uint EVENT_SYSTEM_FOREGROUND_M = 0x0003;
    private const uint EVENT_OBJECT_LOCATIONCHANGE_M = 0x800B;
    private const uint WINEVENT_OUTOFCONTEXT_M = 0x0000;
    private const int OBJID_WINDOW_M = 0;
    private const int ManagedWindowEventIgnoreDurationMs = 120;

    private IntPtr _managedWinEventHookMoveSize;
    private IntPtr _managedWinEventHookMinimize;
    private IntPtr _managedWinEventHookLocation;
    private IntPtr _managedWinEventHookForeground;
    private ManagedWinEventDelegate? _managedWinEventProc;
    private IntPtr _managedSyncWindowHandle;
    private bool _isSyncingManagedWindowFromWind;
    private bool _isSyncingWindFromManagedWindow;
    private long _ignoreManagedWindowEventsUntilTick;
    private bool _suppressManagedMinimizeIntent;

    private bool IsManagedWindowSyncSuppressed()
    {
        return _suppressManagedMinimizeIntent ||
               _viewModel.IsWindowPickerOpen ||
               _viewModel.IsCommandPaletteOpen ||
               _viewModel.IsContentTabActive ||
               _viewModel.IsWebTabActive ||
               _viewModel.IsTileVisible;
    }

    private void MirrorManagedWindowMinimizeIntent(IntPtr hwnd)
    {
        if (_managedSyncWindowHandle == IntPtr.Zero || hwnd != _managedSyncWindowHandle)
            return;

        if (_isSyncingManagedWindowFromWind || IsManagedWindowSyncSuppressed())
            return;

        // Keep managed app alive and map minimize intent to Wind minimize.
        RestoreManagedWindowSilently(hwnd);

        if (WindowState != WindowState.Minimized)
        {
            _isSyncingWindFromManagedWindow = true;
            try
            {
                WindowState = WindowState.Minimized;
            }
            finally
            {
                _isSyncingWindFromManagedWindow = false;
            }
        }
    }

    private void EnsureManagedWindowSyncHooks(IntPtr handle)
    {
        if (handle == IntPtr.Zero)
            return;

        if (_managedSyncWindowHandle == handle &&
            (_managedWinEventHookMoveSize != IntPtr.Zero ||
             _managedWinEventHookMinimize != IntPtr.Zero ||
             _managedWinEventHookLocation != IntPtr.Zero ||
             _managedWinEventHookForeground != IntPtr.Zero))
        {
            return;
        }

        RemoveManagedWindowSyncHooks();

        NativeMethods.GetWindowThreadProcessId(handle, out uint processId);
        if (processId == 0)
            return;

        _managedSyncWindowHandle = handle;
        _managedWinEventProc = ManagedWindowWinEventCallback;

        _managedWinEventHookMoveSize = SetWinEventHook(
            EVENT_SYSTEM_MOVESIZESTART_M,
            EVENT_SYSTEM_MOVESIZEEND_M,
            IntPtr.Zero,
            _managedWinEventProc,
            processId,
            0,
            WINEVENT_OUTOFCONTEXT_M);

        _managedWinEventHookMinimize = SetWinEventHook(
            EVENT_SYSTEM_MINIMIZESTART_M,
            EVENT_SYSTEM_MINIMIZESTART_M,
            IntPtr.Zero,
            _managedWinEventProc,
            processId,
            0,
            WINEVENT_OUTOFCONTEXT_M);

        _managedWinEventHookLocation = SetWinEventHook(
            EVENT_OBJECT_LOCATIONCHANGE_M,
            EVENT_OBJECT_LOCATIONCHANGE_M,
            IntPtr.Zero,
            _managedWinEventProc,
            processId,
            0,
            WINEVENT_OUTOFCONTEXT_M);

        // Foreground change must be global to catch transitions from other processes.
        _managedWinEventHookForeground = SetWinEventHook(
            EVENT_SYSTEM_FOREGROUND_M,
            EVENT_SYSTEM_FOREGROUND_M,
            IntPtr.Zero,
            _managedWinEventProc,
            0,
            0,
            WINEVENT_OUTOFCONTEXT_M);
    }

    private void RemoveManagedWindowSyncHooks()
    {
        if (_managedWinEventHookMoveSize != IntPtr.Zero)
        {
            UnhookWinEvent(_managedWinEventHookMoveSize);
            _managedWinEventHookMoveSize = IntPtr.Zero;
        }

        if (_managedWinEventHookMinimize != IntPtr.Zero)
        {
            UnhookWinEvent(_managedWinEventHookMinimize);
            _managedWinEventHookMinimize = IntPtr.Zero;
        }

        if (_managedWinEventHookLocation != IntPtr.Zero)
        {
            UnhookWinEvent(_managedWinEventHookLocation);
            _managedWinEventHookLocation = IntPtr.Zero;
        }

        if (_managedWinEventHookForeground != IntPtr.Zero)
        {
            UnhookWinEvent(_managedWinEventHookForeground);
            _managedWinEventHookForeground = IntPtr.Zero;
        }

        _managedWinEventProc = null;
        _managedSyncWindowHandle = IntPtr.Zero;
    }

    private void ManagedWindowWinEventCallback(
        IntPtr hWinEventHook,
        uint eventType,
        IntPtr hwnd,
        int idObject,
        int idChild,
        uint dwEventThread,
        uint dwmsEventTime)
    {
        if (_managedSyncWindowHandle == IntPtr.Zero || hwnd != _managedSyncWindowHandle)
            return;

        Dispatcher.BeginInvoke(DispatcherPriority.Send, () => OnManagedWindowEvent(eventType, hwnd, idObject));
    }

    private void OnManagedWindowEvent(uint eventType, IntPtr hwnd, int idObject)
    {
        if (_managedSyncWindowHandle == IntPtr.Zero || hwnd != _managedSyncWindowHandle)
            return;

        switch (eventType)
        {
            case EVENT_SYSTEM_FOREGROUND_M:
                EnsureWindBehindManagedWindow(hwnd);
                return;

            case EVENT_SYSTEM_MOVESIZESTART_M:
                if (_isSyncingManagedWindowFromWind ||
                    Environment.TickCount64 <= _ignoreManagedWindowEventsUntilTick)
                {
                    return;
                }

                return;

            case EVENT_SYSTEM_MOVESIZEEND_M:
                if (_isSyncingManagedWindowFromWind ||
                    Environment.TickCount64 <= _ignoreManagedWindowEventsUntilTick)
                {
                    return;
                }

                SyncWindFromManagedWindow();
                return;

            case EVENT_SYSTEM_MINIMIZESTART_M:
                MirrorManagedWindowMinimizeIntent(hwnd);
                return;

            case EVENT_OBJECT_LOCATIONCHANGE_M:
                if (_isSyncingManagedWindowFromWind ||
                    Environment.TickCount64 <= _ignoreManagedWindowEventsUntilTick)
                {
                    return;
                }

                if (idObject != OBJID_WINDOW_M)
                    return;

                if (NativeMethods.IsZoomed(hwnd))
                {
                    HandleManagedWindowMaximize(hwnd);
                    return;
                }

                SyncWindFromManagedWindow();
                return;
        }
    }

    private void EnsureWindBehindManagedWindow(IntPtr hwnd)
    {
        if (hwnd == IntPtr.Zero || !NativeMethods.IsWindow(hwnd))
            return;

        if (WindowState == WindowState.Minimized ||
            IsManagedWindowSyncSuppressed())
        {
            return;
        }

        var windHwnd = new WindowInteropHelper(this).Handle;
        if (windHwnd == IntPtr.Zero || windHwnd == hwnd || !NativeMethods.IsWindow(windHwnd))
            return;

        NativeMethods.SetWindowPos(
            windHwnd,
            hwnd,
            0,
            0,
            0,
            0,
            NativeMethods.SWP_NOMOVE | NativeMethods.SWP_NOSIZE | NativeMethods.SWP_NOACTIVATE);

        UpdateBackdropPosition();
    }

    private void HandleManagedWindowMaximize(IntPtr hwnd)
    {
        // External managed windows must not remain maximized.
        // Immediately cancel their maximize, then mirror maximize onto Wind.
        RestoreManagedWindowSilently(hwnd, ManagedWindowEventIgnoreDurationMs * 3);

        if (WindowState != WindowState.Maximized)
        {
            _isSyncingWindFromManagedWindow = true;
            try
            {
                WindowState = WindowState.Maximized;
            }
            finally
            {
                _isSyncingWindFromManagedWindow = false;
            }
        }

        // Re-apply once after maximize layout settles and once at idle to beat late Z-order updates.
        Dispatcher.BeginInvoke(DispatcherPriority.Loaded, () =>
        {
            UpdateManagedWindowLayout(activate: false);
            Dispatcher.BeginInvoke(DispatcherPriority.ApplicationIdle, () => UpdateManagedWindowLayout(activate: false));
        });
    }

    private void RestoreManagedWindowSilently(IntPtr hwnd, int ignoreDurationMs = ManagedWindowEventIgnoreDurationMs)
    {
        if (hwnd == IntPtr.Zero)
            return;

        _ignoreManagedWindowEventsUntilTick = Environment.TickCount64 + Math.Max(ignoreDurationMs, ManagedWindowEventIgnoreDurationMs);
        _isSyncingManagedWindowFromWind = true;
        try
        {
            NativeMethods.ShowWindow(hwnd, NativeMethods.SW_RESTORE);
        }
        finally
        {
            _isSyncingManagedWindowFromWind = false;
        }
    }

    private void SyncWindFromManagedWindow()
    {
        if (_managedSyncWindowHandle == IntPtr.Zero)
            return;

        if (IsManagedWindowSyncSuppressed())
        {
            return;
        }

        if (!NativeMethods.IsWindow(_managedSyncWindowHandle))
        {
            RemoveManagedWindowSyncHooks();
            return;
        }

        bool isMinimized = NativeMethods.IsIconic(_managedSyncWindowHandle);
        bool isMaximized = NativeMethods.IsZoomed(_managedSyncWindowHandle);

        if (isMaximized)
        {
            HandleManagedWindowMaximize(_managedSyncWindowHandle);
            return;
        }

        _isSyncingWindFromManagedWindow = true;
        try
        {
            if (isMinimized)
            {
                if (WindowState != WindowState.Minimized)
                    WindowState = WindowState.Minimized;
                return;
            }

            if (WindowState == WindowState.Maximized)
                WindowState = WindowState.Normal;

            if (!NativeMethods.GetWindowRect(_managedSyncWindowHandle, out var managedRect))
                return;

            if (!TryGetManagedWindowOffsets(
                    out double dpiScaleX,
                    out double dpiScaleY,
                    out double offsetXPx,
                    out double offsetYPx,
                    out double frameExtraWidthDip,
                    out double frameExtraHeightDip))
            {
                return;
            }

            double nextLeft = managedRect.Left - offsetXPx;
            double nextTop = managedRect.Top - offsetYPx;
            double nextContainerWidthDip = managedRect.Width / dpiScaleX;
            double nextContainerHeightDip = managedRect.Height / dpiScaleY;
            double nextWidth = Math.Max(MinWidth, nextContainerWidthDip + frameExtraWidthDip);
            double nextHeight = Math.Max(MinHeight, nextContainerHeightDip + frameExtraHeightDip);

            const double epsilon = 0.5;
            if (Math.Abs(Left - nextLeft) > epsilon) Left = nextLeft;
            if (Math.Abs(Top - nextTop) > epsilon) Top = nextTop;
            if (Math.Abs(Width - nextWidth) > epsilon) Width = nextWidth;
            if (Math.Abs(Height - nextHeight) > epsilon) Height = nextHeight;
        }
        finally
        {
            _isSyncingWindFromManagedWindow = false;
        }
    }

    private bool TryGetManagedWindowOffsets(
        out double dpiScaleX,
        out double dpiScaleY,
        out double offsetXPx,
        out double offsetYPx,
        out double frameExtraWidthDip,
        out double frameExtraHeightDip)
    {
        dpiScaleX = 1.0;
        dpiScaleY = 1.0;
        offsetXPx = 0;
        offsetYPx = 0;
        frameExtraWidthDip = 0;
        frameExtraHeightDip = 0;

        double containerWidthDip = WindowHostContainer.ActualWidth;
        double containerHeightDip = WindowHostContainer.ActualHeight;
        if (containerWidthDip <= 0 || containerHeightDip <= 0)
            return false;

        var source = PresentationSource.FromVisual(this);
        dpiScaleX = source?.CompositionTarget?.TransformToDevice.M11 ?? 1.0;
        dpiScaleY = source?.CompositionTarget?.TransformToDevice.M22 ?? 1.0;

        var containerOffsetDip = WindowHostContainer.TranslatePoint(new Point(0, 0), this);
        offsetXPx = containerOffsetDip.X * dpiScaleX;
        offsetYPx = containerOffsetDip.Y * dpiScaleY;

        frameExtraWidthDip = ActualWidth - containerWidthDip;
        frameExtraHeightDip = ActualHeight - containerHeightDip;
        return true;
    }

    // ── Managed-window layout ─────────────────────────────────────────────────

    /// <summary>
    /// Called by Window_StateChanged to handle WinUI3/externally managed window
    /// state changes without mixing that logic into the embedded-app path.
    /// <paramref name="wasRestored"/> is true when the previous state was minimized
    /// and the window is now being restored (i.e. coming out of minimize).
    /// </summary>
    private void OnWindStateChangedManagedSync(bool wasRestored)
    {
        if (wasRestored)
        {
            // Allow UpdateManagedWindowLayout to bring the managed window to the
            // foreground regardless of whether the handle has changed since minimize.
            _activeManagedWindowHandle = IntPtr.Zero;
        }

        UpdateManagedWindowLayout(activate: false);
    }

    private void UpdateManagedWindowLayout(bool activate)
    {
        if (_isSyncingWindFromManagedWindow)
            return;

        IntPtr targetHandle = IntPtr.Zero;
        bool canShowManagedWindow =
            !_viewModel.IsWindowPickerOpen &&
            !_viewModel.IsCommandPaletteOpen &&
            !_viewModel.IsContentTabActive &&
            !_viewModel.IsWebTabActive &&
            !_viewModel.IsTileVisible &&
            WindowState != WindowState.Minimized &&
            _viewModel.SelectedTab != null &&
            _viewModel.TryGetExternallyManagedWindowHandle(_viewModel.SelectedTab, out targetHandle);

        if (!canShowManagedWindow)
        {
            targetHandle = IntPtr.Zero;
        }

        if (targetHandle == IntPtr.Zero)
        {
            _suppressManagedMinimizeIntent = true;
            try
            {
                _windowManager.MinimizeAllManagedWindowsExcept(targetHandle);
                RemoveManagedWindowSyncHooks();
                _activeManagedWindowHandle = IntPtr.Zero;
            }
            finally
            {
                _suppressManagedMinimizeIntent = false;
            }

            return;
        }

        _windowManager.MinimizeAllManagedWindowsExcept(targetHandle);

        EnsureManagedWindowSyncHooks(targetHandle);

        bool bringToFront = activate || targetHandle != _activeManagedWindowHandle;
        var windHwnd = new WindowInteropHelper(this).Handle;

        if (!TryGetManagedWindowBounds(out var bounds))
        {
            // Layout is not ready yet. Keep the managed window at its current position
            // without updating _activeManagedWindowHandle, so the next call (once layout
            // has settled) still treats this as a first-time activation and can set
            // bringToFront correctly.
            if (NativeMethods.GetWindowRect(targetHandle, out var currentRect))
            {
                _ignoreManagedWindowEventsUntilTick = Environment.TickCount64 + ManagedWindowEventIgnoreDurationMs;
                _isSyncingManagedWindowFromWind = true;
                try
                {
                    _windowManager.ActivateManagedWindow(
                        targetHandle,
                        currentRect.Left,
                        currentRect.Top,
                        Math.Max(1, currentRect.Width),
                        Math.Max(1, currentRect.Height),
                        bringToFront: false,
                        windHwnd);
                }
                finally
                {
                    _isSyncingManagedWindowFromWind = false;
                }
            }

            return;
        }

        _ignoreManagedWindowEventsUntilTick = Environment.TickCount64 + ManagedWindowEventIgnoreDurationMs;
        _isSyncingManagedWindowFromWind = true;
        try
        {
            _windowManager.ActivateManagedWindow(
                targetHandle,
                bounds.Left,
                bounds.Top,
                bounds.Width,
                bounds.Height,
                bringToFront,
                windHwnd);
        }
        finally
        {
            _isSyncingManagedWindowFromWind = false;
        }

        _activeManagedWindowHandle = targetHandle;
    }

    private bool TryGetManagedWindowBounds(out NativeMethods.RECT bounds)
    {
        bounds = default;

        double widthDip = WindowHostContainer.ActualWidth;
        double heightDip = WindowHostContainer.ActualHeight;
        if (widthDip <= 0 || heightDip <= 0)
            return false;

        var hwnd = new WindowInteropHelper(this).Handle;
        if (hwnd == IntPtr.Zero)
            return false;

        if (!NativeMethods.GetWindowRect(hwnd, out var windRect))
            return false;

        var source = PresentationSource.FromVisual(this);
        double dpiScaleX = source?.CompositionTarget?.TransformToDevice.M11 ?? 1.0;
        double dpiScaleY = source?.CompositionTarget?.TransformToDevice.M22 ?? 1.0;

        Point contentOffsetDip = WindowHostContainer.TranslatePoint(new Point(0, 0), this);
        int left = windRect.Left + (int)Math.Round(contentOffsetDip.X * dpiScaleX);
        int top = windRect.Top + (int)Math.Round(contentOffsetDip.Y * dpiScaleY);
        int width = Math.Max(1, (int)Math.Round(widthDip * dpiScaleX));
        int height = Math.Max(1, (int)Math.Round(heightDip * dpiScaleY));

        bounds = new NativeMethods.RECT
        {
            Left = left,
            Top = top,
            Right = left + width,
            Bottom = top + height
        };
        return true;
    }
}
