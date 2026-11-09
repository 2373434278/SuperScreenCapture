using System.Text.Json;
using SSC.Infrastructure.Localization;
using Xunit;

namespace SSC.UI.Tests;

public sealed class LocalizationTests
{
    [Fact]
    public void Catalogs_DefineTheSameKeys()
    {
        var zh = Load("zh.json");
        var en = Load("en.json");

        Assert.Equal(
            zh.Keys.OrderBy(key => key, StringComparer.Ordinal),
            en.Keys.OrderBy(key => key, StringComparer.Ordinal));
    }

    private static Dictionary<string, string> Load(string fileName)
    {
        var assembly = typeof(JsonLocalizer).Assembly;
        using var stream = assembly.GetManifestResourceStream(
            $"SSC.Infrastructure.Localization.Resources.{fileName}")
            ?? throw new InvalidOperationException($"Missing localization resource {fileName}.");
        using var document = JsonDocument.Parse(stream);
        return document.RootElement
            .EnumerateObject()
            .ToDictionary(property => property.Name, property => property.Value.GetString() ?? string.Empty);
    }
}
