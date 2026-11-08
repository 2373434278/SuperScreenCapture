using Microsoft.Extensions.DependencyInjection;
using SSC.Application.Abstractions;
using SSC.Application.Settings;
using SSC.Infrastructure.DependencyInjection;
using SSC.Infrastructure.Logging;
using SSC.Infrastructure.Paths;
using SSC.Infrastructure.Persistence;
using SSC.UI.ViewModels;
using SSC.UI.Views;
#if SSC_WINDOWS
using SSC.Platform.Windows;
#else
using SSC.Platform.Mac;
#endif

namespace SSC.App;

internal static class Bootstrap
{
    public static ServiceProvider Build()
    {
        var paths = new DefaultAppPaths();
        paths.EnsureCreated();
        LoggingSetup.Configure(paths);

        var services = new ServiceCollection();
        services.AddInfrastructure(paths);
#if SSC_WINDOWS
        services.AddWindowsPlatform();
#else
        services.AddMacPlatform();
#endif
        services.AddSingleton<MainWindowViewModel>();
        services.AddSingleton<MainWindow>();

        var provider = services.BuildServiceProvider(new ServiceProviderOptions
        {
            ValidateOnBuild = true,
            ValidateScopes = true
        });

        provider.GetRequiredService<DatabaseMigrator>().Migrate();

        var settings = provider.GetRequiredService<SettingsService>().Current;
        provider.GetRequiredService<ILocalizer>().SetLanguage(settings.Language);

        return provider;
    }
}
