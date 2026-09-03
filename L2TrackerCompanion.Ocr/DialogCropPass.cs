using System.Globalization;
using System.Text;
using L2TrackerCompanion.Parsing;
using Windows.Graphics.Imaging;

namespace L2TrackerCompanion.Ocr;

/// <summary>
/// Step 8: full-image OCR to locate the dialog, fixed-pixel crop, second OCR
/// pass on the crop. No XP/Adena/lamp-XP parsing — this only checks the crop
/// actually contains the dialog (and the lamp table when it was in frame).
/// </summary>
public static class DialogCropPass
{
    public const string DefaultCropFolderName = "ocr-poc-crops";

    public static string GetDefaultCropDirectory()
    {
        var directory = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            OcrWordDump.AppDataFolderName,
            DefaultCropFolderName);
        Directory.CreateDirectory(directory);
        return directory;
    }

    public static async Task<DialogCropResult> RunFileAsync(
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
            using var recognized = await RecognizeAsync(imagePath, cancellationToken).ConfigureAwait(false);
            if (!recognized.Success || recognized.CropBitmap is null || recognized.Engine is null)
            {
                return Fail(recognized.ErrorMessage ?? "Dialog crop failed");
            }

            Directory.CreateDirectory(outputDirectory);
            var stem = Path.GetFileNameWithoutExtension(imagePath);
            var cropPngPath = Path.Combine(outputDirectory, stem + ".png");
            var cropDumpPath = Path.Combine(outputDirectory, stem + ".txt");

            await OcrRecognize.SavePngAsync(recognized.CropBitmap, cropPngPath, cancellationToken)
                .ConfigureAwait(false);

            var result = ToDumpResult(recognized, cropPngPath, cropDumpPath);
            await File.WriteAllTextAsync(cropDumpPath, FormatDump(result), Encoding.UTF8, cancellationToken)
                .ConfigureAwait(false);

            return result;
        }
        catch (Exception ex)
        {
            return Fail(ex.Message);
        }
    }

    /// <summary>
    /// Locate the dialog, crop, second OCR. The crop bitmap stays alive for
    /// farm-field micro-crops — caller must dispose.
    /// </summary>
    public static async Task<DialogCropRecognition> RecognizeAsync(
        string imagePath,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(imagePath) || !File.Exists(imagePath))
        {
            return new DialogCropRecognition
            {
                Success = false,
                ErrorMessage = $"PNG not found: {imagePath}",
            };
        }

        var bytes = await File.ReadAllBytesAsync(imagePath, cancellationToken).ConfigureAwait(false);
        using var stream = await OcrRecognize.CreateStreamAsync(bytes).ConfigureAwait(false);
        var decoder = await BitmapDecoder.CreateAsync(stream).AsTask(cancellationToken).ConfigureAwait(false);

        using var fullBitmap = await decoder.GetSoftwareBitmapAsync().AsTask(cancellationToken).ConfigureAwait(false);
        using var preparedFull = OcrRecognize.PrepareForOcr(fullBitmap);

        var engine = OcrRecognize.SharedEngine;
        var fullRecognized = await engine.RecognizeAsync(preparedFull).AsTask(cancellationToken).ConfigureAwait(false);
        var fullWords = OcrRecognize.ToWords(fullRecognized);

        var imageWidth = (int)decoder.PixelWidth;
        var imageHeight = (int)decoder.PixelHeight;
        var boxes = fullWords.Select(w => new WordBox(w.Text, w.X, w.Y, w.Width, w.Height));
        var anchor = DialogCrop.FindAnchor(boxes);

        CropRect cropRect;
        string? anchorKind;
        if (anchor is null)
        {
            // Same fallback as screenshotOcr.js: no title → keep the full frame
            // rather than inventing a crop. The second pass still runs on that
            // frame so the caller sees one consistent crop-word list.
            cropRect = new CropRect(0, 0, imageWidth, imageHeight);
            anchorKind = null;
        }
        else
        {
            cropRect = DialogCrop.Rect(anchor, imageWidth, imageHeight);
            anchorKind = anchor.Kind;
        }

        if (cropRect.IsEmpty)
        {
            return new DialogCropRecognition
            {
                Success = false,
                ErrorMessage = "Dialog crop rectangle is empty",
                SourcePath = Path.GetFullPath(imagePath),
                Engine = engine,
                FullWords = fullWords,
                ImageWidth = decoder.PixelWidth,
                ImageHeight = decoder.PixelHeight,
            };
        }

        var bounds = new BitmapBounds
        {
            X = (uint)cropRect.Left,
            Y = (uint)cropRect.Top,
            Width = (uint)cropRect.Width,
            Height = (uint)cropRect.Height,
        };
        var transform = new BitmapTransform { Bounds = bounds };
        using var cropped = await decoder.GetSoftwareBitmapAsync(
                BitmapPixelFormat.Bgra8,
                BitmapAlphaMode.Premultiplied,
                transform,
                ExifOrientationMode.IgnoreExifOrientation,
                ColorManagementMode.DoNotColorManage)
            .AsTask(cancellationToken)
            .ConfigureAwait(false);
        var preparedCrop = OcrRecognize.PrepareForOcr(cropped);

        var cropRecognized = await engine.RecognizeAsync(preparedCrop).AsTask(cancellationToken).ConfigureAwait(false);
        var cropWords = OcrRecognize.ToWords(cropRecognized);

        return new DialogCropRecognition
        {
            Success = true,
            SourcePath = Path.GetFullPath(imagePath),
            CropBitmap = preparedCrop,
            Engine = engine,
            Crop = cropRect,
            AnchorKind = anchorKind,
            ImageWidth = decoder.PixelWidth,
            ImageHeight = decoder.PixelHeight,
            FullWords = fullWords,
            CropWords = cropWords,
            CropLineCount = cropRecognized.Lines.Count,
        };
    }

    private static DialogCropResult ToDumpResult(
        DialogCropRecognition recognized,
        string cropPngPath,
        string cropDumpPath)
    {
        var cropWords = recognized.CropWords;
        var fullWords = recognized.FullWords;
        return new DialogCropResult
        {
            Success = true,
            SourcePath = recognized.SourcePath,
            CropPngPath = Path.GetFullPath(cropPngPath),
            CropDumpPath = Path.GetFullPath(cropDumpPath),
            ImageWidth = recognized.ImageWidth,
            ImageHeight = recognized.ImageHeight,
            Language = recognized.Engine?.RecognizerLanguage.LanguageTag,
            AnchorKind = recognized.AnchorKind,
            Crop = recognized.Crop,
            FullWords = fullWords,
            CropWords = cropWords,
            CropLineCount = recognized.CropLineCount,
            CropHasPlay = OcrRecognize.ContainsWord(cropWords, "Play")
                || OcrRecognize.ContainsWord(cropWords, "PlayReport"),
            CropHasReport = OcrRecognize.ContainsWord(cropWords, "Report")
                || OcrRecognize.ContainsWord(cropWords, "PlayReport"),
            CropHasCharacters = OcrRecognize.ContainsWord(cropWords, "Characters"),
            CropHasAdena = OcrRecognize.ContainsWord(cropWords, "adena"),
            CropLampColors = OcrRecognize.FoundLampColors(cropWords),
            FullLampColors = OcrRecognize.FoundLampColors(fullWords),
        };
    }

    public static string FormatStatus(DialogCropResult result)
    {
        if (!result.Success)
        {
            return $"Dialog crop failed: {result.ErrorMessage}";
        }

        var lamps = result.CropLampColors.Count == 0
            ? "(none)"
            : string.Join(", ", result.CropLampColors);
        var dialog = result.DialogContained ? "yes" : "no";
        var anchor = result.AnchorKind ?? "none (full frame)";
        return $"anchor={anchor} crop={result.Crop.Width}x{result.Crop.Height} "
            + $"@ {result.Crop.Left},{result.Crop.Top}  dialog={dialog}  "
            + $"adena={YesNo(result.CropHasAdena)}  Characters={YesNo(result.CropHasCharacters)}  "
            + $"lamps={lamps}\nWrote {result.CropPngPath}";
    }

    public static string FormatBatchSummary(IReadOnlyList<DialogCropResult> results)
    {
        var builder = new StringBuilder();
        builder.AppendLine("# Windows.Media.Ocr dialog crop — locate + second pass, no parsing");
        builder.AppendLine($"# files: {results.Count}");
        builder.AppendLine($"# succeeded: {results.Count(r => r.Success)}");
        builder.AppendLine($"# failed: {results.Count(r => !r.Success)}");
        builder.AppendLine($"# dialog contained: {results.Count(r => r.Success && r.DialogContained)}/{results.Count}");
        builder.AppendLine($"# lamps in crop: {results.Count(r => r.Success && r.LampTableInCrop)}/{results.Count}");

        foreach (var kind in new[] { "dialog", "framed", "desktop" })
        {
            var group = results.Where(r => r.Success && r.FrameKind == kind).ToList();
            if (group.Count == 0)
            {
                continue;
            }

            builder.AppendLine(
                $"# {kind} n={group.Count} "
                + $"anchor={group.Count(r => r.AnchorKind is not null)} "
                + $"dialog={group.Count(r => r.DialogContained)} "
                + $"adena={group.Count(r => r.CropHasAdena)} "
                + $"lamps={group.Count(r => r.LampTableInCrop)} "
                + $"Report={group.Count(r => r.AnchorKind == "Report")} "
                + $"Characters={group.Count(r => r.AnchorKind == "Characters")}");
        }

        builder.AppendLine(
            "file\tkind\twidth\theight\tanchor\tcrop_x\tcrop_y\tcrop_w\tcrop_h\t"
            + "crop_words\tadena\tCharacters\tlamps\tdialog\tlamp_table");
        foreach (var result in results)
        {
            var name = result.SourcePath is null ? "" : Path.GetFileName(result.SourcePath);
            var lamps = result.CropLampColors.Count == 0
                ? "(none)"
                : string.Join(",", result.CropLampColors);
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
            builder.Append(result.Crop.Left.ToString(CultureInfo.InvariantCulture));
            builder.Append('\t');
            builder.Append(result.Crop.Top.ToString(CultureInfo.InvariantCulture));
            builder.Append('\t');
            builder.Append(result.Crop.Width.ToString(CultureInfo.InvariantCulture));
            builder.Append('\t');
            builder.Append(result.Crop.Height.ToString(CultureInfo.InvariantCulture));
            builder.Append('\t');
            builder.Append(result.CropWords.Count.ToString(CultureInfo.InvariantCulture));
            builder.Append('\t');
            builder.Append(YesNo(result.CropHasAdena));
            builder.Append('\t');
            builder.Append(YesNo(result.CropHasCharacters));
            builder.Append('\t');
            builder.Append(lamps);
            builder.Append('\t');
            builder.Append(YesNo(result.DialogContained));
            builder.Append('\t');
            builder.AppendLine(YesNo(result.LampTableInCrop));
        }

        return builder.ToString();
    }

    public static string FormatBatchStatus(IReadOnlyList<DialogCropResult> results)
    {
        var ok = results.Count(r => r.Success);
        var dialog = results.Count(r => r.Success && r.DialogContained);
        var lines = new List<string>
        {
            $"Crop batch: {ok}/{results.Count} wrote, dialog contained {dialog}/{results.Count}.",
        };
        foreach (var kind in new[] { "dialog", "framed", "desktop" })
        {
            var group = results.Where(r => r.Success && r.FrameKind == kind).ToList();
            if (group.Count == 0)
            {
                continue;
            }

            lines.Add(
                $"{kind}: {group.Count}  dialog {group.Count(r => r.DialogContained)}/{group.Count}  "
                + $"lamps {group.Count(r => r.LampTableInCrop)}/{group.Count}  "
                + $"anchor Report {group.Count(r => r.AnchorKind == "Report")} "
                + $"Characters {group.Count(r => r.AnchorKind == "Characters")}");
        }

        return string.Join("\n", lines);
    }

    private static string FormatDump(DialogCropResult result)
    {
        var lamps = result.CropLampColors.Count == 0
            ? "(none)"
            : string.Join(", ", result.CropLampColors);

        var builder = new StringBuilder();
        builder.AppendLine("# Windows.Media.Ocr dialog crop — second pass, no parsing");
        builder.AppendLine($"# source: {result.SourcePath}");
        builder.AppendLine($"# image: {result.ImageWidth} x {result.ImageHeight}");
        builder.AppendLine($"# language: {result.Language}");
        builder.AppendLine($"# anchor: {result.AnchorKind ?? "none (full frame)"}");
        builder.AppendLine(
            $"# crop: {result.Crop.Left},{result.Crop.Top} {result.Crop.Width}x{result.Crop.Height}");
        builder.AppendLine($"# crop png: {result.CropPngPath}");
        builder.AppendLine($"# crop lines: {result.CropLineCount}");
        builder.AppendLine($"# crop words: {result.CropWords.Count}");
        builder.AppendLine($"# crop Play: {YesNo(result.CropHasPlay)}");
        builder.AppendLine($"# crop Report: {YesNo(result.CropHasReport)}");
        builder.AppendLine($"# crop Characters: {YesNo(result.CropHasCharacters)}");
        builder.AppendLine($"# crop adena: {YesNo(result.CropHasAdena)}");
        builder.AppendLine($"# crop lamp colors: {lamps}");
        builder.AppendLine($"# dialog contained: {YesNo(result.DialogContained)}");
        builder.AppendLine("line\tword\tx\ty\twidth\theight\ttext");

        foreach (var word in result.CropWords)
        {
            builder.Append(word.LineIndex.ToString(CultureInfo.InvariantCulture));
            builder.Append('\t');
            builder.Append(word.WordIndex.ToString(CultureInfo.InvariantCulture));
            builder.Append('\t');
            builder.Append(FormatNumber(word.X));
            builder.Append('\t');
            builder.Append(FormatNumber(word.Y));
            builder.Append('\t');
            builder.Append(FormatNumber(word.Width));
            builder.Append('\t');
            builder.Append(FormatNumber(word.Height));
            builder.Append('\t');
            builder.AppendLine(Sanitize(word.Text));
        }

        return builder.ToString();
    }

    private static string FormatNumber(double value)
        => value.ToString("0.###", CultureInfo.InvariantCulture);

    private static string Sanitize(string text)
        => text.Replace('\t', ' ').Replace('\r', ' ').Replace('\n', ' ');

    private static string YesNo(bool value) => value ? "yes" : "no";

    private static DialogCropResult Fail(string message) => new()
    {
        Success = false,
        ErrorMessage = message,
    };
}
