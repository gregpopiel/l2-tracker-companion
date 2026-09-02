namespace L2TrackerCompanion.Parsing.Tests;

public class DialogCropTests
{
    [Fact]
    public void ReportIsPreferredOverCharacters()
    {
        var words = new[]
        {
            Box("Characters", left: 100, top: 10),
            Box("Report", left: 120, top: 40),
            Box("Characters", left: 100, top: 400),
        };

        var anchor = DialogCrop.FindAnchor(words);
        Assert.NotNull(anchor);
        Assert.Equal("Report", anchor.Kind);
        Assert.Equal(40, anchor.Word.Top);
    }

    [Fact]
    public void CharactersFallbackUsesTheTopmostOccurrence()
    {
        var words = new[]
        {
            Box("Characters", left: 100, top: 400),
            Box("adena", left: 80, top: 200),
            Box("Characters", left: 100, top: 12),
        };

        var anchor = DialogCrop.FindAnchor(words);
        Assert.NotNull(anchor);
        Assert.Equal("Characters", anchor.Kind);
        Assert.Equal(12, anchor.Word.Top);
    }

    [Fact]
    public void ReportMatchIsCaseInsensitive()
    {
        var anchor = DialogCrop.FindAnchor([Box("report", left: 10, top: 10)]);
        Assert.NotNull(anchor);
        Assert.Equal("Report", anchor.Kind);
    }

    [Fact]
    public void ChatReportFarFromTheTitleIsIgnored()
    {
        var words = new[]
        {
            Box("Characters", left: 928, top: 290),
            Box("report", left: 169, top: 634),
            Box("adena", left: 993, top: 430),
        };

        var anchor = DialogCrop.FindAnchor(words);
        Assert.NotNull(anchor);
        Assert.Equal("Characters", anchor.Kind);
        Assert.Equal(290, anchor.Word.Top);
    }

    [Fact]
    public void ReportJustUnderTheTitleIsStillPreferred()
    {
        var words = new[]
        {
            Box("Characters", left: 393, top: 35),
            Box("Report", left: 426, top: 93),
            Box("Characters", left: 399, top: 379),
        };

        var anchor = DialogCrop.FindAnchor(words);
        Assert.NotNull(anchor);
        Assert.Equal("Report", anchor.Kind);
        Assert.Equal(93, anchor.Word.Top);
    }

    [Fact]
    public void ReportLookalikesAreNotAnchors()
    {
        var words = new[]
        {
            Box("Rewrt", left: 120, top: 40),
            Box("Recort", left: 120, top: 40),
            Box("Characters", left: 100, top: 10),
        };

        var anchor = DialogCrop.FindAnchor(words);
        Assert.NotNull(anchor);
        Assert.Equal("Characters", anchor.Kind);
    }

    [Fact]
    public void NoAnchorWhenNeitherLabelIsPresent()
    {
        Assert.Null(DialogCrop.FindAnchor([Box("adena", left: 10, top: 10), Box("racters", left: 10, top: 10)]));
    }

    [Fact]
    public void MarginsAreEqualLeftAndRight()
    {
        Assert.Equal(DialogCrop.MarginLeft, DialogCrop.MarginRight);
        Assert.Equal(550, DialogCrop.MarginLeft);
        Assert.Equal(80, DialogCrop.MarginTop);
        Assert.Equal(550, DialogCrop.MarginBottom);
    }

    [Fact]
    public void CropIsFixedPixelsAroundTheAnchor()
    {
        var anchor = Box("Report", left: 900, top: 200);
        var crop = DialogCrop.Rect(anchor, imageWidth: 1919, imageHeight: 1079);

        Assert.Equal(350, crop.Left);
        Assert.Equal(120, crop.Top);
        Assert.Equal(1450, crop.Right);
        Assert.Equal(750, crop.Bottom);
        Assert.Equal(1100, crop.Width);
        Assert.Equal(630, crop.Height);
    }

    [Fact]
    public void LeftDockedTable369PxFromAnchorStaysInsideTheCrop()
    {
        // Measured: a left-docked table needed 369px; a 350px left margin
        // clipped it. Equal 550px margins keep that table in frame.
        var anchor = Box("Characters", left: 900, top: 200);
        var crop = DialogCrop.Rect(anchor, 1919, 1079);
        const double lampNameLeft = 900 - 369;

        Assert.True(crop.Left <= lampNameLeft, $"crop.Left={crop.Left} clipped lamp at {lampNameLeft}");
        Assert.True(crop.Right >= 900 + 250);
    }

    [Fact]
    public void TightDialogCropClampsToTheFullImage()
    {
        var anchor = Box("Characters", left: 80, top: 12);
        var crop = DialogCrop.Rect(anchor, imageWidth: 551, imageHeight: 428);

        Assert.Equal(0, crop.Left);
        Assert.Equal(0, crop.Top);
        Assert.Equal(551, crop.Width);
        Assert.Equal(428, crop.Height);
    }

    [Fact]
    public void LeftDockedTightCropKeepsLampsAtTheLeftEdge()
    {
        // 2026-08-22 235004: Characters at x=393, lamp names around x=60, image 567px.
        var crop = DialogCrop.Rect(Box("Characters", left: 393, top: 10), 567, 420);
        Assert.Equal(0, crop.Left);
        Assert.True(crop.Right >= 60 + 20);
    }

    private static WordBox Box(string text, double left, double top, double width = 80, double height = 14)
        => new(text, left, top, width, height);
}
