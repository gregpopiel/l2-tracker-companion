using System.Globalization;
using System.Text;
using L2TrackerCompanion.Parsing;

namespace L2TrackerCompanion.Ocr;

/// <summary>
/// Step 12: minimap location header, off the same full-image pass that
/// locates the dialog. No extra OCR. Dialog-only crops return nothing.
/// </summary>
public static class LocationHintPass
{
    public const string DefaultLocationFolderName = "ocr-poc-location";

    public static string GetDefaultLocationDirectory()
    {
        var directory = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            OcrWordDump.AppDataFolderName,
            DefaultLocationFolderName);
        Directory.CreateDirectory(directory);
        return directory;
    }

    public static async Task<LocationHintResult> RunFileAsync(
        string imagePath,
        string outputDirectory,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(imagePath) || !File.Exists(imagePath))
        {
            return Fail($"PNG not found: {imagePath}");
        }

        try
        {
            using var dialog = await DialogCropPass.RecognizeAsync(imagePath, cancellationToken)
                .ConfigureAwait(false);
            if (!dialog.Success)
            {
                return Fail(dialog.ErrorMessage ?? "Full-image OCR failed");
            }

            var boxes = OcrRecognize.ToWordBoxes(dialog.FullWords);
            var width = (int)dialog.ImageWidth;
            var height = (int)dialog.ImageHeight;
            var hint = LocationHint.Read(boxes, width, height);
            var zoneCount = boxes.Count(w =>
                width >= LocationHint.MinImageWidth
                && w.Top < height * LocationHint.MaxTopFraction
                && (width - (w.Left + w.Width)) < width * LocationHint.MaxRightGapFraction
                && w.Text.Any(char.IsAsciiLetter));

            Directory.CreateDirectory(outputDirectory);
            var dumpPath = Path.Combine(outputDirectory, Path.GetFileNameWithoutExtension(imagePath) + ".txt");
            var result = new LocationHintResult
            {
                Success = true,
                SourcePath = Path.GetFullPath(imagePath),
                DumpPath = Path.GetFullPath(dumpPath),
                ImageWidth = dialog.ImageWidth,
                ImageHeight = dialog.ImageHeight,
                Hint = hint,
                ZoneWordCount = zoneCount,
            };
            await File.WriteAllTextAsync(dumpPath, FormatDump(result), Encoding.UTF8, cancellationToken)
                .ConfigureAwait(false);
            return result;
        }
        catch (Exception ex)
        {
            return Fail(ex.Message);
        }
    }

    public static string FormatStatus(LocationHintResult result)
    {
        if (!result.Success)
        {
            return $"Location failed: {result.ErrorMessage}";
        }

        return $"hint={(result.Hint ?? "(none)")}  {result.FrameKind}  {result.ImageWidth}x{result.ImageHeight}";
    }

    public static string FormatBatchSummary(IReadOnlyList<LocationHintResult> results)
    {
        var builder = new StringBuilder();
        builder.AppendLine("# Windows.Media.Ocr location hint — full-image pass, no extra OCR");
        builder.AppendLine($"# files: {results.Count}");
        builder.AppendLine($"# succeeded: {results.Count(r => r.Success)}");
        builder.AppendLine($"# with hint: {results.Count(r => r.Hint is not null)}/{results.Count}");
        builder.AppendLine("file\tkind\twidth\theight\thint");
        foreach (var result in results)
        {
            var name = result.SourcePath is null ? "" : Path.GetFileName(result.SourcePath);
            builder.Append(Sanitize(name));
            builder.Append('\t');
            builder.Append(result.Success ? result.FrameKind : "fail");
            builder.Append('\t');
            builder.Append(result.ImageWidth.ToString(CultureInfo.InvariantCulture));
            builder.Append('\t');
            builder.Append(result.ImageHeight.ToString(CultureInfo.InvariantCulture));
            builder.Append('\t');
            builder.AppendLine(Sanitize(result.Hint ?? ""));
        }

        return builder.ToString();
    }

    public static string FormatBatchStatus(IReadOnlyList<LocationHintResult> results)
    {
        var desktop = results.Where(r => r.Success && r.FrameKind == "desktop").ToList();
        var framed = results.Where(r => r.Success && r.FrameKind == "framed").ToList();
        var dialog = results.Where(r => r.Success && r.FrameKind == "dialog").ToList();
        return $"Location batch: hint {desktop.Count(r => r.Hint is not null)}/{desktop.Count} desktop, "
            + $"{framed.Count(r => r.Hint is not null)}/{framed.Count} framed, "
            + $"{dialog.Count(r => r.Hint is not null)}/{dialog.Count} dialog.";
    }

    public static string FormatBaselineComparison(
        IReadOnlyList<LocationHintResult> results,
        IReadOnlyDictionary<string, string?> baseline)
    {
        var match = 0;
        var compared = 0;
        var mismatches = new List<string>();
        foreach (var result in results)
        {
            var name = result.SourcePath is null ? "" : Path.GetFileName(result.SourcePath);
            if (!baseline.TryGetValue(name, out var expected))
            {
                mismatches.Add($"{name}\t(no tesseract baseline row)");
                continue;
            }

            compared++;
            var ours = Normalize(result.Hint);
            var theirs = Normalize(expected);
            if (ours == theirs)
            {
                match++;
            }
            else
            {
                mismatches.Add(
                    $"{name}\tours={result.Hint ?? "(none)"}\ttheirs={expected ?? "(none)"}");
            }
        }

        var builder = new StringBuilder();
        builder.AppendLine($"vs tesseract.js: hint {match}/{compared}.");
        if (mismatches.Count == 0)
        {
            builder.AppendLine("No mismatches.");
        }
        else
        {
            builder.AppendLine($"Mismatches ({mismatches.Count}):");
            foreach (var line in mismatches)
            {
                builder.AppendLine(line);
            }
        }

        return builder.ToString().TrimEnd();
    }

    public static Dictionary<string, string?> LoadBaselineTsv(string path)
    {
        var map = new Dictionary<string, string?>(StringComparer.OrdinalIgnoreCase);
        foreach (var raw in File.ReadAllLines(path))
        {
            if (raw.Length == 0 || raw.StartsWith('#') || raw.StartsWith("file\t", StringComparison.Ordinal))
            {
                continue;
            }

            var parts = raw.Split('\t');
            if (parts.Length < 1 || string.IsNullOrWhiteSpace(parts[0]))
            {
                continue;
            }

            var hint = parts.Length < 2 ? "" : parts[1].Trim();
            map[parts[0].Trim()] = string.IsNullOrEmpty(hint) || hint is "(none)" or "null"
                ? null
                : hint;
        }

        return map;
    }

    private static string FormatDump(LocationHintResult result)
    {
        var builder = new StringBuilder();
        builder.AppendLine("# Windows.Media.Ocr location hint — same full-image pass as dialog locate");
        builder.AppendLine($"# source: {result.SourcePath}");
        builder.AppendLine($"# image: {result.ImageWidth} x {result.ImageHeight}");
        builder.AppendLine($"# kind: {result.FrameKind}");
        builder.AppendLine($"# zone letter-words: {result.ZoneWordCount}");
        builder.AppendLine($"# hint: {result.Hint ?? "(none)"}");
        return builder.ToString();
    }

    private static string? Normalize(string? hint)
        => string.IsNullOrWhiteSpace(hint) ? null : hint.Trim();

    private static string Sanitize(string text)
        => text.Replace('\t', ' ').Replace('\r', ' ').Replace('\n', ' ');

    private static LocationHintResult Fail(string message) => new()
    {
        Success = false,
        ErrorMessage = message,
    };
}
