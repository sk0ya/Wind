using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Threading;
using Wind.Interop;
using Wind.Models;
using Wind.Services;
using Wind.ViewModels;
using Wind.Views;

namespace Wind;

public partial class MainWindow : Window
{
    private readonly MainViewModel _viewModel;
    private readonly HotkeyManager _hotkeyManager;
    private readonly TabManager _tabManager;
    private readonly SettingsManager _settingsManager;
    private WindowHost? _currentHost;
    private Point? _dragStartPoint;
    private bool _isDragging;
    private readonly List<WindowHost> _tiledHosts = new();
    private Views.GeneralSettingsPage? _generalSettingsPage;
    private Views.HotkeySettingsPage? _hotkeySettingsPage;
    private Views.StartupSettingsPage? _startupSettingsPage;
    private Views.QuickLaunchSettingsPage? _quickLaunchSettingsPage;
    private Views.ProcessInfoPage? _processInfoPage;
    private string _currentTabPosition = "Top";
    private WindowResizeHelper? _resizeHelper;
    private bool _isTabBarCollapsed;

    public MainWindow()
    {
        InitializeComponent();

        _viewModel = App.GetService<MainViewModel>();
        _hotkeyManager = App.GetService<HotkeyManager>();
        _tabManager = App.GetService<TabManager>();
        _settingsManager = App.GetService<SettingsManager>();

        DataContext = _viewModel;
        WindowPickerControl.DataContext = App.GetService<WindowPickerViewModel>();

        Loaded += MainWindow_Loaded;
        Closing += MainWindow_Closing;
        SizeChanged += MainWindow_SizeChanged;
        Activated += MainWindow_Activated;
        WindowHostContainer.SizeChanged += WindowHostContainer_SizeChanged;

        _viewModel.PropertyChanged += ViewModel_PropertyChanged;

        // Wire up window picker events
        var pickerVm = (WindowPickerViewModel) WindowPickerControl.DataContext;
        pickerVm.WindowSelected += (s, window) =>
        {
            _viewModel.AddWindowCommand.Execute(window);
            RestoreEmbeddedWindow();
        };
        pickerVm.Cancelled += (s, e) =>
        {
            _viewModel.CloseWindowPickerCommand.Execute(null);
            RestoreEmbeddedWindow();
        };

        // Wire up command palette events
        CommandPaletteControl.DataContext = App.GetService<CommandPaletteViewModel>();
        var paletteVm = (CommandPaletteViewModel)CommandPaletteControl.DataContext;
        paletteVm.ItemExecuted += OnCommandPaletteItemExecuted;
        paletteVm.Cancelled += (s, e) =>
        {
            _viewModel.CloseCommandPaletteCommand.Execute(null);
            RestoreEmbeddedWindow();
        };

        // Wire up hosted window control events
        _tabManager.MinimizeRequested += (s, e) => { WindowState = WindowState.Minimized; };
        _tabManager.MaximizeRequested += (s, e) =>
        {
            WindowState = WindowState == WindowState.Maximized ? WindowState.Normal : WindowState.Maximized;
        };
        _tabManager.MoveRequested += (dx, dy) =>
        {
            // Convert physical pixels to WPF device-independent units
            var source = PresentationSource.FromVisual(this);
            double dpiScaleX = source?.CompositionTarget?.TransformToDevice.M11 ?? 1.0;
            double dpiScaleY = source?.CompositionTarget?.TransformToDevice.M22 ?? 1.0;

            if (WindowState == WindowState.Maximized)
            {
                // Get current cursor position in screen coordinates
                NativeMethods.GetCursorPos(out var cursorPos);

                // Calculate relative X position within the maximized window
                var relativeX = (cursorPos.X - Left * dpiScaleX) / (ActualWidth * dpiScaleX);

                // Restore window
                WindowState = WindowState.Normal;

                // Position window so cursor stays at the same relative position
                Left = cursorPos.X / dpiScaleX - (Width * relativeX);
                Top = cursorPos.Y / dpiScaleY - 18; // Half of title bar height
            }
            else
            {
                Left += dx / dpiScaleX;
                Top += dy / dpiScaleY;
            }
        };
        _tabManager.CloseWindRequested += (s, e) => { Close(); };

        _tabManager.TileLayoutUpdated += (s, e) =>
        {
            Dispatcher.BeginInvoke(DispatcherPriority.Loaded, () =>
            {
                if (_viewModel.CurrentTileLayout != null)
                {
                    ClearTileLayout();
                    BuildTileLayout(_viewModel.CurrentTileLayout);
                }
            });
        };

        // Subscribe to tab position changes
        _settingsManager.TabHeaderPositionChanged += OnTabHeaderPositionChanged;

        // Apply initial tab position
        ApplyTabHeaderPosition(_settingsManager.Settings.TabHeaderPosition);
    }

    private void OnTabHeaderPositionChanged(string position)
    {
        Dispatcher.BeginInvoke(DispatcherPriority.Normal, () =>
        {
            ApplyTabHeaderPosition(position);
            UpdateWindowHostSize();
            Dispatcher.BeginInvoke(DispatcherPriority.Loaded, UpdateBlockerPosition);
        });
    }

    private void ResetLayoutProperties()
    {
        // Clear all grid definitions
        RootGrid.RowDefinitions.Clear();
        RootGrid.ColumnDefinitions.Clear();
        TabBarArea.RowDefinitions.Clear();
        TabBarArea.ColumnDefinitions.Clear();

        // Reset Grid attached properties for all major elements to defaults
        UIElement[] elements =
        [
            TabBarArea, TabBarSeparator, ContentPanel, WindowPickerOverlay, CommandPaletteOverlay,
            TabScrollViewer, WindowControlsPanel
        ];
        foreach (var el in elements)
        {
            Grid.SetRow(el, 0);
            Grid.SetColumn(el, 0);
            Grid.SetRowSpan(el, 1);
            Grid.SetColumnSpan(el, 1);
        }

        // Reset TabBarArea sizing (set by Left/Right layouts)
        TabBarArea.ClearValue(MinWidthProperty);
        TabBarArea.ClearValue(MaxWidthProperty);
        TabBarArea.ClearValue(MinHeightProperty);
        TabBarArea.ClearValue(MaxHeightProperty);
        TabBarArea.ClearValue(WidthProperty);
        TabBarArea.ClearValue(HeightProperty);

        // Reset WindowControlsPanel alignment
        WindowControlsPanel.ClearValue(HorizontalAlignmentProperty);
        WindowControlsPanel.ClearValue(VerticalAlignmentProperty);

        // Reset DragBar and Separator
        TabBarSeparator.Visibility = Visibility.Collapsed;
        TabBarSeparator.ClearValue(WidthProperty);
        TabBarSeparator.ClearValue(HeightProperty);

        // Reset collapsed state
        _isTabBarCollapsed = false;
        AddWindowButton.Visibility = Visibility.Visible;
    }

