using System.Globalization;
using System.Text;
using L2TrackerCompanion.Parsing;

namespace L2TrackerCompanion.Ocr;

/// <summary>
/// Step 13: one-shot Play Report pipeline. One full-image locate, then farm
/// fields, play time, lamp XP and the minimap hint. No polling loop, no API.
/// </summary>
public static class PlayReportPipeline
{
    public const string DefaultParseFolderName = "ocr-poc-parse";

    public static string GetDefaultParseDirectory()
    {
        var directory = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            OcrWordDump.AppDataFolderName,
            DefaultParseFolderName);
        Directory.CreateDirectory(directory);
        return directory;
    }

    public static async Task<PlayReportResult> RunFileAsync(
        string imagePath,
        CancellationToken cancellationToken = default)
    {
        return await RunFileAsync(imagePath, GetDefaultParseDirectory(), cancellationToken)
            .ConfigureAwait(false);
    }

    public static async Task<PlayReportResult> RunFileAsync(
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

            var stem = Path.GetFileNameWithoutExtension(imagePath);
            Directory.CreateDirectory(outputDirectory);

            var farm = await FarmFieldsPass.ReadAsync(dialog, outputDirectory, stem + "-farm", cancellationToken)
                .ConfigureAwait(false);
            var play = await PlayTimePass.ReadAsync(dialog, outputDirectory, stem + "-time", cancellationToken)
                .ConfigureAwait(false);
            var lamps = await LampXpPass.ReadAsync(
                    dialog,
                    outputDirectory,
                    stem + "-lamps",
                    cancellationToken,
                    farm)
                .ConfigureAwait(false);

            var hint = LocationHint.Read(
                OcrRecognize.ToWordBoxes(dialog.FullWords),
                (int)dialog.ImageWidth,
                (int)dialog.ImageHeight);

            var decision = new LampXpDecision(
                lamps.LampXpRead,
                lamps.LampPanelClosed,
                lamps.ExceedsDialogXp,
                lamps.LampXpTotal,
                lamps.DialogColors.Count > 0,
                lamps.Red,
                lamps.Purple,
                lamps.Blue,
                lamps.Green);

            var confidence = new ReadConfidence(
                XpDisagreed: farm.XpDisagreed,
                XpSpliced: farm.XpSpliced,
                XpMagnitudeMismatch: farm.XpMagnitudeMismatch,
                AdenaDisagreed: farm.AdenaDisagreed,
                PlayTimeDisagreed: play.RefusedContradiction,
                XpFromTokens: farm.XpFromTokens,
                XpFromCrop: farm.XpFromCrop,
                AdenaFromTokens: farm.AdenaFromTokens,
                AdenaFromCrop: farm.AdenaFromCrop);

            var report = PlayReport.From(farm.Xp, farm.Adena, play.Minutes, decision, hint, confidence);
            return new PlayReportResult
            {
                Success = true,
                SourcePath = Path.GetFullPath(imagePath),
                ImageWidth = dialog.ImageWidth,
                ImageHeight = dialog.ImageHeight,
                Report = report,
            };
        }
        catch (Exception ex)
        {
            return Fail(ex.Message);
        }
    }

    public static string FormatWindow(PlayReportResult result)
    {
        if (!result.Success || result.Report is null)
        {
            return $"Parse failed: {result.ErrorMessage}";
        }

        var report = result.Report;
        var inv = CultureInfo.InvariantCulture;
        var builder = new StringBuilder();
        if (result.SourcePath is not null)
        {
            builder.AppendLine(Path.GetFileName(result.SourcePath));
        }

        builder.AppendLine($"XP: {Amt(report.Xp, inv)}");
        builder.AppendLine($"Adena: {Amt(report.Adena, inv)}");
        builder.AppendLine($"Play time: {(report.Minutes is null ? "(unread)" : report.Minutes.Value.ToString(inv) + " min")}");
        builder.Append("Lamps: ");
        if (report.LampPanelClosed)
        {
            builder.AppendLine("closed (panel collapsed)");
        }
        else if (report.LampXpExceedsDialog)
        {
            builder.AppendLine("discarded (sum exceeds dialog XP)");
        }
        else if (report.LampXpRead)
        {
            builder.AppendLine(
                $"read  R={Amt(report.RedLampXp, inv)}  P={Amt(report.PurpleLampXp, inv)}  "
                + $"B={Amt(report.BlueLampXp, inv)}  G={Amt(report.GreenLampXp, inv)}");
        }
        else
        {
            builder.AppendLine("unread");
        }

        builder.AppendLine($"Location: {report.LocationHint ?? "(not visible)"}");
        if (report.UnreadFields.Count > 0)
        {
            builder.AppendLine($"Unread: {string.Join(", ", report.UnreadFields)}");
        }

        foreach (var warning in report.Warnings)
        {
            builder.AppendLine($"Warning: {warning}");
        }

        return builder.ToString().TrimEnd();
    }

    private static string Amt(long? value, CultureInfo inv)
        => value is null ? "(unread)" : value.Value.ToString("N0", inv);

    private static PlayReportResult Fail(string message) => new()
    {
        Success = false,
        ErrorMessage = message,
    };
}

public sealed class PlayReportResult
{
    public required bool Success { get; init; }

    public string? ErrorMessage { get; init; }

    public string? SourcePath { get; init; }

    public uint ImageWidth { get; init; }

    public uint ImageHeight { get; init; }

    public PlayReport? Report { get; init; }
}
