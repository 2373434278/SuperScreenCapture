using Avalonia;
using Avalonia.Headless;
using Avalonia.Markup.Xaml;
using SSC.UI.Platform;

[assembly: AvaloniaTestApplication(typeof(SSC.UI.Tests.TestAppBuilder))]

namespace SSC.UI.Tests;

public static class TestAppBuilder
{
    public static AppBuilder BuildAvaloniaApp() =>
        AppBuilder.Configure<TestApp>()
            .UseSkia()
            .UseHeadless(new AvaloniaHeadlessPlatformOptions { UseHeadlessDrawing = false });
}

public partial class TestApp : Avalonia.Application
{
    public override void Initialize()
    {
        // Headless tests do not advance animations; disable motion so captured
        // frames show the final opacity instead of a blank first frame.
        Environment.SetEnvironmentVariable("SSC_REDUCE_MOTION", "1");
        AvaloniaXamlLoader.Load(this);
        PlatformStyles.Apply(this, PlatformInfo.Current);
    }
}
