using System.ComponentModel;
using SSC.Infrastructure.Localization;
using Xunit;

namespace SSC.Infrastructure.Tests;

public sealed class JsonLocalizerTests
{
    [Fact]
    public void DefaultLanguage_IsChinese()
    {
        var localizer = new JsonLocalizer();

        Assert.Equal("zh", localizer.Language);
        Assert.Equal("通用", localizer["navGeneral"]);
        Assert.Equal("SuperScreenCapture", localizer["appTitle"]);
    }

    [Fact]
    public void SetLanguage_UpdatesCatalogAndRaisesEvents()
    {
        var localizer = new JsonLocalizer();
        string? propertyName = null;
        string? observedLanguage = null;
        localizer.PropertyChanged += (_, args) =>
        {
            if (args.PropertyName == "Item[]")
            {
                propertyName = args.PropertyName;
            }
        };
        localizer.LanguageChanged += (_, language) => observedLanguage = language;

        Assert.True(localizer.SetLanguage("en"));

        Assert.Equal("General", localizer["navGeneral"]);
        Assert.Equal("Item[]", propertyName);
        Assert.Equal("en", observedLanguage);
    }

    [Fact]
    public void SetLanguage_RejectsUnsupportedLanguage()
    {
        var localizer = new JsonLocalizer();

        Assert.False(localizer.SetLanguage("fr"));
        Assert.Equal("zh", localizer.Language);
    }

    [Fact]
    public void MissingKey_FallsBackToKey()
    {
        var localizer = new JsonLocalizer();

        Assert.Equal("missing.key", localizer["missing.key"]);
    }

    [Fact]
    public void BothCatalogs_HaveTheSameKeys()
    {
        var localizer = new JsonLocalizer();
        var zhKeys = GetKeys(localizer, "zh");
        var enKeys = GetKeys(localizer, "en");

        Assert.Equal(zhKeys.OrderBy(static key => key), enKeys.OrderBy(static key => key));
    }

    private static HashSet<string> GetKeys(JsonLocalizer localizer, string language)
    {
        localizer.SetLanguage(language);
        var keys = new HashSet<string>(StringComparer.Ordinal);
        var type = typeof(JsonLocalizer);
        var field = type.GetField("_catalogs", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance)!;
        var catalogs = (Dictionary<string, IReadOnlyDictionary<string, string>>)field.GetValue(localizer)!;
        foreach (var key in catalogs[language].Keys)
        {
            keys.Add(key);
        }

        return keys;
    }
}
