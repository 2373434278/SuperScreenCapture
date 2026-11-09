namespace SSC.Application;

public static class AppInfo
{
    public const string Name = "SuperScreenCapture";

    public const string License = "Apache-2.0";

    public static string Version =>
        typeof(AppInfo).Assembly.GetName().Version?.ToString(3) ?? "1.1.0";
}
