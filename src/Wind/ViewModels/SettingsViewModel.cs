using System.Collections.ObjectModel;
using System.IO;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Microsoft.Win32;
using Wind.Models;
using Wind.Services;

namespace Wind.ViewModels;

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
    private string _selectedTheme = "Dark";

    [ObservableProperty]
    private bool _autoSaveSession = true;

    [ObservableProperty]
    private bool _restoreSessionOnStartup = true;

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
        AutoSaveSession = settings.AutoSaveSession;
        RestoreSessionOnStartup = settings.RestoreSessionOnStartup;

        StartupApplications.Clear();
        foreach (var app in settings.StartupApplications)
        {
            StartupApplications.Add(app);
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

    partial void OnAutoSaveSessionChanged(bool value)
    {
        _settingsManager.Settings.AutoSaveSession = value;
        _settingsManager.SaveSettings();
    }

    partial void OnRestoreSessionOnStartupChanged(bool value)
    {
        _settingsManager.Settings.RestoreSessionOnStartup = value;
        _settingsManager.SaveSettings();
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

            _settingsManager.AddStartupApplication(path, "", name);

            var app = new StartupApplication
            {
                Path = path,
                Name = name,
                Arguments = ""
            };
            StartupApplications.Add(app);
        }
    }

    [RelayCommand]
    private void RemoveStartupApplication()
    {
        if (SelectedStartupApplication == null) return;

        _settingsManager.RemoveStartupApplication(SelectedStartupApplication.Path);
        StartupApplications.Remove(SelectedStartupApplication);
        SelectedStartupApplication = null;
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
