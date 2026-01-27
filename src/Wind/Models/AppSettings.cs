namespace Wind.Models;

public class AppSettings
{
    public bool RunAtWindowsStartup { get; set; } = false;
    public List<StartupApplication> StartupApplications { get; set; } = new();
    public string Theme { get; set; } = "Dark";
    public bool AutoSaveSession { get; set; } = true;
    public bool RestoreSessionOnStartup { get; set; } = true;
}

public class StartupApplication
{
    public string Path { get; set; } = string.Empty;
    public string Arguments { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
}
