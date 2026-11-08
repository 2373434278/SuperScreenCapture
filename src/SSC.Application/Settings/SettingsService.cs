using System.Text.Json;
using SSC.Application.Abstractions;
using SSC.Platform.Abstractions;

namespace SSC.Application.Settings;

public sealed class SettingsService
{
    public const string StorageKey = "app.settings";

    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web)
    {
        WriteIndented = false
    };

    private readonly ISettingsRepository _repository;
    private readonly IAppPaths _paths;
    private readonly PlatformDescriptor _platform;
    private AppSettings _current;

    public SettingsService(
        ISettingsRepository repository,
        IAppPaths paths,
        PlatformDescriptor platform)
    {
        _repository = repository ?? throw new ArgumentNullException(nameof(repository));
        _paths = paths ?? throw new ArgumentNullException(nameof(paths));
        _platform = platform ?? throw new ArgumentNullException(nameof(platform));
        _current = LoadCore();
    }

    public event EventHandler<AppSettings>? Changed;

    public AppSettings Current => _current.Clone();

    public AppSettings CreateDefaults() => new()
    {
        Language = "zh",
        Theme = "dark",
        CloseToTray = true,
        SaveDirectory = _paths.ScreenshotsDirectory,
        Hotkeys = HotkeyCatalog.CreateDefaults(_platform.Kind)
    };

    public AppSettings Load()
    {
        _current = LoadCore();
        return _current.Clone();
    }

    public void Save(AppSettings settings)
    {
        ArgumentNullException.ThrowIfNull(settings);

        var normalized = Normalize(settings.Clone());
        var conflicts = HotkeyConflictDetector.Detect(normalized.Hotkeys);
        if (conflicts.Count > 0)
        {
            throw new SettingsValidationException("Hotkey conflicts detected.", conflicts);
        }

        _repository.Write(StorageKey, JsonSerializer.Serialize(normalized, JsonOptions));
        _current = normalized;
        Changed?.Invoke(this, normalized.Clone());
    }

    private AppSettings LoadCore()
    {
        var defaults = CreateDefaults();
        var raw = _repository.Read(StorageKey);
        if (string.IsNullOrWhiteSpace(raw))
        {
            return defaults;
        }

        AppSettings? loaded;
        try
        {
            loaded = JsonSerializer.Deserialize<AppSettings>(raw, JsonOptions);
        }
        catch (JsonException)
        {
            return defaults;
        }

        return loaded is null ? defaults : Normalize(loaded);
    }

    private AppSettings Normalize(AppSettings settings)
    {
        var defaults = CreateDefaults();

        settings.SchemaVersion = 1;
        settings.Language = settings.Language is "en" or "zh" ? settings.Language : defaults.Language;
        settings.Theme = settings.Theme is "light" or "dark" ? settings.Theme : defaults.Theme;
        settings.SaveDirectory = string.IsNullOrWhiteSpace(settings.SaveDirectory)
            ? defaults.SaveDirectory
            : settings.SaveDirectory.Trim();
        settings.FileNamePattern = string.IsNullOrWhiteSpace(settings.FileNamePattern)
            ? defaults.FileNamePattern
            : settings.FileNamePattern.Trim();
        settings.DefaultFormat = settings.DefaultFormat?.Trim().ToLowerInvariant() switch
        {
            "jpg" => "jpg",
            "webp" => "webp",
            _ => "png"
        };
        settings.JpegQuality = Math.Clamp(settings.JpegQuality, 40, 100);
        settings.RecordingFps = settings.RecordingFps is 15 or 30 or 60 ? settings.RecordingFps : 30;
        settings.RecordingBitrateMbps = Math.Clamp(settings.RecordingBitrateMbps, 1, 100);
        settings.OcrEngine = settings.OcrEngine is "tesseract" ? "tesseract" : "native";

        var hotkeys = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (var action in HotkeyCatalog.Actions)
        {
            var gesture = settings.Hotkeys.TryGetValue(action.Key, out var stored)
                && !string.IsNullOrWhiteSpace(stored)
                    ? stored.Trim()
                    : HotkeyCatalog.GetDefaultGesture(action, _platform.Kind);

            hotkeys[action.Key] = gesture;
        }

        settings.Hotkeys = hotkeys;
        return settings;
    }
}
