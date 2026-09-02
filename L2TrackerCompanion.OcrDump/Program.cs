using L2TrackerCompanion.Ocr;
using L2TrackerCompanion.Session;

if (args.Length >= 1 && string.Equals(args[0], "--crop", StringComparison.OrdinalIgnoreCase))
{
    if (args.Length < 2)
    {
        Console.Error.WriteLine("Usage: L2TrackerCompanion.OcrDump --crop <images-dir> [output-dir]");
        return 1;
    }

    return await RunCropBatchAsync(args[1], args.Length >= 3 ? args[2] : DialogCropPass.GetDefaultCropDirectory());
}

if (args.Length >= 1 && string.Equals(args[0], "--farm", StringComparison.OrdinalIgnoreCase))
{
    if (args.Length < 2)
    {
        Console.Error.WriteLine("Usage: L2TrackerCompanion.OcrDump --farm <images-dir> [output-dir]");
        return 1;
    }

    return await RunFarmBatchAsync(args[1], args.Length >= 3 ? args[2] : FarmFieldsPass.GetDefaultFarmDirectory());
}

if (args.Length >= 1 && string.Equals(args[0], "--playtime", StringComparison.OrdinalIgnoreCase))
{
    if (args.Length < 2)
    {
        Console.Error.WriteLine("Usage: L2TrackerCompanion.OcrDump --playtime <images-dir> [output-dir]");
        return 1;
    }

    return await RunPlayTimeBatchAsync(args[1], args.Length >= 3 ? args[2] : PlayTimePass.GetDefaultPlayTimeDirectory());
}

if (args.Length >= 1 && string.Equals(args[0], "--lamps", StringComparison.OrdinalIgnoreCase))
{
    if (args.Length < 2)
    {
        Console.Error.WriteLine("Usage: L2TrackerCompanion.OcrDump --lamps <images-dir> [output-dir]");
        return 1;
    }

    return await RunLampBatchAsync(args[1], args.Length >= 3 ? args[2] : LampXpPass.GetDefaultLampsDirectory());
}

if (args.Length >= 1 && string.Equals(args[0], "--location", StringComparison.OrdinalIgnoreCase))
{
    if (args.Length < 2)
    {
        Console.Error.WriteLine("Usage: L2TrackerCompanion.OcrDump --location <images-dir> [output-dir]");
        return 1;
    }

    return await RunLocationBatchAsync(args[1], args.Length >= 3 ? args[2] : LocationHintPass.GetDefaultLocationDirectory());
}

if (args.Length >= 1 && string.Equals(args[0], "--new-session", StringComparison.OrdinalIgnoreCase))
{
    using var wiped = new SessionStore(SessionStore.GetDefaultPath());
    wiped.NewSession();
    Console.WriteLine(SessionStore.FormatInspect(wiped.List(), wiped.Path));
    return 0;
}

if (args.Length >= 1 && string.Equals(args[0], "--parse", StringComparison.OrdinalIgnoreCase))
{
    if (args.Length < 2)
    {
        Console.Error.WriteLine("Usage: L2TrackerCompanion.OcrDump --parse <image.png>");
        return 1;
    }

    var parsed = await PlayReportPipeline.RunFileAsync(args[1], CancellationToken.None);
    Console.WriteLine(PlayReportPipeline.FormatWindow(parsed));
    if (parsed.Success && parsed.Report is not null)
    {
        using var store = new SessionStore(SessionStore.GetDefaultPath());
        store.Append(parsed.Report);
        Console.WriteLine();
        Console.WriteLine(SessionStore.FormatInspect(store.List(), store.Path));
    }

    return parsed.Success ? 0 : 2;
}

