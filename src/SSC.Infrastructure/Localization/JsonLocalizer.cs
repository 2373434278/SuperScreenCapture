using System.ComponentModel;
using System.Reflection;
using System.Text.Json;
using SSC.Application.Abstractions;

namespace SSC.Infrastructure.Localization;

public sealed class JsonLocalizer : ILocalizer
{
    private static readonly string[] Languages = { "zh", "en" };
    private readonly Dictionary<string, IReadOnlyDictionary<string, string>> _catalogs;
    private string _language = "zh";

    public JsonLocalizer()
    {
        _catalogs = new Dictionary<string, IReadOnlyDictionary<string, string>>(StringComparer.OrdinalIgnoreCase)
        {
            ["zh"] = LoadCatalog("zh"),
            ["en"] = LoadCatalog("en")
        };
    }

    public event PropertyChangedEventHandler? PropertyChanged;

    public event EventHandler<string>? LanguageChanged;

    public string Language => _language;

    public IReadOnlyList<string> SupportedLanguages => Languages;

    public string this[string key]
    {
        get
        {
            if (string.IsNullOrWhiteSpace(key))
            {
                return string.Empty;
            }

            if (_catalogs.TryGetValue(_language, out var primary) && primary.TryGetValue(key, out var value))
            {
                return value;
            }

            if (_catalogs.TryGetValue("zh", out var fallback) && fallback.TryGetValue(key, out var fallbackValue))
            {
                return fallbackValue;
            }

            return key;
        }
    }

    public bool SetLanguage(string language)
    {
        if (string.IsNullOrWhiteSpace(language) || !_catalogs.ContainsKey(language))
        {
            return false;
        }

        if (string.Equals(_language, language, StringComparison.OrdinalIgnoreCase))
        {
            return true;
        }

        _language = language.ToLowerInvariant();
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs("Item[]"));
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(string.Empty));
        LanguageChanged?.Invoke(this, _language);
        return true;
    }

    private static IReadOnlyDictionary<string, string> LoadCatalog(string language)
    {
        var assembly = typeof(JsonLocalizer).Assembly;
        var resourceName = $"SSC.Infrastructure.Localization.Resources.{language}.json";

        using var stream = assembly.GetManifestResourceStream(resourceName)
            ?? throw new InvalidOperationException(
                $"Embedded localization resource '{resourceName}' was not found. Available: {string.Join(", ", assembly.GetManifestResourceNames())}");

        var catalog = JsonSerializer.Deserialize<Dictionary<string, string>>(stream)
            ?? new Dictionary<string, string>();

        return new Dictionary<string, string>(catalog, StringComparer.OrdinalIgnoreCase);
    }
}