    private void ApplyTabHeaderPosition(string position)
    {
        _currentTabPosition = position;
        bool isVertical = position is "Left" or "Right";

        // Full reset of all layout properties from previous layout
        ResetLayoutProperties();

        // Configure tab items and scroll for orientation
        // Reset AddWindowButton local values
        AddWindowButton.ClearValue(WidthProperty);
        AddWindowButton.ClearValue(HeightProperty);
        AddWindowButton.ClearValue(HorizontalAlignmentProperty);

        if (isVertical)
        {
            TabItemsControl.ItemsPanel = (ItemsPanelTemplate) FindResource("VerticalTabPanel");
            TabItemsControl.ItemTemplate = (DataTemplate) FindResource("VerticalTabItemTemplate");
            TabsPanel.Orientation = Orientation.Vertical;
            TabScrollViewer.HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled;
            TabScrollViewer.VerticalScrollBarVisibility = ScrollBarVisibility.Hidden;
            WindowControlsPanel.Orientation = Orientation.Horizontal;
            AddWindowButton.Width = double.NaN;
            AddWindowButton.Height = 36;
            AddWindowButton.HorizontalAlignment = HorizontalAlignment.Stretch;

            // Set accent bar side: Left position → right accent, Right position → left accent
            Resources["VerticalTabAccentThickness"] = position == "Left"
                ? new Thickness(0, 0, 2, 0)
                : new Thickness(2, 0, 0, 0);
        }
        else
        {
            TabItemsControl.ItemsPanel = (ItemsPanelTemplate) FindResource("HorizontalTabPanel");
            TabItemsControl.ItemTemplate = (DataTemplate) FindResource("HorizontalTabItemTemplate");
            TabsPanel.Orientation = Orientation.Horizontal;
            TabScrollViewer.HorizontalScrollBarVisibility = ScrollBarVisibility.Hidden;
            TabScrollViewer.VerticalScrollBarVisibility = ScrollBarVisibility.Disabled;
            WindowControlsPanel.Orientation = Orientation.Horizontal;
            AddWindowButton.Width = 36;
            AddWindowButton.Height = 36;

            // Set accent bar side: Bottom position → top accent, Top position → bottom accent
            Resources["HorizontalTabAccentThickness"] = position == "Bottom"
                ? new Thickness(0, 2, 0, 0)
                : new Thickness(0, 0, 0, 2);
        }

        // Set button sizes for vertical/horizontal mode
        SetButtonSizesForMode(isVertical);

        switch (position)
        {
            case "Top":
                ApplyTopLayout();
                break;
            case "Bottom":
                ApplyBottomLayout();
                break;
            case "Left":
                ApplyLeftLayout();
                break;
            case "Right":
                ApplyRightLayout();
                break;
            default:
                ApplyTopLayout();
                break;
        }
    }

    private void SetButtonSizesForMode(bool isVertical)
    {
        // The TitleBarButtonStyle sets Width=46, Height=36.
        // For vertical mode, we clear the local Width so buttons share the row evenly.
        // For horizontal mode, we clear local values to let the Style apply.
        Button[] buttons = [MenuButton, MinimizeButton, MaximizeButton, CloseButton];

        foreach (var btn in buttons)
        {
            if (isVertical)
            {
                btn.ClearValue(WidthProperty);
                btn.Height = 36;
                btn.HorizontalAlignment = HorizontalAlignment.Stretch;
            }
            else
            {
                btn.ClearValue(WidthProperty);
                btn.ClearValue(HeightProperty);
                btn.ClearValue(HorizontalAlignmentProperty);
            }
        }
    }

    private void ApplyTopLayout()
    {
        // Grid: 2 rows (36px, *)
        RootGrid.RowDefinitions.Add(new RowDefinition {Height = new GridLength(36)});
        RootGrid.RowDefinitions.Add(new RowDefinition {Height = new GridLength(1, GridUnitType.Star)});

        // TabBarArea: Row 0
        Grid.SetRow(TabBarArea, 0);
        Grid.SetColumn(TabBarArea, 0);
        Grid.SetColumnSpan(TabBarArea, 1);

        // TabBarArea internal: 2 columns [ScrollViewer | Controls]
        TabBarArea.ColumnDefinitions.Add(new ColumnDefinition {Width = new GridLength(1, GridUnitType.Star)});
        TabBarArea.ColumnDefinitions.Add(new ColumnDefinition {Width = GridLength.Auto});
        Grid.SetRow(TabScrollViewer, 0);
        Grid.SetColumn(TabScrollViewer, 0);
        Grid.SetRow(WindowControlsPanel, 0);
        Grid.SetColumn(WindowControlsPanel, 1);

        // ContentPanel: Row 1
        Grid.SetRow(ContentPanel, 1);
        Grid.SetColumn(ContentPanel, 0);
        Grid.SetColumnSpan(ContentPanel, 1);

        // Overlay spans all rows
        Grid.SetRowSpan(WindowPickerOverlay, 2);
        Grid.SetColumnSpan(WindowPickerOverlay, 1);
        Grid.SetRowSpan(CommandPaletteOverlay, 2);
        Grid.SetColumnSpan(CommandPaletteOverlay, 1);
    }

    private void ApplyBottomLayout()
    {
        // Grid: 3 rows (6px drag, *, 36px tabs)
        RootGrid.RowDefinitions.Add(new RowDefinition {Height = new GridLength(0)});
        RootGrid.RowDefinitions.Add(new RowDefinition {Height = new GridLength(1, GridUnitType.Star)});
        RootGrid.RowDefinitions.Add(new RowDefinition {Height = new GridLength(36)});

        // ContentPanel: Row 1
        Grid.SetRow(ContentPanel, 1);
        Grid.SetColumn(ContentPanel, 0);
        Grid.SetColumnSpan(ContentPanel, 1);

        // TabBarArea: Row 2
        Grid.SetRow(TabBarArea, 2);
        Grid.SetColumn(TabBarArea, 0);
        Grid.SetColumnSpan(TabBarArea, 1);

        // TabBarArea internal: 2 columns [ScrollViewer | Controls]
        TabBarArea.ColumnDefinitions.Add(new ColumnDefinition {Width = new GridLength(1, GridUnitType.Star)});
        TabBarArea.ColumnDefinitions.Add(new ColumnDefinition {Width = GridLength.Auto});
        Grid.SetRow(TabScrollViewer, 0);
        Grid.SetColumn(TabScrollViewer, 0);
        Grid.SetRow(WindowControlsPanel, 0);
        Grid.SetColumn(WindowControlsPanel, 1);

        // Overlay spans all rows
        Grid.SetRowSpan(WindowPickerOverlay, 3);
        Grid.SetColumnSpan(WindowPickerOverlay, 1);
        Grid.SetRowSpan(CommandPaletteOverlay, 3);
        Grid.SetColumnSpan(CommandPaletteOverlay, 1);
    }

