using Microsoft.Extensions.DependencyInjection;
using SSC.Application.Abstractions;
using SSC.Application.Capture;
using SSC.Application.Editing;
using SSC.Application.Events;
using SSC.Application.Ocr;
using SSC.Application.Scroll;
using SSC.Application.Settings;
using SSC.Application.Storage;
using SSC.Application.Updates;
using SSC.Application.Upload;
using SSC.Infrastructure.Imaging;
using SSC.Infrastructure.Localization;
using SSC.Infrastructure.Logging;
using SSC.Infrastructure.Paths;
using SSC.Infrastructure.Persistence;
using SSC.Infrastructure.SingleInstance;
using SSC.Infrastructure.Storage;
using SSC.Infrastructure.Time;
using SSC.Infrastructure.Upload;

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
        services.AddSingleton<IImageEncoder, SkiaImageEncoder>();
        services.AddSingleton<ICaptureFrameLoader, SkiaCaptureFrameLoader>();
        services.AddSingleton<IAnnotationRenderer, SkiaAnnotationRenderer>();
        services.AddSingleton<IOcrService, OcrServiceChain>();
        services.AddSingleton<IScrollStitcher, ScrollStitcher>();
        services.AddSingleton<IUploadProvider, ImgurUploadProvider>();
        services.AddSingleton<IUploadProvider, S3UploadProvider>();
        services.AddSingleton<IUploadProvider, CustomHttpUploadProvider>();
        services.AddSingleton<UploadPipeline>();
        services.AddSingleton<CaptureFileNamer>();
        services.AddSingleton<ScreenshotService>();
        services.AddSingleton<IStorageMaintenance, FileSystemStorageMaintenance>();

        return services;
    }

    public static Serilog.ILogger ConfigureLogging(this IAppPaths paths) => LoggingSetup.Configure(paths);
}
