using L2TrackerCompanion.Api;
using Xunit;

namespace L2TrackerCompanion.Api.Tests;

public class LastCharacterStoreTests
{
    private const string UserId = "usr_abc123";

    [Fact]
    public void MissingFileRemembersNothing()
    {
        var dir = NewTempDir();
        try
        {
            var store = new LastCharacterStore(dir);
            Assert.Null(store.TryLoad(UserId));
            Assert.False(File.Exists(store.FilePath));
        }
        finally
        {
            Directory.Delete(dir, recursive: true);
        }
    }

    [Fact]
    public void SaveRoundTripsAndPersists()
    {
        var dir = NewTempDir();
        try
        {
            new LastCharacterStore(dir).Save(UserId, 42);
            Assert.Equal(42, new LastCharacterStore(dir).TryLoad(UserId));

            new LastCharacterStore(dir).Save(UserId, 7);
            Assert.Equal(7, new LastCharacterStore(dir).TryLoad(UserId));
        }
        finally
        {
            Directory.Delete(dir, recursive: true);
        }
    }

    [Fact]
    public void AnotherAccountRemembersNothing()
    {
        var dir = NewTempDir();
        try
        {
            var store = new LastCharacterStore(dir);
            store.Save(UserId, 42);
            Assert.Null(store.TryLoad("usr_someone_else"));
        }
        finally
        {
            Directory.Delete(dir, recursive: true);
        }
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void BlankUserIdNeitherSavesNorLoads(string? userId)
    {
        var dir = NewTempDir();
        try
        {
            var store = new LastCharacterStore(dir);
            store.Save(userId, 42);
            Assert.False(File.Exists(store.FilePath));
            Assert.Null(store.TryLoad(userId));
        }
        finally
        {
            Directory.Delete(dir, recursive: true);
        }
    }

    [Theory]
    [InlineData(UserId)]
    [InlineData(UserId + "\nnot-a-number")]
    [InlineData("")]
    [InlineData("\n42")]
    public void UnreadableContentsRememberNothing(string contents)
    {
        var dir = NewTempDir();
        try
        {
            File.WriteAllText(Path.Combine(dir, LastCharacterStore.FileName), contents);
            Assert.Null(new LastCharacterStore(dir).TryLoad(UserId));
        }
        finally
        {
            Directory.Delete(dir, recursive: true);
        }
    }

    private static string NewTempDir()
    {
        var dir = Path.Combine(
            Path.GetTempPath(),
            "l2-last-character-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(dir);
        return dir;
    }
}
