using L2TrackerCompanion.Api;
using Xunit;

namespace L2TrackerCompanion.Api.Tests;

public class SpotLocationWarningTests
{
    private static readonly SpotInfo DragonValley = new(
        10, "Dragon Valley", 1, new SpotAreaInfo(1, "World"));

    [Fact]
    public void NoWarningWhenNoSpotIsSelected()
    {
        // Auto-resolve handles this case (SpotResolve); nothing to compare
        // the location against yet.
        Assert.Null(SpotLocationWarning.Evaluate(null, "Training Zone"));
    }

    [Fact]
    public void NoWarningWhileLocationIsUnsettled()
    {
        // LocationStability passes null/blank while its window disagrees —
        // that is a gap, not evidence of a move (mirrors LocationChangeWatch).
        Assert.Null(SpotLocationWarning.Evaluate(DragonValley, null));
        Assert.Null(SpotLocationWarning.Evaluate(DragonValley, string.Empty));
        Assert.Null(SpotLocationWarning.Evaluate(DragonValley, "   "));
    }

    [Fact]
    public void NoWarningWhileStandingAtTheSelectedSpot()
    {
        Assert.Null(SpotLocationWarning.Evaluate(DragonValley, "Dragon Valley"));
    }

    [Fact]
    public void MatchingIgnoresCaseAndSurroundingWhitespace()
    {
        Assert.Null(SpotLocationWarning.Evaluate(DragonValley, "  DRAGON VALLEY  "));
    }

    [Fact]
    public void WarnsWhenTheSettledLocationDiffersFromTheSelectedSpot()
    {
        // This is the reported bug: tracking started at Dragon Valley (spot
        // already selected/stabilized), the player walked to Training Zone
        // and the minimap settled there — Save must not stay silent about it.
        var warning = SpotLocationWarning.Evaluate(DragonValley, "Training Zone");

        Assert.NotNull(warning);
        Assert.Contains("Training Zone", warning, StringComparison.Ordinal);
        Assert.Contains("Dragon Valley", warning, StringComparison.Ordinal);
    }

    [Fact]
    public void GoingBackToTheOriginalSpotClearsTheWarningAgain()
    {
        // Simulates: stable at Dragon Valley -> settles on Training Zone
        // (warns) -> walks back and settles on Dragon Valley again (quiet).
        Assert.Null(SpotLocationWarning.Evaluate(DragonValley, "Dragon Valley"));
        Assert.NotNull(SpotLocationWarning.Evaluate(DragonValley, "Training Zone"));
        Assert.Null(SpotLocationWarning.Evaluate(DragonValley, "Dragon Valley"));
    }

    [Fact]
    public void NoWarningWhenTheSelectedSpotHasNoName()
    {
        var unnamed = new SpotInfo(0, string.Empty, 1, null);

        Assert.Null(SpotLocationWarning.Evaluate(unnamed, "Training Zone"));
    }
}
