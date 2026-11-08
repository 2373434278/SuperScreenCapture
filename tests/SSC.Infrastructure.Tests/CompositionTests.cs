using Microsoft.Extensions.DependencyInjection;
using SSC.Application.Abstractions;
using SSC.Application.Settings;
using SSC.Infrastructure.DependencyInjection;
using SSC.Infrastructure.Paths;
using SSC.Infrastructure.Persistence;
using SSC.Platform.Mock;
using Xunit;

namespace SSC.Infrastructure.Tests;

public sealed class CompositionTests
{
    [Fact]
    public void InfrastructureAndMockPlatform_ResolveCoreServices()
    {
        using var temp = new TestDirectory();
        var paths = new DefaultAppPaths(temp.Path);
        paths.EnsureCreated();

        var services = new ServiceCollection();
        services.AddInfrastructure(paths);
        services.AddMockPlatform();
        using var provider = services.BuildServiceProvider();

        provider.GetRequiredService<DatabaseMigrator>().Migrate();

        var settings = provider.GetRequiredService<SettingsService>();
        Assert.Equal("zh", settings.Current.Language);
        Assert.Equal("⌘⇧2", settings.Current.Hotkeys["captureRegion"]);

        var localizer = provider.GetRequiredService<ILocalizer>();
        Assert.Equal("zh", localizer.Language);

        var history = provider.GetRequiredService<IHistoryRepository>();
        Assert.Equal(0, history.Count());

        var hotkeys = provider.GetRequiredService<IHotkeyService>();
        Assert.True(hotkeys.Register("captureRegion", "⌘⇧2", () => { }));

        var recording = provider.GetRequiredService<IRecordingService>();
        Assert.IsType<MockRecordingService>(recording);
    }
}
