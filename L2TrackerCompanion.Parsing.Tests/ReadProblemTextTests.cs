using L2TrackerCompanion.Parsing;
using Xunit;

namespace L2TrackerCompanion.Parsing.Tests;

public class ReadProblemTextTests
{
    [Fact]
    public void OneDefectDescribedByBothSourcesIsShownOnce()
    {
        const string defect = "The Magic Lamp XP column could not be read (no silent zeros).";

        var text = ReadProblemText.Compose(defect, defect);

        Assert.Equal(defect, text);
    }

    [Fact]
    public void ASaveWarningThatAddsANewFactIsAppendedAfterTheFrameProblem()
    {
        var text = ReadProblemText.Compose(
            "The last read was not accepted.",
            "XP was assembled from two disagreeing reads. Saving 6,412,172 (spliced) – check it against the panel.");

        Assert.Equal(
            "The last read was not accepted."
            + ReadProblemText.Separator
            + "XP was assembled from two disagreeing reads. Saving 6,412,172 (spliced) – check it against the panel.",
            text);
    }

    [Fact]
    public void NothingWorthSayingCollapsesTheBanner()
    {
        Assert.Null(ReadProblemText.Compose(null, null));
    }

    [Fact]
    public void UnrelatedFrameAndSaveFactsStayJoined()
    {
        // Compose only drops an identical sentence. A generic hold reason
        // must not be invented in SaveGate just to fill this slot – that is
        // what used to produce "Game not running. · The current read is not
        // trustworthy."
        var text = ReadProblemText.Compose(
            "Game not running.",
            "The current read is not trustworthy.");

        Assert.Equal(
            "Game not running."
            + ReadProblemText.Separator
            + "The current read is not trustworthy.",
            text);
    }

    [Fact]
    public void ABlankSourceIsNotJoinedAsAnEmptySegment()
    {
        Assert.Equal("Game not running.", ReadProblemText.Compose("Game not running.", "  "));
        Assert.Equal("The last read was not accepted.", ReadProblemText.Compose(null, "The last read was not accepted."));
        Assert.Equal("The last read was not accepted.", ReadProblemText.Compose("", "The last read was not accepted."));
        Assert.Null(ReadProblemText.Compose("   ", ""));
        Assert.Null(ReadProblemText.Compose());
    }

    [Fact]
    public void AHoldReasonThatRestatesTheFrameProblemIsNotShownTwiceAlongsideAHeldIssue()
    {
        // MainWindow used to pre-compose HoldReason + Issue into _saveWarning,
        // then Compose that with _currentFrameProblem. When the live defect
        // equalled HoldReason, the banner read the same sentence twice.
        const string frame = "The Magic Lamp XP column could not be read (no silent zeros).";
        const string spliced =
            "XP was assembled from two disagreeing reads. Saving 6,412,172 (spliced) – check it against the panel.";

        var text = ReadProblemText.Compose(frame, frame, spliced);

        Assert.Equal(frame + ReadProblemText.Separator + spliced, text);
    }

    [Fact]
    public void ThreeDistinctFactsStayJoinedInOrder()
    {
        var text = ReadProblemText.Compose(
            "XP dropped from 1,000,000 to 900,000",
            "The last read was not accepted.",
            "XP was assembled from two disagreeing reads. Saving 6,412,172 (spliced) – check it against the panel.");

        Assert.Equal(
            "XP dropped from 1,000,000 to 900,000"
            + ReadProblemText.Separator
            + "The last read was not accepted."
            + ReadProblemText.Separator
            + "XP was assembled from two disagreeing reads. Saving 6,412,172 (spliced) – check it against the panel.",
            text);
    }
}
