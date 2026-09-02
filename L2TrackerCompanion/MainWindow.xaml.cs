using System.IO;
using System.Windows;
using System.Windows.Media;
using System.Windows.Threading;
using L2TrackerCompanion.Capture;
using L2TrackerCompanion.Ocr;
using L2TrackerCompanion.Parsing;
using L2TrackerCompanion.Services;
using L2TrackerCompanion.Session;

namespace L2TrackerCompanion;

public partial class MainWindow : Window
{
    private readonly WindowCaptureService _windowCaptureService = new();
    private readonly SessionStore _sessionStore = new(SessionStore.GetDefaultPath());
    private readonly PollingLoop _polling = new();
    private readonly DispatcherTimer _refreshTimer;
    private readonly DispatcherTimer _pollTimer;
    private CancellationTokenSource _pollCts = new();
    private int _pollTickBusy;

    public MainWindow()
    {
        InitializeComponent();
        Closed += OnClosed;

        _refreshTimer = new DispatcherTimer
        {
            Interval = TimeSpan.FromSeconds(1),
        };
        _refreshTimer.Tick += (_, _) => RefreshGameWindowStatus();
        _refreshTimer.Start();

        _pollTimer = new DispatcherTimer
        {
            Interval = PollingLoop.Interval,
        };
        _pollTimer.Tick += (_, _) => _ = RunPollTickAsync();

        RefreshGameWindowStatus();
        CaptureStatusLabel.Text = $"Captures save to:\n{WindowCaptureService.GetDefaultCapturePath()}";
        ParseStatusLabel.Text = "Capture once, or parse a PNG, to read XP / Adena / play time / lamps / location.";
        RefreshSessionStatus();
        RefreshPollStatus("Not tracking.");
        ShowLiveStatus(LiveStatus.Idle());
    }

    private void OnClosed(object? sender, EventArgs e)
    {
        _polling.Stop();
        _pollTimer.Stop();
        _pollCts.Cancel();
        _sessionStore.Dispose();
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
        if (_polling.IsRunning && gameWindow is null)
        {
            ShowLiveStatus(LiveStatus.GameNotRunning());
        }
    }

    private void RefreshSessionStatus()
    {
        SessionStatusLabel.Text = SessionStore.FormatInspect(_sessionStore.List(), _sessionStore.Path);
    }

    private void RefreshPollStatus(string message)
    {
        var prefix = _polling.IsRunning
            ? $"Tracking every {(int)PollingLoop.Interval.TotalSeconds}s. "
            : "Not tracking. ";
        PollStatusLabel.Text = prefix + message;
    }

    private void ShowLiveStatus(LiveStatusSnapshot status)
    {
        LiveLight.Fill = LightBrush(status.Light);
        LiveLightLabel.Text = status.Light == TrafficLight.Idle ? "Idle" : status.Light.ToString();
        LiveDetailLabel.Text = status.Detail;
        LiveValuesLabel.Text = LiveStatus.FormatValues(status.Report);
    }

    private static Brush LightBrush(TrafficLight light) => light switch
    {
        TrafficLight.Green => new SolidColorBrush(Color.FromRgb(0x3F, 0xB9, 0x50)),
        TrafficLight.Orange => new SolidColorBrush(Color.FromRgb(0xD2, 0x99, 0x22)),
        TrafficLight.Red => new SolidColorBrush(Color.FromRgb(0xF8, 0x51, 0x49)),
        _ => new SolidColorBrush(Color.FromRgb(0x6E, 0x76, 0x81)),
    };

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
            await RunParseAsync(outputPath, fromPoll: false);
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

        await RunParseAsync(capturePath, fromPoll: false);
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

        await RunParseAsync(dialog.FileName, fromPoll: false);
    }

    private void NewSessionButton_Click(object sender, RoutedEventArgs e)
    {
        _sessionStore.NewSession();
        RefreshSessionStatus();
        ShowLiveStatus(LiveStatus.Idle());
    }

    private async void StartStopButton_Click(object sender, RoutedEventArgs e)
    {
        if (_polling.IsRunning)
        {
            StopTracking("Stopped.");
            return;
        }

        _polling.Start();
        _pollCts = new CancellationTokenSource();
        StartStopButton.Content = "Stop tracking";
        RefreshPollStatus("Starting…");
        _pollTimer.Start();
        await RunPollTickAsync();
    }

    private void StopTracking(string message)
    {
        _polling.Stop();
        _pollTimer.Stop();
        _pollCts.Cancel();
        StartStopButton.Content = "Start tracking";
        RefreshPollStatus(message);
    }

    private async Task RunPollTickAsync()
    {
        if (Interlocked.CompareExchange(ref _pollTickBusy, 1, 0) != 0)
        {
            return;
        }

        try
        {
            if (!_polling.IsRunning)
            {
                return;
            }

            RefreshPollStatus("Capturing…");
            var outputPath = WindowCaptureService.GetDefaultCapturePath();
            var capture = _windowCaptureService.TryCaptureOnce(outputPath);
            if (!capture.Success)
            {
                RefreshPollStatus($"Skipped: {capture.ErrorMessage}");
                ShowLiveStatus(capture.ErrorMessage is not null
                        && capture.ErrorMessage.Contains("Game not running", StringComparison.Ordinal)
                    ? LiveStatus.GameNotRunning()
                    : LiveStatus.CaptureFailed(capture.ErrorMessage ?? "Capture failed"));
                return;
            }

            CaptureStatusLabel.Text = FormatCaptureSuccess(capture);
            await RunParseAsync(outputPath, fromPoll: true, _pollCts.Token);
        }
        finally
        {
            Interlocked.Exchange(ref _pollTickBusy, 0);
        }
    }

    private async Task RunParseAsync(string imagePath, bool fromPoll, CancellationToken cancellationToken = default)
    {
        if (!fromPoll)
        {
            ParseLastButton.IsEnabled = false;
            ParsePngButton.IsEnabled = false;
        }

        ParseStatusLabel.Text = $"Parsing {imagePath}...";

        try
        {
            var result = await PlayReportPipeline.RunFileAsync(imagePath, cancellationToken);
            if (cancellationToken.IsCancellationRequested)
            {
                return;
            }

            ParseStatusLabel.Text = PlayReportPipeline.FormatWindow(result);
            if (!result.Success || result.Report is null)
            {
                ShowLiveStatus(LiveStatus.ParseFailed(result.ErrorMessage ?? "Parse failed"));
                if (fromPoll)
                {
                    RefreshPollStatus($"Skipped: {result.ErrorMessage ?? "parse failed"}");
                }

                return;
            }

            ShowLiveStatus(LiveStatus.FromReport(result.Report));

            if (fromPoll)
            {
                var tick = _polling.Tick(_sessionStore, result.Report);
                RefreshPollStatus(tick.Message);
                if (!tick.Appended && tick.Tracking)
                {
                    ParseStatusLabel.Text += "\n\n" + tick.Message;
                }
            }
            else
            {
                var accepted = _sessionStore.TryAccept(result.Report);
                if (!accepted.Appended)
                {
                    ParseStatusLabel.Text += $"\n\nDiscarded: {accepted.Reason}";
                }
            }

            RefreshSessionStatus();
        }
        finally
        {
            if (!fromPoll)
            {
                ParseLastButton.IsEnabled = true;
                ParsePngButton.IsEnabled = true;
            }
        }
    }
}
