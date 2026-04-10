using EventLogTracer.Core.Enums;
using EventLogTracer.Core.Models;
using EventLogTracer.Infrastructure.Services;
using FluentAssertions;
using Xunit;

namespace EventLogTracer.Tests.InfrastructureTests;

public class MockEventLogReaderTests : IDisposable
{
    private readonly MockEventLogReader _reader = new();

    [Fact]
    public async Task GetEventsAsync_ReturnsEvents()
    {
        var filter = new EventFilter();
        var events = await _reader.GetEventsAsync(filter);

        events.Should().NotBeNull();
        events.Should().NotBeEmpty();
    }

    [Fact]
    public async Task GetEventsAsync_WithLevelFilter_ReturnsMatchingOnly()
    {
        var filter = new EventFilter
        {
            Levels = [EventLevel.Error, EventLevel.Critical]
        };

        var events = (await _reader.GetEventsAsync(filter)).ToList();

        events.Should().OnlyContain(e =>
            e.Level == EventLevel.Error || e.Level == EventLevel.Critical);
    }

    [Fact]
    public void StartMonitoring_SetsIsMonitoringTrue()
    {
        _reader.StartMonitoring(_ => { });
        _reader.IsMonitoring.Should().BeTrue();
    }

    [Fact]
    public void StopMonitoring_SetsIsMonitoringFalse()
    {
        _reader.StartMonitoring(_ => { });
        _reader.StopMonitoring();

        // Allow the task to observe cancellation
        Thread.Sleep(100);
        _reader.IsMonitoring.Should().BeFalse();
    }

    [Fact]
    public async Task StartMonitoring_DeliversEventWithinTimeout()
    {
        EventEntry? received = null;
        var tcs = new TaskCompletionSource<EventEntry>();

        _reader.StartMonitoring(e =>
        {
            received = e;
            tcs.TrySetResult(e);
        });

        var completed = await Task.WhenAny(tcs.Task, Task.Delay(5000));
        _reader.StopMonitoring();

        completed.Should().Be(tcs.Task, "an event should be delivered within 5 seconds");
        received.Should().NotBeNull();
        received!.Id.Should().NotBe(Guid.Empty);
    }

    public void Dispose() => _reader.Dispose();
}
