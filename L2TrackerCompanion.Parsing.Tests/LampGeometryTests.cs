namespace L2TrackerCompanion.Parsing.Tests;

public class LampGeometryTests
{
    [Fact]
    public void FindRowsKeepsTheTallestBoxPerColour()
    {
        var words = new[]
        {
            Box("Red", left: 10, top: 80, height: 9),
            Box("red", left: 10, top: 80, height: 14),
            Box("Purple", left: 10, top: 118, height: 14),
            Box("Blue", left: 10, top: 156, height: 14),
            Box("Green", left: 10, top: 194, height: 14),
        };

        var rows = LampGeometry.FindRows(words);
        Assert.Equal(4, rows.Count);
        Assert.Equal(14, rows["red"].Height);
        Assert.Equal(118, rows["purple"].Top);
    }

    [Fact]
    public void RowPitchIsTheMedianGapInColourOrder()
    {
        var rows = LampGeometry.FindRows(
        [
            Box("red", left: 10, top: 80, height: 14),
            Box("purple", left: 10, top: 118, height: 14),
            Box("blue", left: 10, top: 156, height: 14),
            Box("green", left: 10, top: 194, height: 14),
        ]);

        Assert.Equal(38, LampGeometry.RowPitch(rows));
    }

    [Fact]
    public void RowPitchDividesASkippedColourByIndexDistance()
    {
        var rows = LampGeometry.FindRows(
        [
            Box("red", left: 10, top: 80, height: 14),
            Box("purple", left: 10, top: 118, height: 14),
            Box("green", left: 10, top: 194, height: 14),
        ]);

        Assert.Equal(38, LampGeometry.RowPitch(rows));
    }

    [Fact]
    public void RowPitchNeedsAtLeastTwoRows()
    {
        var rows = LampGeometry.FindRows([Box("red", left: 10, top: 80)]);
        Assert.Null(LampGeometry.RowPitch(rows));
    }

    [Fact]
    public void NegativePitchFromAFalseColourHitIsMissing()
    {
        var rows = LampGeometry.FindRows(
        [
            Box("red", left: 139, top: 178, height: 10),
            Box("Red", left: 285, top: 66, height: 9),
            Box("purple", left: 286, top: 103, height: 12),
        ]);

        Assert.Null(LampGeometry.RowPitch(rows));
    }

    private static WordBox Box(
        string text,
        double left,
        double top,
        double width = 40,
        double height = 14)
        => new(text, left, top, width, height);
}