if (args.Length < 1)
{
    Console.Error.WriteLine("Usage: L2TrackerCompanion.OcrDump <image.png|images-dir> [output.txt|output-dir]");
    Console.Error.WriteLine("       L2TrackerCompanion.OcrDump --crop <images-dir> [output-dir]");
    Console.Error.WriteLine("       L2TrackerCompanion.OcrDump --farm <images-dir> [output-dir]");
    Console.Error.WriteLine("       L2TrackerCompanion.OcrDump --playtime <images-dir> [output-dir]");
    Console.Error.WriteLine("       L2TrackerCompanion.OcrDump --lamps <images-dir> [output-dir]");
    Console.Error.WriteLine("       L2TrackerCompanion.OcrDump --location <images-dir> [output-dir]");
    Console.Error.WriteLine("       L2TrackerCompanion.OcrDump --parse <image.png>");
    Console.Error.WriteLine("       L2TrackerCompanion.OcrDump --new-session");
    return 1;
}

var inputPath = args[0];

if (Directory.Exists(inputPath))
{
    return await RunBatchAsync(inputPath, args.Length >= 2 ? args[1] : OcrWordDump.GetDefaultBatchDumpDirectory());
}

var outputPath = args.Length >= 2 ? args[1] : OcrWordDump.GetDefaultDumpPath();
var result = await OcrWordDump.DumpFileAsync(inputPath, outputPath);
Console.WriteLine(OcrWordDump.FormatStatus(result));

if (!result.Success)
{
    return 2;
}

return result.SmokePassed ? 0 : 3;

static async Task<int> RunBatchAsync(string imageDirectory, string outputDirectory)
{
    var pngs = OcrWordDump.ListPngsInDirectory(imageDirectory);
    if (pngs.Count == 0)
    {
        Console.Error.WriteLine($"No PNG files in {imageDirectory} (top-level only; processed/ is skipped).");
        return 1;
    }

    Directory.CreateDirectory(outputDirectory);
    Console.WriteLine($"OCR {pngs.Count} PNG(s) from {imageDirectory}");
    Console.WriteLine($"Writing dumps to {outputDirectory}");

    var results = new List<OcrDumpResult>(pngs.Count);
    for (var i = 0; i < pngs.Count; i++)
    {
        var png = pngs[i];
        var dest = Path.Combine(outputDirectory, Path.GetFileNameWithoutExtension(png) + ".txt");
        Console.WriteLine($"[{i + 1}/{pngs.Count}] {Path.GetFileName(png)}");
        var dump = await OcrWordDump.DumpFileAsync(png, dest);
        Console.WriteLine(OcrWordDump.FormatStatus(dump));
        results.Add(dump);
    }

    var summaryPath = Path.Combine(outputDirectory, "_summary.tsv");
    await File.WriteAllTextAsync(summaryPath, OcrWordDump.FormatBatchSummary(results));
    Console.WriteLine();
    Console.WriteLine(OcrWordDump.FormatBatchStatus(results));
    Console.WriteLine($"Summary: {summaryPath}");

    return results.All(r => r.Success) ? 0 : 2;
}

static async Task<int> RunCropBatchAsync(string imageDirectory, string outputDirectory)
{
    var pngs = OcrWordDump.ListPngsInDirectory(imageDirectory);
    if (pngs.Count == 0)
    {
        Console.Error.WriteLine($"No PNG files in {imageDirectory} (top-level only; processed/ is skipped).");
        return 1;
    }

    Directory.CreateDirectory(outputDirectory);
    Console.WriteLine($"Dialog crop {pngs.Count} PNG(s) from {imageDirectory}");
    Console.WriteLine($"Writing crops to {outputDirectory}");

    var results = new List<DialogCropResult>(pngs.Count);
    for (var i = 0; i < pngs.Count; i++)
    {
        var png = pngs[i];
        Console.WriteLine($"[{i + 1}/{pngs.Count}] {Path.GetFileName(png)}");
        var crop = await DialogCropPass.RunFileAsync(png, outputDirectory, CancellationToken.None);
        Console.WriteLine(DialogCropPass.FormatStatus(crop));
        results.Add(crop);
    }

    var summaryPath = Path.Combine(outputDirectory, "_summary.tsv");
    await File.WriteAllTextAsync(summaryPath, DialogCropPass.FormatBatchSummary(results));
    Console.WriteLine();
    Console.WriteLine(DialogCropPass.FormatBatchStatus(results));
    Console.WriteLine($"Summary: {summaryPath}");

    var dialogOk = results.Count(r => r.DialogContained);
    Console.WriteLine($"Dialog contained: {dialogOk}/{results.Count}");
    return results.All(r => r.Success) ? 0 : 2;
}

