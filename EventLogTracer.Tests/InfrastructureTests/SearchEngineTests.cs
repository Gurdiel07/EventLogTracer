using EventLogTracer.Core.Enums;
using EventLogTracer.Core.Interfaces;
using EventLogTracer.Core.Models;
using EventLogTracer.Infrastructure.Services;
using FluentAssertions;
using Xunit;

namespace EventLogTracer.Tests.InfrastructureTests;

public class SearchEngineTests
{
    private readonly ISearchEngine _engine = new SearchEngine();

    // ── Helpers ───────────────────────────────────────────────────────────────

    private static EventEntry E(
        string message    = "test message",
        string source     = "TestSource",
        EventLevel level  = EventLevel.Information,
        int eventId       = 1000,
        string logName    = "Application",
        string machine    = "SERVER01") => new()
    {
        Message     = message,
        Source      = source,
        Level       = level,
        EventId     = eventId,
        LogName     = logName,
        MachineName = machine,
        TimeCreated = DateTime.UtcNow
    };

    // ── Simple term search ────────────────────────────────────────────────────

    [Fact]
    public void Search_SimpleText_MatchesMessageAndSource()
    {
        var entries = new List<EventEntry>
        {
            E(message: "svchost started"),
            E(source: "svchost"),
            E(message: "explorer error"),
        };

        _engine.Search(entries, "svchost").Should().HaveCount(2);
    }

    [Fact]
    public void Search_CaseInsensitive()
    {
        var entries = new List<EventEntry> { E(message: "SVCHOST started") };

        _engine.Search(entries, "svchost").Should().HaveCount(1);
        _engine.Search(entries, "SVCHOST").Should().HaveCount(1);
    }

    [Fact]
    public void Search_QuotedPhrase_MatchesExact()
    {
        var entries = new List<EventEntry>
        {
            E(message: "failed logon attempt"),
            E(message: "failed attempt logon"),
        };

        var results = _engine.Search(entries, "\"failed logon\"");

        results.Should().HaveCount(1);
        results[0].Message.Should().Contain("failed logon");
    }

    // ── Field filters ─────────────────────────────────────────────────────────

    [Fact]
    public void Search_SourceField_ContainsMatch()
    {
        var entries = new List<EventEntry>
        {
            E(source: "SecurityAuditing"),
            E(source: "Kernel"),
        };

        _engine.Search(entries, "source:Security").Should().HaveCount(1);
    }

    [Fact]
    public void Search_LevelField_ExactMatch()
    {
        var entries = new List<EventEntry>
        {
            E(level: EventLevel.Error),
            E(level: EventLevel.Warning),
        };

        _engine.Search(entries, "level:Error").Should().HaveCount(1);
    }

    [Fact]
    public void Search_EventIdField_ExactMatch()
    {
        var entries = new List<EventEntry>
        {
            E(eventId: 4624),
            E(eventId: 4625),
        };

        var results = _engine.Search(entries, "eventid:4624");

        results.Should().HaveCount(1);
        results[0].EventId.Should().Be(4624);
    }

    [Fact]
    public void Search_LogField_ContainsMatch()
    {
        var entries = new List<EventEntry>
        {
            E(logName: "Security"),
            E(logName: "Application"),
        };

        _engine.Search(entries, "log:Sec").Should().HaveCount(1);
    }

    [Fact]
    public void Search_MachineField_ContainsMatch()
    {
        var entries = new List<EventEntry>
        {
            E(machine: "DC01"),
            E(machine: "WEB01"),
        };

        _engine.Search(entries, "machine:DC").Should().HaveCount(1);
    }

    // ── Boolean operators ─────────────────────────────────────────────────────

    [Fact]
    public void Search_ExplicitAnd_BothConditionsMustMatch()
    {
        var entries = new List<EventEntry>
        {
            E(message: "login failed", level: EventLevel.Error),
            E(message: "login ok",     level: EventLevel.Information),
            E(message: "disk error",   level: EventLevel.Error),
        };

        var results = _engine.Search(entries, "login AND level:Error");

        results.Should().HaveCount(1);
        results[0].Message.Should().Contain("login failed");
    }

