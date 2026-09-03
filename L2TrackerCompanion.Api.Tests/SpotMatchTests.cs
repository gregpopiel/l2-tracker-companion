using L2TrackerCompanion.Api;
using Xunit;

namespace L2TrackerCompanion.Api.Tests;

public class SpotMatchTests
{
    private static readonly SpotInfo Dragon = new(
        10, "Dragon Valley (east)", 1, new SpotAreaInfo(1, "World"));

    private static readonly SpotInfo Aligator = new(
        184, "Aligator", 2, new SpotAreaInfo(2, "Special Zone"));

    private static readonly IReadOnlyList<SpotInfo> Spots = [Dragon, Aligator];

    [Fact]
    public void HudHintSelectsExactNameIgnoringCase()
    {
        var match = SpotMatch.ExactName("Dragon Valley (east)", Spots);
        Assert.Same(Dragon, match);

        var upper = SpotMatch.ExactName("  DRAGON VALLEY (EAST)  ", Spots);
        Assert.Same(Dragon, upper);
    }

    [Fact]
    public void MatchesSpotNameNotTheAreaLabel()
    {
        Assert.Null(SpotMatch.ExactName("Dragon Valley (east) (World)", Spots));
        Assert.Null(SpotMatch.ExactName("World", Spots));
    }

    [Fact]
    public void MissAndMissingHintLeavePickerUnchanged()
    {
        Assert.Null(SpotMatch.ExactName("Dragon Valley", Spots));
        Assert.Null(SpotMatch.ExactName("east", Spots));
        Assert.Null(SpotMatch.ExactName(null, Spots));
        Assert.Null(SpotMatch.ExactName("", Spots));
        Assert.Null(SpotMatch.ExactName("Dragon Valley (east)", []));
        Assert.Null(SpotMatch.ExactName("Dragon Valley (east)", null));
    }

    [Fact]
    public void TwoExactHitsAreNotAUniqueMatch()
    {
        var spots = new[]
        {
            Dragon,
            new SpotInfo(11, "dragon valley (east)", 1, new SpotAreaInfo(1, "World")),
        };

        Assert.Equal(2, SpotMatch.ExactNames("Dragon Valley (east)", spots).Count);
        Assert.Null(SpotMatch.ExactName("Dragon Valley (east)", spots));
    }

    [Fact]
    public void SameNameIgnoresCaseAndWhitespaceAndRejectsBlanks()
    {
        Assert.True(SpotMatch.SameName("  Dragon Valley (east) ", "DRAGON VALLEY (EAST)"));
        Assert.False(SpotMatch.SameName(null, "Dragon Valley (east)"));
        Assert.False(SpotMatch.SameName("", "  "));
        Assert.False(SpotMatch.SameName("Alpha", "Beta"));
    }

    [Fact]
    public void DialogOnlyHintDoesNotSelect()
    {
        Assert.Null(SpotMatch.ExactName(null, Spots));
    }
}
