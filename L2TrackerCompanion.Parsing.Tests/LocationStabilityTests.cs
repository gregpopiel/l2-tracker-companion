using L2TrackerCompanion.Parsing;
using Xunit;

namespace L2TrackerCompanion.Parsing.Tests;

public class LocationStabilityTests
{
    [Fact]
    public void FourOfTheLastFiveTheSameIsStable()
    {
        var decision = LocationStability.Evaluate(
        [
            "Noise",
            "Dragon Valley (east)",
            "Dragon Valley (east)",
            "Somewhere Else",
            "Dragon Valley (east)",
            "Dragon Valley (east)",
        ]);

        Assert.True(decision.IsStable);
        Assert.Equal("Dragon Valley (east)", decision.CanonicalName);
        Assert.Equal(LocationStability.WindowSize, decision.SampleCount);
        Assert.Equal(LocationStability.MinMajority, decision.MajorityCount);
    }

    [Fact]
    public void ThreeOfFiveIsNotEnough()
    {
        var decision = LocationStability.Evaluate(
        [
            "Alpha",
            "Alpha",
            "Alpha",
            "Beta",
            "Gamma",
        ]);

        Assert.False(decision.IsStable);
        Assert.Null(decision.CanonicalName);
        Assert.Equal(5, decision.SampleCount);
        Assert.Equal(3, decision.MajorityCount);
    }

    [Fact]
    public void FewerThanFiveNonEmptyHintsIsUnstableEvenIfTheyAllAgree()
    {
        var decision = LocationStability.Evaluate(
        [
            "Dragon Valley (east)",
            null,
            "  ",
            "Dragon Valley (east)",
            "Dragon Valley (east)",
            "Dragon Valley (east)",
        ]);

        Assert.False(decision.IsStable);
        Assert.Null(decision.CanonicalName);
        Assert.Equal(4, decision.SampleCount);
        Assert.Equal(0, decision.MajorityCount);
    }

    [Fact]
    public void EmptyAndWhitespaceHintsAreSkipped()
    {
        var decision = LocationStability.Evaluate(
        [
            "Dragon Valley (east)",
            null,
            "",
            "   ",
            "Dragon Valley (east)",
            "Dragon Valley (east)",
            "\t",
            "Dragon Valley (east)",
            "Dragon Valley (east)",
        ]);

        Assert.True(decision.IsStable);
        Assert.Equal("Dragon Valley (east)", decision.CanonicalName);
    }

    [Fact]
    public void OnlyTheLastFiveNonEmptyHintsCount()
    {
        var decision = LocationStability.Evaluate(
        [
            "Old Spot",
            "Old Spot",
            "Old Spot",
            "Old Spot",
            "Old Spot",
            "New Spot",
            "New Spot",
            "Other",
            "New Spot",
            "New Spot",
        ]);

        Assert.True(decision.IsStable);
        Assert.Equal("New Spot", decision.CanonicalName);
        Assert.Equal(4, decision.MajorityCount);
    }

    [Fact]
    public void MatchingIgnoresCaseAndUsesTheMostCommonOriginalSpelling()
    {
        var decision = LocationStability.Evaluate(
        [
            "dragon valley (east)",
            "Dragon Valley (east)",
            "Dragon Valley (east)",
            "DRAGON VALLEY (EAST)",
            "Dragon Valley (east)",
        ]);

        Assert.True(decision.IsStable);
        Assert.Equal("Dragon Valley (east)", decision.CanonicalName);
        Assert.Equal(5, decision.MajorityCount);
    }

    [Fact]
    public void LeadingAndTrailingWhitespaceDoesNotSplitAGroup()
    {
        var decision = LocationStability.Evaluate(
        [
            "  Dragon Valley (east)",
            "Dragon Valley (east)  ",
            "Dragon Valley (east)",
            "Dragon Valley (east)",
            "Dragon Valley (east)",
        ]);

        Assert.True(decision.IsStable);
        Assert.Equal("Dragon Valley (east)", decision.CanonicalName);
    }

    [Fact]
    public void NoHintsYetIsUnstable()
    {
        var decision = LocationStability.Evaluate([]);

        Assert.False(decision.IsStable);
        Assert.Equal(0, decision.SampleCount);
    }
}
