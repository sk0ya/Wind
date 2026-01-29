namespace Wind.Models;

public class AppSettings
{
    public bool RunAtWindowsStartup { get; set; } = false;
    public List<StartupApplication> StartupApplications { get; set; } = new();
    public string Theme { get; set; } = "Dark";
    public List<StartupGroup> StartupGroups { get; set; } = new();
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
