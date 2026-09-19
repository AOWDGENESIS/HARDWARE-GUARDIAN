using System.Globalization;
using System.IO;
using System.Windows;
using System.Windows.Threading;
using HardwareGuardian.App.Services;
using HardwareGuardian.App.ViewModels;
using HardwareGuardian.App.Views;
using HardwareGuardian.Core;
using HardwareGuardian.Core.Abstractions;
using HardwareGuardian.Core.Diagnostics;
using HardwareGuardian.Core.Models;
using HardwareGuardian.Core.Services;
using HardwareGuardian.Infrastructure.Diagnostics;
using HardwareGuardian.Infrastructure.Http;
using HardwareGuardian.Infrastructure.Localization;
using HardwareGuardian.Infrastructure.Logging;
using HardwareGuardian.Infrastructure.Persistence;
using HardwareGuardian.Infrastructure.Platform;
using HardwareGuardian.Infrastructure.Security;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace HardwareGuardian.App;

/// <summary>
/// Composition root. Everything the application uses is created here, once, and only through the
/// container - there is no service locator in the views (spec section 3.3).
/// </summary>
public partial class App : Application
{
    private ServiceProvider? _provider;
    private ILoggerFactory? _loggerFactory;
    private ThemeManager? _themes;

    public static bool IsSimulationRequested { get; private set; }

