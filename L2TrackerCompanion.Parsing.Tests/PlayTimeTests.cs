namespace L2TrackerCompanion.Parsing.Tests;

public class PlayTimeTests
{
    [Fact]
    public void CleanLineParsesToMinutes()
    {
        Assert.Equal(672, PlayTime.ParseMinutes("0 d. 11 h. 12 min."));
    }

    [Fact]
    public void ZeroDayMisreadAs04dIsZeroDaysNotFour()
    {
        Assert.Equal(672, PlayTime.ParseMinutes("04d. 11h. 12 min."));
        Assert.Equal(672, PlayTime.ParseMinutes("04. 11 h. 12 min."));
    }

    [Fact]
    public void GenuineFourDaySessionStillParsesAsFourDays()
    {
        Assert.Equal(6432, PlayTime.ParseMinutes("4 d. 11 h. 12 min."));
    }

    [Fact]
    public void HoursAbove23AreRefused()
    {
        Assert.Null(PlayTime.ParseMinutes("0 d. 24 h. 00 min."));
    }

    [Fact]
    public void MinutesAbove59AreRefused()
    {
        Assert.Null(PlayTime.ParseMinutes("0 d. 11 h. 60 min."));
    }

    [Fact]
    public void SmearedMicroCropTextIsUnread()
    {
        Assert.Null(PlayTime.ParseMinutes("0a 10mm."));
        Assert.Null(PlayTime.ParseMinutes("31. 19 mn."));
    }

    [Fact]
    public void WinOcrOhForZeroHoursFoldsToZeroNotUnread()
    {
        Assert.Equal(12, PlayTime.ParseMinutes("0 d. O h. 12 min."));
    }

    [Theory]
    [InlineData("0")]
    [InlineData("04d.")]
    [InlineData("11h.")]
    [InlineData("d.")]
    [InlineData("h")]
    [InlineData("min.")]
    [InlineData("min")]
    public void ValueTokensAdmitDigitsAndBareUnitFragments(string text)
    {
        Assert.True(PlayTime.IsValueToken(text));
    }

    [Theory]
    [InlineData("Total")]
    [InlineData("play")]
    [InlineData("time")]
    [InlineData("Reset")]
    public void LabelWordsAreNotValueTokens(string text)
    {
        Assert.False(PlayTime.IsValueToken(text));
    }

    [Fact]
    public void PickTimeAnchorPrefersTheLabelWithValueTokensBelowIt()
    {
        var chat = Box("time", left: 20, top: 10);
        var label = Box("time", left: 400, top: 200);
        var words = new[]
        {
            chat,
            Box("hello", left: 20, top: 30),
            Box("Total", left: 300, top: 202),
            Box("play", left: 350, top: 200),
            label,
            Box("0", left: 300, top: 216),
            Box("d.", left: 320, top: 216),
            Box("11", left: 340, top: 216),
            Box("h.", left: 360, top: 216),
            Box("12", left: 380, top: 216),
            Box("min.", left: 400, top: 216),
        };

        Assert.Same(label, PlayTime.PickTimeAnchor(words));
    }

    [Fact]
    public void PickTimeAnchorFallsBackToTotalWhenTimeIsMissing()
    {
        var total = Box("Total", left: 75, top: 250);
        var words = new[]
        {
            total,
            Box("d.", left: 85, top: 265),
            Box("h.", left: 106, top: 265),
            Box("4", left: 118, top: 265),
            Box("min.", left: 127, top: 265),
        };

        Assert.Same(total, PlayTime.PickTimeAnchor(words));
        var read = PlayTime.ReadTokens(words);
        Assert.Same(total, read.Anchor);
        Assert.NotEmpty(read.ValueTokens);
    }

