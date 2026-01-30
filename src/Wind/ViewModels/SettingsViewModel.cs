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
    private string _closeWindowsOnExit = "None";

    [ObservableProperty]
    private string _selectedTheme = "Dark";

    [ObservableProperty]
    private string _tabHeaderPosition = "Top";

    // Quick Launch
    [ObservableProperty]
    private ObservableCollection<QuickLaunchApp> _quickLaunchApps = new();

    [ObservableProperty]
    private QuickLaunchApp? _selectedQuickLaunchApp;

    [ObservableProperty]
    private string _selectedQuickLaunchAppArguments = string.Empty;

    [ObservableProperty]
    private string _newQuickLaunchPath = string.Empty;

    [ObservableProperty]
    private ObservableCollection<string> _pathSuggestions = new();

    [ObservableProperty]
    private bool _isSuggestionsOpen;

    [ObservableProperty]
    private string? _selectedSuggestion;

    private List<string> _allPathExecutables = new();

    // Tile Sets
    [ObservableProperty]
    private ObservableCollection<TileSetItem> _tileSets = new();

    [ObservableProperty]
    private string _newTileSetName = string.Empty;

    public SettingsViewModel(SettingsManager settingsManager)
    {
        _settingsManager = settingsManager;
        LoadSettings();
        ScanPathExecutables();
    }

    private void LoadSettings()
    {
        var settings = _settingsManager.Settings;

        RunAtWindowsStartup = _settingsManager.IsRunAtWindowsStartup();
        CloseWindowsOnExit = settings.CloseWindowsOnExit;
        SelectedTheme = settings.Theme;
        TabHeaderPosition = settings.TabHeaderPosition;
        StartupApplications.Clear();
        foreach (var app in settings.StartupApplications)
        {
            StartupApplications.Add(app);
        }

        QuickLaunchApps.Clear();
        foreach (var app in settings.QuickLaunchApps)
        {
            QuickLaunchApps.Add(app);
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

    private void ScanPathExecutables()
    {
        var extensions = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
            { ".exe", ".cmd", ".bat", ".com" };

        var pathVar = Environment.GetEnvironmentVariable("PATH") ?? "";
        var dirs = pathVar.Split(';', StringSplitOptions.RemoveEmptyEntries);
        var names = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        foreach (var dir in dirs)
        {
            try
            {
                if (!Directory.Exists(dir)) continue;
                foreach (var file in Directory.EnumerateFiles(dir))
                {
                    var ext = Path.GetExtension(file);
                    if (extensions.Contains(ext))
                    {
                        names.Add(Path.GetFileNameWithoutExtension(file));
                    }
                }
            }
            catch
            {
                // skip inaccessible directories
            }
        }

        _allPathExecutables = names.OrderBy(n => n, StringComparer.OrdinalIgnoreCase).ToList();
    }

    private bool _suppressSuggestions;

    partial void OnNewQuickLaunchPathChanged(string value)
    {
        if (_suppressSuggestions) return;

        if (string.IsNullOrEmpty(value))
        {
            IsSuggestionsOpen = false;
            return;
        }

        List<string> matches;

        if (value.Contains('\\') || value.Contains('/'))
        {
            matches = GetFilePathSuggestions(value);
        }
        else
        {
            var query = value.Trim();
            matches = _allPathExecutables
                .Where(n => n.Contains(query, StringComparison.OrdinalIgnoreCase))
                .Take(10)
                .ToList();
        }

        PathSuggestions.Clear();
        foreach (var m in matches)
        {
            PathSuggestions.Add(m);
        }

        IsSuggestionsOpen = PathSuggestions.Count > 0;
    }

    private static List<string> GetFilePathSuggestions(string input)
    {
        try
        {
            var lastSep = input.LastIndexOfAny(['\\', '/']);
            var dir = input[..(lastSep + 1)];
            var prefix = input[(lastSep + 1)..];

            if (!Directory.Exists(dir)) return [];

            var results = new List<string>();

            // Directories first
            foreach (var d in Directory.EnumerateDirectories(dir))
            {
                var name = Path.GetFileName(d);
                if (name.Contains(prefix, StringComparison.OrdinalIgnoreCase))
                {
                    results.Add(d + "\\");
                }
                if (results.Count >= 15) break;
            }

            // Then files
            foreach (var f in Directory.EnumerateFiles(dir))
            {
                var name = Path.GetFileName(f);
                if (name.Contains(prefix, StringComparison.OrdinalIgnoreCase))
                {
                    results.Add(f);
                }
                if (results.Count >= 15) break;
            }

            return results;
        }
        catch
        {
            return [];
        }
    }

    public void ApplySuggestion(string value)
    {
        _suppressSuggestions = true;
        NewQuickLaunchPath = value;
        IsSuggestionsOpen = false;
        SelectedSuggestion = null;
        _suppressSuggestions = false;

        // If it's a directory, immediately re-trigger to show contents
        if (value.EndsWith('\\'))
        {
            OnNewQuickLaunchPathChanged(value);
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

    partial void OnSelectedQuickLaunchAppChanged(QuickLaunchApp? value)
    {
        SelectedQuickLaunchAppArguments = value?.Arguments ?? string.Empty;
    }

    partial void OnSelectedQuickLaunchAppArgumentsChanged(string value)
    {
        if (SelectedQuickLaunchApp != null && SelectedQuickLaunchApp.Arguments != value)
        {
            SelectedQuickLaunchApp.Arguments = value;
            _settingsManager.SaveQuickLaunchApp();
        }
    }

    partial void OnRunAtWindowsStartupChanged(bool value)
    {
        _settingsManager.SetRunAtWindowsStartup(value);
    }

    partial void OnCloseWindowsOnExitChanged(string value)
    {
        _settingsManager.Settings.CloseWindowsOnExit = value;
        _settingsManager.SaveSettings();
    }

    partial void OnSelectedThemeChanged(string value)
    {
        _settingsManager.Settings.Theme = value;
        _settingsManager.SaveSettings();
        ApplyTheme(value);
    }

    partial void OnTabHeaderPositionChanged(string value)
    {
        _settingsManager.SetTabHeaderPosition(value);
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

    // --- Quick Launch commands ---

    [RelayCommand]
    private void BrowseQuickLaunchApp()
    {
        var dialog = new OpenFileDialog
        {
            Title = "Select Application",
            Filter = "Executable Files (*.exe)|*.exe|All Files (*.*)|*.*",
            CheckFileExists = true
        };

        if (dialog.ShowDialog() == true)
        {
            NewQuickLaunchPath = dialog.FileName;
        }
    }

    [RelayCommand]
    private void AddQuickLaunchApp()
    {
        if (string.IsNullOrWhiteSpace(NewQuickLaunchPath)) return;

        var input = NewQuickLaunchPath.Trim();
        ParsePathAndArguments(input, out var path, out var arguments);
        var name = Path.GetFileNameWithoutExtension(path);

        var app = _settingsManager.AddQuickLaunchApp(path, arguments, name);
        QuickLaunchApps.Add(app);
        NewQuickLaunchPath = string.Empty;
    }

    private static void ParsePathAndArguments(string input, out string path, out string arguments)
    {
        // "C:\Program Files\app.exe" --flag
        if (input.StartsWith('"'))
        {
            var closeQuote = input.IndexOf('"', 1);
            if (closeQuote > 0)
            {
                path = input[1..closeQuote];
                arguments = input[(closeQuote + 1)..].TrimStart();
                return;
            }
        }

        // C:\path\to\app.exe --flag  (split after .exe / .cmd / .bat / .com)
        var extPattern = new[] { ".exe ", ".cmd ", ".bat ", ".com " };
        foreach (var ext in extPattern)
        {
            var idx = input.IndexOf(ext, StringComparison.OrdinalIgnoreCase);
            if (idx >= 0)
            {
                var splitAt = idx + ext.Length - 1; // position of the space
                path = input[..splitAt].Trim();
                arguments = input[(splitAt + 1)..].TrimStart();
                return;
            }
        }

        // Simple command: "code --new-window" → split on first space
        var spaceIdx = input.IndexOf(' ');
        if (spaceIdx > 0)
        {
            path = input[..spaceIdx];
            arguments = input[(spaceIdx + 1)..].TrimStart();
            return;
        }

        path = input;
        arguments = string.Empty;
    }

    [RelayCommand]
    private void RemoveQuickLaunchApp()
    {
        if (SelectedQuickLaunchApp == null) return;

        _settingsManager.RemoveQuickLaunchApp(SelectedQuickLaunchApp);
        QuickLaunchApps.Remove(SelectedQuickLaunchApp);
        SelectedQuickLaunchApp = null;
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
