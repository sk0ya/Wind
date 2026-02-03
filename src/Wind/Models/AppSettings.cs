namespace Wind.Models;

public class AppSettings
{
    public bool RunAtWindowsStartup { get; set; } = false;
    public List<StartupApplication> StartupApplications { get; set; } = new();
    public string Theme { get; set; } = "Dark";
    public List<StartupGroup> StartupGroups { get; set; } = new();
    /// <summary>
    /// "None" = release windows to desktop (default),
    /// "All" = close all tab windows,
    /// "StartupOnly" = close only windows launched at startup
    /// </summary>
    public string CloseWindowsOnExit { get; set; } = "None";
    public List<QuickLaunchApp> QuickLaunchApps { get; set; } = new();
    public string TabHeaderPosition { get; set; } = "Top";
    /// <summary>
    /// "CloseApp" = close the embedded application (default),
    /// "ReleaseEmbed" = release embedding and restore to desktop,
    /// "CloseWind" = close Wind application
    /// </summary>
    public string EmbedCloseAction { get; set; } = "CloseApp";
    public List<HotkeyBindingSetting> CustomHotkeys { get; set; } = new();
}

public class HotkeyBindingSetting
{
    public string Action { get; set; } = "";
    public string Modifiers { get; set; } = "";
    public string Key { get; set; } = "";
}

public class StartupApplication
{
    public string Path { get; set; } = string.Empty;
    public string Arguments { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public string? Group { get; set; }
    public string? Tile { get; set; }
    public int? TilePosition { get; set; }
}

public class StartupGroup
{
    public string Name { get; set; } = string.Empty;
    public string Color { get; set; } = "#6495ED";
}

public class QuickLaunchApp
{
    public string Path { get; set; } = string.Empty;
    public string Arguments { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public bool ShouldEmbed { get; set; } = true;
}