    private void ApplyLeftLayout()
    {
        // Grid: 1 row, 3 columns (Auto tabbar, 1px separator, * content)
        RootGrid.RowDefinitions.Add(new RowDefinition {Height = new GridLength(1, GridUnitType.Star)});
        RootGrid.ColumnDefinitions.Add(new ColumnDefinition {Width = GridLength.Auto});
        RootGrid.ColumnDefinitions.Add(new ColumnDefinition {Width = new GridLength(1)});
        RootGrid.ColumnDefinitions.Add(new ColumnDefinition {Width = new GridLength(1, GridUnitType.Star)});

        // TabBarArea: Row 0, Column 0 (vertical)
        Grid.SetRow(TabBarArea, 0);
        Grid.SetColumn(TabBarArea, 0);
        TabBarArea.MinWidth = 180;
        TabBarArea.MaxWidth = 300;

        // Separator: Row 0, Column 1
        TabBarSeparator.Visibility = Visibility.Visible;
        TabBarSeparator.Width = 1;
        Grid.SetRow(TabBarSeparator, 0);
        Grid.SetColumn(TabBarSeparator, 1);

        // TabBarArea internal: 2 rows [ButtonsPanel | ScrollViewer]
        TabBarArea.RowDefinitions.Add(new RowDefinition {Height = GridLength.Auto});
        TabBarArea.RowDefinitions.Add(new RowDefinition {Height = new GridLength(1, GridUnitType.Star)});
        Grid.SetRow(WindowControlsPanel, 0);
        Grid.SetColumn(WindowControlsPanel, 0);
        WindowControlsPanel.HorizontalAlignment = HorizontalAlignment.Stretch;
        Grid.SetRow(TabScrollViewer, 1);
        Grid.SetColumn(TabScrollViewer, 0);

        // ContentPanel: Row 0, Column 2
        Grid.SetRow(ContentPanel, 0);
        Grid.SetColumn(ContentPanel, 2);

        // Overlay spans everything
        Grid.SetRowSpan(WindowPickerOverlay, 1);
        Grid.SetColumnSpan(WindowPickerOverlay, 3);
        Grid.SetRowSpan(CommandPaletteOverlay, 1);
        Grid.SetColumnSpan(CommandPaletteOverlay, 3);
    }

    private void ApplyRightLayout()
    {
        // Grid: 1 row, 3 columns (* content, 1px separator, Auto tabbar)
        RootGrid.RowDefinitions.Add(new RowDefinition {Height = new GridLength(1, GridUnitType.Star)});
        RootGrid.ColumnDefinitions.Add(new ColumnDefinition {Width = new GridLength(1, GridUnitType.Star)});
        RootGrid.ColumnDefinitions.Add(new ColumnDefinition {Width = new GridLength(1)});
        RootGrid.ColumnDefinitions.Add(new ColumnDefinition {Width = GridLength.Auto});

        // ContentPanel: Row 0, Column 0
        Grid.SetRow(ContentPanel, 0);
        Grid.SetColumn(ContentPanel, 0);

        // Separator: Row 0, Column 1
        TabBarSeparator.Visibility = Visibility.Visible;
        TabBarSeparator.Width = 1;
        Grid.SetRow(TabBarSeparator, 0);
        Grid.SetColumn(TabBarSeparator, 1);

        // TabBarArea: Row 0, Column 2 (vertical)
        Grid.SetRow(TabBarArea, 0);
        Grid.SetColumn(TabBarArea, 2);
        TabBarArea.MinWidth = 180;
        TabBarArea.MaxWidth = 300;

        // TabBarArea internal: 2 rows [ButtonsPanel | ScrollViewer]
        TabBarArea.RowDefinitions.Add(new RowDefinition {Height = GridLength.Auto});
        TabBarArea.RowDefinitions.Add(new RowDefinition {Height = new GridLength(1, GridUnitType.Star)});
        Grid.SetRow(WindowControlsPanel, 0);
        Grid.SetColumn(WindowControlsPanel, 0);
        WindowControlsPanel.HorizontalAlignment = HorizontalAlignment.Stretch;
        Grid.SetRow(TabScrollViewer, 1);
        Grid.SetColumn(TabScrollViewer, 0);

        // Overlay spans everything
        Grid.SetRowSpan(WindowPickerOverlay, 1);
        Grid.SetColumnSpan(WindowPickerOverlay, 3);
        Grid.SetRowSpan(CommandPaletteOverlay, 1);
        Grid.SetColumnSpan(CommandPaletteOverlay, 3);
    }

    private void ToggleTabBarCollapsed()
    {
        if (_currentTabPosition is not ("Left" or "Right"))
            return;

        _isTabBarCollapsed = !_isTabBarCollapsed;

        if (_isTabBarCollapsed)
        {
            // Collapse: icon-only mode
            TabItemsControl.ItemTemplate = (DataTemplate) FindResource("CollapsedVerticalTabItemTemplate");
            TabBarArea.MinWidth = 0;
            TabBarArea.MaxWidth = double.PositiveInfinity;
            TabBarArea.Width = 40;
            AddWindowButton.Visibility = Visibility.Collapsed;

            // Hide window control button text, keep icons compact
            foreach (UIElement child in WindowControlsPanel.Children)
            {
                if (child is Button btn)
                {
                    btn.Width = 36;
                    btn.Height = 28;
                }
            }
        }
        else
        {
            // Expand: restore full vertical mode
            TabItemsControl.ItemTemplate = (DataTemplate) FindResource("VerticalTabItemTemplate");
            TabBarArea.ClearValue(WidthProperty);
            TabBarArea.MinWidth = 180;
            TabBarArea.MaxWidth = 300;
            AddWindowButton.Visibility = Visibility.Visible;

            SetButtonSizesForMode(isVertical: true);
        }

        Dispatcher.BeginInvoke(DispatcherPriority.Loaded, () =>
        {
            UpdateWindowHostSize();
            UpdateBlockerPosition();
        });
    }

