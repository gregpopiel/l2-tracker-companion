namespace L2TrackerCompanion.Parsing.Tests;

public class XpReadsTests
{
    [Fact]
    public void DifferentDigitCountTakesTheCrop()
    {
        Assert.Equal(22_921_247, XpReads.Combine(2_921_247, 22_921_247));
        Assert.Equal(8_497_052, XpReads.Combine(2, 8_497_052));
    }

    [Fact]
    public void SameLengthDifferentLeadingGroupSplicesCropLeadOntoTokenTail()
    {
        Assert.Equal(283_881_103, XpReads.Combine(263_881_103, 283_881_103));
    }

    [Fact]
    public void SameLeadingGroupKeepsTheTokenRead()
    {
        Assert.Equal(1_596_804, XpReads.Combine(1_596_804, 1_506_804));
        Assert.Equal(282_744_857, XpReads.Combine(282_744_857, 282_744_897));
    }

    [Fact]
    public void NullFallsBackToTheOtherRead()
    {
        Assert.Equal(22_921_247, XpReads.Combine(null, 22_921_247));
        Assert.Equal(22_921_247, XpReads.Combine(22_921_247, null));
        Assert.Null(XpReads.Combine(null, null));
    }
}
