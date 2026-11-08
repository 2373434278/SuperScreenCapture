namespace SSC.Application.Abstractions;

public interface IAppPaths
{
    string AppDataDirectory { get; }

    string LogsDirectory { get; }

    string DatabasePath { get; }

    string BlobStoreDirectory { get; }

    string ScreenshotsDirectory { get; }

    string TempDirectory { get; }

    void EnsureCreated();
}
