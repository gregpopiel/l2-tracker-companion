using L2TrackerCompanion.Api;
using Xunit;

namespace L2TrackerCompanion.Api.Tests;

public class MisreadStoreTests
{
    [Fact]
    public void SaveWritesTheCaptureAndTheDetails()
    {
        var dir = NewTempDir();
        try
        {
            var image = WriteFakeCapture(dir, "capture.png", "first frame");
            var store = new MisreadStore(Path.Combine(dir, "misreads"));

            var folder = store.Save(image, "Tick rejected", "green: 2000000");

            Assert.NotNull(folder);
            Assert.Equal("first frame", File.ReadAllText(Path.Combine(folder!, MisreadStore.ImageFileName)));
            var details = File.ReadAllText(Path.Combine(folder, MisreadStore.DetailsFileName));
            Assert.Contains("Tick rejected", details, StringComparison.Ordinal);
            Assert.Contains("green: 2000000", details, StringComparison.Ordinal);
        }
        finally
        {
            Directory.Delete(dir, recursive: true);
        }
    }

    /// <summary>
    /// Polling overwrites one capture.png every 10s, so the archived copy has
    /// to be a copy — a saved path would point at a later frame.
    /// </summary>
    [Fact]
    public void SavedCaptureIsACopyNotAReference()
    {
        var dir = NewTempDir();
        try
        {
            var image = WriteFakeCapture(dir, "capture.png", "first frame");
            var store = new MisreadStore(Path.Combine(dir, "misreads"));
            var folder = store.Save(image, "rejected", "details");

            File.WriteAllText(image, "a later frame");

            Assert.Equal("first frame", File.ReadAllText(Path.Combine(folder!, MisreadStore.ImageFileName)));
        }
        finally
        {
            Directory.Delete(dir, recursive: true);
        }
    }

    [Fact]
    public void RetentionKeepsTheNewestEntries()
    {
        var dir = NewTempDir();
        try
        {
            var image = WriteFakeCapture(dir, "capture.png", "frame");
            var root = Path.Combine(dir, "misreads");
            var store = new MisreadStore(root);
            Directory.CreateDirectory(root);

            // Folder names lead with a timestamp and prune in ordinal order,
            // so stale entries can be staged directly.
            for (var i = 0; i < MisreadStore.MaxEntries + 5; i++)
            {
                Directory.CreateDirectory(Path.Combine(root, $"20000101-000000-{i:000}-stale"));
            }

            store.Save(image, "rejected", "details");

            var folders = Directory.GetDirectories(root);
            Assert.Equal(MisreadStore.MaxEntries, folders.Length);
            Assert.DoesNotContain(folders, f => Path.GetFileName(f) == "20000101-000000-000-stale");
        }
        finally
        {
            Directory.Delete(dir, recursive: true);
        }
    }

    /// <summary>
    /// A save runs inside a poll tick; a capture that vanished must not take
    /// the tick down with it.
    /// </summary>
    [Fact]
    public void MissingCaptureSavesNothingAndDoesNotThrow()
    {
        var dir = NewTempDir();
        try
        {
            var store = new MisreadStore(Path.Combine(dir, "misreads"));

            Assert.Null(store.Save(Path.Combine(dir, "gone.png"), "rejected", "details"));
            Assert.Null(store.Save(string.Empty, "rejected", "details"));
            Assert.False(Directory.Exists(Path.Combine(dir, "misreads")));
        }
        finally
        {
            Directory.Delete(dir, recursive: true);
        }
    }

    [Fact]
    public void FolderNameCarriesTheReason()
    {
        var dir = NewTempDir();
        try
        {
            var image = WriteFakeCapture(dir, "capture.png", "frame");
            var store = new MisreadStore(Path.Combine(dir, "misreads"));

            var folder = store.Save(image, "Lamp XP not read", "details");

            Assert.EndsWith("-lamp-xp-not-read", Path.GetFileName(folder)!, StringComparison.Ordinal);
        }
        finally
        {
            Directory.Delete(dir, recursive: true);
        }
    }

    private static string WriteFakeCapture(string dir, string name, string content)
    {
        var path = Path.Combine(dir, name);
        File.WriteAllText(path, content);
        return path;
    }

    private static string NewTempDir()
    {
        var dir = Path.Combine(Path.GetTempPath(), "l2tc-misread-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(dir);
        return dir;
    }
}
