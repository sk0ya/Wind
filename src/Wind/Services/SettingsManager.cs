using System.Diagnostics;
using System.IO;
using System.Text.Json;
using Microsoft.Win32;
using Wind.Models;

namespace Wind.Services;

public class SettingsManager
{
    private const string RegistryKeyPath = @"SOFTWARE\Microsoft\Windows\CurrentVersion\Run";
    private const string AppName = "Wind";

    private readonly string _settingsFilePath;
    private readonly JsonSerializerOptions _jsonOptions;
    private AppSettings _settings;

    public AppSettings Settings => _settings;

    public SettingsManager()
    {
        var appDataPath = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "Wind");

        Directory.CreateDirectory(appDataPath);

        _settingsFilePath = Path.Combine(appDataPath, "settings.json");

        _jsonOptions = new JsonSerializerOptions
        {
            WriteIndented = true,
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
            PropertyNameCaseInsensitive = true
        };

        _settings = LoadSettings();
    }

    private AppSettings LoadSettings()
    {
        if (!File.Exists(_settingsFilePath))
            return new AppSettings();

        try
        {
            var json = File.ReadAllText(_settingsFilePath);
            return JsonSerializer.Deserialize<AppSettings>(json, _jsonOptions) ?? new AppSettings();
        }
        catch
        {
            return new AppSettings();
        }
    }

    public void SaveSettings()
    {
        try
        {
            var json = JsonSerializer.Serialize(_settings, _jsonOptions);
            File.WriteAllText(_settingsFilePath, json);
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"Failed to save settings: {ex.Message}");
        }
    }

    public bool IsRunAtWindowsStartup()
    {
        try
        {
            using var key = Registry.CurrentUser.OpenSubKey(RegistryKeyPath, false);
            return key?.GetValue(AppName) != null;
        }
        catch
        {
            return false;
        }
    }

    public void SetRunAtWindowsStartup(bool enable)
    {
        try
        {
            using var key = Registry.CurrentUser.OpenSubKey(RegistryKeyPath, true);
            if (key == null) return;

            if (enable)
            {
                var exePath = Environment.ProcessPath;
                if (!string.IsNullOrEmpty(exePath))
                {
                    key.SetValue(AppName, $"\"{exePath}\"");
                }
            }
            else
            {
                key.DeleteValue(AppName, false);
            }

            _settings.RunAtWindowsStartup = enable;
            SaveSettings();
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"Failed to set startup: {ex.Message}");
        }
    }

    public StartupApplication AddStartupApplication(string path, string arguments = "", string? name = null)
    {
        if (string.IsNullOrWhiteSpace(path)) return null!;

        var appName = name ?? Path.GetFileNameWithoutExtension(path);

        var app = new StartupApplication
        {
            Path = path,
            Arguments = arguments,
            Name = appName
        };

        _settings.StartupApplications.Add(app);
        SaveSettings();

        return app;
    }

    public void RemoveStartupApplication(StartupApplication app)
    {
        if (_settings.StartupApplications.Remove(app))
        {
            SaveSettings();
        }
    }

    public void SaveStartupApplication()
    {
        SaveSettings();
    }

    public void AddStartupGroup(string name, string color)
    {
        if (string.IsNullOrWhiteSpace(name)) return;

        if (_settings.StartupGroups.Any(g => g.Name.Equals(name, StringComparison.OrdinalIgnoreCase)))
            return;

        _settings.StartupGroups.Add(new StartupGroup
        {
            Name = name,
            Color = color
        });

        SaveSettings();
    }

    public void RemoveStartupGroup(string name)
    {
        var group = _settings.StartupGroups
            .FirstOrDefault(g => g.Name.Equals(name, StringComparison.OrdinalIgnoreCase));

        if (group != null)
        {
            _settings.StartupGroups.Remove(group);
            SaveSettings();
        }
    }

    public QuickLaunchApp AddQuickLaunchApp(string path, string arguments = "", string? name = null)
    {
        if (string.IsNullOrWhiteSpace(path)) return null!;

        var appName = name ?? Path.GetFileNameWithoutExtension(path);

        var app = new QuickLaunchApp
        {
            Path = path,
            Arguments = arguments,
            Name = appName
        };

        _settings.QuickLaunchApps.Add(app);
        SaveSettings();

        return app;
    }

    public void RemoveQuickLaunchApp(QuickLaunchApp app)
    {
        if (_settings.QuickLaunchApps.Remove(app))
        {
            SaveSettings();
        }
    }

    public void SaveQuickLaunchApp()
    {
        SaveSettings();
    }

    public List<(Process Process, StartupApplication Config)> LaunchStartupApplications()
    {
        var results = new List<(Process, StartupApplication)>();

        foreach (var app in _settings.StartupApplications)
        {
            try
            {
                if (!File.Exists(app.Path)) continue;

                var startInfo = new ProcessStartInfo
                {
                    FileName = app.Path,
                    Arguments = app.Arguments,
                    UseShellExecute = true
                };

                var process = Process.Start(startInfo);
                if (process != null)
                {
                    results.Add((process, app));
                }
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"Failed to launch {app.Name}: {ex.Message}");
            }
        }

        return results;
    }
}
