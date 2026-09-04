using System.Globalization;

namespace L2TrackerCompanion.Api;

/// <summary>
/// The character picked last, so a restart lands on it instead of the first one in
/// the list. Two lines next to <see cref="AppOptionsStore"/> — the account's user id,
/// then the character id (a plain preference, so no DPAPI).
/// </summary>
/// <remarks>
/// Scoped to the account because character ids are per-user integers: without the
/// user id, signing in with another account's token could match an unrelated
/// character by coincidence. A mismatch simply reads as "nothing remembered".
/// </remarks>
public sealed class LastCharacterStore
{
    public const string FileName = "last-character.txt";

    private readonly string _directory;

    public LastCharacterStore(string directory)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(directory);
        _directory = directory;
        Directory.CreateDirectory(_directory);
    }

    public static LastCharacterStore GetDefault()
    {
        var directory = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            TokenStore.AppDataFolderName);
        return new LastCharacterStore(directory);
    }

    public string FilePath => Path.Combine(_directory, FileName);

    /// <summary>
    /// The remembered character id for <paramref name="userId"/>, or null when nothing
    /// was stored, the file belongs to another account, or its contents are unreadable.
    /// </summary>
    public int? TryLoad(string? userId)
    {
        if (string.IsNullOrWhiteSpace(userId) || !File.Exists(FilePath))
        {
            return null;
        }

        try
        {
            var lines = File.ReadAllLines(FilePath);
            if (lines.Length < 2 || !string.Equals(lines[0].Trim(), userId.Trim(), StringComparison.Ordinal))
            {
                return null;
            }

            return int.TryParse(lines[1].Trim(), NumberStyles.Integer, CultureInfo.InvariantCulture, out var characterId)
                ? characterId
                : null;
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            return null;
        }
    }

    public void Save(string? userId, int characterId)
    {
        if (string.IsNullOrWhiteSpace(userId))
        {
            return;
        }

        // Losing the preference is not worth crashing over — it is re-saved on the
        // next pick, and a missing file already means "start on the first character".
        try
        {
            File.WriteAllLines(FilePath, [userId.Trim(), characterId.ToString(CultureInfo.InvariantCulture)]);
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
        }
    }
}
