using Avalonia;
using Velopack;

namespace SSC.App;

internal static class Program
{
    /// <summary>
    /// 应用入口。Velopack 安装器要求在 UI 初始化前处理安装/更新钩子；
    /// 非安装器启动时该调用为空操作。
    /// </summary>
    [STAThread]
    public static void Main(string[] args)
    {
        VelopackApp.Build().Run();
        BuildAvaloniaApp().StartWithClassicDesktopLifetime(args);
    }

    public static AppBuilder BuildAvaloniaApp() =>
        AppBuilder.Configure<App>()
            .UsePlatformDetect()
            .WithInterFont()
#if DEBUG
            .WithDeveloperTools()
#endif
            .LogToTrace();
}