    [Fact]
    public void Search_ExplicitOr_EitherConditionMatches()
    {
        var entries = new List<EventEntry>
        {
            E(level: EventLevel.Error),
            E(level: EventLevel.Critical),
            E(level: EventLevel.Information),
        };

        _engine.Search(entries, "level:Error OR level:Critical").Should().HaveCount(2);
    }

    [Fact]
    public void Search_ExplicitNot_ExcludesMatches()
    {
        var entries = new List<EventEntry>
        {
            E(level: EventLevel.Information),
            E(level: EventLevel.Error),
            E(level: EventLevel.Warning),
        };

        _engine.Search(entries, "NOT level:Error").Should().HaveCount(2);
    }

    [Fact]
    public void Search_ImplicitAnd_AdjacentTerms()
    {
        var entries = new List<EventEntry>
        {
            E(message: "login failed attempt"),
            E(message: "login success"),
            E(message: "disk failed"),
        };

        // "login failed" without quotes → login AND failed
        var results = _engine.Search(entries, "login failed");

        results.Should().HaveCount(1);
        results[0].Message.Should().Contain("login failed");
    }

    // ── Operator precedence: NOT > AND > OR ───────────────────────────────────

    [Fact]
    public void Search_NotHasHigherPrecedenceThanAnd()
    {
        // "svchost NOT level:Error"  =  "svchost AND (NOT level:Error)"
        var entries = new List<EventEntry>
        {
            E(message: "svchost", level: EventLevel.Information),  // match
            E(message: "svchost", level: EventLevel.Error),        // no match (Error excluded)
            E(message: "other",   level: EventLevel.Information),  // no match (no svchost)
        };

        var results = _engine.Search(entries, "svchost NOT level:Error");

        results.Should().HaveCount(1);
        results[0].Level.Should().Be(EventLevel.Information);
    }

    [Fact]
    public void Search_AndHasHigherPrecedenceThanOr()
    {
        // "level:Error OR level:Warning AND source:Security"
        // correct:   level:Error  OR  (level:Warning AND source:Security)
        // incorrect: (level:Error OR level:Warning) AND source:Security
        var e1 = E(level: EventLevel.Error,   source: "OtherSource"); // matches level:Error
        var e2 = E(level: EventLevel.Warning, source: "Security");    // matches (Warning AND source:Security)
        var e3 = E(level: EventLevel.Warning, source: "OtherSource"); // matches Warning but not Security

        var results = _engine.Search(
            new List<EventEntry> { e1, e2, e3 },
            "level:Error OR level:Warning AND source:Security");

        results.Should().HaveCount(2);
        results.Should().Contain(e1);
        results.Should().Contain(e2);
        results.Should().NotContain(e3);
    }

    [Fact]
    public void Search_NotHasHigherPrecedenceThanOr()
    {
        // "level:Error OR NOT level:Information"
        // = level:Error OR (NOT level:Information)
        var err  = E(level: EventLevel.Error);
        var info = E(level: EventLevel.Information);
        var warn = E(level: EventLevel.Warning);

        var results = _engine.Search(
            new List<EventEntry> { err, info, warn },
            "level:Error OR NOT level:Information");

        // err  → level:Error → true
        // info → level:Error=false, NOT level:Information=false → false
        // warn → level:Error=false, NOT level:Information=true  → true
        results.Should().HaveCount(2);
        results.Should().Contain(err);
        results.Should().Contain(warn);
        results.Should().NotContain(info);
    }

    // ── Parentheses ───────────────────────────────────────────────────────────

