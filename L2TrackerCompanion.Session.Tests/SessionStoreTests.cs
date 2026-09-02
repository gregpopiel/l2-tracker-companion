using L2TrackerCompanion.Parsing;
using Xunit;

namespace L2TrackerCompanion.Session.Tests;

public class SessionStoreTests
{
    [Fact]
    public void TwoAppendsAreTwoInspectableRows()
    {
        using var store = new SessionStore(":memory:");
        var firstAt = DateTimeOffset.Parse("2026-09-02T09:00:00Z");
        var secondAt = DateTimeOffset.Parse("2026-09-02T09:00:10Z");

        store.Append(OpenRead(xp: 100, adena: 10, minutes: 1), firstAt);
        store.Append(OpenRead(xp: 200, adena: 20, minutes: 2, hint: "Dragon Valley (east)"), secondAt);

        var rows = store.List();
        Assert.Equal(2, rows.Count);
        Assert.Equal(2, store.Count);
        Assert.Equal(1, rows[0].Id);
        Assert.Equal(2, rows[1].Id);
        Assert.Equal(100, rows[0].Report.Xp);
        Assert.Equal(200, rows[1].Report.Xp);
        Assert.Equal("Dragon Valley (east)", rows[1].Report.LocationHint);
        Assert.Equal(firstAt, rows[0].CapturedAt);

        var inspect = SessionStore.FormatInspect(rows, ":memory:");
        Assert.Contains("#1", inspect, StringComparison.Ordinal);
        Assert.Contains("#2", inspect, StringComparison.Ordinal);
        Assert.Contains("xp=100", inspect, StringComparison.Ordinal);
        Assert.Contains("xp=200", inspect, StringComparison.Ordinal);
        Assert.Contains("Dragon Valley (east)", inspect, StringComparison.Ordinal);
    }

    [Fact]
    public void NullFarmFieldsRoundTripAsNull()
    {
        using var store = new SessionStore(":memory:");
        var report = new PlayReport(
            Xp: null,
            Adena: null,
            Minutes: null,
            RedLampXp: null,
            PurpleLampXp: null,
            BlueLampXp: null,
            GreenLampXp: null,
            LampXpRead: false,
            LampPanelClosed: false,
            LampXpExceedsDialog: false,
            LampXpTotal: 0,
            LocationHint: null,
            UnreadFields: ["XP", "Adena", "play time"],
            Warnings: ["XP could not be read"]);

        store.Append(report);
        var loaded = store.List().Single().Report;
        Assert.Null(loaded.Xp);
        Assert.Equal(["XP", "Adena", "play time"], loaded.UnreadFields);
        Assert.Equal(["XP could not be read"], loaded.Warnings);
    }

    [Fact]
    public void NewSessionWipesExistingRows()
    {
        using var store = new SessionStore(":memory:");
        store.Append(OpenRead(1, 1, 1));
        store.Append(OpenRead(2, 2, 2));
        store.NewSession();
        Assert.Equal(0, store.Count);
        store.Append(OpenRead(3, 3, 3));
        Assert.Equal(3, store.List().Single().Report.Xp);
    }

    [Fact]
    public void FileStoreSurvivesReopen()
    {
        var path = System.IO.Path.Combine(System.IO.Path.GetTempPath(), $"l2-session-{Guid.NewGuid():N}.db");
        try
        {
            using (var store = new SessionStore(path))
            {
                store.Append(OpenRead(50, 5, 4));
            }

            using (var store = new SessionStore(path))
            {
                Assert.Equal(1, store.Count);
                Assert.Equal(50, store.List().Single().Report.Xp);
                store.NewSession();
            }

            Assert.True(File.Exists(path));
            using var empty = new SessionStore(path);
            Assert.Equal(0, empty.Count);
        }
        finally
        {
            if (File.Exists(path))
            {
                File.Delete(path);
            }
        }
    }

    private static PlayReport OpenRead(long xp, long adena, int minutes, string? hint = null)
        => PlayReport.From(
            xp,
            adena,
            minutes,
            LampXp.Decide(
                new Dictionary<string, long?>
                {
                    ["red"] = 0,
                    ["purple"] = 0,
                    ["blue"] = 0,
                    ["green"] = 0,
                },
                new Dictionary<string, WordBox>
                {
                    ["red"] = new("Red", 10, 80, 40, 14),
                    ["purple"] = new("Purple", 10, 118, 40, 14),
                    ["blue"] = new("Blue", 10, 156, 40, 14),
                    ["green"] = new("Green", 10, 194, 40, 14),
                },
                xp,
                adena),
            hint);
}
