using L2TrackerCompanion.Parsing;
using Xunit;

namespace L2TrackerCompanion.Parsing.Tests;

public class SessionSnapshotTests
{
    private static readonly DateTimeOffset At = new(2026, 9, 3, 21, 0, 0, TimeSpan.Zero);

    [Fact]
    public void TotalsComeFromTheFrameAlone()
    {
        var report = TestReports.Open(
            xp: 4_210_400,
            adena: 883_500,
            minutes: 134,
            red: 120_000,
            purple: 40_000);

        var result = SessionSnapshot.TryCreate(report, At);

        Assert.True(result.Ok);
        var totals = result.Totals!;
        Assert.Equal(4_210, totals.XpFarmed);
        Assert.Equal(884, totals.Adena);
        Assert.Equal(120, totals.RedLampXP);
        Assert.Equal(40, totals.PurpleLampXP);
        Assert.Equal(134, totals.Minutes);
    }

    [Fact]
    public void MinutesComeFromThePanelNotTheWallClock()
    {
        var result = SessionSnapshot.TryCreate(TestReports.Open(minutes: 90), At);

        Assert.True(result.Ok);
        Assert.Equal(90, result.Totals!.Minutes);
        Assert.Equal(At.AddMinutes(-90), result.Totals.StartedAt);
        Assert.Equal(At, result.Totals.EndedAt);
    }

    [Fact]
    public void UnreadFarmFieldBlocksTheSave()
    {
        Assert.False(SessionSnapshot.TryCreate(TestReports.Open(xp: null), At).Ok);
        Assert.False(SessionSnapshot.TryCreate(TestReports.Open(adena: null), At).Ok);
        Assert.False(SessionSnapshot.TryCreate(TestReports.Open(minutes: null), At).Ok);
    }

    [Fact]
    public void ZeroPlayTimeBlocksTheSave()
    {
        var result = SessionSnapshot.TryCreate(TestReports.Open(minutes: 0), At);

        Assert.False(result.Ok);
        Assert.Contains("no elapsed time", result.Error, StringComparison.Ordinal);
    }

    [Fact]
    public void ClosedLampPanelBlocksTheSaveRatherThanWritingZeros()
    {
        var result = SessionSnapshot.TryCreate(TestReports.ClosedPanel(), At);

        Assert.False(result.Ok);
        Assert.Contains("Magic Lamp", result.Error, StringComparison.Ordinal);
    }

    [Fact]
    public void UnreadLampColumnBlocksTheSave()
    {
        var result = SessionSnapshot.TryCreate(TestReports.UnreadLamps(), At);

        Assert.False(result.Ok);
        Assert.Contains("no silent zeros", result.Error, StringComparison.Ordinal);
    }

    [Fact]
    public void RoundingHappensOnceSoAFullReportKeepsItsThousands()
    {
        // The old two-snapshot path rounded both ends before subtracting and
        // could land a thousand off; a single conversion cannot.
        var result = SessionSnapshot.TryCreate(TestReports.Open(xp: 1_999_500, adena: 1_500), At);

        Assert.True(result.Ok);
        Assert.Equal(2_000, result.Totals!.XpFarmed);
        Assert.Equal(2, result.Totals.Adena);
    }

    [Fact]
    public void FingerprintChangesWithEveryFigure()
    {
        var baseline = SessionSnapshot.Fingerprint(TestReports.Open(xp: 100, adena: 10, minutes: 5));

        Assert.Equal(baseline, SessionSnapshot.Fingerprint(TestReports.Open(xp: 100, adena: 10, minutes: 5)));
        Assert.NotEqual(baseline, SessionSnapshot.Fingerprint(TestReports.Open(xp: 101, adena: 10, minutes: 5)));
        Assert.NotEqual(baseline, SessionSnapshot.Fingerprint(TestReports.Open(xp: 100, adena: 11, minutes: 5)));
        Assert.NotEqual(baseline, SessionSnapshot.Fingerprint(TestReports.Open(xp: 100, adena: 10, minutes: 6)));
    }
}
