using System.Collections.ObjectModel;
using System.Windows.Input;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Wind.Models;
using Wind.Services;

namespace Wind.ViewModels;

public partial class HotkeyBindingItem : ObservableObject
{
    [ObservableProperty]
    private string _name = "";

    [ObservableProperty]
    private string _displayString = "";

    [ObservableProperty]
    private bool _isRecording;

    public HotkeyAction Action { get; set; }
    public ModifierKeys Modifiers { get; set; }
    public Key Key { get; set; }
}

public partial class GeneralSettingsViewModel : ObservableObject
{
    private readonly SettingsManager _settingsManager;
    private readonly HotkeyManager _hotkeyManager;

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

    // Hotkeys
    [ObservableProperty]
    private ObservableCollection<HotkeyBindingItem> _hotkeyBindings = new();

    [ObservableProperty]
    private HotkeyBindingItem? _recordingHotkey;

    public GeneralSettingsViewModel(SettingsManager settingsManager, HotkeyManager hotkeyManager)
    {
        _settingsManager = settingsManager;
        _hotkeyManager = hotkeyManager;
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
        LoadHotkeyBindings();
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

    // --- Hotkey commands ---

    private void LoadHotkeyBindings()
    {
        HotkeyBindings.Clear();
        foreach (var hotkey in _hotkeyManager.Hotkeys)
        {
            HotkeyBindings.Add(new HotkeyBindingItem
            {
                Name = hotkey.Name,
                DisplayString = hotkey.DisplayString,
                Action = hotkey.Action,
                Modifiers = hotkey.Modifiers,
                Key = hotkey.Key
            });
        }
    }

    [RelayCommand]
    private void StartRecording(HotkeyBindingItem item)
    {
        if (RecordingHotkey is not null)
        {
            RecordingHotkey.IsRecording = false;
        }

        item.IsRecording = true;
        RecordingHotkey = item;
    }

    [RelayCommand]
    private void CancelRecording()
    {
        if (RecordingHotkey is not null)
        {
            RecordingHotkey.IsRecording = false;
            RecordingHotkey = null;
        }
    }

    public bool ApplyRecordedKey(ModifierKeys modifiers, Key key)
    {
        if (RecordingHotkey is null) return false;

        if (key is Key.LeftCtrl or Key.RightCtrl or Key.LeftAlt or Key.RightAlt
            or Key.LeftShift or Key.RightShift or Key.LWin or Key.RWin or Key.System)
        {
            return false;
        }

        if (key == Key.Escape)
        {
            CancelRecording();
            return true;
        }

        var duplicate = HotkeyBindings.FirstOrDefault(h =>
            h != RecordingHotkey && h.Modifiers == modifiers && h.Key == key);

        if (duplicate is not null)
        {
            return false;
        }

        var item = RecordingHotkey;
        var success = _hotkeyManager.UpdateHotkey(item.Action, modifiers, key);

        if (success)
        {
            item.Modifiers = modifiers;
            item.Key = key;
            item.DisplayString = FormatHotkeyDisplay(modifiers, key);
        }

        item.IsRecording = false;
        RecordingHotkey = null;

        return success;
    }

    [RelayCommand]
    private void ResetHotkeys()
    {
        _hotkeyManager.ResetToDefaults();
        LoadHotkeyBindings();
    }

    private static string FormatHotkeyDisplay(ModifierKeys modifiers, Key key)
    {
        var parts = new List<string>();
        if (modifiers.HasFlag(ModifierKeys.Control)) parts.Add("Ctrl");
        if (modifiers.HasFlag(ModifierKeys.Alt)) parts.Add("Alt");
        if (modifiers.HasFlag(ModifierKeys.Shift)) parts.Add("Shift");
        if (modifiers.HasFlag(ModifierKeys.Windows)) parts.Add("Win");
        parts.Add(key.ToString());
        return string.Join(" + ", parts);
    }
}
