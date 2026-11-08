using Microsoft.Extensions.DependencyInjection;
using SSC.Application.Abstractions;
using SSC.Application.Events;
using SSC.Application.Settings;
using SSC.Infrastructure.Localization;
using SSC.Infrastructure.Logging;
using SSC.Infrastructure.Paths;
using SSC.Infrastructure.Persistence;
using SSC.Infrastructure.SingleInstance;
using SSC.Infrastructure.Storage;
using SSC.Infrastructure.Time;

namespace SSC.Infrastructure.DependencyInjection;

public static class InfrastructureServiceCollectionExtensions
{
    public static IServiceCollection AddInfrastructure(this IServiceCollection services, IAppPaths paths)
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentNullException.ThrowIfNull(paths);

        services.AddSingleton(paths);
        services.AddSingleton<IClock, SystemClock>();
        services.AddSingleton(new SqliteConnectionFactory(paths.DatabasePath));
        services.AddSingleton<DatabaseMigrator>();
        services.AddSingleton<ISettingsRepository, SqliteSettingsRepository>();
        services.AddSingleton<IHistoryRepository, SqliteHistoryRepository>();
        services.AddSingleton<IBlobStore>(_ => new FileBlobStore(paths.BlobStoreDirectory));
        services.AddSingleton<ILocalizer, JsonLocalizer>();
        services.AddSingleton<ISingleInstanceService>(_ =>
            new FileLockSingleInstanceService(Path.Combine(paths.AppDataDirectory, "ssc.lock")));
        services.AddSingleton<IAppEventBus, InMemoryAppEventBus>();
        services.AddSingleton<SettingsService>();

        return services;
    }

    public static Serilog.ILogger ConfigureLogging(this IAppPaths paths) => LoggingSetup.Configure(paths);
}
