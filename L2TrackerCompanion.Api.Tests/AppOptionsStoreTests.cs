using L2TrackerCompanion.Api;
using Xunit;

namespace L2TrackerCompanion.Api.Tests;

public class AppOptionsStoreTests
{
    [Fact]
    public void MissingFileDefaultsToUserMode()
    {
        var dir = NewTempDir();
        try
        {
            var store = new AppOptionsStore(dir);
            Assert.False(store.DebugMode);
            Assert.False(File.Exists(store.FilePath));
        }
        finally
        {
            Directory.Delete(dir, recursive: true);
        }
    }

    [Fact]
    public void SetDebugModeRoundTripsAndPersists()
    {
        var dir = NewTempDir();
        try
        {
            var store = new AppOptionsStore(dir);
            store.SetDebugMode(true);
            Assert.True(store.DebugMode);
            Assert.Equal(AppOptionsStore.DebugValue, File.ReadAllText(store.FilePath).Trim());

            var reloaded = new AppOptionsStore(dir);
            Assert.True(reloaded.DebugMode);

            reloaded.SetDebugMode(false);
            Assert.False(reloaded.DebugMode);
            Assert.Equal(AppOptionsStore.UserValue, File.ReadAllText(reloaded.FilePath).Trim());
            Assert.False(new AppOptionsStore(dir).DebugMode);
        }
        finally
        {
            Directory.Delete(dir, recursive: true);
        }
    }

    [Theory]
    [InlineData("debug")]
    [InlineData("DEBUG")]
    [InlineData(" Debug \n")]
    public void DebugFileValueIsCaseInsensitive(string contents)
    {
        var dir = NewTempDir();
        try
        {
            File.WriteAllText(System.IO.Path.Combine(dir, AppOptionsStore.FileName), contents);
            Assert.True(new AppOptionsStore(dir).DebugMode);
        }
        finally
        {
            Directory.Delete(dir, recursive: true);
        }
    }

    [Theory]
    [InlineData("user")]
    [InlineData("")]
    [InlineData("nope")]
    public void UnknownOrUserValueIsUserMode(string contents)
    {
        var dir = NewTempDir();
        try
        {
            File.WriteAllText(System.IO.Path.Combine(dir, AppOptionsStore.FileName), contents);
            Assert.False(new AppOptionsStore(dir).DebugMode);
        }
        finally
        {
            Directory.Delete(dir, recursive: true);
        }
    }

    private static string NewTempDir()
    {
        var dir = System.IO.Path.Combine(
            System.IO.Path.GetTempPath(),
            "l2-options-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(dir);
        return dir;
    }
}
