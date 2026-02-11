using Microsoft.Extensions.DependencyInjection;
using System.Windows;
using System.Windows.Media;
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
        services.AddSingleton<SettingsManager>();
        services.AddSingleton<HotkeyManager>();

        // ViewModels
        services.AddSingleton<MainViewModel>();
        services.AddSingleton<WindowPickerViewModel>();
        services.AddSingleton<GeneralSettingsViewModel>();
        services.AddSingleton<HotkeySettingsViewModel>();
        services.AddSingleton<StartupSettingsViewModel>();
        services.AddSingleton<QuickLaunchSettingsViewModel>();
        services.AddSingleton<ProcessInfoViewModel>();
        services.AddSingleton<CommandPaletteViewModel>();

        // Views
        services.AddSingleton<MainWindow>();
        services.AddSingleton<Views.GeneralSettingsPage>();
        services.AddSingleton<Views.HotkeySettingsPage>();
        services.AddSingleton<Views.StartupSettingsPage>();
        services.AddSingleton<Views.QuickLaunchSettingsPage>();
        services.AddSingleton<Views.ProcessInfoPage>();
    }

    protected override async void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);

        // Apply dark theme as base
        var settingsManager = _serviceProvider.GetRequiredService<SettingsManager>();
        var settings = settingsManager.Settings;
        Wpf.Ui.Appearance.ApplicationThemeManager.Apply(Wpf.Ui.Appearance.ApplicationTheme.Dark);

        // Apply accent color
        if (settings.UseSystemAccent)
        {
            Wpf.Ui.Appearance.ApplicationAccentColorManager.ApplySystemAccent();
        }
        else
        {
            try
            {
                var color = (Color)ColorConverter.ConvertFromString(settings.AccentColor);
                Wpf.Ui.Appearance.ApplicationAccentColorManager.Apply(color, Wpf.Ui.Appearance.ApplicationTheme.Dark);
            }
            catch
            {
                // Invalid color, use default
            }
        }

        // Apply background color
        GeneralSettingsViewModel.ApplyBackgroundColorStatic(settings.BackgroundColor);

        // Show main window first
        var mainWindow = _serviceProvider.GetRequiredService<MainWindow>();
        mainWindow.Show();

        // Snapshot existing window handles before launching, so we can detect
        // newly created windows for processes like explorer.exe that delegate
        // to an already-running instance and exit immediately.
        var windowManager = _serviceProvider.GetRequiredService<WindowManager>();
        var preExistingWindows = new HashSet<IntPtr>(
            windowManager.EnumerateWindows().Select(w => w.Handle));

        // Launch startup applications and embed them
        var processConfigs = settingsManager.LaunchStartupApplications();
        if (processConfigs.Count > 0)
        {
            var viewModel = _serviceProvider.GetRequiredService<MainViewModel>();
            await viewModel.EmbedStartupProcessesAsync(processConfigs, settingsManager.Settings, preExistingWindows);
        }
    }

    public static T GetService<T>() where T : class
    {
        var app = (App)Current;
        return app._serviceProvider.GetRequiredService<T>();
    }
}
