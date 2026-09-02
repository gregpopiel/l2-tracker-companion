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
    public void RowPitchIgnoresANegativeGapFromAMisplacedRow()
    {
        var rows = new Dictionary<string, WordBox>(StringComparer.Ordinal)
        {
            ["red"] = Box("red", left: 139, top: 178, height: 10),
            ["purple"] = Box("purple", left: 286, top: 103, height: 12),
        };

        Assert.Null(LampGeometry.RowPitch(rows));
    }

    [Fact]
    public void FindRowsKeepsTheColourNameColumnOverALoneFalseHit()
    {
        var rows = LampGeometry.FindRows(
        [
            Box("red", left: 139, top: 178, height: 10),
            Box("Red", left: 285, top: 66, height: 9),
            Box("purple", left: 286, top: 103, height: 12),
            Box("ue", left: 297, top: 145, height: 6),
            Box("G-reen", left: 289, top: 181, height: 8),
        ]);

        Assert.Equal(4, rows.Count);
        Assert.Equal(285, rows["red"].Left);
        Assert.Equal(66, rows["red"].Top);
        Assert.Equal(145, rows["blue"].Top);
        Assert.Equal(181, rows["green"].Top);
        Assert.Equal(37, LampGeometry.RowPitch(rows));
    }

    [Fact]
    public void FindRowsMapsNueGueAndUeToBlue()
    {
        var rows = LampGeometry.FindRows(
        [
            Box("Red", left: 10, top: 80),
            Box("Purple", left: 10, top: 118),
            Box("Nue", left: 10, top: 156),
            Box("Green", left: 10, top: 194),
        ]);

        Assert.True(rows.ContainsKey("blue"));
        Assert.Equal(156, rows["blue"].Top);

        var gue = LampGeometry.FindRows([Box("gue", left: 10, top: 156)]);
        Assert.True(gue.ContainsKey("blue"));

        var ue = LampGeometry.FindRows([Box("ue", left: 10, top: 156)]);
        Assert.True(ue.ContainsKey("blue"));
    }

    [Fact]
    public void FindRowsMapsHyphenatedGreen()
    {
        var rows = LampGeometry.FindRows([Box("G-reen", left: 10, top: 194)]);
        Assert.True(rows.ContainsKey("green"));
    }

    [Fact]
    public void TableCropIsMeasuredFromNameAnchorsAndPitch()
    {
        var rows = LampGeometry.FindRows(
        [
            Box("red", left: 100, top: 80, height: 14),
            Box("purple", left: 100, top: 118, height: 14),
            Box("blue", left: 100, top: 156, height: 14),
            Box("green", left: 100, top: 194, height: 14),
        ]);
        var pitch = LampGeometry.RowPitch(rows)!.Value;
        var crop = LampGeometry.TableCrop(rows, pitch, 500, 400);

        Assert.Equal(100 - LampGeometry.TableLeftOfNamePx, crop.Left);
        Assert.Equal(100 + LampGeometry.TableRightOfNamePx, crop.Right);
        Assert.Equal((int)Math.Round(80 - (0.8 * pitch), MidpointRounding.AwayFromZero), crop.Top);
        Assert.Equal((int)Math.Round(194 + (1.1 * pitch), MidpointRounding.AwayFromZero), crop.Bottom);
    }

    [Fact]
    public void TableCropExtendsToMissingLeadingAndTrailingRows()
    {
        var rows = LampGeometry.FindRows(
        [
            Box("purple", left: 100, top: 118, height: 14),
            Box("blue", left: 100, top: 156, height: 14),
            Box("green", left: 100, top: 194, height: 14),
        ]);
        var pitch = LampGeometry.RowPitch(rows)!.Value;
        var crop = LampGeometry.TableCrop(rows, pitch, 500, 400);

        Assert.Equal(
            (int)Math.Round(118 - pitch - (0.8 * pitch), MidpointRounding.AwayFromZero),
            crop.Top);
        Assert.Equal(
            (int)Math.Round(194 + (1.1 * pitch), MidpointRounding.AwayFromZero),
            crop.Bottom);
    }

    [Fact]
    public void TableCropWithoutRowsOrPitchIsEmpty()
    {
        Assert.True(LampGeometry.TableCrop(new Dictionary<string, WordBox>(), 38, 500, 400).IsEmpty);
        var rows = LampGeometry.FindRows([Box("red", left: 10, top: 80)]);
        Assert.True(LampGeometry.TableCrop(rows, 0, 500, 400).IsEmpty);
    }

    [Fact]
    public void RowXpCellCropUsesPitchNotGlyphHeight()
    {
        var anchor = Box("red", left: 10, top: 80, width: 40, height: 9);
        var crop = LampGeometry.RowXpCellCrop(anchor, pitch: 38, pxScale: 1, 500, 400);

        Assert.Equal(10 + LampGeometry.RowXpCropDxStart, crop.Left);
        Assert.Equal((int)Math.Round(80 + (0.18 * 38), MidpointRounding.AwayFromZero), crop.Top);
        Assert.Equal(LampGeometry.RowXpCropDxEnd - LampGeometry.RowXpCropDxStart, crop.Width);
        Assert.Equal((int)Math.Round(0.56 * 38, MidpointRounding.AwayFromZero), crop.Height);
    }

    [Fact]
    public void RowXpTokensStayInsideTheDxWindow()
    {
        var anchor = Box("red", left: 10, top: 80);
        var words = new[]
        {
            anchor,
            Box("3M", left: 10 + 200, top: 90),
            Box("200K", left: 10 + 230, top: 90),
            Box("intruder", left: 10 + 260, top: 90),
            Box("above", left: 10 + 200, top: 80),
        };

        var tokens = LampGeometry.RowXpTokens(words, anchor, pitch: 38);
        Assert.Equal(["3M", "200K"], tokens.Select(w => w.Text).ToArray());
    }

    private static WordBox Box(
        string text,
        double left,
        double top,
        double width = 40,
        double height = 14)
        => new(text, left, top, width, height);
}
