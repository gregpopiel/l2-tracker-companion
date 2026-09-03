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
    public void ASaveLocksEveryFrameThatStillCoversTheSavedMinutes()
    {
        using var store = new SessionStore(":memory:");
        var saved = Report(5_000_000, 900_000, 134);

        Assert.False(store.IsSaveLocked(saved));
        store.MarkSaved(saved);
        Assert.True(store.IsSaveLocked(saved));

        // Farming on produces a different frame that still contains every
        // minute already posted, so it must stay locked.
        Assert.True(store.IsSaveLocked(Report(5_100_000, 910_000, 139)));
    }

    [Fact]
    public void AShortSessionAfterALongOneStillLocks()
    {
        // The lock is current state, not a history: reading an aggregate over
        // every save ever made meant the older, longer session kept looking
        // like something the new frame had already gone back past.
        using var store = new SessionStore(":memory:");
        store.MarkSaved(Report(5_000_000, 900_000, 134));

        // Reset, a short second session, saved.
        store.MarkSaved(Report(300_000, 40_000, 20));

        Assert.True(store.IsSaveLocked(Report(310_000, 41_000, 21)));
    }

    [Fact]
    public void ANewSessionLongerThanTheSavedOneIsNotLocked()
    {
        // Nobody watched the reset and by the time we look the new run has
        // already outgrown the saved one. XP is what settles it: within a run
        // it never falls.
        using var store = new SessionStore(":memory:");
        store.MarkSaved(Report(5_000_000, 900_000, 134));

        Assert.False(store.IsSaveLocked(Report(300_000, 40_000, 150)));
    }

    [Fact]
    public void ADetectedResetReleasesTheLock()
    {
        using var store = new SessionStore(":memory:");
        var saved = Report(5_000_000, 900_000, 134);
        store.TryAccept(saved);
        store.MarkSaved(saved);

        var reset = store.TryAccept(Report(0, 0, 0));

        Assert.True(reset.WasReset);
        Assert.False(store.IsSaveLocked(Report(120_000, 30_000, 3)));
    }

    [Fact]
    public void AMisclassifiedResetDoesNotThrowAwayTheLock()
    {
        // One frame that misreads XP low *and* the duration low satisfies the
        // reset rule. Releasing the lock on that alone would arm Save again as
        // soon as the next clean frame showed the real cumulative panel.
        using var store = new SessionStore(":memory:");
        var saved = Report(5_000_000, 900_000, 134);
        store.TryAccept(saved);
        store.MarkSaved(saved);

        var misread = store.TryAccept(Report(400_000, 90_000, 13));
        Assert.True(misread.WasReset);

        Assert.True(store.IsSaveLocked(Report(5_100_000, 910_000, 139)));
    }

    [Fact]
    public void AStaleBaselineDropDoesNotReleaseTheLock()
    {
        // Dropping an unusable baseline is housekeeping, not proof that the
        // posted session is behind us.
        using var store = new SessionStore(":memory:");
        var saved = Report(5_000_000, 900_000, 134);
        store.TryAccept(saved);
        store.MarkSaved(saved);

        for (var i = 0; i < SessionStore.StaleBaselineStrikes; i++)
        {
            store.TryAccept(Report(5_000_000, 900_000, 34));
        }

        Assert.True(store.IsSaveLocked(Report(5_000_000, 900_000, 34)));
    }

    [Fact]
    public void APanelThatWentBackwardsReleasesTheLock()
    {
        using var store = new SessionStore(":memory:");
        store.MarkSaved(Report(5_000_000, 900_000, 134));

        Assert.False(store.IsSaveLocked(Report(120_000, 30_000, 3)));
    }

    [Fact]
    public void AShorterDurationAloneDoesNotReleaseTheLock()
    {
        // A misread of the duration line must not unlock a live session; the
        // XP has to have gone backwards too.
        using var store = new SessionStore(":memory:");
        store.MarkSaved(Report(5_000_000, 900_000, 134));

        Assert.True(store.IsSaveLocked(Report(5_100_000, 910_000, 34)));
    }

    [Fact]
    public void AnUnreadableFrameKeepsTheLock()
    {
        using var store = new SessionStore(":memory:");
        store.MarkSaved(Report(5_000_000, 900_000, 134));

        var unread = PlayReport.From(null, null, null, Lamps(1, 1), null);
        Assert.True(store.IsSaveLocked(unread));
    }

    [Fact]
    public void ClearingTheBufferDoesNotUnlockTheSave()
    {
        using var store = new SessionStore(":memory:");
        var saved = Report(5_000_000, 900_000, 134);
        store.TryAccept(saved);
        store.MarkSaved(saved);

        store.NewSession();

        Assert.Equal(0, store.Count);
        Assert.True(store.IsSaveLocked(Report(5_100_000, 910_000, 139)));
        Assert.False(store.IsSaveLocked(Report(120_000, 30_000, 3)));
    }

    [Fact]
    public void SignOutClearsTheLockExplicitly()
    {
        using var store = new SessionStore(":memory:");
        var saved = Report(5_000_000, 900_000, 134);
        store.MarkSaved(saved);

        store.ClearSaveLock();

        Assert.False(store.IsSaveLocked(saved));
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
