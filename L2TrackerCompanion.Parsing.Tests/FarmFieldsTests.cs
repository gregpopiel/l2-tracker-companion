namespace L2TrackerCompanion.Parsing.Tests;

public class FarmFieldsTests
{
    [Fact]
    public void PickAdenaUnitPrefersTheWordWithDigitsToItsLeft()
    {
        var heading = Box("Adena", left: 400, top: 100);
        var unit = Box("adena", left: 400, top: 150);
        var figure = Box("862K", left: 280, top: 148);
        var picked = FarmFields.PickAdenaUnit([heading, unit, figure]);

        Assert.Same(unit, picked);
    }

    [Fact]
    public void PickAdenaUnitSkipsALowerHeadingThatHasNoDigitsBesideIt()
    {
        // Heading rendered below the unit (confidence used to pick this and
        // shift both bands up a line). Digits-left must still win.
        var unit = Box("adena", left: 400, top: 150);
        var heading = Box("adena", left: 400, top: 220);
        var figure = Box("159", left: 340, top: 150);

        var picked = FarmFields.PickAdenaUnit([heading, unit, figure]);
        Assert.Same(unit, picked);
    }

    [Fact]
    public void PickAdenaUnitFallsBackToTheLowerCandidateWhenNeitherHasDigits()
    {
        var upper = Box("adena", left: 400, top: 100);
        var lower = Box("adena", left: 400, top: 150);

        var picked = FarmFields.PickAdenaUnit([upper, lower]);
        Assert.Same(lower, picked);
    }

    [Fact]
    public void TokenBandDoesNotSwallowALeftDockedLampTable()
    {
        // True XP 22,921,247. A 22× glyph-height window used to reach the
        // Red row's "3M 200K" ~350px left of the unit and sum it in.
        var unit = Box("adena", left: 400, top: 150, width: 50, height: 20);
        var words = new[]
        {
            Box("22M", left: 250, top: 75),
            Box("921K", left: 310, top: 76),
            Box("247", left: 370, top: 75),
            Box("862K", left: 280, top: 148),
            Box("159", left: 340, top: 150),
            unit,
            Box("3M", left: 50, top: 80),
            Box("200K", left: 90, top: 80),
            Box("red", left: 10, top: 80, height: 14),
        };

        var read = FarmFields.ReadTokens(words);
        Assert.Same(unit, read.Unit);
        Assert.Equal(22_921_247, read.XpFromTokens);
        Assert.Equal(862_159, read.AdenaFromTokens);
        Assert.DoesNotContain(read.XpTokens, w => w.Text is "3M" or "200K");
    }

    [Fact]
    public void TokenBandDoesIncludeFiguresAbout120PxLeftOfTheUnit()
    {
        var unit = Box("adena", left: 400, top: 150, width: 50, height: 20);
        var words = new[]
        {
            Box("22M", left: 280, top: 75),
            Box("921K", left: 330, top: 75),
            Box("247", left: 370, top: 75),
            Box("862K", left: 300, top: 150),
            unit,
        };

        var read = FarmFields.ReadTokens(words);
        Assert.Equal(22_921_247, read.XpFromTokens);
        Assert.Equal(862_000, read.AdenaFromTokens);
    }

    [Fact]
    public void XpMicroCropPadsTheTokenUnion()
    {
        var tokens = new[]
        {
            Box("22M", left: 100, top: 40, width: 40, height: 16),
            Box("247", left: 200, top: 42, width: 30, height: 16),
        };

        var crop = FarmFields.XpMicroCrop(tokens, imageWidth: 500, imageHeight: 300);
        Assert.Equal(100 - FarmFields.XpCropPadX, crop.Left);
        Assert.Equal(40 - FarmFields.XpCropPadY, crop.Top);
        Assert.Equal(200 + 30 + FarmFields.XpCropPadX, crop.Right);
        Assert.Equal(42 + 16 + FarmFields.XpCropPadY, crop.Bottom);
    }

    [Fact]
    public void AdenaFallbackCropIsAFixedStripLeftOfTheUnit()
    {
        var unit = Box("adena", left: 400, top: 150, width: 50, height: 20);
        var crop = FarmFields.AdenaFallbackCrop(unit, 500, 300);

        Assert.Equal(400 - FarmFields.AdenaFallbackWidth, crop.Left);
        Assert.Equal(150 - FarmFields.AdenaFallbackAbove, crop.Top);
        Assert.Equal(400, crop.Right);
        Assert.Equal(150 + 20 + FarmFields.AdenaFallbackBelow, crop.Bottom);
    }

    [Fact]
    public void ColourNameColumnPitchDoesNotMoveTheXpBandBelowAdena()
    {
        var unit = Box("adena", left: 126, top: 154, width: 34, height: 10);
        var words = new[]
        {
            Box("857K", left: 96, top: 116, width: 25, height: 9),
            Box("10", left: 124, top: 116, width: 11, height: 8),
            Box("70K", left: 74, top: 156),
            unit,
            Box("red", left: 139, top: 178, height: 10),
            Box("Red", left: 285, top: 66, height: 9),
            Box("purple", left: 286, top: 103, height: 12),
        };

        var read = FarmFields.ReadTokens(words);
        Assert.Equal(37, read.Pitch);
        Assert.Equal(857_010, read.XpFromTokens);
    }

    [Fact]
    public void ReadTokensWithoutAdenaIsEmpty()
    {
        var read = FarmFields.ReadTokens([Box("Characters", left: 10, top: 10)]);
        Assert.Null(read.Unit);
        Assert.Null(read.XpFromTokens);
        Assert.Null(read.AdenaFromTokens);
    }

    private static WordBox Box(
        string text,
        double left,
        double top,
        double width = 40,
        double height = 14)
        => new(text, left, top, width, height);
}
