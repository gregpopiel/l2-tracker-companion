using L2TrackerCompanion.Parsing;
using Xunit;

namespace L2TrackerCompanion.Parsing.Tests;

public class LampContinuityTests
{
    [Fact]
    public void AZeroLampCellAfterARealFigureIsTreatedAsAnUnreadColumn()
    {
        var read = TestReports.Open(minutes: 30, green: 256_000);
        var zero = TestReports.Open(xp: 1_200_000, minutes: 31, green: 0);

        var result = LampContinuity.Withdraw(read, read, zero);

        Assert.False(result.LampXpRead);
        Assert.Null(result.RedLampXp);
        Assert.Null(result.PurpleLampXp);
        Assert.Null(result.BlueLampXp);
        Assert.Null(result.GreenLampXp);
        Assert.Equal(0, result.LampXpTotal);
        Assert.Contains(
            "Green lamp XP read 0 after 256,000 — lamp XP only ever grows, so the column was treated as unread",
            result.Warnings);
    }

    [Fact]
    public void WithdrawingTheLampsLeavesTheFarmFieldsOnTheFrame()
    {
        var read = TestReports.Open(xp: 1_000_000, adena: 250_000, minutes: 30, green: 256_000);
        var zero = TestReports.Open(xp: 1_200_000, adena: 300_000, minutes: 31, green: 0);

        var result = LampContinuity.Withdraw(read, read, zero);

        Assert.Equal(1_200_000, result.Xp);
        Assert.Equal(300_000, result.Adena);
        Assert.Equal(31, result.Minutes);
    }

    [Fact]
    public void TheFirstLampReadingOfASessionIsKeptBecauseNothingContradictsIt()
    {
        var first = TestReports.Open(green: 0);

        Assert.Same(first, LampContinuity.Withdraw(null, null, first));
        Assert.True(first.LampXpRead);
        Assert.Equal(0, first.GreenLampXp);
    }

    [Fact]
    public void LampsAreComparedAgainstTheLastFrameThatReadThemNotMerelyTheLastFrame()
    {
        var read = TestReports.Open(minutes: 30, green: 256_000);
        var withdrawn = LampContinuity.Withdraw(
            read,
            read,
            TestReports.Open(xp: 1_100_000, minutes: 31, green: 0));
        Assert.False(withdrawn.LampXpRead);

        // Last() is now the withdrawn frame. Comparing against that would
        // accept the next zero, because an unread column contradicts nothing.
        var again = TestReports.Open(xp: 1_200_000, minutes: 32, green: 0);
        var result = LampContinuity.Withdraw(withdrawn, read, again);

        Assert.False(result.LampXpRead);
        Assert.Null(result.GreenLampXp);
    }

    [Fact]
    public void ARestartedPlayReportIsAllowedToZeroEveryLamp()
    {
        var previous = TestReports.Open(xp: 5_000_000, adena: 1_000_000, minutes: 60, green: 256_000);
        var restarted = TestReports.Open(xp: 1_000, adena: 0, minutes: 1, green: 0);

        var result = LampContinuity.Withdraw(previous, previous, restarted);

        Assert.Same(restarted, result);
        Assert.True(result.LampXpRead);
        Assert.Equal(0, result.GreenLampXp);
    }

    [Fact]
    public void AClosedLampPanelIsNotAWithdrawal()
    {
        var read = TestReports.Open(green: 256_000);
        var closed = TestReports.ClosedPanel();

        Assert.Same(closed, LampContinuity.Withdraw(read, read, closed));
    }

    [Fact]
    public void AGrowingLampFigureIsLeftAlone()
    {
        var previous = TestReports.Open(green: 256_000);
        var grown = TestReports.Open(xp: 2_000_000, minutes: 70, green: 300_000);

        Assert.Same(grown, LampContinuity.Withdraw(previous, previous, grown));
    }

    [Fact]
    public void AnAlreadyWithdrawnFrameSurvivesASecondPass()
    {
        var previous = TestReports.Open(green: 256_000);
        var zero = TestReports.Open(xp: 1_100_000, minutes: 61, green: 0);
        var once = LampContinuity.Withdraw(previous, previous, zero);

        var twice = LampContinuity.Withdraw(once, previous, once);

        Assert.Same(once, twice);
        Assert.False(twice.LampXpRead);
    }
}