static async Task<int> RunFarmBatchAsync(string imageDirectory, string outputDirectory)
{
    var pngs = OcrWordDump.ListPngsInDirectory(imageDirectory);
    if (pngs.Count == 0)
    {
        Console.Error.WriteLine($"No PNG files in {imageDirectory} (top-level only; processed/ is skipped).");
        return 1;
    }

    Directory.CreateDirectory(outputDirectory);
    Console.WriteLine($"Farm fields {pngs.Count} PNG(s) from {imageDirectory}");
    Console.WriteLine($"Writing dumps to {outputDirectory}");

    var results = new List<FarmFieldsResult>(pngs.Count);
    for (var i = 0; i < pngs.Count; i++)
    {
        var png = pngs[i];
        Console.WriteLine($"[{i + 1}/{pngs.Count}] {Path.GetFileName(png)}");
        var farm = await FarmFieldsPass.RunFileAsync(png, outputDirectory, CancellationToken.None);
        Console.WriteLine(FarmFieldsPass.FormatStatus(farm));
        results.Add(farm);
    }

    var summaryPath = Path.Combine(outputDirectory, "_farm.tsv");
    await File.WriteAllTextAsync(summaryPath, FarmFieldsPass.FormatBatchSummary(results));
    Console.WriteLine();
    Console.WriteLine(FarmFieldsPass.FormatBatchStatus(results));
    Console.WriteLine($"Summary: {summaryPath}");

    var baselinePath = Path.Combine(Directory.GetCurrentDirectory(), "baselines", "tesseract-farm.tsv");
    if (File.Exists(baselinePath))
    {
        var baseline = FarmFieldsPass.LoadBaselineTsv(baselinePath);
        Console.WriteLine();
        Console.WriteLine(FarmFieldsPass.FormatBaselineComparison(results, baseline));
    }
    else
    {
        Console.WriteLine($"No tesseract baseline at {baselinePath} — skipped comparison.");
    }

    return results.All(r => r.Success) ? 0 : 2;
}

static async Task<int> RunPlayTimeBatchAsync(string imageDirectory, string outputDirectory)
{
    var pngs = OcrWordDump.ListPngsInDirectory(imageDirectory);
    if (pngs.Count == 0)
    {
        Console.Error.WriteLine($"No PNG files in {imageDirectory} (top-level only; processed/ is skipped).");
        return 1;
    }

    Directory.CreateDirectory(outputDirectory);
    Console.WriteLine($"Play time {pngs.Count} PNG(s) from {imageDirectory}");
    Console.WriteLine($"Writing dumps to {outputDirectory}");

    var results = new List<PlayTimeResult>(pngs.Count);
    for (var i = 0; i < pngs.Count; i++)
    {
        var png = pngs[i];
        Console.WriteLine($"[{i + 1}/{pngs.Count}] {Path.GetFileName(png)}");
        var playTime = await PlayTimePass.RunFileAsync(png, outputDirectory, CancellationToken.None);
        Console.WriteLine(PlayTimePass.FormatStatus(playTime));
        results.Add(playTime);
    }

    var summaryPath = Path.Combine(outputDirectory, "_playtime.tsv");
    await File.WriteAllTextAsync(summaryPath, PlayTimePass.FormatBatchSummary(results));
    Console.WriteLine();
    Console.WriteLine(PlayTimePass.FormatBatchStatus(results));
    Console.WriteLine($"Summary: {summaryPath}");

    return results.All(r => r.Success) ? 0 : 2;
}

