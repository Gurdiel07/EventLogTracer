using EventLogTracer.Core.Enums;
using EventLogTracer.Core.Models;
using FluentAssertions;
using Xunit;

namespace EventLogTracer.Tests.CoreTests;

public class EventEntryTests
{
    [Fact]
    public void NewEventEntry_HasNonEmptyId()
    {
        var entry = new EventEntry();
        entry.Id.Should().NotBe(Guid.Empty);
    }

    [Fact]
    public void NewEventEntry_TagsCollectionIsInitialized()
    {
        var entry = new EventEntry();
        entry.Tags.Should().NotBeNull();
        entry.Tags.Should().BeEmpty();
    }

    [Theory]
    [InlineData(EventLevel.Critical)]
    [InlineData(EventLevel.Error)]
    [InlineData(EventLevel.Warning)]
    [InlineData(EventLevel.Information)]
    [InlineData(EventLevel.Verbose)]
    public void EventEntry_AcceptsAllLevels(EventLevel level)
    {
        var entry = new EventEntry { Level = level };
        entry.Level.Should().Be(level);
    }

    [Fact]
    public void EventFilter_DefaultIsRegex_IsFalse()
    {
        var filter = new EventFilter();
        filter.IsRegex.Should().BeFalse();
    }

    [Fact]
    public void AlertRule_DefaultIsEnabled_IsTrue()
    {
        var rule = new AlertRule();
        rule.IsEnabled.Should().BeTrue();
    }

    [Fact]
    public void AlertRule_HasFilterInitialized()
    {
        var rule = new AlertRule();
        rule.Filter.Should().NotBeNull();
    }

    [Fact]
    public void AnomalyResult_Confidence_AcceptsValidRange()
    {
        var result = new AnomalyResult { Confidence = 0.95 };
        result.Confidence.Should().BeInRange(0.0, 1.0);
    }
}
