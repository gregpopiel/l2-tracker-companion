namespace L2TrackerCompanion.Parsing.Tests;

public class AmountsTests
{
    [Theory]
    [InlineData(1_165_047, 1165)]
    [InlineData(14_751_635, 14752)]
    [InlineData(500, 1)]
    [InlineData(499, 0)]
    [InlineData(0, 0)]
    public void ToThousandsRoundsToNearestWholeThousand(long raw, long expected)
    {
        Assert.Equal(expected, Amounts.ToThousands(raw));
    }
}
