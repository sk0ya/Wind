using Microsoft.Extensions.DependencyInjection;
using System.Windows;
using Wind.Services;
using Wind.ViewModels;
using Wind.Views;

namespace Wind;

public partial class App : Application
{
    private readonly IServiceProvider _serviceProvider;

    public App()
    {
        var services = new ServiceCollection();
        ConfigureServices(services);
        _serviceProvider = services.BuildServiceProvider();
    }

    private void ConfigureServices(IServiceCollection services)
    {
        // Services
        services.AddSingleton<WindowManager>();
        services.AddSingleton<TabManager>();
        services.AddSingleton<SessionManager>();
        services.AddSingleton<SettingsManager>();
        services.AddSingleton<HotkeyManager>();

        // ViewModels
        services.AddSingleton<MainViewModel>();
        services.AddTransient<WindowPickerViewModel>();
        services.AddTransient<SettingsViewModel>();

        // Windows
        services.AddSingleton<MainWindow>();
        services.AddTransient<SettingsWindow>();
    }

    protected override async void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);

        // Apply saved theme
        var settingsManager = _serviceProvider.GetRequiredService<SettingsManager>();
        var theme = settingsManager.Settings.Theme;
        var wpfuiTheme = theme switch
        {
            "Light" => Wpf.Ui.Appearance.ApplicationTheme.Light,
            _ => Wpf.Ui.Appearance.ApplicationTheme.Dark
        };
        Wpf.Ui.Appearance.ApplicationThemeManager.Apply(wpfuiTheme);

        // Show main window first
        var mainWindow = _serviceProvider.GetRequiredService<MainWindow>();
        mainWindow.Show();

        // Launch startup applications and embed them
        var processes = settingsManager.LaunchStartupApplications();
        if (processes.Count > 0)
        {
            var viewModel = _serviceProvider.GetRequiredService<MainViewModel>();
            await viewModel.EmbedStartupProcessesAsync(processes);
        }
    }

    public static T GetService<T>() where T : class
    {
        var app = (App)Current;
        return app._serviceProvider.GetRequiredService<T>();
    }
}
