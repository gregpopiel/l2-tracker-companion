using L2TrackerCompanion.Parsing;
using Xunit;

namespace L2TrackerCompanion.Parsing.Tests;

public class MonotonicityResetTests
{
    [Fact]
    public void PlayTimeBackToZeroWithNothingGrowingIsAReset()
    {
        var before = TestReports.Open(xp: 5_000_000, adena: 900_000, minutes: 134);
        var after = TestReports.Open(xp: 0, adena: 0, minutes: 0);

        var decision = Monotonicity.Evaluate(before, after);

        Assert.Equal(MonotonicityOutcome.Reset, decision.Outcome);
        Assert.True(decision.IsReset);
        Assert.True(decision.Accepted);
    }

    [Fact]
    public void ResetIsStillAResetOnTheFirstMinute()
    {
        var before = TestReports.Open(xp: 5_000_000, adena: 900_000, minutes: 134);
        var after = TestReports.Open(xp: 12_000, adena: 400, minutes: 1);

        Assert.Equal(MonotonicityOutcome.Reset, Monotonicity.Evaluate(before, after).Outcome);
    }

    [Fact]
    public void ResetSpottedLateIsStillAReset()
    {
        // Nothing landed for a quarter of an hour (panel closed, client
        // relogged, tracking paused). Whether we caught the first minute or the
        // fifteenth says nothing about whether a reset happened.
        var before = TestReports.Open(xp: 5_010_000, adena: 900_000, minutes: 134);
        var after = TestReports.Open(xp: 320_000, adena: 41_000, minutes: 15);

        Assert.Equal(MonotonicityOutcome.Reset, Monotonicity.Evaluate(before, after).Outcome);
    }

    [Fact]
    public void ShorterDurationWithUnchangedXpIsAMisreadOfTheDurationLine()
    {
        // A restarted panel starts near zero; identical XP means the duration
        // line was misread, not that a new session began.
        var before = TestReports.Open(xp: 5_000_000, adena: 900_000, minutes: 134);
        var after = TestReports.Open(xp: 5_000_000, adena: 900_000, minutes: 34);

        Assert.Equal(MonotonicityOutcome.Misread, Monotonicity.Evaluate(before, after).Outcome);
    }

    [Fact]
    public void PartialDropIsAMisreadNotAReset()
    {
        var before = TestReports.Open(xp: 5_000_000, adena: 900_000, minutes: 134);
        var onlyXpDropped = TestReports.Open(xp: 500_000, adena: 900_000, minutes: 134);

        var decision = Monotonicity.Evaluate(before, onlyXpDropped);

        Assert.Equal(MonotonicityOutcome.Misread, decision.Outcome);
        Assert.False(decision.Accepted);
    }

    [Fact]
    public void XpGrowingWhilePlayTimeZeroesIsAMisread()
    {
        var before = TestReports.Open(xp: 5_000_000, adena: 900_000, minutes: 134);
        var after = TestReports.Open(xp: 9_000_000, adena: 900_000, minutes: 0);

        Assert.Equal(MonotonicityOutcome.Misread, Monotonicity.Evaluate(before, after).Outcome);
    }

    [Fact]
    public void UnreadFieldIsNeverEvidenceOfAReset()
    {
        var before = TestReports.Open(xp: 5_000_000, adena: 900_000, minutes: 134);
        var after = TestReports.Open(xp: null, adena: 0, minutes: 0);

        Assert.Equal(MonotonicityOutcome.Misread, Monotonicity.Evaluate(before, after).Outcome);
    }
}