    protected override async void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);

        DispatcherUnhandledException += OnDispatcherUnhandledException;
        AppDomain.CurrentDomain.UnhandledException += OnDomainUnhandledException;
        TaskScheduler.UnobservedTaskException += OnUnobservedTaskException;

        try
        {
            IsSimulationRequested = e.Args.Any(a => string.Equals(a, "--simulation", StringComparison.OrdinalIgnoreCase));
            if (!OperatingSystem.IsWindows())
            {
                // WPF is Windows only; this branch exists so the failure is explained instead of crashing.
                MessageBox.Show(
                    $"Hardware Guardian requires Windows. Current platform: {Environment.OSVersion}.",
                    "Hardware Guardian",
                    MessageBoxButton.OK,
                    MessageBoxImage.Error);
                Shutdown(1);
                return;
            }

            var paths = new PathProvider();
            paths.EnsureDirectory(paths.DataRoot);
            paths.EnsureDirectory(paths.LogDirectory);

            var settingsService = new FileSettingsService(paths);
            var settings = await settingsService.LoadAsync(CancellationToken.None).ConfigureAwait(true);

            _loggerFactory = LoggerFactory.Create(builder =>
            {
                builder.SetMinimumLevel(ParseLevel(settings.LogLevel));
                builder.AddProvider(new TechnicalFileLoggerProvider(paths.LogDirectory, ParseLevel(settings.LogLevel), settings.LogRetentionDays));
            });

            var localizer = new JsonLocalizer(paths.ConfigurationDirectory, settingsService, settings.Language);
            AppServices.CurrentLocalizer = localizer;
            LocalizationProxy.Instance.Attach(localizer);

            _themes = new ThemeManager(this);
            _themes.Apply(settings.Theme);

            _provider = BuildProvider(paths, settingsService, settings, localizer);

            var window = _provider.GetRequiredService<MainWindow>();
            MainWindow = window;
            window.Show();

            // Read the inventory right after the window appears: no network access, so it is allowed
            // at startup, and the user sees the machine before any analysis.
            var main = _provider.GetRequiredService<MainViewModel>();
            await main.InitialiseAsync().ConfigureAwait(true);
        }
        catch (Exception ex)
        {
            LogFatal(ex);
            MessageBox.Show(
                $"Hardware Guardian could not start.{Environment.NewLine}{Environment.NewLine}{ex.GetType().Name}: {ex.Message}",
                "Hardware Guardian",
                MessageBoxButton.OK,
                MessageBoxImage.Error);
            Shutdown(1);
        }
    }

    protected override void OnExit(ExitEventArgs e)
    {
        _provider?.Dispose();
        _loggerFactory?.Dispose();
        base.OnExit(e);
    }

    private ServiceProvider BuildProvider(PathProvider paths, FileSettingsService settingsService, AppSettings settings, JsonLocalizer localizer)
    {
        var services = new ServiceCollection();
        var loggerFactory = _loggerFactory!;

        // Platform and settings
        services.AddSingleton<IPathProvider>(paths);
        services.AddSingleton<IEnvironmentProbe>(paths);
        services.AddSingleton<ISettingsService>(settingsService);
        services.AddSingleton<IClock>(SystemClock.Instance);
        services.AddSingleton<ILocalizer>(localizer);
        services.AddSingleton(loggerFactory);
        services.AddSingleton(typeof(ILogger<>), typeof(Logger<>));

        // Protocol, state and audit
        services.AddSingleton<ILiveProtocol, LiveProtocol>();
        services.AddSingleton<IEventBus>(sp => new EventBus(sp.GetRequiredService<ILogger<EventBus>>()));
        services.AddSingleton<IProblemRegistry>(sp => new ProblemRegistry(
            sp.GetRequiredService<IClock>(),
            sp.GetRequiredService<IEventBus>(),
            sp.GetRequiredService<ILogger<ProblemRegistry>>()));
        services.AddSingleton<IProgressReporter, ProgressReporter>();
        services.AddSingleton<IAuditSink>(_ => new FileAuditSink(paths, localizer));
        services.AddSingleton<IBuildInfoProvider>(_ => new BuildInfoProvider(paths, paths));
        services.AddSingleton<IAuditLog>(sp => new AuditLogService(
            sp.GetRequiredService<IAuditSink>(),
            sp.GetRequiredService<IClock>(),
            sp.GetRequiredService<IEnvironmentProbe>(),
            sp.GetRequiredService<IBuildInfoProvider>()));
        services.AddSingleton<ISnapshotStore>(_ => new FileSnapshotStore(paths));
        services.AddSingleton<IHistoryStore>(_ => new FileHistoryStore(paths));
        services.AddSingleton<ISourceCacheStore>(_ => new FileSourceCacheStore(paths));
        services.AddSingleton<ISystemStateMachine>(sp => new SystemStateMachine(
            sp.GetRequiredService<IClock>(),
            sp.GetRequiredService<IEventBus>(),
            sp.GetRequiredService<ILogger<SystemStateMachine>>()));
        services.AddSingleton<IApprovalService>(sp => new ApprovalService(
            sp.GetRequiredService<IClock>(),
            sp.GetRequiredService<ISettingsService>(),
            sp.GetRequiredService<IEventBus>(),
            sp.GetRequiredService<ILogger<ApprovalService>>()));

        // Platform access
        services.AddSingleton<IProcessRunner, ProcessRunner>();
        services.AddSingleton<PowerShellRunner>(sp => new PowerShellRunner(sp.GetRequiredService<IProcessRunner>()));
        services.AddSingleton<IRegistryAccess>(sp => new WindowsRegistryAccess(sp.GetRequiredService<IClock>()));
        services.AddSingleton<IProcessSnapshotProvider, WindowsProcessSnapshotProvider>();
        services.AddSingleton<IElevationService, ElevationService>();
        services.AddSingleton<IPathGuard, PathGuard>();
        services.AddSingleton<INotificationService, NotificationService>();
        services.AddSingleton<IUiDispatcher>(_ => new WpfUiDispatcher());
        services.AddSingleton(_themes!);

        // Security and sources
        services.AddSingleton<IHashService, HashService>();
        services.AddSingleton<IHttpClientProvider>(sp => new HttpClientProvider(sp.GetRequiredService<IBuildInfoProvider>().Get().Version));
        services.AddSingleton<ISignatureVerifier>(sp => new WindowsSignatureVerifier(
            sp.GetRequiredService<PowerShellRunner>(),
            sp.GetRequiredService<IClock>()));
        services.AddSingleton<ISourceVerifier, SourceVerifier>();
        services.AddSingleton<IDownloadService, DownloadService>();
        services.AddSingleton<IBackupService>(sp => new BackupService(
            paths,
            sp.GetRequiredService<IProcessRunner>(),
            sp.GetRequiredService<IClock>(),
            sp.GetRequiredService<IEnvironmentProbe>(),
            sp.GetRequiredService<IBuildInfoProvider>(),
            sp.GetRequiredService<ILogger<BackupService>>()));
        services.AddSingleton<IRollbackService>(sp => new RollbackService(
            paths,
            sp.GetRequiredService<IProcessRunner>(),
            sp.GetRequiredService<IHashService>(),
            sp.GetRequiredService<IAuditLog>(),
            sp.GetRequiredService<ILiveProtocol>(),
            sp.GetRequiredService<IClock>(),
            sp.GetRequiredService<ILogger<RollbackService>>()));

        // Hardware: exactly one provider, chosen explicitly. Simulation is never the default on Windows.
        services.AddSingleton<WmiReader>();
        if (IsSimulationRequested)
        {
            services.AddSingleton<IHardwareProvider>(sp => new Simulation.MockHardwareProvider(sp.GetRequiredService<IClock>()));
        }
        else
        {
            services.AddSingleton<IHardwareProvider>(sp => new Hardware.WindowsHardwareProvider(
                sp.GetRequiredService<WmiReader>(),
                sp.GetRequiredService<IClock>()));
            services.AddSingleton<Hardware.WindowsHardwareProvider>(sp => (Hardware.WindowsHardwareProvider)sp.GetRequiredService<IHardwareProvider>());
        }

        // Domain services
        services.AddSingleton<IDriverInventoryService, Drivers.DriverInventoryService>();
        services.AddSingleton<IManufacturerResolver, Manufacturer.ManufacturerResolver>();
        services.AddSingleton<UpdateDecisionEngine>();
        services.AddSingleton<IUpdateCenter, Manufacturer.UpdateCenterService>();
        services.AddSingleton<IBiosService, Bios.BiosService>();
        services.AddSingleton<Security.SecurityAuditService>();
        services.AddSingleton<IMaintenanceService, Maintenance.MaintenanceService>();
        services.AddSingleton<ISoftwareInventoryService, Maintenance.SoftwareInventoryService>();
        services.AddSingleton<IProcessInventoryService, Maintenance.ProcessInventoryService>();
        services.AddSingleton<IWorkloadDetector, Maintenance.WorkloadDetector>();
        services.AddSingleton<IReportGenerator, Reporting.ReportGenerator>();
        services.AddSingleton<IWindowsHealthService>(sp => new Windows.WindowsHealthService(
            sp.GetRequiredService<IProcessRunner>(),
            sp.GetRequiredService<PowerShellRunner>(),
            sp.GetRequiredService<WmiReader>(),
            sp.GetRequiredService<IEnvironmentProbe>(),
            sp.GetRequiredService<IHardwareProvider>(),
            sp.GetRequiredService<IRegistryAccess>(),
            sp.GetRequiredService<ILiveProtocol>(),
            sp.GetRequiredService<IAuditLog>(),
            sp.GetRequiredService<IClock>()));

        // Sensors
        services.AddSingleton<ISensorProvider, Sensors.AcpiThermalZoneProvider>();
        services.AddSingleton<ISensorProvider, Sensors.StorageSensorProvider>();
        services.AddSingleton<ISensorProvider, Sensors.PerformanceSensorProvider>();
        services.AddSingleton<ISensorProvider>(sp => new Sensors.VendorToolSensorProvider(
            sp.GetRequiredService<IProcessRunner>(),
            sp.GetRequiredService<IEnvironmentProbe>(),
            sp.GetRequiredService<ISettingsService>(),
            sp.GetRequiredService<IClock>()));
        services.AddSingleton<ISensorService, Sensors.SensorService>();

        // Diagnostic modules (read only) and the orchestrator
        services.AddSingleton<IDiagnosticModule, Diagnostics.DriverHealthModule>();
        services.AddSingleton<IDiagnosticModule, Diagnostics.SensorHealthModule>();
        services.AddSingleton<IDiagnosticModule, Diagnostics.WindowsHealthModule>();
        services.AddSingleton<IDiagnosticModule, Diagnostics.WorkloadModule>();
        services.AddSingleton<IScanOrchestrator>(sp => new ScanOrchestrator(
            sp.GetRequiredService<IHardwareProvider>(),
            sp.GetRequiredService<ILiveProtocol>(),
            sp.GetRequiredService<IEventBus>(),
            sp.GetRequiredService<IProgressReporter>(),
            sp.GetRequiredService<IProblemRegistry>(),
            sp.GetRequiredService<IClock>(),
            sp.GetRequiredService<IEnvironmentProbe>(),
            sp.GetRequiredService<ISettingsService>(),
            sp,
            sp.GetRequiredService<ISystemStateMachine>(),
            sp.GetServices<IDiagnosticModule>(),
            sp.GetRequiredService<ISnapshotStore>(),
            sp.GetRequiredService<IHistoryStore>(),
            sp.GetRequiredService<IBuildInfoProvider>(),
            sp.GetRequiredService<ILogger<ScanOrchestrator>>()));

        // View models and window
        services.AddSingleton<MainViewModel>();
        services.AddSingleton<DashboardViewModel>();
        services.AddSingleton<HardwareViewModel>();
        services.AddSingleton<WindowsHealthViewModel>();
        services.AddSingleton<MaintenanceViewModel>();
        services.AddSingleton<SettingsViewModel>();
        services.AddSingleton<MainWindow>();

        return services.BuildServiceProvider(new ServiceProviderOptions
        {
            ValidateOnBuild = true,
            ValidateScopes = true,
        });
    }

    private static LogLevel ParseLevel(string level) =>
        Enum.TryParse<LogLevel>(level, ignoreCase: true, out var parsed) ? parsed : LogLevel.Information;

    private void OnDispatcherUnhandledException(object sender, DispatcherUnhandledExceptionEventArgs e)
    {
        LogFatal(e.Exception);
        e.Handled = true;
        Notify("Protocol_ModuleFailed");
    }

    private void OnDomainUnhandledException(object sender, UnhandledExceptionEventArgs e)
    {
        if (e.ExceptionObject is Exception ex)
        {
            LogFatal(ex);
            Notify("Protocol_ModuleFailed");
        }
    }

    private void OnUnobservedTaskException(object? sender, UnobservedTaskExceptionEventArgs e)
    {
        LogFatal(e.Exception);
        e.SetObserved();
    }

    /// <summary>
    /// Shows an unhandled error in the notification area. The message text stays empty on purpose:
    /// the technical details are in the log file, and the UI must not present a stack trace as prose.
    /// </summary>
    private void Notify(string titleKey)
    {
        try
        {
            var notifications = _provider?.GetService<INotificationService>();
            notifications?.Notify(new NotificationMessage
            {
                Kind = NotificationKind.Critical,
                ModuleKey = "Module_System",
                Title = Core.Values.LocalizedText.Of(titleKey),
                Message = Core.Values.LocalizedText.Of("Notification_Empty"),
                IsPersistent = true,
            });
        }
        catch (Exception ex) when (ex is InvalidOperationException or ObjectDisposedException)
        {
            // Nothing left to notify with; the file log already has the details.
        }
    }

    private void LogFatal(Exception exception) => _loggerFactory?.CreateLogger<App>().LogCritical(exception, "Unhandled exception");
}
