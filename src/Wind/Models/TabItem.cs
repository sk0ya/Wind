using CommunityToolkit.Mvvm.ComponentModel;
using System.Windows.Media;

namespace Wind.Models;

public partial class TabItem : ObservableObject
{
    public Guid Id { get; } = Guid.NewGuid();

    [ObservableProperty]
    private WindowInfo? _window;

    [ObservableProperty]
    private string _title = string.Empty;

    [ObservableProperty]
    private ImageSource? _icon;

    [ObservableProperty]
    private bool _isSelected;

    [ObservableProperty]
    private bool _isMultiSelected;

    [ObservableProperty]
    private bool _isTiled;

    [ObservableProperty]
    private TabGroup? _group;

    [ObservableProperty]
    private bool _isLaunchedAtStartup;

    /// <summary>
    /// Key identifying the content type for content tabs (e.g. "Settings").
    /// Null for regular window tabs.
    /// </summary>
    public string? ContentKey { get; init; }

    public bool IsContentTab => ContentKey != null;

    public TabItem()
    {
    }

    public TabItem(WindowInfo window)
    {
        Window = window;
        Title = window.Title;
        Icon = window.Icon;
    }

    partial void OnWindowChanged(WindowInfo? value)
    {
        if (value != null)
        {
            Title = value.Title;
            Icon = value.Icon;
        }
    }
}
