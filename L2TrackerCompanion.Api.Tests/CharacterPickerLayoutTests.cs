using L2TrackerCompanion.Api;
using Xunit;

namespace L2TrackerCompanion.Api.Tests;

public class CharacterPickerLayoutTests
{
    private static CharacterInfo Char(int id, string name)
        => new(id, name, "S", 80, 0, 85);

    [Fact]
    public void ZeroCharactersKeepADisabledCombo()
    {
        var layout = CharacterPickerLayout.For([], null);

        Assert.True(layout.ShowCombo);
        Assert.False(layout.ComboEnabled);
        Assert.Equal(string.Empty, layout.LabelText);
    }

    [Fact]
    public void OneCharacterIsTheNameNotADropdown()
    {
        var only = Char(1, "LongNicknameHere");
        var layout = CharacterPickerLayout.For([only], only);

        Assert.False(layout.ShowCombo);
        Assert.False(layout.ComboEnabled);
        Assert.Equal("LongNicknameHere", layout.LabelText);
    }

    [Fact]
    public void SeveralCharactersKeepAnEnabledCombo()
    {
        var first = Char(1, "Alpha");
        var second = Char(2, "Beta");
        var layout = CharacterPickerLayout.For([first, second], second);

        Assert.True(layout.ShowCombo);
        Assert.True(layout.ComboEnabled);
        Assert.Equal(string.Empty, layout.LabelText);
    }

    [Fact]
    public void ANullListIsTheEmptyPicker()
    {
        var layout = CharacterPickerLayout.For(null, null);

        Assert.True(layout.ShowCombo);
        Assert.False(layout.ComboEnabled);
        Assert.Equal(string.Empty, layout.LabelText);
    }
}
