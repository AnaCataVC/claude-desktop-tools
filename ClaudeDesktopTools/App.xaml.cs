using System;
using System.IO;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.UI.Dispatching;
using Microsoft.UI.Xaml;
using ClaudeDesktopTools.Services;
using ClaudeDesktopTools.Services.Interfaces;
using ClaudeDesktopTools.ViewModels;

namespace ClaudeDesktopTools;

public partial class App : Application
{
    private readonly IHost _host;
    public static Window? MainWindow { get; private set; }
    public static DispatcherQueue? MainDispatcherQueue { get; private set; }

    public static IServiceProvider Services => ((App)Current)._host.Services;

    public App()
    {
        InitializeLoggingAndCrashHandlers();

        this.UnhandledException += (sender, e) =>
        {
            LogCrash("Application.UnhandledException", e.Exception);
        };

        _host = Host.CreateDefaultBuilder()
            .ConfigureServices((context, services) =>
            {
                // Core Services
                services.AddSingleton<IClaudeMaintenanceService, ClaudeMaintenanceService>();
                services.AddSingleton<IClaudeConfigDiscoveryService, ClaudeConfigDiscoveryService>();
                services.AddSingleton<IDriveSyncService, DriveSyncService>();
                services.AddSingleton<IProcessMonitorService, ProcessMonitorService>();

                // ViewModels
                services.AddTransient<DashboardViewModel>();
                services.AddTransient<SessionsViewModel>();
                services.AddSingleton<ContextDiscoveryViewModel>();
                services.AddTransient<SettingsViewModel>();
                services.AddTransient<ProcessMonitorViewModel>();
            })
            .Build();

        this.InitializeComponent();
    }

    protected override void OnLaunched(LaunchActivatedEventArgs args)
    {
        // Obtain DispatcherQueue before window creation per desktop stability guidelines
        MainDispatcherQueue = DispatcherQueue.GetForCurrentThread();

        MainWindow = new MainWindow();
        MainWindow.Activate();
    }

    private static void InitializeLoggingAndCrashHandlers()
    {
        AppDomain.CurrentDomain.UnhandledException += (s, e) =>
        {
            LogCrash("AppDomain.UnhandledException", e.ExceptionObject as Exception);
        };

        System.Threading.Tasks.TaskScheduler.UnobservedTaskException += (s, e) =>
        {
            LogCrash("TaskScheduler.UnobservedTaskException", e.Exception);
            e.SetObserved();
        };
    }

    public static void LogCrash(string source, Exception? ex)
    {
        try
        {
            string localAppData = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
            string logDir = Path.Combine(localAppData, "ClaudeDesktopTools", "Logs");
            Directory.CreateDirectory(logDir);
            string logFile = Path.Combine(logDir, "crash.log");

            string entry = $"[{DateTime.Now:yyyy-MM-dd HH:mm:ss}] [{source}]\n{ex?.ToString()}\n----------------------------------------\n";
            File.AppendAllText(logFile, entry);
        }
        catch { }
    }
}
