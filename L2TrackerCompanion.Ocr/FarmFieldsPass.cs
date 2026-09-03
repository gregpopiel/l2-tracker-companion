using System.Globalization;
using System.Text;
using Windows.Graphics.Imaging;
using Windows.Media.Ocr;
using L2TrackerCompanion.Parsing;

namespace L2TrackerCompanion.Ocr;

/// <summary>
/// Step 9: read XP and Adena from the dialog crop. Token bands around the
/// <c>adena</c> unit word, XP always spliced with a micro-crop, Adena
/// micro-crop only when the token band is empty.
/// </summary>
public static class FarmFieldsPass
{
    public const string DefaultFarmFolderName = "ocr-poc-farm";

    public static string GetDefaultFarmDirectory()
    {
        var directory = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            OcrWordDump.AppDataFolderName,
            DefaultFarmFolderName);
        Directory.CreateDirectory(directory);
        return directory;
    }

    public static async Task<FarmFieldsResult> RunFileAsync(
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

    public static async Task<FarmFieldsResult> ReadAsync(
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
        var tokens = FarmFields.ReadTokens(boxes);

        long? xpFromCrop = null;
        string? xpCropPath = null;
        if (tokens.XpTokens.Count > 0)
        {
            var xpRect = FarmFields.XpMicroCrop(tokens.XpTokens, cropWidth, cropHeight);
            if (!xpRect.IsEmpty)
            {
                var (text, pngPath) = await EnhanceAndRecognizeAsync(
                        dialog.CropBitmap,
                        dialog.Engine,
                        xpRect,
                        outputDirectory,
                        stem + "_xp-crop.png",
                        cancellationToken)
                    .ConfigureAwait(false);
                xpFromCrop = GameNumber.ParseLine(text);
                xpCropPath = pngPath;
            }
        }
        else if (tokens.Unit is not null)
        {
            var xpRect = FarmFields.XpBandCrop(tokens.Unit, tokens.Pitch, cropWidth, cropHeight);
            if (!xpRect.IsEmpty)
            {
                var (text, pngPath) = await EnhanceAndRecognizeAsync(
                        dialog.CropBitmap,
                        dialog.Engine,
                        xpRect,
                        outputDirectory,
                        stem + "_xp-crop.png",
                        cancellationToken)
                    .ConfigureAwait(false);
                xpFromCrop = GameNumber.ParseLine(text);
                xpCropPath = pngPath;
            }
        }

        var xpCombine = tokens.XpTokens.Count == 0
            ? new XpCombineResult(xpFromCrop ?? tokens.XpFromTokens, false, false, false)
            : XpReads.CombineDetailed(tokens.XpFromTokens, xpFromCrop);
        var xp = xpCombine.Value;

        long? adenaFromCrop = null;
        string? adenaCropPath = null;
        var usedFallback = false;

        // Always take the second opinion, not only when the token read failed.
        // OCR of an unchanged frame is deterministic, so a lone read can never
        // be re-confirmed by looking again later — the crop is the only
        // independent check Adena gets, and its figure goes straight to the API.
        if (tokens.Unit is not null)
        {
            var adenaRect = FarmFields.AdenaFallbackCrop(tokens.Unit, cropWidth, cropHeight);
            if (!adenaRect.IsEmpty)
            {
                usedFallback = tokens.AdenaFromTokens is null;

                // The crop is now read on every frame, but its debug PNG is
                // only worth writing when it is the figure being used — during
                // tracking that is a file write every 10s for nothing.
                var (text, pngPath) = await EnhanceAndRecognizeAsync(
                        dialog.CropBitmap,
                        dialog.Engine,
                        adenaRect,
                        outputDirectory,
                        usedFallback ? stem + "_adena-crop.png" : null,
                        cancellationToken)
                    .ConfigureAwait(false);
                adenaFromCrop = GameNumber.ParseLine(text);
                adenaCropPath = pngPath;
            }
        }

        var adena = tokens.AdenaFromTokens ?? adenaFromCrop;
        var adenaDisagreed = tokens.AdenaFromTokens is not null
            && adenaFromCrop is not null
            && tokens.AdenaFromTokens != adenaFromCrop;

        Directory.CreateDirectory(outputDirectory);
        var dumpPath = Path.Combine(outputDirectory, stem + ".txt");
        var result = new FarmFieldsResult
        {
            Success = true,
            SourcePath = dialog.SourcePath,
            DumpPath = Path.GetFullPath(dumpPath),
            XpCropPngPath = xpCropPath,
            AdenaCropPngPath = adenaCropPath,
            ImageWidth = dialog.ImageWidth,
            ImageHeight = dialog.ImageHeight,
            DialogCrop = dialog.Crop,
            AnchorKind = dialog.AnchorKind,
            AdenaUnit = tokens.Unit,
            Xp = xp,
            Adena = adena,
            XpFromTokens = tokens.XpFromTokens,
            XpFromCrop = xpFromCrop,
            AdenaFromTokens = tokens.AdenaFromTokens,
            AdenaFromCrop = adenaFromCrop,
            UsedAdenaFallback = usedFallback,
            AdenaDisagreed = adenaDisagreed,
            XpDisagreed = xpCombine.Disagreed,
            XpSpliced = xpCombine.Spliced,
            XpMagnitudeMismatch = xpCombine.MagnitudeMismatch,
            Pitch = tokens.Pitch,
            XpTokenTexts = tokens.XpTokens.Select(w => w.Text).ToArray(),
            AdenaTokenTexts = tokens.AdenaTokens.Select(w => w.Text).ToArray(),
        };

        await File.WriteAllTextAsync(dumpPath, FormatDump(result), Encoding.UTF8, cancellationToken)
            .ConfigureAwait(false);
        return result;
    }

    public static string FormatStatus(FarmFieldsResult result)
    {
        if (!result.Success)
        {
            return $"Farm fields failed: {result.ErrorMessage}";
        }

        var unit = result.AdenaUnit is null
            ? "none"
            : $"{result.AdenaUnit.Text} @ {FormatNumber(result.AdenaUnit.Left)},{FormatNumber(result.AdenaUnit.Top)}";
        return $"xp={FormatAmount(result.Xp)}  adena={FormatAmount(result.Adena)}  "
            + $"unit={unit}  fallback={YesNo(result.UsedAdenaFallback)}  "
            + $"xpTokens={result.XpFromTokens?.ToString(CultureInfo.InvariantCulture) ?? "null"}  "
            + $"xpCrop={result.XpFromCrop?.ToString(CultureInfo.InvariantCulture) ?? "null"}";
    }

    public static string FormatBatchSummary(IReadOnlyList<FarmFieldsResult> results)
    {
        var builder = new StringBuilder();
        builder.AppendLine("# Windows.Media.Ocr farm fields — XP + Adena");
        builder.AppendLine($"# files: {results.Count}");
        builder.AppendLine($"# succeeded: {results.Count(r => r.Success)}");
        builder.AppendLine($"# failed: {results.Count(r => !r.Success)}");
        builder.AppendLine($"# XP read: {results.Count(r => r.Xp is not null)}/{results.Count}");
        builder.AppendLine($"# Adena read: {results.Count(r => r.Adena is not null)}/{results.Count}");
        builder.AppendLine($"# Adena fallback used: {results.Count(r => r.UsedAdenaFallback)}/{results.Count}");
        builder.AppendLine("file\tkind\twidth\theight\tanchor\tunit_x\tunit_y\txp\tadena\txp_tokens\txp_crop\tadena_tokens\tadena_crop\tfallback");
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
            builder.Append(result.AdenaUnit is null ? "" : FormatNumber(result.AdenaUnit.Left));
            builder.Append('\t');
            builder.Append(result.AdenaUnit is null ? "" : FormatNumber(result.AdenaUnit.Top));
            builder.Append('\t');
            builder.Append(FormatAmount(result.Xp));
            builder.Append('\t');
            builder.Append(FormatAmount(result.Adena));
            builder.Append('\t');
            builder.Append(FormatAmount(result.XpFromTokens));
            builder.Append('\t');
            builder.Append(FormatAmount(result.XpFromCrop));
            builder.Append('\t');
            builder.Append(FormatAmount(result.AdenaFromTokens));
            builder.Append('\t');
            builder.Append(FormatAmount(result.AdenaFromCrop));
            builder.Append('\t');
            builder.AppendLine(YesNo(result.UsedAdenaFallback));
        }

        return builder.ToString();
    }

    public static string FormatBatchStatus(IReadOnlyList<FarmFieldsResult> results)
    {
        var ok = results.Count(r => r.Success);
        var xp = results.Count(r => r.Xp is not null);
        var adena = results.Count(r => r.Adena is not null);
        return $"Farm batch: {ok}/{results.Count} wrote, XP {xp}/{results.Count}, Adena {adena}/{results.Count}.";
    }

    public static string FormatBaselineComparison(
        IReadOnlyList<FarmFieldsResult> results,
        IReadOnlyDictionary<string, (long? Xp, long? Adena)> baseline)
    {
        var xpMatch = 0;
        var adenaMatch = 0;
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
            var xpOk = result.Xp == expected.Xp;
            var adenaOk = result.Adena == expected.Adena;
            if (xpOk)
            {
                xpMatch++;
            }

            if (adenaOk)
            {
                adenaMatch++;
            }

            if (!xpOk || !adenaOk)
            {
                mismatches.Add(
                    $"{name}\txp ours={FormatAmount(result.Xp)} theirs={FormatAmount(expected.Xp)}"
                    + $"\tadena ours={FormatAmount(result.Adena)} theirs={FormatAmount(expected.Adena)}");
            }
        }

        var builder = new StringBuilder();
        builder.AppendLine($"vs tesseract.js: XP {xpMatch}/{compared}, Adena {adenaMatch}/{compared}.");
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

    public static Dictionary<string, (long? Xp, long? Adena)> LoadBaselineTsv(string path)
    {
        var map = new Dictionary<string, (long? Xp, long? Adena)>(StringComparer.OrdinalIgnoreCase);
        foreach (var raw in File.ReadAllLines(path))
        {
            var line = raw.Trim();
            if (line.Length == 0 || line.StartsWith('#') || line.StartsWith("file\t", StringComparison.Ordinal))
            {
                continue;
            }

            var parts = line.Split('\t');
            if (parts.Length < 3)
            {
                continue;
            }

            map[parts[0]] = (ParseBaselineAmount(parts[1]), ParseBaselineAmount(parts[2]));
        }

        return map;
    }

    /// <param name="fileName">
    /// Debug PNG to write beside the dump, or <c>null</c> to recognise without
    /// leaving a file behind.
    /// </param>
    private static async Task<(string Text, string? PngPath)> EnhanceAndRecognizeAsync(
        SoftwareBitmap source,
        OcrEngine engine,
        CropRect crop,
        string outputDirectory,
        string? fileName,
        CancellationToken cancellationToken)
    {
        using var enhanced = await ImageEnhance.CropAndEnhanceAsync(
                source,
                crop,
                FarmFields.EnhanceTargetHeight,
                cancellationToken)
            .ConfigureAwait(false);

        string? pngPath = null;
        if (fileName is not null)
        {
            Directory.CreateDirectory(outputDirectory);
            pngPath = Path.GetFullPath(Path.Combine(outputDirectory, fileName));
            await OcrRecognize.SavePngAsync(enhanced, pngPath, cancellationToken).ConfigureAwait(false);
        }

        var recognized = await engine.RecognizeAsync(enhanced).AsTask(cancellationToken).ConfigureAwait(false);
        return (OcrRecognize.JoinRecognizedText(recognized), pngPath);
    }

    private static string FormatDump(FarmFieldsResult result)
    {
        var builder = new StringBuilder();
        builder.AppendLine("# Windows.Media.Ocr farm fields — XP + Adena");
        builder.AppendLine($"# source: {result.SourcePath}");
        builder.AppendLine($"# image: {result.ImageWidth} x {result.ImageHeight}");
        builder.AppendLine($"# dialog crop: {result.DialogCrop.Left},{result.DialogCrop.Top} {result.DialogCrop.Width}x{result.DialogCrop.Height}");
        builder.AppendLine($"# anchor: {result.AnchorKind ?? "none"}");
        builder.AppendLine($"# adena unit: {(result.AdenaUnit is null ? "none" : result.AdenaUnit.Text)}");
        builder.AppendLine($"# pitch: {result.Pitch?.ToString("0.###", CultureInfo.InvariantCulture) ?? "null"}");
        builder.AppendLine($"# xp tokens: {string.Join(" ", result.XpTokenTexts)}");
        builder.AppendLine($"# adena tokens: {string.Join(" ", result.AdenaTokenTexts)}");
        builder.AppendLine($"# xp from tokens: {FormatAmount(result.XpFromTokens)}");
        builder.AppendLine($"# xp from crop: {FormatAmount(result.XpFromCrop)}");
        builder.AppendLine($"# xp: {FormatAmount(result.Xp)}");
        builder.AppendLine($"# adena from tokens: {FormatAmount(result.AdenaFromTokens)}");
        builder.AppendLine($"# adena from crop: {FormatAmount(result.AdenaFromCrop)}");
        builder.AppendLine($"# adena fallback used: {YesNo(result.UsedAdenaFallback)}");
        builder.AppendLine($"# adena: {FormatAmount(result.Adena)}");
        if (result.XpCropPngPath is not null)
        {
            builder.AppendLine($"# xp crop png: {result.XpCropPngPath}");
        }

        if (result.AdenaCropPngPath is not null)
        {
            builder.AppendLine($"# adena crop png: {result.AdenaCropPngPath}");
        }

        return builder.ToString();
    }

    private static long? ParseBaselineAmount(string text)
    {
        if (string.IsNullOrWhiteSpace(text) || text is "null" or "FAILED")
        {
            return null;
        }

        var compact = text.Replace(",", "", StringComparison.Ordinal).Replace(" ", "", StringComparison.Ordinal);
        return long.TryParse(compact, NumberStyles.Integer, CultureInfo.InvariantCulture, out var value)
            ? value
            : null;
    }

    private static string FormatAmount(long? value)
        => value?.ToString(CultureInfo.InvariantCulture) ?? "null";

    private static string FormatNumber(double value)
        => value.ToString("0.###", CultureInfo.InvariantCulture);

    private static string Sanitize(string text)
        => text.Replace('\t', ' ').Replace('\r', ' ').Replace('\n', ' ');

    private static string YesNo(bool value) => value ? "yes" : "no";

    private static FarmFieldsResult Fail(string message) => new()
    {
        Success = false,
        ErrorMessage = message,
    };
}
