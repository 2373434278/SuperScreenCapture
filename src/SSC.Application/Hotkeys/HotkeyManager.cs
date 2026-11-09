using SSC.Application.Abstractions;
using SSC.Application.Settings;
using SSC.Platform.Abstractions;

namespace SSC.Application.Hotkeys;

public sealed record HotkeyApplyFailure(
    string ActionKey,
    string Gesture,
    HotkeyRegistrationError Error,
    string? Detail);

public sealed record HotkeyApplyResult(
    bool Success,
    IReadOnlyList<string> InvalidActionKeys,
    IReadOnlyList<HotkeyApplyFailure> Failures)
{
    public static HotkeyApplyResult Ok { get; } = new(true, Array.Empty<string>(), Array.Empty<HotkeyApplyFailure>());
}

/// <summary>
/// 把设置里的快捷键字符串事务化地应用到 <see cref="IHotkeyService"/>：
/// 校验失败时不改动现有注册；注册中途失败时全部回滚。
/// </summary>
public sealed class HotkeyManager
{
    private readonly IHotkeyService _service;
    private readonly PlatformKind _platform;
    private readonly HotkeyRouter _router;
    private readonly object _gate = new();
    private Dictionary<string, HotkeyGesture> _current = new(StringComparer.OrdinalIgnoreCase);

    public HotkeyManager(IHotkeyService service, PlatformKind platform, HotkeyRouter router)
    {
        _service = service ?? throw new ArgumentNullException(nameof(service));
        _platform = platform;
        _router = router ?? throw new ArgumentNullException(nameof(router));
    }

    public IReadOnlyDictionary<string, HotkeyGesture> Current
    {
        get
        {
            lock (_gate)
            {
                return new Dictionary<string, HotkeyGesture>(_current, StringComparer.OrdinalIgnoreCase);
            }
        }
    }

    public HotkeyApplyResult Apply(IReadOnlyDictionary<string, string> bindings)
    {
        ArgumentNullException.ThrowIfNull(bindings);

        lock (_gate)
        {
            var parsed = new Dictionary<string, HotkeyGesture>(StringComparer.OrdinalIgnoreCase);
            var invalid = new List<string>();
            var duplicates = new List<HotkeyApplyFailure>();
            var byGesture = new Dictionary<HotkeyGesture, string>();

            foreach (var action in HotkeyCatalog.Actions)
            {
                if (!bindings.TryGetValue(action.Key, out var raw) || string.IsNullOrWhiteSpace(raw))
                {
                    continue;
                }

                if (!HotkeyGesture.TryParse(raw, out var gesture, out _))
                {
                    invalid.Add(action.Key);
                    continue;
                }

                if (byGesture.TryGetValue(gesture, out var existingAction))
                {
                    duplicates.Add(new HotkeyApplyFailure(
                        action.Key,
                        gesture.ToDisplayString(_platform),
                        HotkeyRegistrationError.AlreadyInUse,
                        existingAction));
                    continue;
                }

                byGesture[gesture] = action.Key;
                parsed[action.Key] = gesture;
            }

            if (invalid.Count > 0 || duplicates.Count > 0)
            {
                return new HotkeyApplyResult(false, invalid, duplicates);
            }

            if (SameAsCurrent(parsed))
            {
                return HotkeyApplyResult.Ok;
            }

            var previous = _current;
            _service.UnregisterAll();

            var failures = new List<HotkeyApplyFailure>();
            foreach (var pair in parsed)
            {
                var callback = CreateCallback(pair.Key);
                var result = _service.Register(pair.Key, pair.Value, callback);
                if (!result.Success)
                {
                    failures.Add(new HotkeyApplyFailure(
                        pair.Key,
                        pair.Value.ToDisplayString(_platform),
                        result.Error,
                        result.Detail));
                }
            }

            if (failures.Count == 0)
            {
                _current = parsed;
                return HotkeyApplyResult.Ok;
            }

            _service.UnregisterAll();
            foreach (var pair in previous)
            {
                _service.Register(pair.Key, pair.Value, CreateCallback(pair.Key));
            }

            _current = previous;
            return new HotkeyApplyResult(false, Array.Empty<string>(), failures);
        }
    }

    private Action CreateCallback(string actionKey) => () => _router.Dispatch(actionKey);

    private bool SameAsCurrent(Dictionary<string, HotkeyGesture> parsed)
    {
        if (parsed.Count != _current.Count)
        {
            return false;
        }

        foreach (var pair in parsed)
        {
            if (!_current.TryGetValue(pair.Key, out var gesture) || gesture != pair.Value)
            {
                return false;
            }
        }

        return true;
    }
}