    [Fact]
    public void Search_Parentheses_OverrideDefaultPrecedence()
    {
        // "(level:Error OR level:Warning) AND source:Security"
        // without parens AND would bind tighter; parens force OR to be evaluated first
        var e1 = E(level: EventLevel.Error,   source: "Security");    // match
        var e2 = E(level: EventLevel.Warning, source: "Security");    // match
        var e3 = E(level: EventLevel.Error,   source: "OtherSource"); // no match (source wrong)

        var results = _engine.Search(
            new List<EventEntry> { e1, e2, e3 },
            "(level:Error OR level:Warning) AND source:Security");

        results.Should().HaveCount(2);
        results.Should().Contain(e1);
        results.Should().Contain(e2);
        results.Should().NotContain(e3);
    }

    [Fact]
    public void Search_NestedParentheses_EvaluateCorrectly()
    {
        var match = E(level: EventLevel.Critical, source: "Kernel", message: "panic");
        var noMatch = E(level: EventLevel.Critical, source: "Kernel", message: "ok");

        var results = _engine.Search(
            new List<EventEntry> { match, noMatch },
            "((level:Critical AND source:Kernel) AND panic)");

        results.Should().HaveCount(1);
        results[0].Should().Be(match);
    }

    // ── Regex ─────────────────────────────────────────────────────────────────

    [Fact]
    public void Search_Regex_MatchesPatternInMessage()
    {
        var entries = new List<EventEntry>
        {
            E(message: "failed logon for user admin"),
            E(message: "successful logon"),
            E(message: "logon failed 3 times"),
        };

        // matches messages containing "fail...logon" or "logon...fail" in any order
        var results = _engine.Search(entries, "/fail.*logon|logon.*fail/");

        results.Should().HaveCount(2);
    }

    [Fact]
    public void Search_RegexCombinedWithField_Works()
    {
        var entries = new List<EventEntry>
        {
            E(message: "failed logon", level: EventLevel.Error),
            E(message: "failed logon", level: EventLevel.Information),
        };

        var results = _engine.Search(entries, "/failed.*logon/ AND level:Error");

        results.Should().HaveCount(1);
        results[0].Level.Should().Be(EventLevel.Error);
    }

    [Fact]
    public void Search_InvalidRegex_ReturnsEmpty_DoesNotThrow()
    {
        var entries = new List<EventEntry> { E() };

        var act = () => _engine.Search(entries, "/[unclosed/");
        act.Should().NotThrow();
        act().Should().BeEmpty();
    }

    [Fact]
    public void Search_ReDoSPattern_DoesNotHang()
    {
        // (a+)+b on a long string of a's is a classic ReDoS trigger; the 2-second
        // timeout should cause EvaluateRegex to return false rather than loop forever.
        var entries = new List<EventEntry>
        {
            E(message: new string('a', 30) + "b")
        };

        var act = () => _engine.Search(entries, "/(a+)+c/");
        act.Should().NotThrow();
        // result may be empty or contain the entry depending on whether timeout fires;
        // either outcome is acceptable — what matters is no hang/exception.
    }

    // ── Validation ────────────────────────────────────────────────────────────

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public void Validate_EmptyOrWhitespace_IsInvalid(string query)
    {
        var result = _engine.Validate(query);

        result.IsValid.Should().BeFalse();
        result.ErrorMessage.Should().NotBeNullOrEmpty();
    }

    [Fact]
    public void Validate_UnmatchedOpenParen_IsInvalid()
    {
        var result = _engine.Validate("(level:Error AND source:DCOM");

        result.IsValid.Should().BeFalse();
        result.ErrorMessage.Should().ContainEquivalentOf("parenthes");
    }

    [Fact]
    public void Validate_UnmatchedCloseParen_IsInvalid()
    {
        var result = _engine.Validate("level:Error)");

        result.IsValid.Should().BeFalse();
        result.ErrorMessage.Should().NotBeNullOrEmpty();
    }

    [Fact]
    public void Validate_UnknownField_IsInvalid()
    {
        var result = _engine.Validate("badfield:value");

        result.IsValid.Should().BeFalse();
        result.ErrorMessage.Should().Contain("badfield");
    }

