using SSC.Application.Abstractions;

namespace SSC.Infrastructure.Paths;

public sealed class DefaultAppPaths : IAppPaths
{
    private const string AppFolderName = "SuperScreenCapture";

    public DefaultAppPaths(string? baseDirectory = null)
    {
        AppDataDirectory = Path.GetFullPath(baseDirectory ?? GetDefaultAppDataDirectory());
        LogsDirectory = Path.Combine(AppDataDirectory, "logs");
        DatabasePath = Path.Combine(AppDataDirectory, "ssc.db");
        BlobStoreDirectory = Path.Combine(AppDataDirectory, "blobs");
        TempDirectory = Path.Combine(AppDataDirectory, "tmp");
        ScreenshotsDirectory = GetDefaultScreenshotsDirectory();
    }

    public string AppDataDirectory { get; }

    public string LogsDirectory { get; }

    public string DatabasePath { get; }

    public string BlobStoreDirectory { get; }

    public string ScreenshotsDirectory { get; }

    public string TempDirectory { get; }

    public void EnsureCreated()
    {
        Directory.CreateDirectory(AppDataDirectory);
        Directory.CreateDirectory(LogsDirectory);
        Directory.CreateDirectory(BlobStoreDirectory);
        Directory.CreateDirectory(TempDirectory);
    }

    public static string GetDefaultAppDataDirectory()
    {
        if (OperatingSystem.IsWindows())
        {
            return Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                AppFolderName);
        }

        var home = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);

        if (OperatingSystem.IsMacOS())
        {
            return Path.Combine(home, "Library", "Application Support", AppFolderName);
        }

        var xdg = Environment.GetEnvironmentVariable("XDG_DATA_HOME");
        var dataRoot = string.IsNullOrWhiteSpace(xdg) ? Path.Combine(home, ".local", "share") : xdg;
        return Path.Combine(dataRoot, AppFolderName);
    }

    public static string GetDefaultScreenshotsDirectory()
    {
        var pictures = Environment.GetFolderPath(Environment.SpecialFolder.MyPictures);
        if (string.IsNullOrWhiteSpace(pictures))
        {
            pictures = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.UserProfile),
                "Pictures");
        }

        return Path.Combine(pictures, AppFolderName);
    }
}
