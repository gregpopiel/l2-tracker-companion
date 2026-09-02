namespace L2TrackerCompanion.Parsing.Tests;

public class DigitFoldTests
{
    [Theory]
    [InlineData('O', '0')]
    [InlineData('o', '0')]
    [InlineData('I', '1')]
    [InlineData('l', '1')]
    [InlineData('i', '1')]
    [InlineData('S', '5')]
    [InlineData('s', '5')]
    [InlineData('B', '8')]
    [InlineData('b', '6')]
    [InlineData('G', '6')]
    [InlineData('Z', '2')]
    [InlineData('z', '2')]
    [InlineData('T', '7')]
    [InlineData('t', '7')]
    [InlineData('g', '9')]
    [InlineData('q', '9')]
    public void LookAlikeLettersFoldToDigits(char input, char expected)
    {
        Assert.Equal(expected, DigitFold.Apply(input));
    }

    [Fact]
    public void RealDigitsAndMagnitudeLettersAreLeftAlone()
    {
        Assert.Equal("850K", DigitFold.Apply("B50K"));
        Assert.Equal("M", DigitFold.Apply("M"));
        Assert.Equal("0123456789", DigitFold.Apply("0123456789"));
    }

    [Fact]
    public void ApplyExceptBKeepsBillionsSuffix()
    {
        Assert.Equal("1B", DigitFold.ApplyExceptB("1B"));
        Assert.Equal("B6", DigitFold.ApplyExceptB("Bb"));
    }
}
