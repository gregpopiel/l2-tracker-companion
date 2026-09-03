using L2TrackerCompanion.Parsing;
using Xunit;

namespace L2TrackerCompanion.Parsing.Tests;

public class SaveLockTests
{
    [Fact]
    public void AFrameThatGrewOnFromTheSavedOneIsStillCovered()
    {
        Assert.True(SaveLock.Covers(134, 5_000_000, TestReports.Open(xp: 5_100_000, minutes: 139)));
    }

    [Fact]
    public void XpBelowTheSavedFigureReleasesTheLockAtAnyDuration()
    {
        // XP cannot fall within one run, so this is a different run whether the
        // new panel reads 3 minutes or 300.
        Assert.False(SaveLock.Covers(134, 5_000_000, TestReports.Open(xp: 300_000, minutes: 3)));
        Assert.False(SaveLock.Covers(134, 5_000_000, TestReports.Open(xp: 300_000, minutes: 300)));
    }

    [Fact]
    public void AShorterDurationAloneDoesNotRelease()
    {
        // A misread of the duration line must not unlock a live session.
        Assert.True(SaveLock.Covers(134, 5_000_000, TestReports.Open(xp: 5_100_000, minutes: 34)));
    }

    [Fact]
    public void PlayTimeIsTheFallbackWhenNothingWasEarned()
    {
        Assert.True(SaveLock.Covers(134, 0, TestReports.Open(xp: 0, minutes: 140)));
        Assert.False(SaveLock.Covers(134, 0, TestReports.Open(xp: 0, minutes: 3)));
        Assert.True(SaveLock.Covers(134, null, TestReports.Open(xp: 10, minutes: 140)));
        Assert.False(SaveLock.Covers(134, null, TestReports.Open(xp: 10, minutes: 3)));
    }

    [Fact]
    public void AnUnreadableFrameStaysCovered()
    {
        Assert.True(SaveLock.Covers(134, 5_000_000, TestReports.Open(xp: null, minutes: 3)));
        Assert.True(SaveLock.Covers(134, 5_000_000, TestReports.Open(xp: 300_000, minutes: null)));
    }
}
