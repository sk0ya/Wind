using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Interop;
using Wind.Interop;
using Wind.Models;

namespace Wind.Services;

public class DragDropService
{
    private readonly WindowManager _windowManager;
    private readonly TabManager _tabManager;
    private HwndSource? _hwndSource;
    private bool _isDragging;
    private IntPtr _draggedWindow;

    public event EventHandler<WindowInfo>? WindowDropped;

    public DragDropService(WindowManager windowManager, TabManager tabManager)
    {
        _windowManager = windowManager;
        _tabManager = tabManager;
    }

    public void Initialize(Window window)
    {
        var helper = new WindowInteropHelper(window);
        _hwndSource = HwndSource.FromHwnd(helper.Handle);
    }

    public void StartDragDrop(IntPtr windowHandle)
    {
        if (windowHandle == IntPtr.Zero) return;

        _isDragging = true;
        _draggedWindow = windowHandle;
    }

    public void EndDragDrop(bool accepted)
    {
        if (!_isDragging) return;

        if (accepted && _draggedWindow != IntPtr.Zero)
        {
            var windowInfo = WindowInfo.FromHandle(_draggedWindow);
            if (windowInfo != null)
            {
                WindowDropped?.Invoke(this, windowInfo);
            }
        }

        _isDragging = false;
        _draggedWindow = IntPtr.Zero;
    }

    public void CancelDragDrop()
    {
        _isDragging = false;
        _draggedWindow = IntPtr.Zero;
    }

    public bool IsDragging => _isDragging;
    public IntPtr DraggedWindow => _draggedWindow;
}
