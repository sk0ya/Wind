using System.Diagnostics;
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
    private Views.SettingsPage? _settingsPage;
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
        var pickerVm = (WindowPickerViewModel)WindowPickerControl.DataContext;
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

        // Wire up hosted window control events
        _tabManager.MinimizeRequested += (s, e) =>
        {
            WindowState = WindowState.Minimized;
        };
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

            Left += dx / dpiScaleX;
            Top += dy / dpiScaleY;
        };

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
        UIElement[] elements = [DragBar, TabBarArea, TabBarSeparator, ContentPanel, WindowPickerOverlay, TabScrollViewer, WindowControlsPanel];
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
        DragBar.Visibility = Visibility.Collapsed;
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
            TabItemsControl.ItemsPanel = (ItemsPanelTemplate)FindResource("VerticalTabPanel");
            TabItemsControl.ItemTemplate = (DataTemplate)FindResource("VerticalTabItemTemplate");
            TabsPanel.Orientation = Orientation.Vertical;
            TabScrollViewer.HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled;
            TabScrollViewer.VerticalScrollBarVisibility = ScrollBarVisibility.Hidden;
            WindowControlsPanel.Orientation = Orientation.Horizontal;
            AddWindowButton.Width = double.NaN;
            AddWindowButton.Height = 36;
            AddWindowButton.HorizontalAlignment = HorizontalAlignment.Stretch;
        }
        else
        {
            TabItemsControl.ItemsPanel = (ItemsPanelTemplate)FindResource("HorizontalTabPanel");
            TabItemsControl.ItemTemplate = (DataTemplate)FindResource("HorizontalTabItemTemplate");
            TabsPanel.Orientation = Orientation.Horizontal;
            TabScrollViewer.HorizontalScrollBarVisibility = ScrollBarVisibility.Hidden;
            TabScrollViewer.VerticalScrollBarVisibility = ScrollBarVisibility.Disabled;
            WindowControlsPanel.Orientation = Orientation.Horizontal;
            AddWindowButton.Width = 36;
            AddWindowButton.Height = 36;
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
        Button[] buttons = [SettingsButton, MinimizeButton, MaximizeButton, CloseButton];

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
        RootGrid.RowDefinitions.Add(new RowDefinition { Height = new GridLength(36) });
        RootGrid.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });

        // DragBar hidden
        DragBar.Visibility = Visibility.Collapsed;
        Grid.SetRow(DragBar, 0);
        Grid.SetColumn(DragBar, 0);
        Grid.SetColumnSpan(DragBar, 1);

        // TabBarArea: Row 0
        Grid.SetRow(TabBarArea, 0);
        Grid.SetColumn(TabBarArea, 0);
        Grid.SetColumnSpan(TabBarArea, 1);

        // TabBarArea internal: 2 columns [ScrollViewer | Controls]
        TabBarArea.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        TabBarArea.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
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

        // CaptionHeight
        Chrome.CaptionHeight = 36;
    }

    private void ApplyBottomLayout()
    {
        // Grid: 3 rows (6px drag, *, 36px tabs)
        RootGrid.RowDefinitions.Add(new RowDefinition { Height = new GridLength(6) });
        RootGrid.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
        RootGrid.RowDefinitions.Add(new RowDefinition { Height = new GridLength(36) });

        // DragBar: Row 0
        DragBar.Visibility = Visibility.Visible;
        Grid.SetRow(DragBar, 0);
        Grid.SetColumn(DragBar, 0);
        Grid.SetColumnSpan(DragBar, 1);

        // ContentPanel: Row 1
        Grid.SetRow(ContentPanel, 1);
        Grid.SetColumn(ContentPanel, 0);
        Grid.SetColumnSpan(ContentPanel, 1);

        // TabBarArea: Row 2
        Grid.SetRow(TabBarArea, 2);
        Grid.SetColumn(TabBarArea, 0);
        Grid.SetColumnSpan(TabBarArea, 1);

        // TabBarArea internal: 2 columns [ScrollViewer | Controls]
        TabBarArea.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        TabBarArea.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        Grid.SetRow(TabScrollViewer, 0);
        Grid.SetColumn(TabScrollViewer, 0);
        Grid.SetRow(WindowControlsPanel, 0);
        Grid.SetColumn(WindowControlsPanel, 1);

        // Overlay spans all rows
        Grid.SetRowSpan(WindowPickerOverlay, 3);
        Grid.SetColumnSpan(WindowPickerOverlay, 1);

        // CaptionHeight for thin drag bar
        Chrome.CaptionHeight = 6;
    }

    private void ApplyLeftLayout()
    {
        // Grid: 1 row, 3 columns (Auto tabbar, 1px separator, * content)
        RootGrid.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
        RootGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        RootGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1) });
        RootGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });

        // DragBar hidden — drag is handled by the tab bar area
        DragBar.Visibility = Visibility.Collapsed;

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
        TabBarArea.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        TabBarArea.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
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

        Chrome.CaptionHeight = 0;
    }

    private void ApplyRightLayout()
    {
        // Grid: 1 row, 3 columns (* content, 1px separator, Auto tabbar)
        RootGrid.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
        RootGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        RootGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1) });
        RootGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

        // DragBar hidden — drag is handled by the tab bar area
        DragBar.Visibility = Visibility.Collapsed;

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
        TabBarArea.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        TabBarArea.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
        Grid.SetRow(WindowControlsPanel, 0);
        Grid.SetColumn(WindowControlsPanel, 0);
        WindowControlsPanel.HorizontalAlignment = HorizontalAlignment.Stretch;
        Grid.SetRow(TabScrollViewer, 1);
        Grid.SetColumn(TabScrollViewer, 0);

        // Overlay spans everything
        Grid.SetRowSpan(WindowPickerOverlay, 1);
        Grid.SetColumnSpan(WindowPickerOverlay, 3);

        Chrome.CaptionHeight = 0;
    }

    private void ToggleTabBarCollapsed()
    {
        if (_currentTabPosition is not ("Left" or "Right"))
            return;

        _isTabBarCollapsed = !_isTabBarCollapsed;

        if (_isTabBarCollapsed)
        {
            // Collapse: icon-only mode
            TabItemsControl.ItemTemplate = (DataTemplate)FindResource("CollapsedVerticalTabItemTemplate");
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
            TabItemsControl.ItemTemplate = (DataTemplate)FindResource("VerticalTabItemTemplate");
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
        _hotkeyManager.Initialize(this);

        // Hook into Windows messages for proper maximize handling
        var hwnd = new WindowInteropHelper(this).Handle;
        var source = HwndSource.FromHwnd(hwnd);
        source?.AddHook(WndProc);

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

        if (msg == WM_GETMINMAXINFO)
        {
            // Adjust maximize size to respect taskbar
            WmGetMinMaxInfo(hwnd, lParam);
            handled = true;
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

        // Get DPI scale factor
        var source = PresentationSource.FromVisual(this);
        double dpiScaleX = source?.CompositionTarget?.TransformToDevice.M11 ?? 1.0;
        double dpiScaleY = source?.CompositionTarget?.TransformToDevice.M22 ?? 1.0;

        // Get ContentPanel position relative to the main window
        var contentPos = ContentPanel.TranslatePoint(new Point(0, 0), this);
        int grip = (int)(GripSize * dpiScaleX);
        int parentWidth = (int)(ActualWidth * dpiScaleX);
        int parentHeight = (int)(ActualHeight * dpiScaleY);

        switch (_currentTabPosition)
        {
            case "Left":
            {
                // Vertical blocker at the left edge of content (adjacent to tab bar)
                int x = (int)(contentPos.X * dpiScaleX);
                _resizeHelper.SetBlocker(x, 0, grip, parentHeight);
                break;
            }
            case "Right":
            {
                // Vertical blocker at the right edge of content (adjacent to tab bar)
                int x = (int)((contentPos.X + ContentPanel.ActualWidth) * dpiScaleX) - grip;
                _resizeHelper.SetBlocker(x, 0, grip, parentHeight);
                break;
            }
            case "Top":
            {
                // Horizontal blocker at the top edge of content (adjacent to tab bar)
                int y = (int)(contentPos.Y * dpiScaleY);
                _resizeHelper.SetBlocker(0, y, parentWidth, grip);
                break;
            }
            case "Bottom":
            {
                // Horizontal blocker at the bottom edge of content (adjacent to tab bar)
                int y = (int)((contentPos.Y + ContentPanel.ActualHeight) * dpiScaleY) - grip;
                _resizeHelper.SetBlocker(0, y, parentWidth, grip);
                break;
            }
            default:
                _resizeHelper.ClearBlocker();
                break;
        }
    }

    private const int GripSize = 6;

    private void Window_StateChanged(object sender, EventArgs e)
    {
        // Update maximize button icon
        if (WindowState == WindowState.Maximized)
        {
            MaximizeIcon.Symbol = Wpf.Ui.Controls.SymbolRegular.SquareMultiple24;
            MainBorder.BorderThickness = new Thickness(0);
            _resizeHelper?.SetVisible(false);
        }
        else
        {
            MaximizeIcon.Symbol = Wpf.Ui.Controls.SymbolRegular.Maximize24;
            MainBorder.BorderThickness = new Thickness(1);
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

    private void TabArea_PreviewMouseLeftButtonDown(object sender, System.Windows.Input.MouseButtonEventArgs e)
    {
        // Allow drag from empty areas in the tab bar (not on buttons or tabs)
        if (e.OriginalSource is System.Windows.Controls.ScrollViewer ||
            e.OriginalSource is System.Windows.Controls.ScrollContentPresenter ||
            e.OriginalSource is System.Windows.Controls.Grid ||
            e.OriginalSource is System.Windows.Controls.StackPanel)
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

    private void StartDragTracking(System.Windows.Input.MouseButtonEventArgs e)
    {
        _dragStartPoint = e.GetPosition(this);
        _isDragging = false;
        CaptureMouse();
    }

    protected override void OnPreviewMouseMove(System.Windows.Input.MouseEventArgs e)
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

    protected override void OnPreviewMouseLeftButtonUp(System.Windows.Input.MouseButtonEventArgs e)
    {
        base.OnPreviewMouseLeftButtonUp(e);

        if (_dragStartPoint.HasValue)
        {
            _dragStartPoint = null;
            _isDragging = false;
            ReleaseMouseCapture();
        }
    }

    private void SettingsButton_Click(object sender, RoutedEventArgs e)
    {
        _viewModel.OpenSettingsCommand.Execute(null);
    }

    private void TabItem_MouseLeftButtonDown(object sender, System.Windows.Input.MouseButtonEventArgs e)
    {
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
        }
    }

    private void TabItem_MouseRightButtonUp(object sender, System.Windows.Input.MouseButtonEventArgs e)
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
                                var w = (int)b.ActualWidth;
                                var h = (int)b.ActualHeight;
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
        else if (e.PropertyName == nameof(MainViewModel.IsTiled))
        {
            if (!_viewModel.IsTiled)
            {
                // Tile layout fully destroyed — clean up hosts
                Dispatcher.BeginInvoke(DispatcherPriority.Loaded, () =>
                {
                    ClearTileLayout();
                });
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

        var width = (int)WindowHostContainer.ActualWidth;
        var height = (int)WindowHostContainer.ActualHeight;

        width = Math.Max(0, width);
        height = Math.Max(0, height);

        if (width > 0 && height > 0)
        {
            _currentHost.ResizeHostedWindow(width, height);
        }
    }

    private void ShowContentTab(string? contentKey)
    {
        if (contentKey == "Settings")
        {
            _settingsPage ??= App.GetService<Views.SettingsPage>();
            ContentTabContent.Content = _settingsPage;
            ContentTabContainer.Visibility = Visibility.Visible;
        }
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
            TileContainer.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
            // Add row splitter (except after last row)
            if (r < rows - 1)
            {
                TileContainer.RowDefinitions.Add(new RowDefinition { Height = new GridLength(4) });
            }
        }

        for (int c = 0; c < cols; c++)
        {
            TileContainer.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            // Add column splitter (except after last column)
            if (c < cols - 1)
            {
                TileContainer.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(4) });
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
                var w = (int)e.NewSize.Width;
                var h = (int)e.NewSize.Height;
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
                    var w = (int)b.ActualWidth;
                    var h = (int)b.ActualHeight;
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

    private static void GetGridLayout(int count, out int cols, out int rows, out List<(int row, int col, int rowSpan, int colSpan)> assignments)
    {
        assignments = new List<(int, int, int, int)>();

        switch (count)
        {
            case 2:
                cols = 2; rows = 1;
                assignments.Add((0, 0, 1, 1));
                assignments.Add((0, 1, 1, 1));
                break;
            case 3:
                cols = 2; rows = 2;
                assignments.Add((0, 0, 2, 1)); // left, spans 2 rows
                assignments.Add((0, 1, 1, 1)); // right top
                assignments.Add((1, 1, 1, 1)); // right bottom
                break;
            case 4:
                cols = 2; rows = 2;
                assignments.Add((0, 0, 1, 1));
                assignments.Add((0, 1, 1, 1));
                assignments.Add((1, 0, 1, 1));
                assignments.Add((1, 1, 1, 1));
                break;
            default:
                // For 5+, use a roughly square grid
                cols = (int)Math.Ceiling(Math.Sqrt(count));
                rows = (int)Math.Ceiling((double)count / cols);
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
}
