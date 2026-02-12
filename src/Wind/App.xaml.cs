using Microsoft.Extensions.DependencyInjection;
using System.Diagnostics;
using System.Security.Principal;
using System.Windows;
using System.Windows.Interop;
using System.Windows.Media;
using Wind.Interop;
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
        services.AddSingleton<WebViewEnvironmentService>();

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
        services.AddSingleton<Views.MainWindow>();
        services.AddSingleton<Views.Settings.GeneralSettingsPage>();
        services.AddSingleton<Views.Settings.HotkeySettingsPage>();
        services.AddSingleton<Views.Settings.StartupSettingsPage>();
        services.AddSingleton<Views.Settings.QuickLaunchSettingsPage>();
        services.AddSingleton<Views.Settings.ProcessInfoPage>();
    }

    public static bool IsRunningAsAdmin()
    {
        using var identity = WindowsIdentity.GetCurrent();
        var principal = new WindowsPrincipal(identity);
        return principal.IsInRole(WindowsBuiltInRole.Administrator);
    }

    protected override async void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);

        var settingsManager = _serviceProvider.GetRequiredService<SettingsManager>();
        var settings = settingsManager.Settings;

        // Re-launch as admin if the setting is enabled and we're not already elevated
        if (settings.RunAsAdmin && !IsRunningAsAdmin())
        {
            try
            {
                var exePath = Environment.ProcessPath;
                if (!string.IsNullOrEmpty(exePath))
                {
                    var startInfo = new ProcessStartInfo
                    {
                        FileName = exePath,
                        UseShellExecute = true,
                        Verb = "runas"
                    };
                    Process.Start(startInfo);
                }
            }
            catch
            {
                // User cancelled UAC prompt — continue without admin
            }
            Shutdown();
            return;
        }

        // Apply dark theme as base
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
        var mainWindow = _serviceProvider.GetRequiredService<Views.MainWindow>();
        mainWindow.Show();

        // Snapshot existing window handles before launching, so we can detect
        // newly created windows for processes like explorer.exe that delegate
        // to an already-running instance and exit immediately.
        var windowManager = _serviceProvider.GetRequiredService<WindowManager>();
        var preExistingWindows = new HashSet<IntPtr>(
            windowManager.EnumerateWindows().Select(w => w.Handle));

        // Launch startup applications and embed them
        var (processConfigs, urlApps) = settingsManager.LaunchStartupApplications();

        var viewModel = _serviceProvider.GetRequiredService<MainViewModel>();

        // Open URL startup items as web tabs
        foreach (var urlApp in urlApps)
        {
            viewModel.OpenWebTabCommand.Execute(urlApp.Path);
        }

        if (processConfigs.Count > 0)
        {
            await viewModel.EmbedStartupProcessesAsync(processConfigs, settingsManager.Settings, preExistingWindows);
        }

        // Startup apps may have stolen foreground focus.
        // Force Wind back to the foreground after embedding completes.
        var windHandle = new WindowInteropHelper(mainWindow).Handle;
        if (windHandle != IntPtr.Zero)
        {
            NativeMethods.ForceForegroundWindow(windHandle);
        }
        mainWindow.Activate();
        mainWindow.Focus();
    }

    public static T GetService<T>() where T : class
    {
        var app = (App)Current;
        return app._serviceProvider.GetRequiredService<T>();
    }
}
