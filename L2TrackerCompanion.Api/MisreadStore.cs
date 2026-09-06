using System.Text;

namespace L2TrackerCompanion.Api;

/// <summary>
/// Debug-mode archive of frames the pipeline could not read: a copy of the
/// capture plus what was parsed from it. Material for improving the OCR
/// passes offline — nothing here is ever sent anywhere or read back by the
/// app. Sits next to <see cref="TokenStore"/> / <see cref="AppOptionsStore"/>.
/// </summary>
/// <remarks>
/// The capture is copied, not referenced: polling overwrites the same
/// <c>capture.png</c> every 10s, so a path saved here would point at a
/// different frame within seconds.
/// </remarks>
public sealed class MisreadStore
{
    public const string FolderName = "misreads";
    public const string ImageFileName = "capture.png";
    public const string DetailsFileName = "read.txt";

    /// <summary>
    /// Newest folders kept. A 10s poll that keeps failing would otherwise
    /// fill the disk — the recent frames are the ones worth having.
    /// </summary>
    public const int MaxEntries = 50;

    private readonly string _directory;

    public MisreadStore(string directory)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(directory);
        _directory = directory;
    }

    public static MisreadStore GetDefault()
    {
        var directory = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            TokenStore.AppDataFolderName,
            FolderName);
        return new MisreadStore(directory);
    }

    public string DirectoryPath => _directory;

    /// <summary>
    /// Archives one failed read. Returns the folder written, or
    /// <see langword="null"/> if nothing was saved — a poll tick must not
    /// fail because the disk is full or the capture vanished, so every error
    /// here is swallowed rather than raised.
    /// </summary>
    public string? Save(string sourceImagePath, string reason, string details)
    {
        if (string.IsNullOrWhiteSpace(sourceImagePath) || !File.Exists(sourceImagePath))
        {
            return null;
        }

        try
        {
            Directory.CreateDirectory(_directory);
            var folder = Path.Combine(
                _directory,
                $"{DateTime.Now:yyyyMMdd-HHmmss-fff}-{Slug(reason)}");
            Directory.CreateDirectory(folder);

            File.Copy(sourceImagePath, Path.Combine(folder, ImageFileName), overwrite: true);

            var text = new StringBuilder()
                .AppendLine($"Saved:  {DateTime.Now:yyyy-MM-dd HH:mm:ss}")
                .AppendLine($"Reason: {reason}")
                .AppendLine($"Source: {sourceImagePath}")
                .AppendLine()
                .AppendLine(details)
                .ToString();
            File.WriteAllText(Path.Combine(folder, DetailsFileName), text, Encoding.UTF8);

            Prune();
            return folder;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return null;
        }
    }

    /// <summary>
    /// Folder names lead with a sortable timestamp, so oldest-first is plain
    /// ordinal order — no directory timestamps involved.
    /// </summary>
    private void Prune()
    {
        string[] folders;
        try
        {
            folders = Directory.GetDirectories(_directory);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            // Housekeeping only: the frame is already archived, so a failure
            // to enumerate must not make Save report it as unsaved.
            return;
        }

        if (folders.Length <= MaxEntries)
        {
            return;
        }

        Array.Sort(folders, StringComparer.Ordinal);
        foreach (var stale in folders.Take(folders.Length - MaxEntries))
        {
            try
            {
                Directory.Delete(stale, recursive: true);
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                // A folder the user has open in Explorer stays; the next
                // save retries. Pruning is housekeeping, never the point.
            }
        }
    }

    private static string Slug(string reason)
    {
        if (string.IsNullOrWhiteSpace(reason))
        {
            return "unknown";
        }

        var slug = new StringBuilder(reason.Length);
        foreach (var ch in reason.Trim().ToLowerInvariant())
        {
            if (char.IsAsciiLetterOrDigit(ch))
            {
                slug.Append(ch);
            }
            else if (slug.Length > 0 && slug[^1] != '-')
            {
                slug.Append('-');
            }
        }

        return slug.ToString().Trim('-') is { Length: > 0 } trimmed
            ? trimmed[..Math.Min(trimmed.Length, 40)]
            : "unknown";
    }
}
