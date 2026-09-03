using L2TrackerCompanion.Parsing;

namespace L2TrackerCompanion.Parsing.Tests;

/// <summary>
/// Shared Play Report builders for the snapshot-save tests.
/// </summary>
internal static class TestReports
{
    public static PlayReport Open(
        long? xp = 1_000_000,
        long? adena = 250_000,
        int? minutes = 60,
        long red = 0,
        long purple = 0,
        long blue = 0,
        long green = 0,
        ReadConfidence? confidence = null)
        => PlayReport.From(
            xp,
            adena,
            minutes,
            LampXp.Decide(
                new Dictionary<string, long?>
                {
                    ["red"] = red,
                    ["purple"] = purple,
                    ["blue"] = blue,
                    ["green"] = green,
                },
                OpenRows(),
                dialogXp: xp ?? long.MaxValue,
                dialogAdena: adena ?? 0),
            null,
            confidence);

    public static PlayReport ClosedPanel(long xp = 1_000_000, long adena = 250_000, int minutes = 60)
        => PlayReport.From(
            xp,
            adena,
            minutes,
            LampXp.Decide(
                new Dictionary<string, long?>
                {
                    ["red"] = null,
                    ["purple"] = null,
                    ["blue"] = null,
                    ["green"] = null,
                },
                new Dictionary<string, WordBox>(),
                xp,
                adena),
            null);

    public static PlayReport UnreadLamps(long xp = 1_000_000, long adena = 250_000, int minutes = 60)
        => PlayReport.From(
            xp,
            adena,
            minutes,
            LampXp.Decide(
                new Dictionary<string, long?>
                {
                    ["red"] = null,
                    ["purple"] = null,
                    ["blue"] = null,
                    ["green"] = null,
                },
                OpenRows(),
                dialogXp: xp,
                dialogAdena: adena),
            null);

    public static Dictionary<string, WordBox> OpenRows() => new()
    {
        ["red"] = new WordBox("Red", 0, 0, 10, 10),
        ["purple"] = new WordBox("Purple", 0, 20, 10, 10),
        ["blue"] = new WordBox("Blue", 0, 40, 10, 10),
        ["green"] = new WordBox("Green", 0, 60, 10, 10),
    };
}
