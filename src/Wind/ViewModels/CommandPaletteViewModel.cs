using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Windows.Data;
using Wind.Models;
using Wind.Services;
using Wpf.Ui.Controls;

namespace Wind.ViewModels;

public partial class CommandPaletteViewModel : ObservableObject
{
    private readonly TabManager _tabManager;
    private readonly SettingsManager _settingsManager;
    private readonly ICollectionView _itemsView;

    public ObservableCollection<CommandPaletteItem> Items { get; } = new();
    public ICollectionView ItemsView => _itemsView;

    [ObservableProperty]
    private string _searchText = string.Empty;

    [ObservableProperty]
    private CommandPaletteItem? _selectedItem;

    public event EventHandler<CommandPaletteItem>? ItemExecuted;
    public event EventHandler? Cancelled;

    public CommandPaletteViewModel(TabManager tabManager, SettingsManager settingsManager)
    {
        _tabManager = tabManager;
        _settingsManager = settingsManager;
        _itemsView = CollectionViewSource.GetDefaultView(Items);
        _itemsView.Filter = FilterItems;
    }

    public void Open()
    {
        SearchText = string.Empty;
        RebuildItems();
        _itemsView.MoveCurrentToFirst();
        SelectedItem = _itemsView.CurrentItem as CommandPaletteItem;
    }

    private void RebuildItems()
    {
        Items.Clear();

        // QuickLaunch apps
        foreach (var app in _settingsManager.Settings.QuickLaunchApps)
        {
            Items.Add(new CommandPaletteItem
            {
                Name = app.Name,
                Category = "QuickLaunch",
                Description = app.Path,
                Tag = app,
                Icon = SymbolRegular.WindowConsole20
            });
        }

        // Open tabs
        foreach (var tab in _tabManager.Tabs)
        {
            Items.Add(new CommandPaletteItem
            {
                Name = tab.Title,
                Category = "Tab",
                Description = "Switch to tab",
                Tag = tab,
                Icon = SymbolRegular.Window24
            });
        }

        // Built-in actions
        Items.Add(new CommandPaletteItem
        {
            Name = "New Tab",
            Category = "Action",
            Description = "Open window picker",
            Tag = HotkeyAction.NewTab,
            Icon = SymbolRegular.Add24
        });
        Items.Add(new CommandPaletteItem
        {
            Name = "Close Tab",
            Category = "Action",
            Description = "Close current tab",
            Tag = HotkeyAction.CloseTab,
            Icon = SymbolRegular.Dismiss24
        });
        Items.Add(new CommandPaletteItem
        {
            Name = "Settings",
            Category = "Action",
            Description = "Open settings",
            Tag = "Settings",
            Icon = SymbolRegular.Settings24
        });
    }

    partial void OnSearchTextChanged(string value)
    {
        _itemsView.Refresh();
        _itemsView.MoveCurrentToFirst();
        SelectedItem = _itemsView.CurrentItem as CommandPaletteItem;
    }

    private bool FilterItems(object obj)
    {
        if (obj is not CommandPaletteItem item) return false;
        if (string.IsNullOrWhiteSpace(SearchText)) return true;
        return item.Name.Contains(SearchText, StringComparison.OrdinalIgnoreCase) ||
               (item.Description?.Contains(SearchText, StringComparison.OrdinalIgnoreCase) ?? false) ||
               item.Category.Contains(SearchText, StringComparison.OrdinalIgnoreCase);
    }

    [RelayCommand]
    private void Execute(CommandPaletteItem? item)
    {
        var target = item ?? SelectedItem;
        if (target != null)
            ItemExecuted?.Invoke(this, target);
    }

    [RelayCommand]
    private void Cancel()
    {
        Cancelled?.Invoke(this, EventArgs.Empty);
    }

    public void MoveSelectionUp()
    {
        _itemsView.MoveCurrentToPrevious();
        if (_itemsView.IsCurrentBeforeFirst)
            _itemsView.MoveCurrentToFirst();
        SelectedItem = _itemsView.CurrentItem as CommandPaletteItem;
    }

    public void MoveSelectionDown()
    {
        _itemsView.MoveCurrentToNext();
        if (_itemsView.IsCurrentAfterLast)
            _itemsView.MoveCurrentToLast();
        SelectedItem = _itemsView.CurrentItem as CommandPaletteItem;
    }
}
