using Microsoft.Extensions.DependencyInjection;
using System.Windows;
using Wind.Services;
using Wind.ViewModels;

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
        services.AddSingleton<HotkeyManager>();

        // ViewModels
        services.AddSingleton<MainViewModel>();
        services.AddTransient<WindowPickerViewModel>();

        // Windows
        services.AddSingleton<MainWindow>();
    }

    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);

        var mainWindow = _serviceProvider.GetRequiredService<MainWindow>();
        mainWindow.Show();
    }

    public static T GetService<T>() where T : class
    {
        var app = (App)Current;
        return app._serviceProvider.GetRequiredService<T>();
    }
}