    private void MainWindow_Loaded(object sender, RoutedEventArgs e)
    {
        _hotkeyManager.Initialize(this, _settingsManager);

        // Hook into Windows messages for proper maximize handling
        var hwnd = new WindowInteropHelper(this).Handle;
        var source = HwndSource.FromHwnd(hwnd);
        source?.AddHook(WndProc);

        // Set window background color to prevent black borders during resize
        if (source?.CompositionTarget != null)
        {
            // Get the current theme background color
            var bgBrush = FindResource("ApplicationBackgroundBrush") as SolidColorBrush;
            if (bgBrush != null)
            {
                source.CompositionTarget.BackgroundColor = bgBrush.Color;
            }
        }

        // Extend DWM frame into client area to prevent black borders
        var margins = new MARGINS { Left = -1, Right = -1, Top = -1, Bottom = -1 };
        DwmExtendFrameIntoClientArea(hwnd, ref margins);

        // Create resize grip overlay windows at the window edges
        _resizeHelper = new WindowResizeHelper(hwnd);
    }

    private void MainWindow_Activated(object? sender, EventArgs e)
    {
        // When Wind window is activated, forward focus to the embedded window
        // only if the mouse is over the content area (not the tab bar).
        Dispatcher.BeginInvoke(DispatcherPriority.Input, () =>
        {
            if (_currentHost == null) return;

            var pos = Mouse.GetPosition(this);
            var contentPos = ContentPanel.TranslatePoint(new Point(0, 0), this);
            var contentRect = new Rect(contentPos, new Size(ContentPanel.ActualWidth, ContentPanel.ActualHeight));

            if (contentRect.Contains(pos))
            {
                _currentHost.FocusHostedWindow();
            }
        });
    }

    private IntPtr WndProc(IntPtr hwnd, int msg, IntPtr wParam, IntPtr lParam, ref bool handled)
    {
        const int WM_GETMINMAXINFO = 0x0024;
        const int WM_ERASEBKGND = 0x0014;

        switch (msg)
        {
            case WM_GETMINMAXINFO:
                // Adjust maximize size to respect taskbar
                WmGetMinMaxInfo(hwnd, lParam);
                handled = true;
                break;

            case WM_ERASEBKGND:
                // Prevent black background flicker during resize
                handled = true;
                return (IntPtr)1;
        }

        return IntPtr.Zero;
    }

    private static void WmGetMinMaxInfo(IntPtr hwnd, IntPtr lParam)
    {
        var mmi = Marshal.PtrToStructure<MINMAXINFO>(lParam);

        var monitor = MonitorFromWindow(hwnd, MONITOR_DEFAULTTONEAREST);
        if (monitor != IntPtr.Zero)
        {
            var monitorInfo = new MONITORINFO();
            monitorInfo.cbSize = Marshal.SizeOf(typeof(MONITORINFO));

            if (GetMonitorInfo(monitor, ref monitorInfo))
            {
                var workArea = monitorInfo.rcWork;
                var monitorArea = monitorInfo.rcMonitor;

                mmi.ptMaxPosition.X = workArea.Left - monitorArea.Left;
                mmi.ptMaxPosition.Y = workArea.Top - monitorArea.Top;
                mmi.ptMaxSize.X = workArea.Right - workArea.Left;
                mmi.ptMaxSize.Y = workArea.Bottom - workArea.Top;
            }
        }

        Marshal.StructureToPtr(mmi, lParam, true);
    }

    private void MainWindow_Closing(object? sender, System.ComponentModel.CancelEventArgs e)
    {
        _settingsManager.TabHeaderPositionChanged -= OnTabHeaderPositionChanged;
        _resizeHelper?.Dispose();
        _resizeHelper = null;
        _viewModel.Cleanup();
    }

    private void MainWindow_SizeChanged(object sender, SizeChangedEventArgs e)
    {
        UpdateWindowHostSize();
        _resizeHelper?.UpdatePositions();
        UpdateBlockerPosition();
    }

    private void WindowHostContainer_SizeChanged(object sender, SizeChangedEventArgs e)
    {
        UpdateWindowHostSize();
    }

    private void UpdateBlockerPosition()
    {
        if (_resizeHelper == null) return;

        // Clear any existing blocker to prevent resize prevention border from showing
        _resizeHelper.ClearBlocker();
    }

    private const int GripSize = 6;

    private void Window_StateChanged(object sender, EventArgs e)
    {
        // Update maximize button icon
        if (WindowState == WindowState.Maximized)
        {
            MaximizeIcon.Symbol = Wpf.Ui.Controls.SymbolRegular.SquareMultiple24;
            _resizeHelper?.SetVisible(false);
        }
        else
        {
            MaximizeIcon.Symbol = Wpf.Ui.Controls.SymbolRegular.Maximize24;
            _resizeHelper?.SetVisible(true);
        }

        UpdateWindowHostSize();
    }

    private void MinimizeButton_Click(object sender, RoutedEventArgs e)
    {
        WindowState = WindowState.Minimized;
    }

    private void MaximizeButton_Click(object sender, RoutedEventArgs e)
    {
        WindowState = WindowState == WindowState.Maximized ? WindowState.Normal : WindowState.Maximized;
    }

    private void CloseButton_Click(object sender, RoutedEventArgs e)
    {
        Close();
    }

    private void TitleBar_MouseLeftButtonDown(object sender, System.Windows.Input.MouseButtonEventArgs e)
    {
        if (e.ClickCount == 2)
        {
            // Double-click on title bar empty area to maximize/restore
            WindowState = WindowState == WindowState.Maximized ? WindowState.Normal : WindowState.Maximized;
        }
        else
        {
            StartDragTracking(e);
        }
    }

    private void TabScrollViewer_PreviewMouseWheel(object sender, MouseWheelEventArgs e)
    {
        if (_currentTabPosition is "Top" or "Bottom")
        {
            // Horizontal tabs: convert vertical wheel to horizontal scroll
            TabScrollViewer.ScrollToHorizontalOffset(TabScrollViewer.HorizontalOffset - e.Delta);
            e.Handled = true;
        }
        // Vertical tabs (Left/Right): default vertical scroll works
    }

