using System.Globalization;
using L2TrackerCompanion.Api;
using Xunit;

namespace L2TrackerCompanion.Api.Tests;

public class BonusTextTests
{
    [Theory]
    [InlineData("25", 25)]
    [InlineData("25.5", 25.5)]
    [InlineData("0", 0)]
    [InlineData(" 30.25 ", 30.25)]
    public void ParsesInvariantDot(string text, double expected)
    {
        Assert.True(BonusText.TryParse(text, out var bonus));
        Assert.Equal(expected, bonus);
    }

    [Fact]
    public void ParsesCommaDecimalInPolishCulture()
    {
        var previous = CultureInfo.CurrentCulture;
        try
        {
            CultureInfo.CurrentCulture = CultureInfo.GetCultureInfo("pl-PL");
            Assert.True(BonusText.TryParse("25,5", out var bonus));
            Assert.Equal(25.5, bonus);
        }
        finally
        {
            CultureInfo.CurrentCulture = previous;
        }
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("abc")]
    [InlineData("-1")]
    public void RejectsEmptyNegativeAndGarbage(string text)
    {
        Assert.False(BonusText.TryParse(text, out _));
    }
}
