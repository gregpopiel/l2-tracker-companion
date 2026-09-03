using L2TrackerCompanion.Api;
using Xunit;

namespace L2TrackerCompanion.Api.Tests;

public class SpotBenchmarkTests
{
    // Stored averages are in thousands; the live figures below are raw, as the
    // Play Report prints them. 12,000 stored == 12,000,000 read.
    private static readonly SpotInfo[] ThreeSpots =
    [
        Spot(1, "Cruma Tower", areaId: 1, xp: 12_000, adena: 3_000),
        Spot(2, "Blazing Swamp", areaId: 1, xp: 15_800, adena: 1_200),
        Spot(3, "Antharas Nest", areaId: 2, xp: 9_400, adena: 5_600),
    ];

    [Fact]
    public void RanksTheReadingAmongTheStoredAverages()
    {
        var snapshot = SpotBenchmark.Evaluate(13_000_000, 2_000_000, ThreeSpots);

        Assert.Equal(3, snapshot.RankedSpots);
        Assert.Equal(2, snapshot.XpRank);
        Assert.Equal(3, snapshot.AdenaRank);
        Assert.Equal("XP/h: #2 of 3 spots\nAdena/h: #3 of 3 spots", Text(snapshot));
    }

    [Fact]
    public void BeatingEveryAverageIsFirstAndLosingToAllIsLast()
    {
        Assert.Equal(1, SpotBenchmark.Evaluate(99_000_000, null, ThreeSpots).XpRank);
        Assert.Equal(4, SpotBenchmark.Evaluate(1_000, null, ThreeSpots).XpRank);
    }

    [Fact]
    public void MatchingAnAverageExactlyRanksAboveIt()
    {
        // > not >=: a tie is not a spot that beat the reading.
        Assert.Equal(2, SpotBenchmark.Evaluate(12_000_000, null, ThreeSpots).XpRank);
    }

    /// <summary>
    /// The regression this whole feature turns on: API amounts are thousands
    /// (see <see cref="LegacyThousands"/>), live readings are raw. Drop the
    /// conversion and every reading outranks everything.
    /// </summary>
    [Fact]
    public void StoredThousandsAreScaledToRawGameUnits()
    {
        // 12,000,000 XP/h read live is the same pace as the 12,000 stored for
        // Cruma Tower — mid-table, not a landslide.
        var snapshot = SpotBenchmark.Evaluate(12_000_000, null, ThreeSpots);
        Assert.Equal(2, snapshot.XpRank);

        // Without the ×1000 the reading would sit above every average at once.
        Assert.NotEqual(1, snapshot.XpRank);
    }

    [Fact]
    public void AreaFilterNarrowsBothTheRankAndTheDenominator()
    {
        var snapshot = SpotBenchmark.Evaluate(13_000_000, null, ThreeSpots, areaId: 1);

        Assert.Equal(2, snapshot.RankedSpots);
        Assert.Equal(2, snapshot.XpRank);
        Assert.Equal("XP/h: #2 of 2 spots", Text(snapshot));
    }

    [Fact]
    public void AreaFilterUsesTheIdSoASpotWithNoAreaObjectStillCounts()
    {
        // PostSpotAsync returns exactly this shape: a real AreaId, no Area.
        var justCreated = new SpotInfo(4, "New Spot", AreaId: 1, Area: null, AverageXpHourly: 20_000);
        var snapshot = SpotBenchmark.Evaluate(13_000_000, null, [.. ThreeSpots, justCreated], areaId: 1);

        Assert.Equal(3, snapshot.RankedSpots);
        Assert.Equal(3, snapshot.XpRank);
    }

    [Fact]
    public void SpotsWithoutHistoryAreNeitherRankedNorCounted()
    {
        var neverFarmed = new SpotInfo(5, "Untouched", AreaId: 1, Area: null);
        var snapshot = SpotBenchmark.Evaluate(13_000_000, null, [.. ThreeSpots, neverFarmed]);

        Assert.Equal(3, snapshot.RankedSpots);
        Assert.Equal(2, snapshot.XpRank);
    }

