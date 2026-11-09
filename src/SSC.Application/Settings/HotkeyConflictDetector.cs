using SSC.Application.Hotkeys;

namespace SSC.Application.Settings;

public sealed record HotkeyConflict(string FirstAction, string SecondAction, string Gesture);

public static class HotkeyConflictDetector
{
    public static IReadOnlyList<HotkeyConflict> Detect(IReadOnlyDictionary<string, string> bindings)
    {
        ArgumentNullException.ThrowIfNull(bindings);

        var conflicts = new List<HotkeyConflict>();
        var seen = new Dictionary<HotkeyGesture, (string Action, string Gesture)>();
        var seenRaw = new Dictionary<string, (string Action, string Gesture)>(StringComparer.OrdinalIgnoreCase);

        foreach (var action in HotkeyCatalog.Actions)
        {
            if (!bindings.TryGetValue(action.Key, out var raw) || string.IsNullOrWhiteSpace(raw))
            {
                continue;
            }

            var gesture = raw.Trim();
            var hasCanonical = HotkeyGesture.TryParse(gesture, out var parsed, out _);
            var duplicate = hasCanonical
                ? seen.TryGetValue(parsed, out var canonicalFirst) ? canonicalFirst : default
                : seenRaw.TryGetValue(gesture, out var rawFirst) ? rawFirst : default;

            if (duplicate.Action is not null)
            {
                conflicts.Add(new HotkeyConflict(duplicate.Action, action.Key, gesture));
                continue;
            }

            if (hasCanonical)
            {
                seen[parsed] = (action.Key, gesture);
            }
            else
            {
                seenRaw[gesture] = (action.Key, gesture);
            }
        }

        return conflicts;
    }
}
