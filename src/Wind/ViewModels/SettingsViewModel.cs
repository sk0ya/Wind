using System.Collections.ObjectModel;
using System.IO;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Microsoft.Win32;
using Wind.Models;
using Wind.Services;

namespace Wind.ViewModels;

public partial class TileSetItem : ObservableObject
{
    [ObservableProperty]
    private string _name;

    [ObservableProperty]
    private ObservableCollection<StartupApplication> _apps = new();

    [ObservableProperty]
    private StartupApplication? _selectedApp;

    [ObservableProperty]
    private StartupApplication? _appToAdd;

    public TileSetItem(string name)
    {
        _name = name;
    }
}

public partial class SettingsViewModel : ObservableObject
{
    private readonly SettingsManager _settingsManager;

    [ObservableProperty]
    private bool _runAtWindowsStartup;

    [ObservableProperty]
    private ObservableCollection<StartupApplication> _startupApplications = new();

    [ObservableProperty]
    private StartupApplication? _selectedStartupApplication;

    [ObservableProperty]
    private string _selectedAppArguments = string.Empty;

    [ObservableProperty]
    private string _selectedTheme = "Dark";

    // Tile Sets
    [ObservableProperty]
    private ObservableCollection<TileSetItem> _tileSets = new();

    [ObservableProperty]
    private string _newTileSetName = string.Empty;

    public SettingsViewModel(SettingsManager settingsManager)
    {
        _settingsManager = settingsManager;
        LoadSettings();
    }

    private void LoadSettings()
    {
        var settings = _settingsManager.Settings;

        RunAtWindowsStartup = _settingsManager.IsRunAtWindowsStartup();
        SelectedTheme = settings.Theme;
        StartupApplications.Clear();
        foreach (var app in settings.StartupApplications)
        {
            StartupApplications.Add(app);
        }

        RebuildTileSets();
    }

    private void RebuildTileSets()
    {
        TileSets.Clear();
        var tileGroups = StartupApplications
            .Where(a => !string.IsNullOrEmpty(a.Tile))
            .GroupBy(a => a.Tile!)
            .OrderBy(g => g.Key);

        foreach (var group in tileGroups)
        {
            var tileSet = new TileSetItem(group.Key);
            foreach (var app in group.OrderBy(a => a.TilePosition ?? int.MaxValue))
            {
                tileSet.Apps.Add(app);
            }
            TileSets.Add(tileSet);
        }
    }

    public ObservableCollection<StartupApplication> GetAvailableAppsForTileSet()
    {
        var usedApps = TileSets.SelectMany(ts => ts.Apps).ToHashSet();
        var available = new ObservableCollection<StartupApplication>();
        foreach (var app in StartupApplications)
        {
            if (!usedApps.Contains(app))
            {
                available.Add(app);
            }
        }
        return available;
    }

    partial void OnSelectedStartupApplicationChanged(StartupApplication? value)
    {
        SelectedAppArguments = value?.Arguments ?? string.Empty;
    }

    partial void OnSelectedAppArgumentsChanged(string value)
    {
        if (SelectedStartupApplication != null && SelectedStartupApplication.Arguments != value)
        {
            SelectedStartupApplication.Arguments = value;
            _settingsManager.SaveStartupApplication();
        }
    }

    partial void OnRunAtWindowsStartupChanged(bool value)
    {
        _settingsManager.SetRunAtWindowsStartup(value);
    }

    partial void OnSelectedThemeChanged(string value)
    {
        _settingsManager.Settings.Theme = value;
        _settingsManager.SaveSettings();
        ApplyTheme(value);
    }

    [RelayCommand]
    private void AddStartupApplication()
    {
        var dialog = new OpenFileDialog
        {
            Title = "Select Application",
            Filter = "Executable Files (*.exe)|*.exe|All Files (*.*)|*.*",
            CheckFileExists = true
        };

        if (dialog.ShowDialog() == true)
        {
            var path = dialog.FileName;
            var name = Path.GetFileNameWithoutExtension(path);

            var app = _settingsManager.AddStartupApplication(path, "", name);
            StartupApplications.Add(app);
        }
    }

    [RelayCommand]
    private void RemoveStartupApplication()
    {
        if (SelectedStartupApplication == null) return;

        // Also remove from any tile set
        foreach (var ts in TileSets)
        {
            ts.Apps.Remove(SelectedStartupApplication);
        }

        _settingsManager.RemoveStartupApplication(SelectedStartupApplication);
        StartupApplications.Remove(SelectedStartupApplication);
        SelectedStartupApplication = null;
    }

    // --- Tile Set commands ---

    [RelayCommand]
    private void AddTileSet()
    {
        if (string.IsNullOrWhiteSpace(NewTileSetName)) return;
        if (TileSets.Any(ts => ts.Name.Equals(NewTileSetName, StringComparison.OrdinalIgnoreCase))) return;

        TileSets.Add(new TileSetItem(NewTileSetName));
        NewTileSetName = string.Empty;
    }

    [RelayCommand]
    private void RemoveTileSet(TileSetItem tileSet)
    {
        foreach (var app in tileSet.Apps)
        {
            app.Tile = null;
            app.TilePosition = null;
            _settingsManager.SaveStartupApplication();
        }
        TileSets.Remove(tileSet);
    }

    [RelayCommand]
    private void AddAppToTileSet(TileSetItem tileSet)
    {
        if (tileSet.AppToAdd == null) return;

        var app = tileSet.AppToAdd;
        app.Tile = tileSet.Name;
        app.TilePosition = tileSet.Apps.Count;
        tileSet.Apps.Add(app);
        tileSet.AppToAdd = null;
        _settingsManager.SaveStartupApplication();
    }

    [RelayCommand]
    private void RemoveAppFromTileSet(TileSetItem tileSet)
    {
        if (tileSet.SelectedApp == null) return;

        var app = tileSet.SelectedApp;
        app.Tile = null;
        app.TilePosition = null;
        tileSet.Apps.Remove(app);
        tileSet.SelectedApp = null;
        UpdateTilePositions(tileSet);
        _settingsManager.SaveStartupApplication();
    }

    [RelayCommand]
    private void MoveAppUpInTileSet(TileSetItem tileSet)
    {
        if (tileSet.SelectedApp == null) return;

        var index = tileSet.Apps.IndexOf(tileSet.SelectedApp);
        if (index <= 0) return;

        tileSet.Apps.Move(index, index - 1);
        UpdateTilePositions(tileSet);
    }

    [RelayCommand]
    private void MoveAppDownInTileSet(TileSetItem tileSet)
    {
        if (tileSet.SelectedApp == null) return;

        var index = tileSet.Apps.IndexOf(tileSet.SelectedApp);
        if (index < 0 || index >= tileSet.Apps.Count - 1) return;

        tileSet.Apps.Move(index, index + 1);
        UpdateTilePositions(tileSet);
    }

    private void UpdateTilePositions(TileSetItem tileSet)
    {
        for (int i = 0; i < tileSet.Apps.Count; i++)
        {
            tileSet.Apps[i].TilePosition = i;
        }
        _settingsManager.SaveStartupApplication();
    }

    private void ApplyTheme(string theme)
    {
        var wpfuiTheme = theme switch
        {
            "Light" => Wpf.Ui.Appearance.ApplicationTheme.Light,
            "Dark" => Wpf.Ui.Appearance.ApplicationTheme.Dark,
            _ => Wpf.Ui.Appearance.ApplicationTheme.Dark
        };

        Wpf.Ui.Appearance.ApplicationThemeManager.Apply(wpfuiTheme);
    }
}
