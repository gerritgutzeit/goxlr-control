using System.Windows;
using GoXlrControl.App.Services;
using GoXlrControl.App.ViewModels;
using GoXlrControl.App.Views;
using GoXlrControl.Audio;
using GoXlrControl.Config;
using GoXlrControl.Diagnostics;
using GoXlrControl.Engine;
using GoXlrControl.Hardware;
using GoXlrControl.Hardware.Abstractions;
using GoXlrControl.Integrations;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Velopack;

namespace GoXlrControl.App;

public partial class App : Application
{
    private static Mutex? _mutex;
    private IHost? _host;

    [STAThread]
    private static void Main(string[] args)
    {
        // Must run before WPF startup so Velopack can handle update hooks with minimal overhead.
        VelopackApp.Build().Run();

        var app = new App();
        app.InitializeComponent();
        app.Run();
    }

    protected override void OnStartup(StartupEventArgs e)
    {
        _mutex = new Mutex(true, @"Local\GoXlrControlStudio", out var created);
        if (!created)
        {
            MessageBox.Show("GoXLR Control Studio läuft bereits.", "GoXLR Control Studio",
                MessageBoxButton.OK, MessageBoxImage.Information);
            Shutdown();
            return;
        }

        base.OnStartup(e);
        _ = StartAppAsync();
    }

    private async Task StartAppAsync()
    {
        try
        {
            _host = Host.CreateDefaultBuilder()
                .ConfigureServices(ConfigureServices)
                .Build();

            await _host.StartAsync().ConfigureAwait(false);

            var bootstrap = _host.Services.GetRequiredService<AppBootstrapper>();
            await bootstrap.InitializeAsync().ConfigureAwait(false);

            await Dispatcher.InvokeAsync(() =>
            {
                bootstrap.InitializeTray();

                var main = _host!.Services.GetRequiredService<MainWindow>();
                MainWindow = main;
                main.Show();
                main.Activate();

                var settings = bootstrap.Settings;
                if (!settings.WizardCompleted)
                {
                    var wizard = _host.Services.GetRequiredService<WizardWindow>();
                    wizard.Owner = main;
                    wizard.ShowDialog();
                }

                if (settings.StartMinimized)
                    main.Hide();
            });

            var updates = _host.Services.GetRequiredService<UpdateService>();
            _ = updates.CheckForUpdatesAsync();
        }
        catch (Exception ex)
        {
            await Dispatcher.InvokeAsync(() =>
            {
                MessageBox.Show(
                    $"Start fehlgeschlagen:\n\n{ex}",
                    "GoXLR Control Studio",
                    MessageBoxButton.OK,
                    MessageBoxImage.Error);
                Shutdown();
            });
        }
    }

