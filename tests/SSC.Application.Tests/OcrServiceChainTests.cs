using SSC.Application.Abstractions;
using SSC.Application.Ocr;
using SSC.Domain.Capture;
using SSC.Domain.Geometry;
using Xunit;

namespace SSC.Application.Tests;

public sealed class OcrServiceChainTests
{
    [Fact]
    public async Task Recognize_FallsBackToNextAvailableEngine()
    {
        var failing = new FakeEngine("first", 0, true, throwOnRecognize: true);
        var working = new FakeEngine("second", 10, true, throwOnRecognize: false);
        var chain = new OcrServiceChain(new[] { working, failing });

        var result = await chain.RecognizeAsync(
            CreateFrame(),
            "en-US",
            TestContext.Current.CancellationToken);

        Assert.Equal("second:hello", result.Text);
        Assert.Equal("second", result.Language);
    }

    [Fact]
    public async Task Recognize_SkipsUnavailableEngines()
    {
        var unavailable = new FakeEngine("off", 0, false, throwOnRecognize: false);
        var working = new FakeEngine("on", 10, true, throwOnRecognize: false);
        var chain = new OcrServiceChain(new[] { unavailable, working });

        var result = await chain.RecognizeAsync(
            CreateFrame(),
            cancellationToken: TestContext.Current.CancellationToken);

        Assert.Equal("on:hello", result.Text);
    }

    [Fact]
    public async Task Recognize_ThrowsWhenNoEngineWorks()
    {
        var chain = new OcrServiceChain(new[]
        {
            new FakeEngine("off", 0, false, throwOnRecognize: false)
        });

        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            chain.RecognizeAsync(CreateFrame(), cancellationToken: TestContext.Current.CancellationToken));
    }

    [Fact]
    public void Languages_AreDistinctAndOrderedByPriority()
    {
        var chain = new OcrServiceChain(new[]
        {
            new FakeEngine("second", 10, true, false, new[] { "en-US" }),
            new FakeEngine("first", 0, true, false, new[] { "zh-Hans", "en-US" })
        });

        Assert.Equal(new[] { "zh-Hans", "en-US" }, chain.Languages);
    }

    private static CaptureFrame CreateFrame() =>
        new(20, 20, new byte[20 * 20 * 4], DateTimeOffset.UtcNow);

    private sealed class FakeEngine : IOcrEngine
    {
        private readonly bool _throw;
        private readonly DateTimeOffset _started = DateTimeOffset.UtcNow;

        public FakeEngine(
            string name,
            int priority,
            bool available,
            bool throwOnRecognize,
            IReadOnlyList<string>? languages = null)
        {
            Name = name;
            Priority = priority;
            IsAvailable = available;
            _throw = throwOnRecognize;
            Languages = languages ?? new[] { "en-US" };
        }

        public string Name { get; }

        public int Priority { get; }

        public bool IsAvailable { get; }

        public IReadOnlyList<string> Languages { get; }

        public Task<OcrResult> RecognizeAsync(
            CaptureFrame frame,
            string? language = null,
            CancellationToken cancellationToken = default)
        {
            _ = frame;
            _ = _started;
            if (_throw)
            {
                throw new InvalidOperationException("engine failed");
            }

            var line = new OcrLine("hello", new PixelRect(1, 1, 10, 10), 0.9);
            return Task.FromResult(new OcrResult($"{Name}:hello", new[] { line }, Name));
        }
    }
}
