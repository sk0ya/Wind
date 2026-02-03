using CommunityToolkit.Mvvm.ComponentModel;
using Wind.Services;

namespace Wind.ViewModels;

public partial class GeneralSettingsViewModel : ObservableObject
{
    private readonly SettingsManager _settingsManager;

    [ObservableProperty]
    private bool _runAtWindowsStartup;

    [ObservableProperty]
    private string _closeWindowsOnExit = "None";

    [ObservableProperty]
    private string _selectedTheme = "Dark";

    [ObservableProperty]
    private string _tabHeaderPosition = "Top";

    [ObservableProperty]
    private string _embedCloseAction = "CloseApp";

    public GeneralSettingsViewModel(SettingsManager settingsManager)
    {
        _settingsManager = settingsManager;
        LoadSettings();
    }

    private void LoadSettings()
    {
        var settings = _settingsManager.Settings;

        RunAtWindowsStartup = _settingsManager.IsRunAtWindowsStartup();
        CloseWindowsOnExit = settings.CloseWindowsOnExit;
        SelectedTheme = settings.Theme;
        TabHeaderPosition = settings.TabHeaderPosition;
        EmbedCloseAction = settings.EmbedCloseAction;
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

    partial void OnEmbedCloseActionChanged(string value)
    {
        _settingsManager.Settings.EmbedCloseAction = value;
        _settingsManager.SaveSettings();
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
