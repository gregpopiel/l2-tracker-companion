namespace L2TrackerCompanion.Parsing.Tests;

public class GameNumberTests
{
    [Fact]
    public void SplitTokensSumMagnitudeGroups()
    {
        Assert.Equal(1_165_047, GameNumber.Parse("1M", "165K", "47"));
    }

    [Fact]
    public void RunTogetherGroupsAreSplitBackApart()
    {
        Assert.Equal(1_165_047, GameNumber.Parse("1M165K", "47."));
        Assert.Equal(22_921_247, GameNumber.Parse("22M921K247"));
    }

    [Fact]
    public void GroupsAreSummedNotConcatenated()
    {
        Assert.Equal(1_000_165, GameNumber.Parse("1M", "165"));
        Assert.NotEqual(1_165, GameNumber.Parse("1M", "165"));
    }

    [Fact]
    public void LeadingBIsEightNotABillionsSuffix()
    {
        Assert.Equal(850_000, GameNumber.Parse("B50K"));
    }

    [Theory]
    [InlineData("1O", 10)]
    [InlineData("l2", 12)]
    [InlineData("5S", 55)]
    [InlineData("G00", 600)]
    [InlineData("Z2K", 22_000)]
    public void LookAlikesFoldInsideAToken(string token, long expected)
    {
        Assert.Equal(expected, GameNumber.Parse(token));
    }

    [Fact]
    public void TrailingPunctuationIsStripped()
    {
        Assert.Equal(1_165_047, GameNumber.Parse("1M", "165K", "47."));
    }

    [Fact]
    public void OrphanMagnitudeLetterRefusesTheWholeFigure()
    {
        Assert.Null(GameNumber.Parse("1M", "K", "47"));
        Assert.Null(GameNumber.Parse("659", "K"));
    }

    [Fact]
    public void RepeatedOrAscendingScaleIsRefused()
    {
        Assert.Null(GameNumber.Parse("1K", "2K"));
        Assert.Null(GameNumber.Parse("165", "1M"));
    }

    [Fact]
    public void NonLeadingGroupOverThreeDigitsIsRefused()
    {
        Assert.Null(GameNumber.Parse("1M", "1650"));
    }

    [Fact]
    public void ArtefactsBetweenGroupsAreSkipped()
    {
        Assert.Equal(1_165_047, GameNumber.Parse("1M", "|", "165K", "47"));
    }

    [Fact]
    public void EmptyInputIsUnread()
    {
        Assert.Null(GameNumber.Parse());
        Assert.Null(GameNumber.Parse(""));
    }

    [Fact]
    public void ParseLineClosesASpaceInsideAGroup()
    {
        Assert.Equal(14_751_635, GameNumber.ParseLine("14M 75 1K 635"));
        Assert.Equal(751_000, GameNumber.ParseLine("75 1K"));
    }

    [Fact]
    public void ParseLineJoinsRunTogetherGroups()
    {
        Assert.Equal(44_250_000, GameNumber.ParseLine("44M250K"));
        Assert.Equal(1_165_047, GameNumber.ParseLine("1M165K47"));
    }

    [Fact]
    public void ParseLineRefusesLeftoverLetters()
    {
        Assert.Null(GameNumber.ParseLine("garbage"));
        Assert.Null(GameNumber.ParseLine("aM"));
        Assert.Null(GameNumber.ParseLine("MB0K"));
    }

    [Fact]
    public void ParseLineDiscardsAStrayPipe()
    {
        Assert.Equal(14_751_635, GameNumber.ParseLine("|14M 751K 635"));
    }

    [Fact]
    public void ParseLineKeepsABillionsSuffix()
    {
        Assert.Equal(1_000_000_000, GameNumber.ParseLine("1B"));
    }

    [Fact]
    public void ParseLineAcceptsABareZero()
    {
        Assert.Equal(0, GameNumber.ParseLine("0"));
    }

    [Fact]
    public void TokenParseDoesNotSilentlyDropASplitGroupTheWayANaiveJoinWould()
    {
        Assert.Null(GameNumber.Parse("14M", "75", "1K", "635"));
    }
}
