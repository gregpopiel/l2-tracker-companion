using L2TrackerCompanion.Parsing;
using Xunit;

namespace L2TrackerCompanion.Parsing.Tests;

public class LocationStabilityTests
{
    [Fact]
    public void FourNonEmptyHintsOfTheSamePlaceAreSettled()
    {
        var name = LocationStability.SettledName(
        [
            "Noise",
            "Dragon Valley (east)",
            "Dragon Valley (east)",
            "Dragon Valley (east)",
            "Dragon Valley (east)",
        ]);

        Assert.Equal("Dragon Valley (east)", name);
    }

    [Fact]
    public void OppositeDriftsFromTheFirstHintDoNotSettle()
    {
        // Cxmp and Camx are each one letter from Camp, the limit for a
        // four-letter word, but two letters from each other.
        Assert.Null(LocationStability.SettledName(
        [
            "Camp",
            "Cxmp",
            "Camp",
            "Camx",
        ]));
    }

    [Fact]
    public void ADifferentPlaceInTheRunUnsettlesIt()
    {
        Assert.Null(LocationStability.SettledName(
        [
            "Dragon Valley (east)",
            "Dragon Valley (east)",
            "Somewhere Else",
            "Dragon Valley (east)",
        ]));
    }

    [Fact]
    public void ThreeAgreeingHintsAreNotEnough()
    {
        Assert.Null(LocationStability.SettledName(
        [
            "Dragon Valley (east)",
            null,
            "  ",
            "Dragon Valley (east)",
            "Dragon Valley (east)",
        ]));
    }

    [Fact]
    public void EmptyAndWhitespaceHintsAreSkipped()
    {
        var name = LocationStability.SettledName(
        [
            "Dragon Valley (east)",
            null,
            "",
            "   ",
            "Dragon Valley (east)",
            "\t",
            "Dragon Valley (east)",
            "Dragon Valley (east)",
        ]);

        Assert.Equal("Dragon Valley (east)", name);
    }

    [Fact]
    public void OnlyTheLastFourNonEmptyHintsCount()
    {
        var name = LocationStability.SettledName(
        [
            "Old Spot",
            "Old Spot",
            "Old Spot",
            "Old Spot",
            "New Spot",
            "New Spot",
            "New Spot",
            "New Spot",
        ]);

        Assert.Equal("New Spot", name);
    }

    [Fact]
    public void ATwoOrThreeLetterArtifactStaysTheCurrentSpelling()
    {
        var name = LocationStability.SettledName(
        [
            "Dragon Valley",
            "Dxagxn Vallxy",
            "Dragon Valley",
            "Dragan Vallex",
        ]);

        Assert.Equal("Dragon Valley", name);
    }

    [Fact]
    public void TheMostCommonSpellingBeatsALeadingArtifact()
    {
        var name = LocationStability.SettledName(
        [
            "Dxagxn Vallxy",
            "Dragon Valley",
            "Dragon Valley",
            "Dragon Valley",
        ]);

        Assert.Equal("Dragon Valley", name);
    }

    [Fact]
    public void MatchingIgnoresCaseAndKeepsTheMajoritySpelling()
    {
        var name = LocationStability.SettledName(
        [
            "dragon valley (east)",
            "Dragon Valley (east)",
            "DRAGON VALLEY (EAST)",
            "Dragon Valley (east)",
        ]);

        Assert.Equal("Dragon Valley (east)", name);
    }

    [Fact]
    public void LeadingAndTrailingWhitespaceDoesNotSplitTheRun()
    {
        var name = LocationStability.SettledName(
        [
            "  Dragon Valley (east)",
            "Dragon Valley (east)  ",
            "Dragon Valley (east)",
            "Dragon Valley (east)",
        ]);

        Assert.Equal("Dragon Valley (east)", name);
    }

    [Fact]
    public void NoHintsYetIsUnsettled()
    {
        Assert.Null(LocationStability.SettledName([]));
    }
}
