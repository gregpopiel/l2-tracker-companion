using L2TrackerCompanion.Parsing;
using Xunit;

namespace L2TrackerCompanion.Parsing.Tests;

public class XpReadsConfidenceTests
{
    [Fact]
    public void AgreeingReadsCarryNoDoubt()
    {
        var result = XpReads.CombineDetailed(4_210_400, 4_210_400);

        Assert.Equal(4_210_400, result.Value);
        Assert.False(result.Disagreed);
        Assert.False(result.Spliced);
        Assert.False(result.MagnitudeMismatch);
    }

    [Fact]
    public void ASingleSourceIsNotADisagreement()
    {
        Assert.False(XpReads.CombineDetailed(4_210_400, null).Disagreed);
        Assert.False(XpReads.CombineDetailed(null, 4_210_400).Disagreed);
    }

    [Fact]
    public void DifferentDigitCountIsFlaggedAsAMagnitudeMismatch()
    {
        var result = XpReads.CombineDetailed(4_210_400, 42_104_000);

        Assert.True(result.Disagreed);
        Assert.True(result.MagnitudeMismatch);
        Assert.False(result.Spliced);
    }

    [Fact]
    public void SpliceIsReportedAsARepair()
    {
        // Same length, leading groups differ: the value returned is a hybrid.
        var result = XpReads.CombineDetailed(1_234_567, 9_234_567);

        Assert.True(result.Disagreed);
        Assert.True(result.Spliced);
        Assert.False(result.MagnitudeMismatch);
    }

    [Fact]
    public void CombineStillReturnsTheSameFigureAsBefore()
    {
        Assert.Equal(
            XpReads.Combine(1_234_567, 9_234_567),
            XpReads.CombineDetailed(1_234_567, 9_234_567).Value);
    }
}
