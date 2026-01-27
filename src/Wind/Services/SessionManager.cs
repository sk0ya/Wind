using System.IO;
using System.Text.Json;
using System.Windows.Media;
using Wind.Models;

namespace Wind.Services;

public class SessionManager
{
    private readonly string _sessionFilePath;
    private readonly JsonSerializerOptions _jsonOptions;

    public SessionManager()
    {
        var appDataPath = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "Wind");

        Directory.CreateDirectory(appDataPath);

        _sessionFilePath = Path.Combine(appDataPath, "session.json");

        _jsonOptions = new JsonSerializerOptions
        {
            WriteIndented = true,
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase
        };
    }

    public async Task SaveSessionAsync(TabManager tabManager)
    {
        var sessionData = new SessionData
        {
            SavedAt = DateTime.UtcNow,
            ActiveTabId = tabManager.ActiveTab?.Id
        };

        // Save groups with their tabs
        foreach (var group in tabManager.Groups)
        {
            var sessionGroup = new SessionTabGroup
            {
                Id = group.Id,
                Name = group.Name,
                Color = group.Color.ToString(),
                IsExpanded = group.IsExpanded,
                Tabs = group.Tabs.Select(CreateSessionTab).ToList()
            };
            sessionData.Groups.Add(sessionGroup);
        }

        // Save ungrouped tabs
        var groupedTabIds = tabManager.Groups.SelectMany(g => g.Tabs).Select(t => t.Id).ToHashSet();
        sessionData.UngroupedTabs = tabManager.Tabs
            .Where(t => !groupedTabIds.Contains(t.Id))
            .Select(CreateSessionTab)
            .ToList();

        var json = JsonSerializer.Serialize(sessionData, _jsonOptions);
        await File.WriteAllTextAsync(_sessionFilePath, json);
    }

    public async Task<SessionData?> LoadSessionAsync()
    {
        if (!File.Exists(_sessionFilePath))
            return null;

        try
        {
            var json = await File.ReadAllTextAsync(_sessionFilePath);
            return JsonSerializer.Deserialize<SessionData>(json, _jsonOptions);
        }
        catch
        {
            return null;
        }
    }

    public async Task RestoreSessionAsync(TabManager tabManager, WindowManager windowManager)
    {
        var session = await LoadSessionAsync();
        if (session == null) return;

        windowManager.RefreshWindowList();
        var availableWindows = windowManager.AvailableWindows.ToList();

        // Restore groups
        foreach (var sessionGroup in session.Groups)
        {
            var color = TryParseColor(sessionGroup.Color) ?? Colors.CornflowerBlue;
            var group = tabManager.CreateGroup(sessionGroup.Name, color);
            group.IsExpanded = sessionGroup.IsExpanded;

            foreach (var sessionTab in sessionGroup.Tabs)
            {
                var window = FindMatchingWindow(availableWindows, sessionTab);
                if (window != null)
                {
                    var tab = tabManager.AddTab(window);
                    if (tab != null)
                    {
                        tabManager.AddTabToGroup(tab, group);
                        availableWindows.Remove(window);
                    }
                }
            }
        }

        // Restore ungrouped tabs
        foreach (var sessionTab in session.UngroupedTabs)
        {
            var window = FindMatchingWindow(availableWindows, sessionTab);
            if (window != null)
            {
                tabManager.AddTab(window);
                availableWindows.Remove(window);
            }
        }

        // Restore active tab
        if (session.ActiveTabId.HasValue)
        {
            var activeTab = tabManager.Tabs.FirstOrDefault(t => t.Id == session.ActiveTabId.Value);
            if (activeTab != null)
            {
                tabManager.ActiveTab = activeTab;
            }
        }
    }

    private SessionTab CreateSessionTab(TabItem tab)
    {
        return new SessionTab
        {
            Id = tab.Id,
            ProcessName = tab.Window?.ProcessName ?? string.Empty,
            WindowTitle = tab.Window?.Title ?? tab.Title,
            ProcessId = tab.Window?.ProcessId ?? 0
        };
    }

    private WindowInfo? FindMatchingWindow(List<WindowInfo> windows, SessionTab sessionTab)
    {
        // First try to match by process ID and title (exact match)
        var exactMatch = windows.FirstOrDefault(w =>
            w.ProcessId == sessionTab.ProcessId &&
            w.Title.Equals(sessionTab.WindowTitle, StringComparison.OrdinalIgnoreCase));

        if (exactMatch != null) return exactMatch;

        // Then try to match by process name and similar title
        var processMatch = windows.FirstOrDefault(w =>
            w.ProcessName.Equals(sessionTab.ProcessName, StringComparison.OrdinalIgnoreCase) &&
            w.Title.Contains(sessionTab.WindowTitle, StringComparison.OrdinalIgnoreCase));

        if (processMatch != null) return processMatch;

        // Finally try just process name
        return windows.FirstOrDefault(w =>
            w.ProcessName.Equals(sessionTab.ProcessName, StringComparison.OrdinalIgnoreCase));
    }

    private Color? TryParseColor(string colorString)
    {
        try
        {
            return (Color)ColorConverter.ConvertFromString(colorString);
        }
        catch
        {
            return null;
        }
    }

    public void DeleteSession()
    {
        if (File.Exists(_sessionFilePath))
        {
            File.Delete(_sessionFilePath);
        }
    }
}
