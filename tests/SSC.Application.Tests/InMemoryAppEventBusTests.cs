using SSC.Application.Events;
using SSC.Application.Settings;
using Xunit;

namespace SSC.Application.Tests;

public sealed class InMemoryAppEventBusTests
{
    [Fact]
    public void Publish_DeliversToSubscriber()
    {
        var bus = new InMemoryAppEventBus();
        var received = new List<string>();
        using var subscription = bus.Subscribe<string>(received.Add);

        bus.Publish("hello");

        Assert.Equal(new[] { "hello" }, received);
    }

    [Fact]
    public void Dispose_StopsDelivery()
    {
        var bus = new InMemoryAppEventBus();
        var count = 0;
        var subscription = bus.Subscribe<string>(_ => count++);
        bus.Publish("one");
        subscription.Dispose();
        bus.Publish("two");

        Assert.Equal(1, count);
    }

    [Fact]
    public void Publish_IsolatesMessageTypes()
    {
        var bus = new InMemoryAppEventBus();
        var strings = 0;
        var settings = 0;
        using var first = bus.Subscribe<string>(_ => strings++);
        using var second = bus.Subscribe<AppSettings>(_ => settings++);

        bus.Publish("hello");

        Assert.Equal(1, strings);
        Assert.Equal(0, settings);
    }
}