    private static void ConfigureServices(HostBuilderContext _, IServiceCollection services)
    {
        services.AddSingleton<SettingsStore>();
        services.AddSingleton<ProfileStore>();
        services.AddSingleton<DiagnosticLog>();
        services.AddSingleton<WindowsAudioService>();
        services.AddSingleton<IVolumeSink>(sp => sp.GetRequiredService<WindowsAudioService>());
        services.AddSingleton<SendInputShortcutService>();
        services.AddSingleton<MediaKeyService>();
        services.AddSingleton<ProcessLaunchService>();
        services.AddSingleton<TrayService>();
        services.AddSingleton(typeof(Lazy<>), typeof(LazyService<>));

        services.AddSingleton<IDiscordIntegration>(sp =>
        {
            var settingsStore = sp.GetRequiredService<SettingsStore>();
            var shortcuts = sp.GetRequiredService<SendInputShortcutService>();
            // Lazy: AppBootstrapper depends on IDiscordIntegration — avoid circular resolve at ctor time.
            var lazyBootstrap = new Lazy<AppBootstrapper>(sp.GetRequiredService<AppBootstrapper>);
            return DiscordIntegrationFactory.Create(
                () =>
                {
                    // Create() reads settings immediately — must NOT resolve AppBootstrapper here
                    // or DI deadlocks (IDiscordIntegration ↔ AppBootstrapper).
                    if (lazyBootstrap.IsValueCreated)
                        return lazyBootstrap.Value.Settings;
                    return settingsStore.Load();
                },
                shortcuts);
        });

        services.AddSingleton<UtilityPatcher>();
        services.AddSingleton(sp =>
        {
            var log = sp.GetRequiredService<DiagnosticLog>();
            return new UpdateService(msg => log.Info(msg));
        });
        services.AddSingleton<IHardwareInputProvider>(sp =>
        {
            var settings = sp.GetRequiredService<SettingsStore>().Load();
            return settings.UseSimulatedHardware
                ? new SimulatedHardwareProvider()
                : new GoXlrUtilityProvider(autoStartDaemon: settings.AutoStartUtilityDaemon);
        });
        services.AddSingleton<IHardwareOutputController>(sp =>
            (IHardwareOutputController)sp.GetRequiredService<IHardwareInputProvider>());

        services.AddSingleton<AppBootstrapper>();

        services.AddSingleton<ActionDispatcher>(sp =>
        {
            var volume = sp.GetRequiredService<IVolumeSink>();
            var shortcuts = sp.GetRequiredService<SendInputShortcutService>();
            var media = sp.GetRequiredService<MediaKeyService>();
            var launch = sp.GetRequiredService<ProcessLaunchService>();
            var discord = sp.GetRequiredService<IDiscordIntegration>();
            var lazyBootstrap = new Lazy<AppBootstrapper>(sp.GetRequiredService<AppBootstrapper>);

            return new ActionDispatcher(
            [
                new MuteActionExecutor(volume),
                new EndpointMuteActionExecutor(volume),
                new ApplicationMuteActionExecutor(volume),
                new ShortcutActionExecutor(shortcuts),
                new DiscordMuteActionExecutor(discord),
                new DiscordDeafenActionExecutor(discord),
                new MediaPlayPauseExecutor(media),
                new MediaNextExecutor(media),
                new MediaPreviousExecutor(media),
                new LaunchApplicationExecutor(launch),
                new SwitchProfileExecutor(id => lazyBootstrap.Value.ActivateProfileAsync(id))
            ]);
        });

        services.AddSingleton<ControllerEngine>();
        services.AddSingleton<LightingFeedbackService>(sp =>
        {
            var settingsStore = sp.GetRequiredService<SettingsStore>();
            var lazyBootstrap = new Lazy<AppBootstrapper>(sp.GetRequiredService<AppBootstrapper>);
            return new LightingFeedbackService(
                sp.GetRequiredService<IHardwareInputProvider>(),
                sp.GetRequiredService<IHardwareOutputController>(),
                sp.GetRequiredService<ControllerEngine>(),
                sp.GetRequiredService<IVolumeSink>(),
                sp.GetRequiredService<IDiscordIntegration>(),
                () => lazyBootstrap.IsValueCreated
                    ? lazyBootstrap.Value.Settings
                    : settingsStore.Load());
        });
        services.AddSingleton<MainViewModel>();
        services.AddSingleton<MainWindow>();
        services.AddTransient<WizardWindow>();
    }

    protected override async void OnExit(ExitEventArgs e)
    {
        if (_host is not null)
        {
            var bootstrap = _host.Services.GetService<AppBootstrapper>();
            if (bootstrap is not null)
                await bootstrap.ShutdownAsync();
            await _host.StopAsync(TimeSpan.FromSeconds(2));
            _host.Dispose();
        }

        _mutex?.ReleaseMutex();
        _mutex?.Dispose();
        base.OnExit(e);
    }
}

internal sealed class LazyService<T> : Lazy<T> where T : notnull
{
    public LazyService(IServiceProvider provider) : base(provider.GetRequiredService<T>) { }
}
