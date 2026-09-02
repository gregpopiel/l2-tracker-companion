namespace L2TrackerCompanion.Parsing.Tests;

public class SessionDeltaTests
{
    [Fact]
    public void ConvertsToThousandsBeforeSubtracting()
    {
        var first = Open(xp: 1_165_047, adena: 500_000, red: 260_000);
        var last = Open(xp: 14_751_635, adena: 1_500_000, red: 1_260_000);
        var firstAt = DateTimeOffset.Parse("2026-09-02T10:00:00Z");
        var lastAt = DateTimeOffset.Parse("2026-09-02T10:12:20Z");

        var result = SessionDelta.TryCreate(first, last, firstAt, lastAt);

        Assert.True(result.Ok);
        Assert.Equal(Amounts.ToThousands(14_751_635) - Amounts.ToThousands(1_165_047), result.Totals!.XpFarmed);
        Assert.Equal(13587, result.Totals.XpFarmed);
        Assert.Equal(Amounts.ToThousands(1_500_000) - Amounts.ToThousands(500_000), result.Totals.Adena);
        Assert.Equal(1000, result.Totals.Adena);
        Assert.Equal(1_000, result.Totals.RedLampXP);
        Assert.Equal(0, result.Totals.PurpleLampXP);
        Assert.Equal(12, result.Totals.Minutes);
    }

    [Fact]
    public void WallClockRoundsHalfAwayFromZeroAndIsAtLeastOne()
    {
        var first = Open(1000, 1000, 0);
        var last = Open(2000, 2000, 0);
        var start = DateTimeOffset.Parse("2026-09-02T10:00:00Z");

        var halfMinute = SessionDelta.TryCreate(first, last, start, start.AddSeconds(30));
        Assert.Equal(1, halfMinute.Totals!.Minutes);

        var ninety = SessionDelta.TryCreate(first, last, start, start.AddSeconds(90));
        Assert.Equal(2, ninety.Totals!.Minutes);
    }

    [Fact]
    public void BlocksWhenLampsWereNeverRead()
    {
        var first = Closed(1000, 1000);
        var last = Closed(2000, 2000);
        var result = SessionDelta.TryCreate(
            first,
            last,
            DateTimeOffset.Parse("2026-09-02T10:00:00Z"),
            DateTimeOffset.Parse("2026-09-02T10:10:00Z"));

        Assert.False(result.Ok);
        Assert.Contains("silent zeros", result.Error, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void BlocksWhenOnlyOneEndReadLamps()
    {
        var first = Closed(1000, 1000);
        var last = Open(2000, 2000, 0);
        var result = SessionDelta.TryCreate(
            first,
            last,
            DateTimeOffset.Parse("2026-09-02T10:00:00Z"),
            DateTimeOffset.Parse("2026-09-02T10:10:00Z"));

        Assert.False(result.Ok);
        Assert.Contains("both ends", result.Error, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void NeedsTwoSnapshots()
    {
        var one = new PlayReportSnapshot(
            Open(1000, 1000, 0),
            DateTimeOffset.Parse("2026-09-02T10:00:00Z"));
        var result = SessionDelta.TryCreate([one]);
        Assert.False(result.Ok);
        Assert.Contains("two accepted snapshots", result.Error, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void UsesFirstAndLastOfAList()
    {
        var t0 = DateTimeOffset.Parse("2026-09-02T10:00:00Z");
        var snapshots = new[]
        {
            new PlayReportSnapshot(Open(1_000_000, 1_000, 0), t0),
            new PlayReportSnapshot(Open(1_500_000, 2_000, 0), t0.AddMinutes(5)),
            new PlayReportSnapshot(Open(2_000_000, 3_000, 0), t0.AddMinutes(10)),
        };
        var result = SessionDelta.TryCreate(snapshots);
        Assert.True(result.Ok);
        Assert.Equal(1000, result.Totals!.XpFarmed);
        Assert.Equal(2, result.Totals.Adena);
        Assert.Equal(10, result.Totals.Minutes);
    }

    private static PlayReport Open(long xp, long adena, long red)
        => PlayReport.From(
            xp,
            adena,
            minutes: 1,
            LampXp.Decide(
                new Dictionary<string, long?>
                {
                    ["red"] = red,
                    ["purple"] = 0,
                    ["blue"] = 0,
                    ["green"] = 0,
                },
                new Dictionary<string, WordBox>
                {
                    ["red"] = new("Red", 10, 80, 40, 14),
                    ["purple"] = new("Purple", 10, 118, 40, 14),
                    ["blue"] = new("Blue", 10, 156, 40, 14),
                    ["green"] = new("Green", 10, 194, 40, 14),
                },
                xp,
                adena),
            locationHint: null);

    private static PlayReport Closed(long xp, long adena)
        => PlayReport.From(
            xp,
            adena,
            minutes: 1,
            LampXp.Decide(
                new Dictionary<string, long?>
                {
                    ["red"] = null,
                    ["purple"] = null,
                    ["blue"] = null,
                    ["green"] = null,
                },
                new Dictionary<string, WordBox>(),
                xp,
                adena),
            locationHint: null);
}
