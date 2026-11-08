namespace SSC.Application.Settings;

public sealed record HotkeyConflict(string FirstAction, string SecondAction, string Gesture);

public static class HotkeyConflictDetector
{
    public static IReadOnlyList<HotkeyConflict> Detect(IReadOnlyDictionary<string, string> bindings)
    {
        ArgumentNullException.ThrowIfNull(bindings);

        var conflicts = new List<HotkeyConflict>();
        var seen = new Dictionary<string, (string Action, string Gesture)>(StringComparer.OrdinalIgnoreCase);

        foreach (var action in HotkeyCatalog.Actions)
        {
            if (!bindings.TryGetValue(action.Key, out var raw) || string.IsNullOrWhiteSpace(raw))
            {
                continue;
            }

            var gesture = raw.Trim();
            if (seen.TryGetValue(gesture, out var first))
            {
                conflicts.Add(new HotkeyConflict(first.Action, action.Key, gesture));
            }
            else
            {
                seen[gesture] = (action.Key, gesture);
            }
        }

        return conflicts;
    }
}
