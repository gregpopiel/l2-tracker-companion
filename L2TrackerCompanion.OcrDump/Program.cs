using L2TrackerCompanion.Ocr;

if (args.Length < 1)
{
    Console.Error.WriteLine("Usage: L2TrackerCompanion.OcrDump <image.png> [output.txt]");
    return 1;
}

var imagePath = args[0];
var outputPath = args.Length >= 2 ? args[1] : OcrWordDump.GetDefaultDumpPath();

var result = await OcrWordDump.DumpFileAsync(imagePath, outputPath);
Console.WriteLine(OcrWordDump.FormatStatus(result));

if (!result.Success)
{
    return 2;
}

return result.SmokePassed ? 0 : 3;