    [Fact]
    public void LabelStripUnderTotalCoversTheDurationLine()
    {
        var total = Box("Total", left: 75, top: 250, width: 25, height: 10);
        var strip = PlayTime.LabelStrip(total, 540, 393);

        Assert.Equal(75 - PlayTime.StripFromTotalLeftPadPx, strip.Left);
        Assert.Equal(250 + PlayTime.StripBelowLabelPx, strip.Top);
        Assert.Equal(75 + PlayTime.StripFromTotalWidthPx, strip.Right);
        Assert.Equal(250 + PlayTime.StripMaxBelowPx, strip.Bottom);
    }

    [Fact]
    public void CombinedValueCropUnionsTheLabelStripWithTokenBoxes()
    {
        var time = Box("time", left: 140, top: 248, width: 22, height: 8);
        var min = Box("min.", left: 200, top: 263, width: 21, height: 8);
        var read = PlayTime.ReadTokens([time, min]);
        var crop = PlayTime.CombinedValueCrop(read, 500, 400);

        var strip = PlayTime.LabelStrip(time, 500, 400);
        Assert.True(crop.Left <= strip.Left);
        Assert.True(crop.Right >= min.Left + min.Width);
    }

    [Fact]
    public void CombinedValueCropStillRunsWhenThereAreNoValueTokens()
    {
        var total = Box("Total", left: 75, top: 250, width: 25, height: 10);
        var read = PlayTime.ReadTokens([total]);
        var crop = PlayTime.CombinedValueCrop(read, 540, 393);

        Assert.False(crop.IsEmpty);
        Assert.Equal(PlayTime.LabelStrip(total, 540, 393), crop);
    }

    [Fact]
    public void TotalSittingSlightlyBelowTimeDoesNotEnterTheValueLine()
    {
        var time = Box("time", left: 400, top: 200, width: 40, height: 20);
        var words = new[]
        {
            Box("Total", left: 300, top: 205),
            Box("play", left: 350, top: 200),
            time,
            Box("04d.", left: 300, top: 216),
            Box("11h.", left: 340, top: 216),
            Box("12", left: 380, top: 216),
            Box("min.", left: 410, top: 216),
        };

        var read = PlayTime.ReadTokens(words);
        Assert.Same(time, read.Anchor);
        Assert.DoesNotContain(read.ValueTokens, w => w.Text == "Total");
        Assert.Equal(672, read.FromTokens);
    }

    [Fact]
    public void ValueCropPadsTheTokenUnion()
    {
        var tokens = new[]
        {
            Box("0", left: 100, top: 40, width: 12, height: 14),
            Box("min.", left: 200, top: 42, width: 30, height: 14),
        };

        var crop = PlayTime.ValueCrop(tokens, imageWidth: 500, imageHeight: 300);
        Assert.Equal(100 - PlayTime.CropPadX, crop.Left);
        Assert.Equal(40 - PlayTime.CropPadY, crop.Top);
        Assert.Equal(200 + 30 + PlayTime.CropPadX, crop.Right);
        Assert.Equal(42 + 14 + PlayTime.CropPadY, crop.Bottom);
    }

    [Fact]
    public void CombineTakesWhicheverSideParsed()
    {
        Assert.Equal(672, PlayTime.Combine(null, 672));
        Assert.Equal(672, PlayTime.Combine(672, null));
        Assert.Equal(672, PlayTime.Combine(672, 672));
        Assert.Null(PlayTime.Combine(null, null));
    }

    [Fact]
    public void CombineRefusesAContradictionRatherThanGuessing()
    {
        // Tokens recovered the 0-day "04d." misread; a smeared crop that
        // somehow parsed as four days must not win, and must not be averaged.
        Assert.Null(PlayTime.Combine(6432, 672));
    }

    [Fact]
    public void ReadTokensWithoutTimeIsEmpty()
    {
        var read = PlayTime.ReadTokens([Box("Characters", left: 10, top: 10)]);
        Assert.Null(read.Anchor);
        Assert.Null(read.FromTokens);
        Assert.Empty(read.ValueTokens);
    }

    private static WordBox Box(
        string text,
        double left,
        double top,
        double width = 40,
        double height = 14)
        => new(text, left, top, width, height);
}
