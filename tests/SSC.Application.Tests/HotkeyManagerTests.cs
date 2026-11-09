using SSC.Application.Abstractions;
using SSC.Application.Hotkeys;
using SSC.Application.Settings;
using SSC.Platform.Abstractions;
using Xunit;

namespace SSC.Application.Tests;

public sealed class HotkeyManagerTests
{
    [Fact]
    public void Apply_RegistersEveryCatalogAction()
    {
        var service = new FakeHotkeyService();
        var manager = new HotkeyManager(service, PlatformKind.MacOS, new HotkeyRouter());
        var bindings = Defaults();

        var result = manager.Apply(bindings);

        Assert.True(result.Success);
        Assert.Equal(HotkeyCatalog.Actions.Count, service.Registered.Count);
        Assert.Equal("⌘⇧2", manager.Current["captureRegion"].ToDisplayString(PlatformKind.MacOS));
    }

    [Fact]
    public void Apply_InvalidGesture_LeavesExistingRegistrations()
    {
        var service = new FakeHotkeyService();
        var manager = new HotkeyManager(service, PlatformKind.MacOS, new HotkeyRouter());
        Assert.True(manager.Apply(Defaults()).Success);
        var before = service.RegistrationVersion;

        var invalid = Defaults();
        invalid["captureRegion"] = "Nope+X";
        var result = manager.Apply(invalid);

        Assert.False(result.Success);
        Assert.Contains("captureRegion", result.InvalidActionKeys);
        Assert.Equal(before, service.RegistrationVersion);
    }

    [Fact]
    public void Apply_DuplicateGestures_ReportsConflict()
    {
        var service = new FakeHotkeyService();
        var manager = new HotkeyManager(service, PlatformKind.MacOS, new HotkeyRouter());

        var duplicates = Defaults();
        duplicates["captureFull"] = duplicates["captureRegion"];
        var result = manager.Apply(duplicates);

        Assert.False(result.Success);
        var failure = Assert.Single(result.Failures);
        Assert.Equal(HotkeyRegistrationError.AlreadyInUse, failure.Error);
        Assert.Empty(service.Registered);
    }

    [Fact]
    public void Apply_RegistrationFailure_RollsBackToPreviousBindings()
    {
        var service = new FakeHotkeyService
        {
            FailurePolicy = (id, _) => id == "record" ? HotkeyRegistrationError.AlreadyInUse : null
        };
        var manager = new HotkeyManager(service, PlatformKind.MacOS, new HotkeyRouter());

        var result = manager.Apply(Defaults());

        Assert.False(result.Success);
        Assert.Equal("record", Assert.Single(result.Failures).ActionKey);
        Assert.Empty(service.Registered);

        service.FailurePolicy = null;
        Assert.True(manager.Apply(Defaults()).Success);
        Assert.Equal(HotkeyCatalog.Actions.Count, service.Registered.Count);
    }

    [Fact]
    public void Apply_UnchangedBindings_DoesNotReRegister()
    {
        var service = new FakeHotkeyService();
        var manager = new HotkeyManager(service, PlatformKind.MacOS, new HotkeyRouter());

        Assert.True(manager.Apply(Defaults()).Success);
        var version = service.RegistrationVersion;

        Assert.True(manager.Apply(Defaults()).Success);
        Assert.Equal(version, service.RegistrationVersion);
    }

    [Fact]
    public void Apply_DispatchesRouterByActionKey()
    {
        var service = new FakeHotkeyService();
        var router = new HotkeyRouter();
        var received = new List<string>();
        router.Handler = received.Add;
        var manager = new HotkeyManager(service, PlatformKind.MacOS, router);

        manager.Apply(Defaults());
        service.Raise("captureRegion");

        Assert.Equal(new[] { "captureRegion" }, received);
    }

    private static Dictionary<string, string> Defaults() =>
        HotkeyCatalog.Actions.ToDictionary(
            static action => action.Key,
            static action => HotkeyCatalog.GetDefaultGesture(action, PlatformKind.MacOS),
            StringComparer.OrdinalIgnoreCase);

    private sealed class FakeHotkeyService : IHotkeyService
    {
        private readonly Dictionary<string, Action> _callbacks = new(StringComparer.OrdinalIgnoreCase);

        public Dictionary<string, HotkeyGesture> Registered { get; } = new(StringComparer.OrdinalIgnoreCase);

        public int RegistrationVersion { get; private set; }

        public Func<string, HotkeyGesture, HotkeyRegistrationError?>? FailurePolicy { get; set; }

        public HotkeyRegistrationResult Register(string id, HotkeyGesture gesture, Action callback)
        {
            var failure = FailurePolicy?.Invoke(id, gesture);
            if (failure is not null)
            {
                return HotkeyRegistrationResult.Fail(failure.Value);
            }

            Registered[id] = gesture;
            _callbacks[id] = callback;
            RegistrationVersion++;
            return HotkeyRegistrationResult.Ok;
        }

        public bool Unregister(string id)
        {
            _callbacks.Remove(id);
            return Registered.Remove(id);
        }

        public void UnregisterAll()
        {
            Registered.Clear();
            _callbacks.Clear();
        }

        public void Raise(string id)
        {
            if (_callbacks.TryGetValue(id, out var callback))
            {
                callback();
            }
        }

        public void Dispose() => UnregisterAll();
    }
}
