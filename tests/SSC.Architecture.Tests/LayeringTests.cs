using SSC.Application.Settings;
using SSC.Domain.Geometry;
using SSC.Infrastructure.Persistence;
using SSC.Platform.Abstractions;
using Xunit;

namespace SSC.Architecture.Tests;

public sealed class LayeringTests
{
    [Fact]
    public void Domain_HasNoExternalDependencies()
    {
        var references = GetReferences(typeof(PixelRect));

        Assert.All(references, name =>
            Assert.True(
                name.StartsWith("System", StringComparison.Ordinal)
                || name is "netstandard" or "mscorlib",
                $"Domain must not reference '{name}'."));
    }

    [Fact]
    public void Application_DoesNotReferenceUiInfrastructureOrPlatformImplementations()
    {
        var references = GetReferences(typeof(SettingsService));

        Assert.Contains("SSC.Domain", references);
        Assert.Contains("SSC.Platform.Abstractions", references);
        Assert.DoesNotContain(references, IsForbiddenApplicationDependency);
    }

    [Fact]
    public void Infrastructure_DoesNotReferenceAvalonia()
    {
        var references = GetReferences(typeof(SqliteConnectionFactory));

        Assert.DoesNotContain(references, name =>
            name.StartsWith("Avalonia", StringComparison.Ordinal));
    }

    [Fact]
    public void PlatformImplementations_DoNotReferenceAvalonia()
    {
        var references = GetReferences(typeof(SSC.Platform.Mock.MockScreenCaptureService));

        Assert.DoesNotContain(references, name =>
            name.StartsWith("Avalonia", StringComparison.Ordinal)
            || name.StartsWith("SkiaSharp", StringComparison.Ordinal));
        Assert.DoesNotContain("SSC.Infrastructure", references);
    }

    [Fact]
    public void Ui_DoesNotReferenceInfrastructureOrPlatformImplementations()
    {
        var references = GetReferences(typeof(SSC.UI.ViewModels.MainWindowViewModel));

        Assert.Contains("SSC.Application", references);
        Assert.DoesNotContain("SSC.Infrastructure", references);
        Assert.DoesNotContain("SSC.Platform.Mac", references);
        Assert.DoesNotContain("SSC.Platform.Windows", references);
        Assert.DoesNotContain("SSC.Platform.Mock", references);
    }

    [Fact]
    public void PlatformAbstractions_StayDependencyFree()
    {
        var references = GetReferences(typeof(PlatformDescriptor));

        Assert.All(references, name =>
            Assert.True(
                name.StartsWith("System", StringComparison.Ordinal)
                || name is "netstandard" or "mscorlib",
                $"Platform abstractions must not reference '{name}'."));
    }

    private static string[] GetReferences(Type type) =>
        type.Assembly.GetReferencedAssemblies()
            .Select(static assembly => assembly.Name ?? string.Empty)
            .Where(static name => name.Length > 0)
            .ToArray();

    private static bool IsForbiddenApplicationDependency(string name) =>
        name.StartsWith("Avalonia", StringComparison.Ordinal)
        || name.StartsWith("SkiaSharp", StringComparison.Ordinal)
        || name.StartsWith("Serilog", StringComparison.Ordinal)
        || name.StartsWith("Microsoft.Data.Sqlite", StringComparison.Ordinal)
        || name.StartsWith("Microsoft.Extensions", StringComparison.Ordinal)
        || name is "SSC.Infrastructure" or "SSC.UI" or "SSC.App"
            or "SSC.Platform.Mac" or "SSC.Platform.Windows" or "SSC.Platform.Mock";
}
