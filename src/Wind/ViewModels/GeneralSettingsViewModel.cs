using CommunityToolkit.Mvvm.ComponentModel;
using System.Collections.ObjectModel;
using System.Windows.Media;
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

    [ObservableProperty]
    private string _selectedAccentColor = "#0078D4";

    [ObservableProperty]
    private bool _useSystemAccent = false;

    [ObservableProperty]
    private string _selectedBackgroundColor = "";

    public ObservableCollection<PresetColor> PresetColors { get; } = new()
    {
        new PresetColor("Blue", "#0078D4"),
        new PresetColor("Purple", "#744DA9"),
        new PresetColor("Pink", "#E3008C"),
        new PresetColor("Red", "#E81123"),
        new PresetColor("Orange", "#FF8C00"),
        new PresetColor("Yellow", "#FFB900"),
        new PresetColor("Green", "#107C10"),
        new PresetColor("Teal", "#00B294"),
    };

    public ObservableCollection<PresetColor> BackgroundPresetColors { get; } = new()
    {
        new PresetColor("Default", ""),
        new PresetColor("Dark", "#1E1E1E"),
        new PresetColor("Darker", "#0D0D0D"),
        new PresetColor("Navy", "#0A1929"),
        new PresetColor("Forest", "#0D1F0D"),
        new PresetColor("Wine", "#1F0D0D"),
        new PresetColor("Slate", "#1A1A2E"),
        new PresetColor("Charcoal", "#2D2D2D"),
    };

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
        SelectedAccentColor = settings.AccentColor;
        UseSystemAccent = settings.UseSystemAccent;
        SelectedBackgroundColor = settings.BackgroundColor;
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

    partial void OnSelectedAccentColorChanged(string value)
    {
        if (UseSystemAccent) return;

        _settingsManager.Settings.AccentColor = value;
        _settingsManager.SaveSettings();
        ApplyAccentColor();
    }

    partial void OnUseSystemAccentChanged(bool value)
    {
        _settingsManager.Settings.UseSystemAccent = value;
        _settingsManager.SaveSettings();
        ApplyAccentColor();
    }

    public void SelectPresetColor(string colorCode)
    {
        UseSystemAccent = false;
        SelectedAccentColor = colorCode;
    }

    partial void OnSelectedBackgroundColorChanged(string value)
    {
        _settingsManager.Settings.BackgroundColor = value;
        _settingsManager.SaveSettings();
        ApplyBackgroundColor();
    }

    public void SelectBackgroundPresetColor(string colorCode)
    {
        SelectedBackgroundColor = colorCode;
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
        ApplyAccentColor();
        ApplyBackgroundColor();
    }

    private void ApplyAccentColor()
    {
        var wpfuiTheme = SelectedTheme switch
        {
            "Light" => Wpf.Ui.Appearance.ApplicationTheme.Light,
            _ => Wpf.Ui.Appearance.ApplicationTheme.Dark
        };

        if (UseSystemAccent)
        {
            Wpf.Ui.Appearance.ApplicationAccentColorManager.ApplySystemAccent();
        }
        else
        {
            try
            {
                var color = (Color)ColorConverter.ConvertFromString(SelectedAccentColor);
                Wpf.Ui.Appearance.ApplicationAccentColorManager.Apply(color, wpfuiTheme);
            }
            catch
            {
                // Invalid color, ignore
            }
        }
    }

    private void ApplyBackgroundColor()
    {
        if (string.IsNullOrEmpty(SelectedBackgroundColor))
        {
            // Reset to theme default - re-apply theme to restore original colors
            return;
        }

        ApplyBackgroundColorStatic(SelectedBackgroundColor);
    }

    public static void ApplyBackgroundColorStatic(string colorCode)
    {
        if (string.IsNullOrEmpty(colorCode))
            return;

        try
        {
            var baseColor = (Color)ColorConverter.ConvertFromString(colorCode);
            var app = System.Windows.Application.Current;

            // Helper to create and freeze brush
            SolidColorBrush CreateBrush(Color c)
            {
                var b = new SolidColorBrush(c);
                b.Freeze();
                return b;
            }

            // Helper to lighten/darken color
            Color AdjustBrightness(Color c, int amount)
            {
                return Color.FromArgb(
                    c.A,
                    (byte)Math.Clamp(c.R + amount, 0, 255),
                    (byte)Math.Clamp(c.G + amount, 0, 255),
                    (byte)Math.Clamp(c.B + amount, 0, 255));
            }

            Color WithAlpha(Color c, byte alpha)
            {
                return Color.FromArgb(alpha, c.R, c.G, c.B);
            }

            // Main background
            app.Resources["ApplicationBackgroundBrush"] = CreateBrush(baseColor);

            // Solid backgrounds
            app.Resources["SolidBackgroundFillColorBaseBrush"] = CreateBrush(baseColor);
            app.Resources["SolidBackgroundFillColorBaseAltBrush"] = CreateBrush(baseColor);
            app.Resources["SolidBackgroundFillColorSecondaryBrush"] = CreateBrush(AdjustBrightness(baseColor, 10));
            app.Resources["SolidBackgroundFillColorTertiaryBrush"] = CreateBrush(AdjustBrightness(baseColor, 20));
            app.Resources["SolidBackgroundFillColorQuarternaryBrush"] = CreateBrush(AdjustBrightness(baseColor, 30));

            // Layer backgrounds
            app.Resources["LayerFillColorDefaultBrush"] = CreateBrush(WithAlpha(AdjustBrightness(baseColor, 15), 128));
            app.Resources["LayerFillColorAltBrush"] = CreateBrush(WithAlpha(baseColor, 128));
            app.Resources["LayerOnMicaBaseAltFillColorDefaultBrush"] = CreateBrush(WithAlpha(AdjustBrightness(baseColor, 20), 200));

            // Card backgrounds
            app.Resources["CardBackgroundFillColorDefaultBrush"] = CreateBrush(WithAlpha(AdjustBrightness(baseColor, 20), 180));
            app.Resources["CardBackgroundFillColorSecondaryBrush"] = CreateBrush(WithAlpha(AdjustBrightness(baseColor, 15), 150));

            // Control backgrounds
            app.Resources["ControlFillColorDefaultBrush"] = CreateBrush(WithAlpha(AdjustBrightness(baseColor, 30), 180));
            app.Resources["ControlFillColorSecondaryBrush"] = CreateBrush(WithAlpha(AdjustBrightness(baseColor, 40), 140));
            app.Resources["ControlFillColorTertiaryBrush"] = CreateBrush(WithAlpha(AdjustBrightness(baseColor, 25), 100));
            app.Resources["ControlFillColorDisabledBrush"] = CreateBrush(WithAlpha(AdjustBrightness(baseColor, 20), 80));

            // Subtle fills
            app.Resources["SubtleFillColorTransparentBrush"] = CreateBrush(Colors.Transparent);
            app.Resources["SubtleFillColorSecondaryBrush"] = CreateBrush(WithAlpha(AdjustBrightness(baseColor, 40), 100));
            app.Resources["SubtleFillColorTertiaryBrush"] = CreateBrush(WithAlpha(AdjustBrightness(baseColor, 30), 80));

            // Smoke/Overlay
            app.Resources["SmokeFillColorDefaultBrush"] = CreateBrush(WithAlpha(baseColor, 100));
        }
        catch
        {
            // Invalid color, ignore
        }
    }
}

public class PresetColor
{
    public string Name { get; }
    public string ColorCode { get; }
    public Color Color { get; }

    public PresetColor(string name, string colorCode)
    {
        Name = name;
        ColorCode = colorCode;
        if (string.IsNullOrEmpty(colorCode))
        {
            Color = Colors.Transparent;
        }
        else
        {
            Color = (Color)ColorConverter.ConvertFromString(colorCode);
        }
    }
}
