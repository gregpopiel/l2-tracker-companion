using L2TrackerCompanion.Parsing;
using Xunit;

namespace L2TrackerCompanion.Parsing.Tests;

public class LocationNameTests
{
    [Fact]
    public void AGarbledSpellingOfTheSameZoneIsTheSamePlace()
    {
        Assert.True(LocationName.SamePlace("prägon Villey (east)", "Dragon Valley (east)"));
    }

    [Fact]
    public void TwoGenuinelyDifferentZonesAreDifferentPlaces()
    {
        Assert.False(LocationName.SamePlace("Cruma Tower", "Blazing Swamp"));
    }

    [Fact]
    public void ANameThatDiffersOnlyByItsDirectionWordIsStillADifferentPlace()
    {
        Assert.False(LocationName.SamePlace("Dragon Valley (east)", "Dragon Valley (west)"));
    }

    [Fact]
    public void DiacriticsPunctuationAndCaseDoNotMakeANewPlace()
    {
        Assert.True(LocationName.SamePlace("DRAGON-VALLEY", "Drägön Valley"));
    }

    [Fact]
    public void AZoneNameGainingADirectionSuffixIsADifferentPlace()
    {
        Assert.False(LocationName.SamePlace("Dragon Valley", "Dragon Valley (east)"));
    }

    [Fact]
    public void AWordThatOcrSplitOrMergedIsStillTheSamePlace()
    {
        Assert.True(LocationName.SamePlace("DragonValley (east)", "Dragon Valley (east)"));
    }

    [Fact]
    public void AShortTokenHasToMatchExactly()
    {
        Assert.False(LocationName.SamePlace("Sea of Spores", "Sea at Spores"));
    }

    [Fact]
    public void ABlankNameMatchesNothingIncludingAnotherBlank()
    {
        Assert.False(LocationName.SamePlace(null, "Dragon Valley"));
        Assert.False(LocationName.SamePlace("Dragon Valley", "   "));
        Assert.False(LocationName.SamePlace("", ""));
        Assert.False(LocationName.SamePlace(null, null));
    }
}
