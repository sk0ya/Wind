using System.Collections.ObjectModel;
using System.Diagnostics;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Wind.Services;

namespace Wind.ViewModels;

public class ProcessInfoItem
{
    public string DisplayTitle { get; set; } = "";
    public string ProcessName { get; set; } = "";
    public int ProcessId { get; set; }
    public string ExecutablePath { get; set; } = "";
    public string MemoryUsage { get; set; } = "";
    public string StartTime { get; set; } = "";
}

public partial class ProcessInfoViewModel : ObservableObject
{
    private readonly TabManager _tabManager;

    [ObservableProperty]
    private ObservableCollection<ProcessInfoItem> _processes = new();

    public ProcessInfoViewModel(TabManager tabManager)
    {
        _tabManager = tabManager;
    }

    [RelayCommand]
    public void Refresh()
    {
        Processes.Clear();
        foreach (var tab in _tabManager.Tabs)
        {
            if (tab.IsContentTab || tab.Window == null) continue;

            var item = new ProcessInfoItem
            {
                DisplayTitle = tab.DisplayTitle,
                ProcessName = tab.Window.ProcessName,
                ProcessId = tab.Window.ProcessId,
                ExecutablePath = tab.Window.ExecutablePath ?? "(access denied)",
            };

            try
            {
                using var p = Process.GetProcessById(tab.Window.ProcessId);
                item.MemoryUsage = $"{p.WorkingSet64 / 1024 / 1024} MB";
                try
                {
                    item.StartTime = p.StartTime.ToString("yyyy-MM-dd HH:mm:ss");
                }
                catch
                {
                    item.StartTime = "N/A";
                }
            }
            catch
            {
                item.MemoryUsage = "N/A";
                item.StartTime = "N/A";
            }

            Processes.Add(item);
        }
    }
}
