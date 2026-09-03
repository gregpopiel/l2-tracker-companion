using L2TrackerCompanion.Parsing;
using L2TrackerCompanion.Session;
using Xunit;

namespace L2TrackerCompanion.Session.Tests;

public class SessionResetTests
{
    [Fact]
    public void InGameResetClearsTheBufferInsteadOfDiscardingTheTick()
    {
        using var store = new SessionStore(":memory:");
        store.TryAccept(Report(5_000_000, 900_000, 134));
        store.TryAccept(Report(5_100_000, 910_000, 136));
        Assert.Equal(2, store.Count);

        var result = store.TryAccept(Report(0, 0, 0));

        Assert.True(result.Appended);
        Assert.True(result.WasReset);
        Assert.Equal(1, store.Count);
        Assert.Equal(0, store.Last()!.Report.Xp);
    }

    [Fact]
    public void MisreadIsStillDiscardedAndLeavesTheBufferAlone()
    {
        using var store = new SessionStore(":memory:");
        store.TryAccept(Report(5_000_000, 900_000, 134));

        var result = store.TryAccept(Report(500_000, 900_000, 134));

        Assert.False(result.Appended);
        Assert.Equal(MonotonicityOutcome.Misread, result.Outcome);
        Assert.Equal(1, store.Count);
    }

    [Fact]
    public void AStaleBaselineIsDroppedAfterRepeatedRejections()
    {
        using var store = new SessionStore(":memory:");
        store.TryAccept(Report(5_000_000, 900_000, 134));

        // Same field dropping every tick, with nothing else moving: a misread
        // by the current rule, but it can never resolve itself.
        SnapshotAcceptResult? last = null;
        for (var i = 0; i < SessionStore.StaleBaselineStrikes; i++)
        {
            last = store.TryAccept(Report(5_000_000, 900_000, 34));
        }

        Assert.True(last!.Appended);
        Assert.True(last.WasReset);
        Assert.Equal(1, store.Count);
        Assert.Equal(34, store.Last()!.Report.Minutes);
    }

    [Fact]
    public void ConfidenceFlagsSurviveARoundTrip()
    {
        using var store = new SessionStore(":memory:");
        var report = PlayReport.From(
            1_000_000,
            250_000,
            60,
            Lamps(1_000_000, 250_000),
            null,
            new ReadConfidence(
                XpDisagreed: true,
                XpSpliced: true,
                XpMagnitudeMismatch: false,
                AdenaDisagreed: true,
                PlayTimeDisagreed: false));

        store.Append(report);
        var loaded = store.List().Single().Report;

        Assert.True(loaded.Confidence.XpDisagreed);
        Assert.True(loaded.Confidence.XpSpliced);
        Assert.False(loaded.Confidence.XpMagnitudeMismatch);
        Assert.True(loaded.Confidence.AdenaDisagreed);
        Assert.False(loaded.Confidence.PlayTimeDisagreed);
    }

    private static PlayReport Report(long xp, long adena, int minutes)
        => PlayReport.From(xp, adena, minutes, Lamps(xp, adena), null);

    private static LampXpDecision Lamps(long dialogXp, long dialogAdena)
        => LampXp.Decide(
            new Dictionary<string, long?>
            {
                ["red"] = 0,
                ["purple"] = 0,
                ["blue"] = 0,
                ["green"] = 0,
            },
            new Dictionary<string, WordBox>
            {
                ["red"] = new WordBox("Red", 0, 0, 10, 10),
                ["purple"] = new WordBox("Purple", 0, 20, 10, 10),
                ["blue"] = new WordBox("Blue", 0, 40, 10, 10),
                ["green"] = new WordBox("Green", 0, 60, 10, 10),
            },
            dialogXp,
            dialogAdena);
}
