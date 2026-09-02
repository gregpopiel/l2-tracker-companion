using System.IO;
using System.Windows;
using System.Windows.Threading;
using L2TrackerCompanion.Capture;
using L2TrackerCompanion.Ocr;
using L2TrackerCompanion.Services;
using L2TrackerCompanion.Session;

namespace L2TrackerCompanion;

public partial class MainWindow : Window
{
    private readonly WindowCaptureService _windowCaptureService = new();
    private readonly SessionStore _sessionStore = new(SessionStore.GetDefaultPath());
    private readonly DispatcherTimer _refreshTimer;

    public MainWindow()
    {
        InitializeComponent();
        Closed += (_, _) => _sessionStore.Dispose();

        _refreshTimer = new DispatcherTimer
        {
            Interval = TimeSpan.FromSeconds(1),
        };
        _refreshTimer.Tick += (_, _) => RefreshGameWindowStatus();
        _refreshTimer.Start();

        RefreshGameWindowStatus();
        CaptureStatusLabel.Text = $"Captures save to:\n{WindowCaptureService.GetDefaultCapturePath()}";
        ParseStatusLabel.Text = "Capture once, or parse a PNG, to read XP / Adena / play time / lamps / location.";
        RefreshSessionStatus();
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

    private void RefreshSessionStatus()
    {
        SessionStatusLabel.Text = SessionStore.FormatInspect(_sessionStore.List(), _sessionStore.Path);
    }

    private async void CaptureOnceButton_Click(object sender, RoutedEventArgs e)
    {
        CaptureOnceButton.IsEnabled = false;
        CaptureStatusLabel.Text = "Capturing...";

        try
        {
            var outputPath = WindowCaptureService.GetDefaultCapturePath();
            var result = _windowCaptureService.TryCaptureOnce(outputPath);

            if (!result.Success)
            {
                CaptureStatusLabel.Text = $"Capture failed: {result.ErrorMessage}";
                return;
            }

            CaptureStatusLabel.Text = FormatCaptureSuccess(result);
            await RunParseAsync(outputPath);
        }
        finally
        {
            RefreshGameWindowStatus();
        }
    }

    private static string FormatCaptureSuccess(CaptureResult result)
    {
        var message = $"Saved {result.OutputPath}";

        if (result.IsLikelyBlank)
        {
            message += "\n\nWarning: the image looks blank or all-black.";
        }

        return message;
    }

    private async void ParseLastButton_Click(object sender, RoutedEventArgs e)
    {
        var capturePath = WindowCaptureService.GetDefaultCapturePath();
        if (!File.Exists(capturePath))
        {
            ParseStatusLabel.Text = $"No capture at {capturePath}\nCapture once, or use Parse a PNG...";
            return;
        }

        await RunParseAsync(capturePath);
    }

    private async void ParsePngButton_Click(object sender, RoutedEventArgs e)
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

        await RunParseAsync(dialog.FileName);
    }

    private void NewSessionButton_Click(object sender, RoutedEventArgs e)
    {
        _sessionStore.NewSession();
        RefreshSessionStatus();
    }

    private async Task RunParseAsync(string imagePath)
    {
        ParseLastButton.IsEnabled = false;
        ParsePngButton.IsEnabled = false;
        ParseStatusLabel.Text = $"Parsing {imagePath}...";

        try
        {
            var result = await PlayReportPipeline.RunFileAsync(imagePath);
            ParseStatusLabel.Text = PlayReportPipeline.FormatWindow(result);
            if (result.Success && result.Report is not null)
            {
                _sessionStore.Append(result.Report);
                RefreshSessionStatus();
            }
        }
        finally
        {
            ParseLastButton.IsEnabled = true;
            ParsePngButton.IsEnabled = true;
        }
    }
}