    private void TabArea_PreviewMouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        // Allow drag from empty areas in the tab bar (not on buttons or tabs)
        if (e.OriginalSource is ScrollViewer ||
            e.OriginalSource is ScrollContentPresenter ||
            e.OriginalSource is Grid ||
            e.OriginalSource is StackPanel)
        {
            if (e.ClickCount == 2)
            {
                // Double-click on tab area empty space to maximize/restore
                WindowState = WindowState == WindowState.Maximized ? WindowState.Normal : WindowState.Maximized;
                e.Handled = true;
            }
            else
            {
                StartDragTracking(e);
            }
        }
    }

    private void StartDragTracking(MouseButtonEventArgs e)
    {
        _dragStartPoint = e.GetPosition(this);
        _isDragging = false;
        CaptureMouse();
    }

    protected override void OnPreviewMouseMove(MouseEventArgs e)
    {
        base.OnPreviewMouseMove(e);

        if (_dragStartPoint.HasValue && !_isDragging)
        {
            var currentPos = e.GetPosition(this);
            var diff = currentPos - _dragStartPoint.Value;

            if (Math.Abs(diff.X) > SystemParameters.MinimumHorizontalDragDistance ||
                Math.Abs(diff.Y) > SystemParameters.MinimumVerticalDragDistance)
            {
                _isDragging = true;
                ReleaseMouseCapture();

                if (WindowState == WindowState.Maximized)
                {
                    // Get mouse position relative to screen
                    var mousePos = PointToScreen(e.GetPosition(this));

                    // Calculate relative position within window (as percentage)
                    var relativeX = e.GetPosition(this).X / ActualWidth;

                    // Restore window
                    WindowState = WindowState.Normal;

                    // Position window so mouse is at same relative position
                    Left = mousePos.X - (Width * relativeX);
                    Top = mousePos.Y - 18; // Half of title bar height
                }

                DragMove();
                _dragStartPoint = null;
            }
        }
    }

    protected override void OnPreviewMouseLeftButtonUp(MouseButtonEventArgs e)
    {
        base.OnPreviewMouseLeftButtonUp(e);

        if (_dragStartPoint.HasValue)
        {
            _dragStartPoint = null;
            _isDragging = false;
            ReleaseMouseCapture();
        }
    }

    private void MenuButton_Click(object sender, RoutedEventArgs e)
    {
        if (sender is Button btn && btn.ContextMenu != null)
        {
            btn.ContextMenu.PlacementTarget = btn;
            btn.ContextMenu.IsOpen = true;
        }
    }

    private void OpenGeneralSettings_Click(object sender, RoutedEventArgs e)
    {
        _viewModel.OpenContentTabCommand.Execute("GeneralSettings");
    }

    private void OpenHotkeySettings_Click(object sender, RoutedEventArgs e)
    {
        _viewModel.OpenContentTabCommand.Execute("HotkeySettings");
    }

    private void OpenProcessInfo_Click(object sender, RoutedEventArgs e)
    {
        _viewModel.OpenContentTabCommand.Execute("ProcessInfo");
    }

    private void OpenStartupSettings_Click(object sender, RoutedEventArgs e)
    {
        _viewModel.OpenContentTabCommand.Execute("StartupSettings");
    }

    private void OpenQuickLaunchSettings_Click(object sender, RoutedEventArgs e)
    {
        _viewModel.OpenContentTabCommand.Execute("QuickLaunchSettings");
    }

    private void TabItem_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        if (e == null) throw new ArgumentNullException(nameof(e));
        if (sender is FrameworkElement element && element.Tag is Models.TabItem tab)
        {
            if (e.ClickCount == 2 && _currentTabPosition is "Left" or "Right")
            {
                ToggleTabBarCollapsed();
                e.Handled = true;
                return;
            }

            if (Keyboard.Modifiers.HasFlag(ModifierKeys.Control))
            {
                _viewModel.ToggleMultiSelectCommand.Execute(tab);
            }
            else
            {
                _tabManager.ClearMultiSelection();
                _viewModel.SelectTabCommand.Execute(tab);
            }

            e.Handled = true;
        }
    }

    private void TabItem_MouseRightButtonUp(object sender, MouseButtonEventArgs e)
    {
        if (sender is FrameworkElement element && element.Tag is Models.TabItem tab)
        {
            // If right-clicked tab is not multi-selected and no other tabs are multi-selected,
            // auto-select it for the context menu
            var selected = _tabManager.GetMultiSelectedTabs();
            if (selected.Count == 0)
            {
                tab.IsMultiSelected = true;
            }
        }
    }

    private void TileSelectedTabs_Click(object sender, RoutedEventArgs e)
    {
        _viewModel.TileSelectedTabsCommand.Execute(null);
    }

    private void StopTile_Click(object sender, RoutedEventArgs e)
    {
        _viewModel.StopTileCommand.Execute(null);
    }

    private void CloseTab_Click(object sender, RoutedEventArgs e)
    {
        if (sender is FrameworkElement element && element.Tag is Models.TabItem tab)
        {
            _viewModel.CloseTabCommand.Execute(tab);
        }

        e.Handled = true;
    }

    private void TabContextMenu_Opened(object sender, RoutedEventArgs e)
    {
        if (sender is not ContextMenu menu || menu.Tag is not Models.TabItem tab) return;

        bool isWindowTab = !tab.IsContentTab && tab.Window != null;
        string? exePath = tab.Window?.ExecutablePath;

        foreach (var item in menu.Items.OfType<MenuItem>())
        {
            var header = item.Header?.ToString() ?? "";

            if (header.StartsWith("Startup"))
            {
                if (isWindowTab && !string.IsNullOrEmpty(exePath))
                {
                    item.Visibility = Visibility.Visible;
                    bool isRegistered = _settingsManager.IsInStartupApplications(exePath);
                    item.Header = isRegistered ? "Startup から削除" : "Startup に登録";
                }
                else
                {
                    item.Visibility = Visibility.Collapsed;
                }
            }
            else if (header.StartsWith("QuickLaunch"))
            {
                if (isWindowTab && !string.IsNullOrEmpty(exePath))
                {
                    item.Visibility = Visibility.Visible;
                    bool isRegistered = _settingsManager.IsInQuickLaunchApps(exePath);
                    item.Header = isRegistered ? "QuickLaunch から削除" : "QuickLaunch に登録";
                }
                else
                {
                    item.Visibility = Visibility.Collapsed;
                }
            }
            else if (header is "ファイルパスをコピー" or "エクスプローラーで開く" or "タブ名を変更")
            {
                item.Visibility = isWindowTab ? Visibility.Visible : Visibility.Collapsed;
            }
        }
    }

    private void ToggleStartup_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not FrameworkElement el || el.Tag is not Models.TabItem tab) return;
        var exePath = tab.Window?.ExecutablePath;
        if (string.IsNullOrEmpty(exePath)) return;

        if (_settingsManager.IsInStartupApplications(exePath))
        {
            _settingsManager.RemoveStartupApplicationByPath(exePath);
            _viewModel.StatusMessage = $"Startup から削除: {tab.DisplayTitle}";
        }
        else
        {
            _settingsManager.AddStartupApplication(exePath, "", tab.DisplayTitle);
            _viewModel.StatusMessage = $"Startup に登録: {tab.DisplayTitle}";
        }
    }

    private void ToggleQuickLaunch_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not FrameworkElement el || el.Tag is not Models.TabItem tab) return;
        var exePath = tab.Window?.ExecutablePath;
        if (string.IsNullOrEmpty(exePath)) return;

        if (_settingsManager.IsInQuickLaunchApps(exePath))
        {
            _settingsManager.RemoveQuickLaunchAppByPath(exePath);
            _viewModel.StatusMessage = $"QuickLaunch から削除: {tab.DisplayTitle}";
        }
        else
        {
            _settingsManager.AddQuickLaunchApp(exePath, "", tab.DisplayTitle);
            _viewModel.StatusMessage = $"QuickLaunch に登録: {tab.DisplayTitle}";
        }
    }

    private void CopyExePath_Click(object sender, RoutedEventArgs e)
    {
        if (sender is FrameworkElement el && el.Tag is Models.TabItem tab && !string.IsNullOrEmpty(tab.Window?.ExecutablePath))
        {
            Clipboard.SetText(tab.Window.ExecutablePath);
            _viewModel.StatusMessage = "パスをコピーしました";
        }
    }

    private void OpenInExplorer_Click(object sender, RoutedEventArgs e)
    {
        if (sender is FrameworkElement el && el.Tag is Models.TabItem tab && !string.IsNullOrEmpty(tab.Window?.ExecutablePath))
        {
            System.Diagnostics.Process.Start("explorer.exe", $"/select,\"{tab.Window.ExecutablePath}\"");
        }
    }

    private void RenameTab_Click(object sender, RoutedEventArgs e)
    {
        if (sender is FrameworkElement el && el.Tag is Models.TabItem tab)
        {
            var dialog = new Views.RenameDialog(tab.DisplayTitle)
            {
                Owner = this,
                WindowStartupLocation = WindowStartupLocation.CenterOwner
            };

            if (dialog.ShowDialog() == true && !string.IsNullOrWhiteSpace(dialog.ResultName))
            {
                tab.CustomTitle = dialog.ResultName;
                _viewModel.StatusMessage = $"タブ名を変更: {tab.DisplayTitle}";
            }
        }
    }

    private void AddWindowButton_Click(object sender, RoutedEventArgs e)
    {
        _viewModel.OpenWindowPickerCommand.Execute(null);
        _resizeHelper?.SetVisible(false);
        // Hide embedded window(s) while picker is open
        if (_viewModel.IsTileVisible)
        {
            foreach (var host in _tiledHosts)
                host.Visibility = Visibility.Hidden;
        }
        else if (_currentHost != null)
        {
            _currentHost.Visibility = Visibility.Hidden;
        }
    }

    private void RestoreEmbeddedWindow()
    {
        if (_viewModel.IsTileVisible)
        {
            foreach (var host in _tiledHosts)
                host.Visibility = Visibility.Visible;
        }
        else if (_currentHost != null)
        {
            _currentHost.Visibility = Visibility.Visible;
        }

        if (WindowState != WindowState.Maximized)
            _resizeHelper?.SetVisible(true);
    }

    private void OnCommandPaletteItemExecuted(object? sender, Models.CommandPaletteItem item)
    {
        _viewModel.CloseCommandPaletteCommand.Execute(null);
        RestoreEmbeddedWindow();

        switch (item.Tag)
        {
            case QuickLaunchApp app:
                _viewModel.OpenWindowPickerCommand.Execute(null);
                var pickerVm = (WindowPickerViewModel)WindowPickerControl.DataContext;
                pickerVm.LaunchQuickAppCommand.Execute(app);
                break;

            case Models.TabItem tab:
                _viewModel.SelectTabCommand.Execute(tab);
                break;

            case HotkeyAction action:
                switch (action)
                {
                    case HotkeyAction.NewTab:
                        _viewModel.OpenWindowPickerCommand.Execute(null);
                        break;
                    case HotkeyAction.CloseTab:
                        if (_viewModel.SelectedTab != null)
                            _viewModel.CloseTabCommand.Execute(_viewModel.SelectedTab);
                        break;
                }
                break;

            case string s when s == "GeneralSettings":
                _viewModel.OpenContentTabCommand.Execute("GeneralSettings");
                break;
        }
    }

    private void CommandPaletteOverlay_BackgroundClick(object sender, MouseButtonEventArgs e)
    {
        if (e.OriginalSource == CommandPaletteOverlay)
        {
            _viewModel.CloseCommandPaletteCommand.Execute(null);
            RestoreEmbeddedWindow();
        }
    }

    private void ViewModel_PropertyChanged(object? sender, System.ComponentModel.PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(MainViewModel.CurrentWindowHost))
        {
            Dispatcher.BeginInvoke(DispatcherPriority.Loaded, () =>
            {
                // Skip if returning from content tab — the IsContentTabActive handler
                // will call UpdateWindowHost after restoring container visibility.
                if (ContentTabContainer.Visibility == Visibility.Visible)
                    return;

                UpdateWindowHost(_viewModel.CurrentWindowHost);
            });
        }
        else if (e.PropertyName == nameof(MainViewModel.ActiveContentKey))
        {
            // Content tab switched (e.g. Settings → Startup Settings)
            Dispatcher.BeginInvoke(DispatcherPriority.Loaded, () =>
            {
                if (_viewModel.IsContentTabActive)
                {
                    ShowContentTab(_viewModel.ActiveContentKey);
                }
            });
        }
        else if (e.PropertyName == nameof(MainViewModel.IsContentTabActive))
        {
            Dispatcher.BeginInvoke(DispatcherPriority.Loaded, () =>
            {
                if (_viewModel.IsContentTabActive)
                {
                    // Hide window host and tile, show content tab
                    WindowHostContainer.Visibility = Visibility.Collapsed;
                    if (_currentHost != null)
                    {
                        WindowHostContent.Content = null;
                        _currentHost = null;
                    }

                    TileContainer.Visibility = Visibility.Collapsed;
                    ShowContentTab(_viewModel.ActiveContentKey);
                }
                else
                {
                    ContentTabContainer.Visibility = Visibility.Collapsed;
                    ContentTabContent.Content = null;

                    // Restore WindowHostContainer and re-embed the window host in one place
                    // to avoid race conditions with the CurrentWindowHost handler.
                    WindowHostContainer.Visibility = _viewModel.SelectedTab != null
                        ? Visibility.Visible
                        : Visibility.Collapsed;

                    if (_viewModel.CurrentWindowHost != null)
                    {
                        UpdateWindowHost(_viewModel.CurrentWindowHost);
                    }
                }
            });
        }
        else if (e.PropertyName == nameof(MainViewModel.IsTileVisible))
        {
            Dispatcher.BeginInvoke(DispatcherPriority.Loaded, () =>
            {
                if (_viewModel.IsTileVisible)
                {
                    // Switch to tile view
                    WindowHostContainer.Visibility = Visibility.Collapsed;
                    if (_currentHost != null)
                    {
                        WindowHostContent.Content = null;
                        _currentHost = null;
                    }

                    // Only rebuild if tile container is empty (first time or after full stop)
                    if (_tiledHosts.Count == 0 && _viewModel.CurrentTileLayout != null)
                    {
                        BuildTileLayout(_viewModel.CurrentTileLayout);
                    }

                    // Ensure all tiled hosts are visible
                    foreach (var host in _tiledHosts)
                        host.Visibility = Visibility.Visible;

                    TileContainer.Visibility = Visibility.Visible;

                    // Trigger resize for all tiled hosts
                    Dispatcher.BeginInvoke(DispatcherPriority.Render, () =>
                    {
                        foreach (var host in _tiledHosts)
                        {
                            if (host.Parent is ContentControl cc && cc.Parent is Border b)
                            {
                                var w = (int) b.ActualWidth;
                                var h = (int) b.ActualHeight;
                                if (w > 0 && h > 0)
                                    host.ResizeHostedWindow(w, h);
                            }
                        }
                    });
                }
                else
                {
                    // Hide tile view but keep hosts attached
                    // Restore visibility of tiled hosts first (they may have been hidden by picker)
                    foreach (var host in _tiledHosts)
                        host.Visibility = Visibility.Visible;

                    TileContainer.Visibility = Visibility.Collapsed;
                    WindowHostContainer.Visibility = Visibility.Visible;
                }
            });
        }
        else if (e.PropertyName == nameof(MainViewModel.IsCommandPaletteOpen))
        {
            if (_viewModel.IsCommandPaletteOpen)
            {
                _resizeHelper?.SetVisible(false);
                if (_viewModel.IsTileVisible)
                {
                    foreach (var host in _tiledHosts)
                        host.Visibility = Visibility.Hidden;
                }
                else if (_currentHost != null)
                {
                    _currentHost.Visibility = Visibility.Hidden;
                }

                var palVm = (CommandPaletteViewModel)CommandPaletteControl.DataContext;
                palVm.Open();
                CommandPaletteControl.FocusSearch();
            }
            else
            {
                RestoreEmbeddedWindow();
            }
        }
        else if (e.PropertyName == nameof(MainViewModel.IsTiled))
        {
            if (!_viewModel.IsTiled)
            {
                // Tile layout fully destroyed — clean up hosts
                Dispatcher.BeginInvoke(DispatcherPriority.Loaded, () => { ClearTileLayout(); });
            }
        }
    }

    private void UpdateWindowHost(WindowHost? newHost)
    {
        if (_currentHost != null)
        {
            WindowHostContent.Content = null;
            _currentHost = null;
        }

        _currentHost = newHost;

        if (_currentHost != null)
        {
            WindowHostContent.Content = _currentHost;

            Dispatcher.BeginInvoke(DispatcherPriority.Render, () =>
            {
                UpdateWindowHostSize();
                _resizeHelper?.BringToTop();
            });

            Dispatcher.BeginInvoke(DispatcherPriority.Input, () =>
            {
                UpdateWindowHostSize();
                _currentHost?.FocusHostedWindow();
            });
        }
    }

    private void UpdateWindowHostSize()
    {
        if (_currentHost == null) return;

        var width = (int) WindowHostContainer.ActualWidth;
        var height = (int) WindowHostContainer.ActualHeight;

        width = Math.Max(0, width);
        height = Math.Max(0, height);

        if (width > 0 && height > 0)
        {
            _currentHost.ResizeHostedWindow(width, height);
        }
    }

    private void ShowContentTab(string? contentKey)
    {
        UserControl? page = contentKey switch
        {
            "GeneralSettings" => _generalSettingsPage ??= App.GetService<GeneralSettingsPage>(),
            "HotkeySettings" => _hotkeySettingsPage ??= App.GetService<HotkeySettingsPage>(),
            "StartupSettings" => GetStartupSettingsPage(),
            "QuickLaunchSettings" => GetQuickLaunchSettingsPage(),
            "ProcessInfo" => GetProcessInfoPage(),
            _ => null
        };

        if (page != null)
        {
            ContentTabContent.Content = page;
            ContentTabContainer.Visibility = Visibility.Visible;
        }
    }

    private StartupSettingsPage GetStartupSettingsPage()
    {
        if (_startupSettingsPage == null)
        {
            _startupSettingsPage = App.GetService<StartupSettingsPage>();
        }
        else
        {
            ((StartupSettingsViewModel)_startupSettingsPage.DataContext).Reload();
        }
        return _startupSettingsPage;
    }

    private QuickLaunchSettingsPage GetQuickLaunchSettingsPage()
    {
        if (_quickLaunchSettingsPage == null)
        {
            _quickLaunchSettingsPage = App.GetService<QuickLaunchSettingsPage>();
        }
        else
        {
            ((QuickLaunchSettingsViewModel)_quickLaunchSettingsPage.DataContext).Reload();
        }
        return _quickLaunchSettingsPage;
    }

    private ProcessInfoPage GetProcessInfoPage()
    {
        _processInfoPage ??= App.GetService<ProcessInfoPage>();
        ((ProcessInfoViewModel)_processInfoPage.DataContext).Refresh();
        return _processInfoPage;
    }

    #region Tile Layout

    private void BuildTileLayout(TileLayout tileLayout)
    {
        ClearTileLayout();

        var tabs = tileLayout.TiledTabs.ToList();
        if (tabs.Count < 2) return;

        TileContainer.RowDefinitions.Clear();
        TileContainer.ColumnDefinitions.Clear();
        TileContainer.Children.Clear();

        // Calculate grid dimensions
        GetGridLayout(tabs.Count, out int cols, out int rows, out var cellAssignments);

        for (int r = 0; r < rows; r++)
        {
            TileContainer.RowDefinitions.Add(new RowDefinition {Height = new GridLength(1, GridUnitType.Star)});
            // Add row splitter (except after last row)
            if (r < rows - 1)
            {
                TileContainer.RowDefinitions.Add(new RowDefinition {Height = new GridLength(4)});
            }
        }

        for (int c = 0; c < cols; c++)
        {
            TileContainer.ColumnDefinitions.Add(new ColumnDefinition {Width = new GridLength(1, GridUnitType.Star)});
            // Add column splitter (except after last column)
            if (c < cols - 1)
            {
                TileContainer.ColumnDefinitions.Add(new ColumnDefinition {Width = new GridLength(4)});
            }
        }

        // Place each tab's WindowHost in its assigned cell
        for (int i = 0; i < tabs.Count && i < cellAssignments.Count; i++)
        {
            var (row, col, rowSpan, colSpan) = cellAssignments[i];
            var host = _viewModel.GetWindowHost(tabs[i]);
            if (host == null) continue;

            var container = new Border
            {
                ClipToBounds = true,
                Padding = new Thickness(0),
                Margin = new Thickness(0),
            };

            var content = new ContentControl
            {
                HorizontalAlignment = HorizontalAlignment.Stretch,
                VerticalAlignment = VerticalAlignment.Stretch,
                HorizontalContentAlignment = HorizontalAlignment.Stretch,
                VerticalContentAlignment = VerticalAlignment.Stretch,
                Focusable = false,
                Content = host,
            };

            container.Child = content;

            // Map to grid positions (accounting for splitter rows/columns)
            int gridRow = row * 2;
            int gridCol = col * 2;
            int gridRowSpan = rowSpan * 2 - 1;
            int gridColSpan = colSpan * 2 - 1;

            Grid.SetRow(container, gridRow);
            Grid.SetColumn(container, gridCol);
            Grid.SetRowSpan(container, gridRowSpan);
            Grid.SetColumnSpan(container, gridColSpan);

            TileContainer.Children.Add(container);
            _tiledHosts.Add(host);

            // Listen for size changes to resize hosted windows
            container.SizeChanged += (s, e) =>
            {
                var w = (int) e.NewSize.Width;
                var h = (int) e.NewSize.Height;
                if (w > 0 && h > 0)
                {
                    host.ResizeHostedWindow(w, h);
                }
            };
        }

        // Add GridSplitters
        // Vertical splitters (between columns)
        for (int c = 0; c < cols - 1; c++)
        {
            var splitter = new GridSplitter
            {
                Width = 4,
                HorizontalAlignment = HorizontalAlignment.Stretch,
                VerticalAlignment = VerticalAlignment.Stretch,
                Background = new SolidColorBrush(Color.FromArgb(40, 128, 128, 128)),
                ResizeBehavior = GridResizeBehavior.PreviousAndNext,
            };
            Grid.SetColumn(splitter, c * 2 + 1);
            Grid.SetRow(splitter, 0);
            Grid.SetRowSpan(splitter, Math.Max(1, TileContainer.RowDefinitions.Count));
            TileContainer.Children.Add(splitter);
        }

        // Horizontal splitters (between rows)
        for (int r = 0; r < rows - 1; r++)
        {
            var splitter = new GridSplitter
            {
                Height = 4,
                HorizontalAlignment = HorizontalAlignment.Stretch,
                VerticalAlignment = VerticalAlignment.Stretch,
                Background = new SolidColorBrush(Color.FromArgb(40, 128, 128, 128)),
                ResizeDirection = GridResizeDirection.Rows,
                ResizeBehavior = GridResizeBehavior.PreviousAndNext,
            };
            Grid.SetRow(splitter, r * 2 + 1);
            Grid.SetColumn(splitter, 0);
            Grid.SetColumnSpan(splitter, Math.Max(1, TileContainer.ColumnDefinitions.Count));
            TileContainer.Children.Add(splitter);
        }

        // Trigger initial resize
        Dispatcher.BeginInvoke(DispatcherPriority.Render, () =>
        {
            foreach (var host in _tiledHosts)
            {
                if (host.Parent is ContentControl cc && cc.Parent is Border b)
                {
                    var w = (int) b.ActualWidth;
                    var h = (int) b.ActualHeight;
                    if (w > 0 && h > 0)
                        host.ResizeHostedWindow(w, h);
                }
            }
        });
    }

    private void ClearTileLayout()
    {
        // Detach all hosts from tile containers (without disposing them)
        foreach (var child in TileContainer.Children.OfType<Border>().ToList())
        {
            if (child.Child is ContentControl cc)
            {
                cc.Content = null;
            }
        }

        TileContainer.Children.Clear();
        TileContainer.RowDefinitions.Clear();
        TileContainer.ColumnDefinitions.Clear();
        _tiledHosts.Clear();
    }

    private static void GetGridLayout(int count, out int cols, out int rows,
        out List<(int row, int col, int rowSpan, int colSpan)> assignments)
    {
        assignments = new List<(int, int, int, int)>();

        switch (count)
        {
            case 2:
                cols = 2;
                rows = 1;
                assignments.Add((0, 0, 1, 1));
                assignments.Add((0, 1, 1, 1));
                break;
            case 3:
                cols = 2;
                rows = 2;
                assignments.Add((0, 0, 2, 1)); // left, spans 2 rows
                assignments.Add((0, 1, 1, 1)); // right top
                assignments.Add((1, 1, 1, 1)); // right bottom
                break;
            case 4:
                cols = 2;
                rows = 2;
                assignments.Add((0, 0, 1, 1));
                assignments.Add((0, 1, 1, 1));
                assignments.Add((1, 0, 1, 1));
                assignments.Add((1, 1, 1, 1));
                break;
            default:
                // For 5+, use a roughly square grid
                cols = (int) Math.Ceiling(Math.Sqrt(count));
                rows = (int) Math.Ceiling((double) count / cols);
                int idx = 0;
                for (int r = 0; r < rows && idx < count; r++)
                {
                    for (int c = 0; c < cols && idx < count; c++)
                    {
                        assignments.Add((r, c, 1, 1));
                        idx++;
                    }
                }

                // If last row is not full, let the last item span remaining columns
                if (count % cols != 0)
                {
                    var last = assignments[assignments.Count - 1];
                    int remaining = cols - (count % cols);
                    assignments[assignments.Count - 1] = (last.row, last.col, 1, 1 + remaining);
                }

                break;
        }
    }

    #endregion

    #region Win32 Interop for Maximize

    private const int MONITOR_DEFAULTTONEAREST = 2;

    [DllImport("user32.dll")]
    private static extern IntPtr MonitorFromWindow(IntPtr hwnd, int dwFlags);

    [DllImport("user32.dll")]
    private static extern bool GetMonitorInfo(IntPtr hMonitor, ref MONITORINFO lpmi);

    [DllImport("user32.dll")]
    static extern IntPtr DefWindowProc(
        IntPtr hWnd,
        int msg,
        IntPtr wParam,
        IntPtr lParam);

    [StructLayout(LayoutKind.Sequential)]
    private struct POINT
    {
        public int X;
        public int Y;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct MINMAXINFO
    {
        public POINT ptReserved;
        public POINT ptMaxSize;
        public POINT ptMaxPosition;
        public POINT ptMinTrackSize;
        public POINT ptMaxTrackSize;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct RECT
    {
        public int Left;
        public int Top;
        public int Right;
        public int Bottom;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct MONITORINFO
    {
        public int cbSize;
        public RECT rcMonitor;
        public RECT rcWork;
        public int dwFlags;
    }

    #endregion

    #region DWM Interop

    [DllImport("dwmapi.dll")]
    private static extern int DwmExtendFrameIntoClientArea(IntPtr hwnd, ref MARGINS margins);

    [StructLayout(LayoutKind.Sequential)]
    private struct MARGINS
    {
        public int Left;
        public int Right;
        public int Top;
        public int Bottom;
    }

    #endregion
}