namespace L2TrackerCompanion.Parsing.Tests;

public class LocationHintTests
{
    [Fact]
    public void DialogOnlyWidthReturnsNothingEvenWithAHeaderInTheCorner()
    {
        var words = Header(left: 1708, top: 12);
        Assert.Null(LocationHint.Read(words, imageWidth: 548, imageHeight: 337));
        Assert.Null(LocationHint.Read(words, imageWidth: 899, imageHeight: 996));
    }

    [Fact]
    public void DesktopHudHeaderReadsTheZoneLine()
    {
        var hint = LocationHint.Read(Header(left: 1708, top: 12), imageWidth: 1904, imageHeight: 996);
        Assert.Equal("Dragon Valley (east)", hint);
    }

    [Fact]
    public void ASingleWordInTheCornerIsNothingNotAGuess()
    {
        var words = new[] { Box("Dragon", left: 1719, top: 45, width: 40) };
        Assert.Null(LocationHint.Read(words, imageWidth: 1919, imageHeight: 1079));
    }

    [Fact]
    public void ANameplateBelowTheTopBandIsIgnored()
    {
        var words = new[]
        {
            Box("Dragon", left: 1335, top: 90, width: 41),
            Box("Valley", left: 1380, top: 90, width: 33),
        };
        Assert.Null(LocationHint.Read(words, imageWidth: 1919, imageHeight: 1079));
    }

    [Fact]
    public void ANameplateTooFarFromTheRightEdgeIsIgnored()
    {
        var words = new[]
        {
            Box("Dragon", left: 946, top: 42, width: 41),
            Box("Valley", left: 991, top: 42, width: 33),
            Box("(east)", left: 1028, top: 42, width: 33),
        };
        Assert.Null(LocationHint.Read(words, imageWidth: 1919, imageHeight: 1079));
    }

    [Fact]
    public void WindowTitleAtTheLeftIsNotTheMinimap()
    {
        var words = new[]
        {
            Box("Lineage", left: 38, top: 11, width: 40),
            Box("11", left: 82, top: 11, width: 5),
        };
        Assert.Null(LocationHint.Read(words, imageWidth: 1919, imageHeight: 1079));
    }

    [Fact]
    public void LongerLineWinsWhenTwoQualify()
    {
        var words = new[]
        {
            Box("Some", left: 1600, top: 4, width: 30),
            Box("Place", left: 1635, top: 4, width: 30),
            Box("Dragon", left: 1708, top: 40, width: 40),
            Box("Valley", left: 1753, top: 40, width: 33),
            Box("(east)", left: 1791, top: 40, width: 33),
        };
        Assert.Equal("Dragon Valley (east)", LocationHint.Read(words, 1904, 996));
    }

    [Fact]
    public void DigitOnlyTokensAreNotCandidates()
    {
        var words = new[]
        {
            Box("18609", left: 1700, top: 12, width: 40),
            Box("10571", left: 1745, top: 12, width: 40),
        };
        Assert.Null(LocationHint.Read(words, 1904, 996));
    }

    private static WordBox[] Header(double left, double top) =>
    [
        Box("Dragon", left: left, top: top, width: 40),
        Box("Valley", left: left + 45, top: top, width: 33),
        Box("(east)", left: left + 83, top: top, width: 33),
    ];

    private static WordBox Box(string text, double left, double top, double width = 40, double height = 13)
        => new(text, left, top, width, height);
}
