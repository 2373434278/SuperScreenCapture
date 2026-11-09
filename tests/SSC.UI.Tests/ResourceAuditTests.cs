using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Avalonia.Markup.Xaml;
using Avalonia.Media;
using Avalonia.Styling;
using Xunit;

namespace SSC.UI.Tests;

public sealed class ResourceAuditTests
{
    [AvaloniaFact]
    public void ThemeDictionaries_DefineTheSameTokens()
    {
        var theme = (ResourceDictionary)AvaloniaXamlLoader.Load(
            new Uri("avares://SSC.UI/Styles/Theme.axaml"));
        var dictionaries = theme.ThemeDictionaries;

        Assert.True(dictionaries.ContainsKey(ThemeVariant.Dark));
        Assert.True(dictionaries.ContainsKey(ThemeVariant.Default));

        var dark = ((ResourceDictionary)dictionaries[ThemeVariant.Dark]!).Keys.ToHashSet();
        var light = ((ResourceDictionary)dictionaries[ThemeVariant.Default]!).Keys.ToHashSet();

        Assert.Equal(dark, light);
    }

    [AvaloniaFact]
    public void IconDictionary_DefinesOnlyStreamGeometries()
    {
        var icons = (ResourceDictionary)AvaloniaXamlLoader.Load(
            new Uri("avares://SSC.UI/Styles/Icons.axaml"));

        Assert.True(icons.Count >= 30);
        foreach (var entry in icons)
        {
            Assert.StartsWith("Ico", (string)entry.Key);
            Assert.NotNull(entry.Value);
        }
    }

    [AvaloniaFact]
    public void ApplicationResources_ResolveEveryIconUsedByWindows()
    {
        string[] required =
        {
            "IcoAnnotate", "IcoBrand", "IcoCapture", "IcoCheck", "IcoChevronDown",
            "IcoClose", "IcoCopy", "IcoDelete", "IcoEdit", "IcoFile", "IcoImage",
            "IcoInfo", "IcoLibrary", "IcoLink", "IcoOcr", "IcoPin", "IcoRecord",
            "IcoRefresh", "IcoSettings", "IcoShield", "IcoStar", "IcoToolRectangle",
            "IcoTray", "IcoUpload"
        };

        var icons = (ResourceDictionary)AvaloniaXamlLoader.Load(
            new Uri("avares://SSC.UI/Styles/Icons.axaml"));
        var all = required
            .Concat(icons.Keys.Select(key => (string)key))
            .Distinct(StringComparer.Ordinal)
            .ToArray();

        foreach (var key in all)
        {
            Assert.True(
                Avalonia.Application.Current!.TryFindResource(key, out var resource),
                $"Missing icon resource {key}.");
            Assert.IsType<StreamGeometry>(resource);
        }
    }
}
