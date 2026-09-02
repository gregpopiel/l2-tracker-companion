namespace L2TrackerCompanion.Parsing.Tests;

public class LampXpTests
{
    [Fact]
    public void AllFourAndSumUnderDialogXpIsRead()
    {
        var parsed = Four(260_000, 0, 0, 0);
        var rows = OpenRows();
        var decision = LampXp.Decide(parsed, rows, dialogXp: 506_625, dialogAdena: 59_493);

        Assert.True(decision.LampXpRead);
        Assert.False(decision.LampPanelClosed);
        Assert.False(decision.ExceedsDialogXp);
        Assert.Equal(260_000, decision.Red);
        Assert.Equal(0, decision.Purple);
    }

    [Fact]
    public void TenXPurpleExceedingDialogXpIsDiscardedNotSaved()
    {
        var parsed = Four(32_825_000, 449_250_000, 49_725_000, 14_400_000);
        var decision = LampXp.Decide(parsed, OpenRows(), dialogXp: 283_881_103, dialogAdena: 1);

        Assert.True(decision.ExceedsDialogXp);
        Assert.False(decision.LampXpRead);
        Assert.False(decision.LampPanelClosed);
        Assert.Null(decision.Purple);
        Assert.True(decision.LampXpTotal > 283_881_103);
    }

    [Fact]
    public void MissingColourIsAllOrNoneNotAPartialSave()
    {
        var parsed = new Dictionary<string, long?>
        {
            ["red"] = 260_000,
            ["purple"] = 0,
            ["blue"] = null,
            ["green"] = 0,
        };
        var decision = LampXp.Decide(parsed, OpenRows(), 1_000_000, 1);

        Assert.False(decision.LampXpRead);
        Assert.Null(decision.Red);
    }

    [Fact]
    public void NoColourNamesWithFarmFieldsIsAClosedPanelNotAFailedRead()
    {
        var parsed = new Dictionary<string, long?>
        {
            ["red"] = null,
            ["purple"] = null,
            ["blue"] = null,
            ["green"] = null,
        };
        var decision = LampXp.Decide(parsed, new Dictionary<string, WordBox>(), dialogXp: 100, dialogAdena: 50);

        Assert.True(decision.LampPanelClosed);
        Assert.False(decision.LampXpRead);
        Assert.False(decision.ExceedsDialogXp);
    }

    [Fact]
    public void NoColourNamesWithoutFarmFieldsIsNotClosed()
    {
        var parsed = new Dictionary<string, long?>
        {
            ["red"] = null,
            ["purple"] = null,
            ["blue"] = null,
            ["green"] = null,
        };
        var decision = LampXp.Decide(parsed, new Dictionary<string, WordBox>(), dialogXp: null, dialogAdena: 50);

        Assert.False(decision.LampPanelClosed);
    }

    [Fact]
    public void FirstParsedKeepsZeroAndSkipsNulls()
    {
        Assert.Equal(0, LampXp.FirstParsed(null, 0, 200_000));
        Assert.Equal(44_250_000, LampXp.FirstParsed(44_250_000, 449_250_000));
        Assert.Null(LampXp.FirstParsed(null, null));
    }

    private static Dictionary<string, long?> Four(long red, long purple, long blue, long green) => new()
    {
        ["red"] = red,
        ["purple"] = purple,
        ["blue"] = blue,
        ["green"] = green,
    };

    private static Dictionary<string, WordBox> OpenRows() => new()
    {
        ["red"] = new WordBox("Red", 10, 80, 40, 14),
        ["purple"] = new WordBox("Purple", 10, 118, 40, 14),
        ["blue"] = new WordBox("Blue", 10, 156, 40, 14),
        ["green"] = new WordBox("Green", 10, 194, 40, 14),
    };
}