    [Fact]
    public void LogCountNeverInfluencesTheRank()
    {
        // It counts every character on the account, so it says nothing about
        // the per-character averages ranked here.
        var busyElsewhere = ThreeSpots.Select(spot => spot with { LogCount = 999 }).ToList();
        Assert.Equal(
            SpotBenchmark.Evaluate(13_000_000, null, ThreeSpots).XpRank,
            SpotBenchmark.Evaluate(13_000_000, null, busyElsewhere).XpRank);
    }

    [Fact]
    public void ASingleSpotStillRanks()
    {
        var snapshot = SpotBenchmark.Evaluate(13_000_000, null, [ThreeSpots[0]]);
        Assert.Equal("XP/h: #1 of 1 spot", Text(snapshot));
    }

    [Fact]
    public void AnUnreadFieldDropsItsOwnLineOnly()
    {
        var snapshot = SpotBenchmark.Evaluate(null, 2_000_000, ThreeSpots);

        Assert.Null(snapshot.XpRank);
        Assert.Equal(3, snapshot.AdenaRank);
        Assert.Equal("Adena/h: #3 of 3 spots", Text(snapshot));
    }

    [Fact]
    public void NothingReadShowsNothingAtAll()
    {
        var snapshot = SpotBenchmark.Evaluate(null, null, ThreeSpots);

        Assert.False(snapshot.HasLiveRate);
        Assert.Equal(string.Empty, Text(snapshot));
    }

    [Fact]
    public void AnEmptyPoolSaysSoInsteadOfRankingAgainstNothing()
    {
        Assert.Equal(
            "No saved sessions yet — nothing to compare against.",
            Text(SpotBenchmark.Evaluate(13_000_000, null, [])));

        Assert.Equal(
            "No sessions in this area yet.",
            Text(SpotBenchmark.Evaluate(13_000_000, null, ThreeSpots, areaId: 99)));
    }

    [Fact]
    public void NullSpotListIsTreatedAsNoHistory()
    {
        var snapshot = SpotBenchmark.Evaluate(13_000_000, null, null);

        Assert.Equal(0, snapshot.RankedSpots);
        Assert.Null(snapshot.XpRank);
    }

    [Fact]
    public void MinuteUnitOnlyChangesTheLabel()
    {
        var snapshot = SpotBenchmark.Evaluate(13_000_000, 2_000_000, ThreeSpots);
        Assert.Equal(
            "XP/min: #2 of 3 spots\nAdena/min: #3 of 3 spots",
            SpotBenchmark.Format(snapshot, perHour: false).ReplaceLineEndings("\n"));
    }

    [Fact]
    public void AreaChoicesLeadWithAllAreas()
    {
        var choices = AreaChoice.Build([new AreaInfo(1, "World"), new AreaInfo(2, " Instance ")]);

        Assert.Equal(3, choices.Count);
        Assert.Null(choices[0].AreaId);
        Assert.Equal(AreaChoice.AllLabel, choices[0].Label);
        Assert.Equal("World", choices[1].Label);
        Assert.Equal("Instance", choices[2].Label);
    }

    [Fact]
    public void AreaChoicesSurviveAFailedAreasFetch()
    {
        var choices = AreaChoice.Build(null);

        Assert.Single(choices);
        Assert.Equal(AreaChoice.All, choices[0]);
    }

    private static string Text(SpotBenchmarkSnapshot snapshot)
        => SpotBenchmark.Format(snapshot, perHour: true).ReplaceLineEndings("\n");

    private static SpotInfo Spot(int id, string name, int areaId, long xp, long adena)
        => new(
            id,
            name,
            areaId,
            new SpotAreaInfo(areaId, $"Area {areaId}"),
            FarmXpHourly: xp,
            AdenaHourly: adena,
            AverageXpHourly: xp,
            LogCount: 1);
}
