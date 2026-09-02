namespace L2TrackerCompanion.Api;

/// <summary>
/// Local UI mode: User hides developer tools; Debug shows capture/parse dumps.
/// Stored as a one-line file next to <see cref="TokenStore"/> (not DPAPI — not a secret).
/// </summary>
public sealed class AppOptionsStore
{
    public const string FileName = "options.txt";
    public const string UserValue = "user";
    public const string DebugValue = "debug";

    private readonly string _directory;

    public AppOptionsStore(string directory)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(directory);
        _directory = directory;
        Directory.CreateDirectory(_directory);
        DebugMode = ReadFile();
    }

    public static AppOptionsStore GetDefault()
    {
        var directory = System.IO.Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            TokenStore.AppDataFolderName);
        return new AppOptionsStore(directory);
    }

    public string FilePath => System.IO.Path.Combine(_directory, FileName);

    /// <summary>
    /// True = Debug (show dumps and parse tools). False = User (hide them).
    /// Missing or unknown file contents default to User.
    /// </summary>
    public bool DebugMode { get; private set; }

    public void SetDebugMode(bool enabled)
    {
        DebugMode = enabled;
        File.WriteAllText(FilePath, enabled ? DebugValue : UserValue);
    }

    private bool ReadFile()
    {
        if (!File.Exists(FilePath))
        {
            return false;
        }

        var value = File.ReadAllText(FilePath).Trim();
        return string.Equals(value, DebugValue, StringComparison.OrdinalIgnoreCase);
    }
}
