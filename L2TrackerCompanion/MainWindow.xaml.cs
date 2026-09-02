using System.IO;
using System.Windows;
using System.Windows.Threading;
using L2TrackerCompanion.Capture;
using L2TrackerCompanion.Ocr;
using L2TrackerCompanion.Services;

namespace L2TrackerCompanion;

public partial class MainWindow : Window
{
    private readonly WindowCaptureService _windowCaptureService = new();
    private readonly DispatcherTimer _refreshTimer;

    public MainWindow()
    {
        InitializeComponent();

        _refreshTimer = new DispatcherTimer
        {
            Interval = TimeSpan.FromSeconds(1),
        };
        _refreshTimer.Tick += (_, _) => RefreshGameWindowStatus();
        _refreshTimer.Start();

        RefreshGameWindowStatus();
        CaptureStatusLabel.Text = $"Captures save to:\n{WindowCaptureService.GetDefaultCapturePath()}";
        OcrStatusLabel.Text = $"OCR dumps save to:\n{OcrWordDump.GetDefaultDumpPath()}";
    }

    private void RefreshGameWindowStatus()
    {
        var gameWindow = _windowCaptureService.TryFindGameWindow();
        GameWindowStatusLabel.Text = gameWindow is null
            ? $"Game not running (no {WindowCaptureService.GameProcessName} process with a visible window)."
            : $"Found HWND 0x{gameWindow.Hwnd.ToInt64():X}\n"
              + $"Title: {gameWindow.Title}\n"
              + $"Size: {gameWindow.Width} x {gameWindow.Height}\n"
              + $"PID: {gameWindow.ProcessId}";

        CaptureOnceButton.IsEnabled = gameWindow is not null;
    }

    private void CaptureOnceButton_Click(object sender, RoutedEventArgs e)
    {
        CaptureOnceButton.IsEnabled = false;
        CaptureStatusLabel.Text = "Capturing...";

        try
        {
            var outputPath = WindowCaptureService.GetDefaultCapturePath();
            var result = _windowCaptureService.TryCaptureOnce(outputPath);

            CaptureStatusLabel.Text = result.Success
                ? FormatCaptureSuccess(result)
                : $"Capture failed: {result.ErrorMessage}";
        }
        finally
        {
            RefreshGameWindowStatus();
        }
    }

    private static string FormatCaptureSuccess(L2TrackerCompanion.Capture.CaptureResult result)
    {
        var message = $"Saved {result.OutputPath}";

        if (result.IsLikelyBlank)
        {
            message += "\n\nWarning: the image looks blank or all-black.";
        }

        return message;
    }

    private async void OcrDumpButton_Click(object sender, RoutedEventArgs e)
    {
        var capturePath = WindowCaptureService.GetDefaultCapturePath();
        if (!File.Exists(capturePath))
        {
            OcrStatusLabel.Text = $"No capture at {capturePath}\nCapture once, or use OCR a PNG...";
            return;
        }

        await RunOcrDumpAsync(capturePath);
    }

    private async void OcrPngButton_Click(object sender, RoutedEventArgs e)
    {
        var dialog = new Microsoft.Win32.OpenFileDialog
        {
            Title = "Choose a Play Report screenshot",
            Filter = "PNG images|*.png|All files|*.*",
            CheckFileExists = true,
        };

        var capturePath = WindowCaptureService.GetDefaultCapturePath();
        if (File.Exists(capturePath))
        {
            dialog.InitialDirectory = Path.GetDirectoryName(capturePath);
            dialog.FileName = Path.GetFileName(capturePath);
        }

        if (dialog.ShowDialog(this) != true)
        {
            return;
        }

        await RunOcrDumpAsync(dialog.FileName);
    }

    private async Task RunOcrDumpAsync(string imagePath)
    {
        OcrDumpButton.IsEnabled = false;
        OcrPngButton.IsEnabled = false;
        OcrStatusLabel.Text = $"OCR: {imagePath}";

        try
        {
            var result = await OcrWordDump.DumpFileAsync(imagePath);
            OcrStatusLabel.Text = OcrWordDump.FormatStatus(result);
        }
        finally
        {
            OcrDumpButton.IsEnabled = true;
            OcrPngButton.IsEnabled = true;
        }
    }
}
