using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Markup.Xaml;
using Avalonia.Platform;
using Avalonia.Styling;
using Microsoft.Extensions.DependencyInjection;
using Serilog;
using SSC.Application.Abstractions;
using SSC.Application.Settings;
using SSC.UI.ViewModels;
using SSC.UI.Views;
using AppInfo = SSC.Application.AppInfo;

namespace SSC.App;

public partial class App : Avalonia.Application
{
    private ServiceProvider? _services;
    private IClassicDesktopStyleApplicationLifetime? _desktop;
    private MainWindow? _mainWindow;
    private TrayIcon? _trayIcon;
    private bool _isQuitting;
    private bool _allowClose;

    public override void Initialize()
    {
        AvaloniaXamlLoader.Load(this);
    }

    public override void OnFrameworkInitializationCompleted()
    {
        if (ApplicationLifetime is not IClassicDesktopStyleApplicationLifetime desktop)
        {
            base.OnFrameworkInitializationCompleted();
            return;
        }

        _desktop = desktop;
        desktop.ShutdownMode = ShutdownMode.OnExplicitShutdown;

        try
        {
            _services = Bootstrap.Build();
        }
        catch (Exception exception)
        {
            Log.Fatal(exception, "Application bootstrap failed.");
            Log.CloseAndFlush();
            desktop.Shutdown();
            base.OnFrameworkInitializationCompleted();
            return;
        }

        var singleInstance = _services.GetRequiredService<ISingleInstanceService>();
        if (!singleInstance.TryAcquire())
        {
            Log.Warning("Another SuperScreenCapture instance is already running.");
            Log.CloseAndFlush();
            desktop.Shutdown();
            base.OnFrameworkInitializationCompleted();
            return;
        }

        var settingsService = _services.GetRequiredService<SettingsService>();
        var localizer = _services.GetRequiredService<ILocalizer>();
        var settings = settingsService.Current;

        RequestedThemeVariant = settings.Theme == "light"
            ? ThemeVariant.Light
            : ThemeVariant.Dark;

        _mainWindow = _services.GetRequiredService<MainWindow>();
        _mainWindow.Icon = LoadWindowIcon();
        _mainWindow.Closing += OnMainWindowClosing;
        _mainWindow.Closed += OnMainWindowClosed;
        desktop.MainWindow = _mainWindow;

        try
        {
            _trayIcon = TrayIconFactory.Create(
                this,
                LoadWindowIcon(),
                localizer,
                ShowMainWindow,
                NotifyComingSoon,
                Quit);
        }
        catch (Exception exception)
        {
            Log.Error(exception, "Tray icon could not be created.");
        }

        localizer.LanguageChanged += OnLanguageChanged;
        desktop.Exit += (_, _) => Cleanup();

        var startHidden = Environment.GetCommandLineArgs().Contains("--hidden", StringComparer.OrdinalIgnoreCase);
        if (!startHidden)
        {
            _mainWindow.Show();
        }

        Log.Information("{Application} {Version} started.", AppInfo.Name, AppInfo.Version);
        base.OnFrameworkInitializationCompleted();
    }

    private static WindowIcon LoadWindowIcon() =>
        new(AssetLoader.Open(new Uri("avares://SSC.App/Assets/ssc-tray.png")));

    private void OnMainWindowClosing(object? sender, WindowClosingEventArgs e)
    {
        if (_isQuitting)
        {
            return;
        }

        var closeToTray = _services?.GetRequiredService<SettingsService>().Current.CloseToTray ?? true;
        if (!closeToTray)
        {
            _allowClose = true;
            return;
        }

        e.Cancel = true;
        if (sender is Window window)
        {
            window.Hide();
        }
    }

    private void OnMainWindowClosed(object? sender, EventArgs e)
    {
        if (_allowClose)
        {
            _desktop?.Shutdown();
        }
    }

    private void OnLanguageChanged(object? sender, string language)
    {
        if (_trayIcon is null || _services is null)
        {
            return;
        }

        TrayIconFactory.Update(
            _trayIcon,
            _services.GetRequiredService<ILocalizer>(),
            ShowMainWindow,
            NotifyComingSoon,
            Quit);
    }

    private void ShowMainWindow()
    {
        if (_mainWindow is null)
        {
            return;
        }

        _mainWindow.Show();
        if (_mainWindow.WindowState == WindowState.Minimized)
        {
            _mainWindow.WindowState = WindowState.Normal;
        }

        _mainWindow.Activate();
    }

    private void NotifyComingSoon()
    {
        ShowMainWindow();
        _services?.GetRequiredService<MainWindowViewModel>().ShowComingSoon();
    }

    private void Quit()
    {
        _isQuitting = true;
        _desktop?.Shutdown();
    }

    private void Cleanup()
    {
        _trayIcon?.Dispose();
        _trayIcon = null;

        if (_services is not null)
        {
            _services.GetService<ISingleInstanceService>()?.Dispose();
            _services.Dispose();
            _services = null;
        }

        Log.CloseAndFlush();
    }
}
