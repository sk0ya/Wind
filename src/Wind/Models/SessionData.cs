using System.Text.Json.Serialization;

namespace Wind.Models;

public class SessionData
{
    public DateTime SavedAt { get; set; } = DateTime.UtcNow;
    public List<SessionTabGroup> Groups { get; set; } = new();
    public List<SessionTab> UngroupedTabs { get; set; } = new();
    public Guid? ActiveTabId { get; set; }
}

public class SessionTabGroup
{
    public Guid Id { get; set; }
    public string Name { get; set; } = string.Empty;
    public string Color { get; set; } = "#6495ED";
    public bool IsExpanded { get; set; } = true;
    public List<SessionTab> Tabs { get; set; } = new();
}

public class SessionTab
{
    public Guid Id { get; set; }
    public string ProcessName { get; set; } = string.Empty;
    public string WindowTitle { get; set; } = string.Empty;
    public int ProcessId { get; set; }
}
