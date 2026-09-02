using System.Globalization;
using System.Text;
using Windows.Graphics.Imaging;
using Windows.Media.Ocr;
using L2TrackerCompanion.Parsing;

namespace L2TrackerCompanion.Ocr;

/// <summary>
/// Step 10: read total play time from the dialog crop. Dual-read of the
/// token band below the "time" label and a micro-crop of that band; refuse
/// when the two parses contradict or hours/minutes are out of range.
/// </summary>
public static class PlayTimePass
{
    public const string DefaultPlayTimeFolderName = "ocr-poc-playtime";

    public static string GetDefaultPlayTimeDirectory()
    {
        var directory = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            OcrWordDump.AppDataFolderName,
            DefaultPlayTimeFolderName);
        Directory.CreateDirectory(directory);
        return directory;
    }

    public static async Task<PlayTimeResult> RunFileAsync(
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
            if (!dialog.Success || dialog.CropBitmap is null || dialog.Engine is null)
            {
                return Fail(dialog.ErrorMessage ?? "Dialog crop failed");
            }

            return await ReadAsync(
                    dialog,
                    outputDirectory,
                    Path.GetFileNameWithoutExtension(imagePath),
                    cancellationToken)
                .ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            return Fail(ex.Message);
        }
    }

    public static async Task<PlayTimeResult> ReadAsync(
        DialogCropRecognition dialog,
        string outputDirectory,
        string stem,
        CancellationToken cancellationToken)
    {
        if (dialog.CropBitmap is null || dialog.Engine is null)
        {
            return Fail(dialog.ErrorMessage ?? "Dialog crop failed");
        }

        var cropWidth = dialog.CropBitmap.PixelWidth;
        var cropHeight = dialog.CropBitmap.PixelHeight;
        var boxes = OcrRecognize.ToWordBoxes(dialog.CropWords);
        var tokens = PlayTime.ReadTokens(boxes);

        int? fromCrop = null;
        string? cropPngPath = null;
        string? cropText = null;
        var valueCrop = PlayTime.CombinedValueCrop(tokens, cropWidth, cropHeight);
        if (!valueCrop.IsEmpty)
        {
            var (text, pngPath) = await EnhanceAndRecognizeAsync(
                    dialog.CropBitmap,
                    dialog.Engine,
                    valueCrop,
                    outputDirectory,
                    stem + "_time-crop.png",
                    cancellationToken)
                .ConfigureAwait(false);
            cropText = text;
            fromCrop = PlayTime.ParseMinutes(text);
            cropPngPath = pngPath;
        }

        var combined = PlayTime.Combine(fromCrop, tokens.FromTokens);
        var refused = fromCrop is not null
            && tokens.FromTokens is not null
            && fromCrop != tokens.FromTokens;

        Directory.CreateDirectory(outputDirectory);
        var dumpPath = Path.Combine(outputDirectory, stem + ".txt");
        var result = new PlayTimeResult
        {
            Success = true,
            SourcePath = dialog.SourcePath,
            DumpPath = Path.GetFullPath(dumpPath),
            CropPngPath = cropPngPath,
            ImageWidth = dialog.ImageWidth,
            ImageHeight = dialog.ImageHeight,
            DialogCrop = dialog.Crop,
            AnchorKind = dialog.AnchorKind,
            TimeAnchor = tokens.Anchor,
            Minutes = combined,
            FromTokens = tokens.FromTokens,
            FromCrop = fromCrop,
            RefusedContradiction = refused,
            ValueCrop = valueCrop,
            ValueTokenTexts = tokens.ValueTokens.Select(w => w.Text).ToArray(),
            CropText = cropText,
        };

        await File.WriteAllTextAsync(dumpPath, FormatDump(result), Encoding.UTF8, cancellationToken)
            .ConfigureAwait(false);
        return result;
    }

    public static string FormatStatus(PlayTimeResult result)
    {
        if (!result.Success)
        {
            return $"Play time failed: {result.ErrorMessage}";
        }

        var anchor = result.TimeAnchor is null
            ? "none"
            : $"{result.TimeAnchor.Text} @ {FormatNumber(result.TimeAnchor.Left)},{FormatNumber(result.TimeAnchor.Top)}";
        return $"minutes={FormatMinutes(result.Minutes)}  "
            + $"tokens={FormatMinutes(result.FromTokens)}  "
            + $"crop={FormatMinutes(result.FromCrop)}  "
            + $"anchor={anchor}  refused={YesNo(result.RefusedContradiction)}";
    }

    public static string FormatBatchSummary(IReadOnlyList<PlayTimeResult> results)
    {
        var builder = new StringBuilder();
        builder.AppendLine("# Windows.Media.Ocr play time — dual-read tokens + line crop");
        builder.AppendLine($"# files: {results.Count}");
        builder.AppendLine($"# succeeded: {results.Count(r => r.Success)}");
        builder.AppendLine($"# failed: {results.Count(r => !r.Success)}");
        builder.AppendLine($"# minutes read: {results.Count(r => r.Minutes is not null)}/{results.Count}");
        builder.AppendLine($"# tokens parsed: {results.Count(r => r.FromTokens is not null)}/{results.Count}");
        builder.AppendLine($"# crop parsed: {results.Count(r => r.FromCrop is not null)}/{results.Count}");
        builder.AppendLine($"# contradiction refused: {results.Count(r => r.RefusedContradiction)}/{results.Count}");
        builder.AppendLine("file\tkind\twidth\theight\tanchor\ttime_x\ttime_y\tminutes\ttokens\tcrop\trefused\tcrop_text");
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
            builder.Append(result.AnchorKind ?? "none");
            builder.Append('\t');
            builder.Append(result.TimeAnchor is null ? "" : FormatNumber(result.TimeAnchor.Left));
            builder.Append('\t');
            builder.Append(result.TimeAnchor is null ? "" : FormatNumber(result.TimeAnchor.Top));
            builder.Append('\t');
            builder.Append(FormatMinutes(result.Minutes));
            builder.Append('\t');
            builder.Append(FormatMinutes(result.FromTokens));
            builder.Append('\t');
            builder.Append(FormatMinutes(result.FromCrop));
            builder.Append('\t');
            builder.Append(YesNo(result.RefusedContradiction));
            builder.Append('\t');
            builder.AppendLine(Sanitize(result.CropText ?? ""));
        }

        return builder.ToString();
    }

    public static string FormatBatchStatus(IReadOnlyList<PlayTimeResult> results)
    {
        var ok = results.Count(r => r.Success);
        var minutes = results.Count(r => r.Minutes is not null);
        var refused = results.Count(r => r.RefusedContradiction);
        return $"Play-time batch: {ok}/{results.Count} wrote, minutes {minutes}/{results.Count}"
            + $" (contradiction refused {refused}).";
    }

    public static string FormatBaselineComparison(
        IReadOnlyList<PlayTimeResult> results,
        IReadOnlyDictionary<string, int?> baseline)
    {
        var match = 0;
        var oursReadable = 0;
        var theirsReadable = 0;
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
            if (result.Minutes is not null)
            {
                oursReadable++;
            }

            if (expected is not null)
            {
                theirsReadable++;
            }

            if (result.Minutes == expected)
            {
                match++;
            }
            else
            {
                mismatches.Add(
                    $"{name}\tours={FormatMinutes(result.Minutes)} theirs={FormatMinutes(expected)}"
                    + $"\ttokens={FormatMinutes(result.FromTokens)} crop={FormatMinutes(result.FromCrop)}"
                    + (result.RefusedContradiction ? "\trefused-contradiction" : ""));
            }
        }

        var builder = new StringBuilder();
        builder.AppendLine(
            $"vs tesseract.js: exact {match}/{compared}, "
            + $"readable ours {oursReadable}/{compared} theirs {theirsReadable}/{compared}.");
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

    public static Dictionary<string, int?> LoadBaselineTsv(string path)
    {
        var map = new Dictionary<string, int?>(StringComparer.OrdinalIgnoreCase);
        foreach (var raw in File.ReadAllLines(path))
        {
            var line = raw.Trim();
            if (line.Length == 0 || line.StartsWith('#') || line.StartsWith("file\t", StringComparison.Ordinal))
            {
                continue;
            }

            var parts = line.Split('\t');
            if (parts.Length < 2)
            {
                continue;
            }

            map[parts[0]] = ParseBaselineMinutes(parts[1]);
        }

        return map;
    }

    private static async Task<(string Text, string PngPath)> EnhanceAndRecognizeAsync(
        SoftwareBitmap source,
        OcrEngine engine,
        CropRect crop,
        string outputDirectory,
        string fileName,
        CancellationToken cancellationToken)
    {
        using var enhanced = await ImageEnhance.CropAndEnhanceAsync(
                source,
                crop,
                PlayTime.EnhanceTargetHeight,
                cancellationToken)
            .ConfigureAwait(false);
        Directory.CreateDirectory(outputDirectory);
        var pngPath = Path.GetFullPath(Path.Combine(outputDirectory, fileName));
        await OcrRecognize.SavePngAsync(enhanced, pngPath, cancellationToken).ConfigureAwait(false);

        var recognized = await engine.RecognizeAsync(enhanced).AsTask(cancellationToken).ConfigureAwait(false);
        return (OcrRecognize.JoinRecognizedText(recognized), pngPath);
    }

    private static string FormatDump(PlayTimeResult result)
    {
        var builder = new StringBuilder();
        builder.AppendLine("# Windows.Media.Ocr play time — dual-read tokens + line crop");
        builder.AppendLine($"# source: {result.SourcePath}");
        builder.AppendLine($"# image: {result.ImageWidth} x {result.ImageHeight}");
        builder.AppendLine($"# dialog crop: {result.DialogCrop.Left},{result.DialogCrop.Top} {result.DialogCrop.Width}x{result.DialogCrop.Height}");
        builder.AppendLine($"# locate anchor: {result.AnchorKind ?? "none"}");
        builder.AppendLine($"# time anchor: {(result.TimeAnchor is null ? "none" : result.TimeAnchor.Text)}");
        builder.AppendLine($"# value tokens: {string.Join(" ", result.ValueTokenTexts)}");
        builder.AppendLine($"# from tokens: {FormatMinutes(result.FromTokens)}");
        builder.AppendLine($"# crop text: {Sanitize(result.CropText ?? "")}");
        builder.AppendLine($"# from crop: {FormatMinutes(result.FromCrop)}");
        builder.AppendLine($"# contradiction refused: {YesNo(result.RefusedContradiction)}");
        builder.AppendLine($"# minutes: {FormatMinutes(result.Minutes)}");
        if (!result.ValueCrop.IsEmpty)
        {
            builder.AppendLine(
                $"# value crop: {result.ValueCrop.Left},{result.ValueCrop.Top} "
                + $"{result.ValueCrop.Width}x{result.ValueCrop.Height}");
        }

        if (result.CropPngPath is not null)
        {
            builder.AppendLine($"# time crop png: {result.CropPngPath}");
        }

        return builder.ToString();
    }

    private static int? ParseBaselineMinutes(string text)
    {
        if (string.IsNullOrWhiteSpace(text) || text is "null" or "FAILED")
        {
            return null;
        }

        return int.TryParse(text, NumberStyles.Integer, CultureInfo.InvariantCulture, out var value)
            ? value
            : null;
    }

    private static string FormatMinutes(int? value)
        => value?.ToString(CultureInfo.InvariantCulture) ?? "null";

    private static string FormatNumber(double value)
        => value.ToString("0.###", CultureInfo.InvariantCulture);

    private static string Sanitize(string text)
        => text.Replace('\t', ' ').Replace('\r', ' ').Replace('\n', ' ');

    private static string YesNo(bool value) => value ? "yes" : "no";

    private static PlayTimeResult Fail(string message) => new()
    {
        Success = false,
        ErrorMessage = message,
    };
}