    [Fact]
    public void Validate_EventIdNonNumeric_IsInvalid()
    {
        var result = _engine.Validate("eventid:abc");

        result.IsValid.Should().BeFalse();
        result.ErrorMessage.Should().ContainEquivalentOf("numeric");
    }

    [Fact]
    public void Validate_InvalidLevel_IsInvalid()
    {
        var result = _engine.Validate("level:SuperCritical");

        result.IsValid.Should().BeFalse();
        result.ErrorMessage.Should().Contain("SuperCritical");
    }

    [Fact]
    public void Validate_InvalidRegexPattern_IsInvalid()
    {
        var result = _engine.Validate("/[unclosed/");

        result.IsValid.Should().BeFalse();
        result.ErrorMessage.Should().NotBeNullOrEmpty();
    }

    [Fact]
    public void Validate_DanglingAnd_IsInvalid()
    {
        var result = _engine.Validate("level:Error AND");

        result.IsValid.Should().BeFalse();
        result.ErrorMessage.Should().NotBeNullOrEmpty();
    }

    [Fact]
    public void Validate_DanglingNot_IsInvalid()
    {
        var result = _engine.Validate("NOT");

        result.IsValid.Should().BeFalse();
        result.ErrorMessage.Should().NotBeNullOrEmpty();
    }

    [Fact]
    public void Validate_EmptyFieldValue_IsInvalid()
    {
        var result = _engine.Validate("level:");

        result.IsValid.Should().BeFalse();
        result.ErrorMessage.Should().NotBeNullOrEmpty();
    }

    [Fact]
    public void Validate_ValidSimpleQuery_IsValid()
    {
        var result = _engine.Validate("svchost");

        result.IsValid.Should().BeTrue();
        result.ParsedTokens.Should().NotBeEmpty();
        result.ErrorMessage.Should().BeNull();
    }

    [Fact]
    public void Validate_ValidComplexQuery_IsValid()
    {
        var result = _engine.Validate(
            "(level:Error OR level:Critical) AND NOT source:TestSource AND /failed.*logon/");

        result.IsValid.Should().BeTrue();
        result.ParsedTokens.Should().NotBeEmpty();
    }

    [Fact]
    public void Validate_ValidQuery_ParsedTokens_ContainExpectedTokens()
    {
        var result = _engine.Validate("level:Error AND source:Security");

        result.IsValid.Should().BeTrue();
        result.ParsedTokens.Should().Contain(t => t.Contains("FieldTerm") && t.Contains("level:Error"));
        result.ParsedTokens.Should().Contain(t => t.Contains("And"));
        result.ParsedTokens.Should().Contain(t => t.Contains("FieldTerm") && t.Contains("source:Security"));
    }

    // ── Edge cases ────────────────────────────────────────────────────────────

    [Fact]
    public void Search_EmptyQuery_ReturnsAllEntries()
    {
        var entries = new List<EventEntry> { E(), E(), E() };

        _engine.Search(entries, "").Should().HaveCount(3);
    }

    [Fact]
    public void Search_WhitespaceQuery_ReturnsAllEntries()
    {
        var entries = new List<EventEntry> { E(), E() };

        _engine.Search(entries, "   ").Should().HaveCount(2);
    }

    [Fact]
    public void Search_MalformedQuery_ReturnsEmpty_DoesNotThrow()
    {
        var entries = new List<EventEntry> { E() };

        var act = () => _engine.Search(entries, "((( invalid AND AND");
        act.Should().NotThrow();
        act().Should().BeEmpty();
    }

    [Fact]
    public void Search_DoubleNot_CancelsOut()
    {
        var entries = new List<EventEntry>
        {
            E(level: EventLevel.Error),
            E(level: EventLevel.Information),
        };

        // NOT NOT level:Error  →  level:Error
        var results = _engine.Search(entries, "NOT NOT level:Error");

        results.Should().HaveCount(1);
        results[0].Level.Should().Be(EventLevel.Error);
    }
}