static async Task<int> RunLampBatchAsync(string imageDirectory, string outputDirectory)
{
    var pngs = OcrWordDump.ListPngsInDirectory(imageDirectory);
    if (pngs.Count == 0)
    {
        Console.Error.WriteLine($"No PNG files in {imageDirectory} (top-level only; processed/ is skipped).");
        return 1;
    }

    Directory.CreateDirectory(outputDirectory);
    Console.WriteLine($"Lamp XP {pngs.Count} PNG(s) from {imageDirectory}");
    Console.WriteLine($"Writing dumps to {outputDirectory}");

    var results = new List<LampXpResult>(pngs.Count);
    for (var i = 0; i < pngs.Count; i++)
    {
        var png = pngs[i];
        Console.WriteLine($"[{i + 1}/{pngs.Count}] {Path.GetFileName(png)}");
        var lamps = await LampXpPass.RunFileAsync(png, outputDirectory, CancellationToken.None);
        Console.WriteLine(LampXpPass.FormatStatus(lamps));
        results.Add(lamps);
    }

    var summaryPath = Path.Combine(outputDirectory, "_lamps.tsv");
    await File.WriteAllTextAsync(summaryPath, LampXpPass.FormatBatchSummary(results));
    Console.WriteLine();
    Console.WriteLine(LampXpPass.FormatBatchStatus(results));
    Console.WriteLine($"Summary: {summaryPath}");

    var baselinePath = Path.Combine(Directory.GetCurrentDirectory(), "baselines", "tesseract-lamps.tsv");
    if (File.Exists(baselinePath))
    {
        var baseline = LampXpPass.LoadBaselineTsv(baselinePath);
        Console.WriteLine();
        Console.WriteLine(LampXpPass.FormatBaselineComparison(results, baseline));
    }
    else
    {
        Console.WriteLine($"No tesseract baseline at {baselinePath} — skipped comparison.");
    }

    return results.All(r => r.Success) ? 0 : 2;
}

static async Task<int> RunLocationBatchAsync(string imageDirectory, string outputDirectory)
{
    var pngs = OcrWordDump.ListPngsInDirectory(imageDirectory);
    if (pngs.Count == 0)
    {
        Console.Error.WriteLine($"No PNG files in {imageDirectory} (top-level only; processed/ is skipped).");
        return 1;
    }

    Directory.CreateDirectory(outputDirectory);
    Console.WriteLine($"Location hint {pngs.Count} PNG(s) from {imageDirectory}");
    Console.WriteLine($"Writing dumps to {outputDirectory}");

    var results = new List<LocationHintResult>(pngs.Count);
    for (var i = 0; i < pngs.Count; i++)
    {
        var png = pngs[i];
        Console.WriteLine($"[{i + 1}/{pngs.Count}] {Path.GetFileName(png)}");
        var location = await LocationHintPass.RunFileAsync(png, outputDirectory, CancellationToken.None);
        Console.WriteLine(LocationHintPass.FormatStatus(location));
        results.Add(location);
    }

    var summaryPath = Path.Combine(outputDirectory, "_location.tsv");
    await File.WriteAllTextAsync(summaryPath, LocationHintPass.FormatBatchSummary(results));
    Console.WriteLine();
    Console.WriteLine(LocationHintPass.FormatBatchStatus(results));
    Console.WriteLine($"Summary: {summaryPath}");

    var baselinePath = Path.Combine(Directory.GetCurrentDirectory(), "baselines", "tesseract-location.tsv");
    if (File.Exists(baselinePath))
    {
        var baseline = LocationHintPass.LoadBaselineTsv(baselinePath);
        Console.WriteLine();
        Console.WriteLine(LocationHintPass.FormatBaselineComparison(results, baseline));
    }
    else
    {
        Console.WriteLine($"No tesseract baseline at {baselinePath} — skipped comparison.");
    }

    return results.All(r => r.Success) ? 0 : 2;
}
