using System.Globalization;
using System.IO;
using System.Windows;
using System.Windows.Media;
using System.Windows.Threading;
using L2TrackerCompanion.Api;
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
    private readonly AuthService _auth = new(TokenStore.GetDefault());
    private readonly AppOptionsStore _options = AppOptionsStore.GetDefault();
    private readonly PollingLoop _polling = new();
    private readonly DispatcherTimer _refreshTimer;
    private readonly DispatcherTimer _pollTimer;
    private CancellationTokenSource _pollCts = new();
    private CancellationTokenSource _spotsCts = new();
    private int _pollTickBusy;
    private bool _suppressPickerEvents;
    private bool _suppressModeEvents;

    public MainWindow()
    {
        InitializeComponent();
        Closed += OnClosed;
        Loaded += (_, _) => _ = RestoreAuthAsync();

        BaseUrlBox.Text = _auth.BaseUrl;

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

        RefreshPollStatus(string.Empty);
        ShowLiveStatus(LiveStatus.Idle());
        ApplyLoadedMode();
    }

    private void MainTabs_SelectionChanged(object sender, System.Windows.Controls.SelectionChangedEventArgs e)
    {
        // ComboBox.SelectionChanged bubbles to TabControl — ignore those.
        if (e.Source is not System.Windows.Controls.TabControl || !IsLoaded)
        {
            return;
        }

        SyncModeRadiosFromStore();
    }

    private void ApplyLoadedMode()
    {
        SyncModeRadiosFromStore();
        ApplyUiMode();
    }

    private void SyncModeRadiosFromStore()
    {
        _suppressModeEvents = true;
        try
        {
            UserModeRadio.IsChecked = !_options.DebugMode;
            DebugModeRadio.IsChecked = _options.DebugMode;
        }
        finally
        {
            _suppressModeEvents = false;
        }
    }

    private void AppMode_Checked(object sender, RoutedEventArgs e)
    {
        if (_suppressModeEvents || sender is not System.Windows.Controls.RadioButton { IsChecked: true } radio)
        {
            return;
        }

        var debug = radio == DebugModeRadio;
        _suppressModeEvents = true;
        try
        {
            UserModeRadio.IsChecked = !debug;
            DebugModeRadio.IsChecked = debug;
        }
        finally
        {
            _suppressModeEvents = false;
        }

        _options.SetDebugMode(debug);
        ApplyUiMode();
    }

    private void ApplyUiMode()
    {
        var debug = _options.DebugMode;
        var debugVisibility = debug ? Visibility.Visible : Visibility.Collapsed;
        DebugToolsPanel.Visibility = debugVisibility;
        Title = debug ? "L2 Tracker Companion (Debug)" : "L2 Tracker Companion";
        AuthHintLabel.Text = debug
            ? "Paste the JWT from the website (browser localStorage key l2_jwt_token)."
            : "Paste the token from the website.";

        RefreshApiUrlHint();
        RefreshGameWindowStatus();
        RefreshSessionStatus();
        if (!debug)
        {
            return;
        }

        if (string.IsNullOrEmpty(CaptureStatusLabel.Text))
        {
            CaptureStatusLabel.Text = $"Captures save to:\n{WindowCaptureService.GetDefaultCapturePath()}";
        }

        if (string.IsNullOrEmpty(ParseStatusLabel.Text))
        {
            ParseStatusLabel.Text =
                "Capture once, or parse a PNG, to read XP / Adena / play time / lamps / location.";
        }
    }

    private void RefreshApiUrlHint()
    {
        var debug = _options.DebugMode;
        ApiUrlRow.Visibility = debug ? Visibility.Visible : Visibility.Collapsed;
        var custom = !TokenStore.IsDefaultBaseUrl(_auth.BaseUrl);
        CustomApiLabel.Visibility = !debug && custom ? Visibility.Visible : Visibility.Collapsed;
        CustomApiLabel.Text = custom ? $"API: {_auth.BaseUrl}" : string.Empty;
    }

    private async Task RestoreAuthAsync()
    {
        if (!_auth.HasStoredToken)
        {
            AuthStatusLabel.Text = "Not signed in. Paste a token to continue.";
            ClearPickers(SessionPickers.SignInToLoad);
            return;
        }

        AuthStatusLabel.Text = "Checking stored token…";
        var result = await _auth.TryRestoreAsync();
        ShowAuthResult(result);
    }

    private async void UseTokenButton_Click(object sender, RoutedEventArgs e)
    {
        UseTokenButton.IsEnabled = false;
        AuthStatusLabel.Text = "Validating token…";
        try
        {
            _auth.SetBaseUrl(string.IsNullOrWhiteSpace(BaseUrlBox.Text)
                ? TokenStore.DefaultBaseUrl
                : BaseUrlBox.Text);
            BaseUrlBox.Text = _auth.BaseUrl;
            var result = await _auth.SignInAsync(TokenBox.Password);
            if (result.Success)
            {
                TokenBox.Clear();
            }

            ShowAuthResult(result);
        }
        finally
        {
            UseTokenButton.IsEnabled = true;
            RefreshApiUrlHint();
        }
    }

    private void SignOutButton_Click(object sender, RoutedEventArgs e)
    {
        _auth.SignOut();
        TokenBox.Clear();
        AuthStatusLabel.Text = "Signed out. Token removed from disk.";
        ClearPickers(SessionPickers.SignInToLoad);
    }

    private void ShowAuthResult(AuthResult result)
    {
        AuthStatusLabel.Text = result.Success
            ? result.Message
            : $"Not signed in. {result.Message}";

        if (result.Success)
        {
            BindCharacters(result.Characters);
            _ = LoadSettingsAsync();
        }
        else
        {
            ClearPickers(SessionPickers.SignInToLoad);
        }
    }

    private CharacterInfo? SelectedCharacter => CharacterCombo.SelectedItem as CharacterInfo;

    private SpotInfo? SelectedSpot => SpotCombo.SelectedItem as SpotInfo;

    private TrackerApiClient Api => TrackerApiClient.Create(_auth.BaseUrl);

    private void BindCharacters(IReadOnlyList<CharacterInfo> characters)
    {
        _suppressPickerEvents = true;
        try
        {
            CharacterCombo.ItemsSource = characters;
            CharacterCombo.IsEnabled = characters.Count > 0;
            CharacterCombo.SelectedItem = characters.Count > 0 ? characters[0] : null;
            SpotCombo.ItemsSource = null;
            SpotCombo.SelectedItem = null;
            SpotCombo.IsEnabled = false;
            BonusBox.IsEnabled = true;
            SaveButton.IsEnabled = false;
        }
        finally
        {
            _suppressPickerEvents = false;
        }

        if (SelectedCharacter is null)
        {
            PickerStatusLabel.Text = characters.Count == 0
                ? "Signed in, but this account has no characters yet."
                : "Pick a character.";
            return;
        }

        _ = LoadSpotsAsync(SelectedCharacter);
    }

    private void ClearPickers(string message)
    {
        _spotsCts.Cancel();
        _suppressPickerEvents = true;
        try
        {
            CharacterCombo.ItemsSource = null;
            CharacterCombo.SelectedItem = null;
            CharacterCombo.IsEnabled = false;
            SpotCombo.ItemsSource = null;
            SpotCombo.SelectedItem = null;
            SpotCombo.IsEnabled = false;
            BonusBox.Text = string.Empty;
            BonusBox.IsEnabled = false;
            HideBonusHint();
            SaveButton.IsEnabled = false;
        }
        finally
        {
            _suppressPickerEvents = false;
        }

        PickerStatusLabel.Text = message;
    }

    private void CharacterCombo_SelectionChanged(object sender, System.Windows.Controls.SelectionChangedEventArgs e)
    {
        if (_suppressPickerEvents)
        {
            return;
        }

        _ = LoadSpotsAsync(SelectedCharacter);
        RefreshSaveEnabled();
    }

    private void SpotCombo_SelectionChanged(object sender, System.Windows.Controls.SelectionChangedEventArgs e)
    {
        if (_suppressPickerEvents)
        {
            return;
        }

        RefreshSaveEnabled();
    }

    private void RefreshSaveEnabled()
    {
        var pickersReady = SessionPickers.SaveEnabled(SelectedCharacter, SelectedSpot);
        var delta = _sessionStore.TryDelta();
        SaveButton.IsEnabled = pickersReady && delta.Ok;
        if (!pickersReady)
        {
            return;
        }

        if (!delta.Ok)
        {
            PickerStatusLabel.Text = delta.Error;
            return;
        }

        PickerStatusLabel.Text =
            $"Ready to save {SelectedCharacter!.Name} at {SelectedSpot!.Label}: "
            + $"{delta.Totals!.XpFarmed}k XP, {delta.Totals.Adena}k Adena, "
            + $"{delta.Totals.Minutes} min wall-clock.";
    }

    private void ApplyLocationHint(string? hint)
    {
        var spots = SpotCombo.ItemsSource as IEnumerable<SpotInfo>;
        var match = SpotMatch.ExactName(hint, spots);
        if (match is null)
        {
            return;
        }

        if (SelectedSpot is not null && SelectedSpot.Id == match.Id)
        {
            return;
        }

        SpotCombo.SelectedItem = match;
    }

    private async Task LoadSpotsAsync(CharacterInfo? character)
    {
        _spotsCts.Cancel();
        _spotsCts = new CancellationTokenSource();
        var cancellationToken = _spotsCts.Token;

        _suppressPickerEvents = true;
        try
        {
            SpotCombo.ItemsSource = null;
            SpotCombo.SelectedItem = null;
            SpotCombo.IsEnabled = false;
        }
        finally
        {
            _suppressPickerEvents = false;
        }

        RefreshSaveEnabled();

        if (character is null)
        {
            PickerStatusLabel.Text = "Pick a character.";
            return;
        }

        var token = _auth.TryLoadToken();
        if (token is null)
        {
            ClearPickers(SessionPickers.SignInToLoad);
            return;
        }

        PickerStatusLabel.Text = $"Loading spots for {character.Name}…";
        var call = await Api.GetSpotsAsync(token, character.Id, cancellationToken);
        if (cancellationToken.IsCancellationRequested)
        {
            return;
        }

        if (!call.Success || call.Value is null)
        {
            PickerStatusLabel.Text = $"Could not load spots: {call.Error}";
            return;
        }

        _suppressPickerEvents = true;
        try
        {
            SpotCombo.ItemsSource = call.Value;
            SpotCombo.SelectedItem = null;
            SpotCombo.IsEnabled = call.Value.Count > 0;
        }
        finally
        {
            _suppressPickerEvents = false;
        }

        ApplyLocationHint(_sessionStore.Last()?.Report.LocationHint);
        RefreshSaveEnabled();
        if (SelectedSpot is null)
        {
            PickerStatusLabel.Text = call.Value.Count == 0
                ? "No spots on this account. Add them on the website."
                : $"{call.Value.Count} spot{(call.Value.Count == 1 ? "" : "s")} for {character.Name}. Pick one to enable Save.";
        }
    }

    private async Task LoadSettingsAsync()
    {
        var token = _auth.TryLoadToken();
        if (token is null)
        {
            return;
        }

        var call = await Api.GetSettingsAsync(token);
        if (!call.Success || call.Value is null)
        {
            var fallback = UserSettingsInfo.SchemaDefaults.DefaultBonus;
            BonusBox.Text = fallback.ToString(CultureInfo.InvariantCulture);
            BonusBox.IsEnabled = true;
            ShowBonusHint(
                $"Could not load default bonus ({call.Error ?? "empty response"}). Using {fallback}.");
            return;
        }

        BonusBox.Text = call.Value.DefaultBonus.ToString(CultureInfo.InvariantCulture);
        BonusBox.IsEnabled = true;
        HideBonusHint();
    }

    private void ShowBonusHint(string message)
    {
        BonusHintLabel.Text = message;
        BonusHintLabel.Visibility = Visibility.Visible;
    }

    private void HideBonusHint()
    {
        BonusHintLabel.Text = string.Empty;
        BonusHintLabel.Visibility = Visibility.Collapsed;
    }

    private async void SaveButton_Click(object sender, RoutedEventArgs e)
    {
        if (_polling.IsRunning)
        {
            StopTracking("Stopped to save.");
        }

        if (!SessionPickers.SaveEnabled(SelectedCharacter, SelectedSpot))
        {
            RefreshSaveEnabled();
            return;
        }

        var delta = _sessionStore.TryDelta();
        if (!delta.Ok || delta.Totals is null)
        {
            RefreshSaveEnabled();
            return;
        }

        if (!BonusText.TryParse(BonusBox.Text, out var bonus))
        {
            PickerStatusLabel.Text = "Bonus must be a number (Acquired XP/SP %).";
            return;
        }

        var token = _auth.TryLoadToken();
        if (token is null)
        {
            ClearPickers(SessionPickers.SignInToSave);
            return;
        }

        SaveButton.IsEnabled = false;
        PickerStatusLabel.Text = "Saving session…";
        var saved = false;
        try
        {
            var totals = delta.Totals;
            var request = new FarmLogRequest(
                CharacterId: SelectedCharacter!.Id,
                SpotId: SelectedSpot!.Id,
                XpFarmed: totals.XpFarmed,
                Adena: totals.Adena,
                Minutes: totals.Minutes,
                AcquiredXpSp: bonus,
                RedLampXP: totals.RedLampXP,
                PurpleLampXP: totals.PurpleLampXP,
                BlueLampXP: totals.BlueLampXP,
                GreenLampXP: totals.GreenLampXP,
                Date: totals.EndedAt);
            var call = await Api.PostFarmLogAsync(token, request);
            if (!call.Success)
            {
                PickerStatusLabel.Text = $"Save failed: {call.Error}";
                return;
            }

            _sessionStore.NewSession();
            ShowLiveStatus(LiveStatus.Idle());
            SessionStatusLabel.Text = SessionStore.FormatInspect(_sessionStore.List(), _sessionStore.Path);
            saved = true;
            PickerStatusLabel.Text =
                $"Saved farm log #{call.Value!.Id} for {SelectedCharacter.Name} at {SelectedSpot.Label} "
                + $"({totals.XpFarmed}k XP, {totals.Minutes} min). Local session cleared.";
        }
        finally
        {
            if (!saved)
            {
                RefreshSaveEnabled();
            }
        }
    }

    private void OnClosed(object? sender, EventArgs e)
    {
        _polling.Stop();
        _pollTimer.Stop();
        _pollCts.Cancel();
        _spotsCts.Cancel();
        _sessionStore.Dispose();
    }

    private void RefreshGameWindowStatus()
    {
        var gameWindow = _windowCaptureService.TryFindGameWindow();
        if (gameWindow is null)
        {
            GameWindowStatusLabel.Text = _options.DebugMode
                ? $"Game not running (no {WindowCaptureService.GameProcessName} process with a visible window)."
                : "Game not running.";
        }
        else
        {
            GameWindowStatusLabel.Text = _options.DebugMode
                ? $"Found HWND 0x{gameWindow.Hwnd.ToInt64():X}\n"
                  + $"Title: {gameWindow.Title}\n"
                  + $"Size: {gameWindow.Width} x {gameWindow.Height}\n"
                  + $"PID: {gameWindow.ProcessId}"
                : "Game running.";
        }

        CaptureOnceButton.IsEnabled = gameWindow is not null;
        if (_polling.IsRunning && gameWindow is null)
        {
            ShowLiveStatus(LiveStatus.GameNotRunning());
        }
    }

    private void RefreshSessionStatus()
    {
        SessionStatusLabel.Text = SessionStore.FormatInspect(_sessionStore.List(), _sessionStore.Path);
        RefreshSaveEnabled();
    }

    private void RefreshPollStatus(string message)
    {
        var prefix = _polling.IsRunning
            ? $"Tracking every {(int)PollingLoop.Interval.TotalSeconds}s."
            : "Not tracking.";
        PollStatusLabel.Text = string.IsNullOrWhiteSpace(message)
            ? prefix
            : prefix + " " + message;
    }

    private void ShowLiveStatus(LiveStatusSnapshot status)
    {
        LiveLight.Fill = LightBrush(status.Light);
        LiveLightLabel.Text = status.Light == TrafficLight.Idle ? "Idle" : status.Light.ToString();
        LiveDetailLabel.Text = status.Detail;
        var rates = LiveRates.Format(status.Report);
        LiveRatesLabel.Text = rates;
        LiveRatesLabel.Visibility = string.IsNullOrEmpty(rates)
            ? Visibility.Collapsed
            : Visibility.Visible;
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
            ApplyLocationHint(result.Report.LocationHint);
            if (!string.IsNullOrWhiteSpace(result.Report.LocationHint))
            {
                var match = SpotMatch.ExactName(
                    result.Report.LocationHint,
                    SpotCombo.ItemsSource as IEnumerable<SpotInfo>);
                ParseStatusLabel.Text += match is null
                    ? $"\n\nLocation hint \"{result.Report.LocationHint}\" did not match a spot; picker unchanged."
                    : $"\n\nPreselected {match.Label}.";
            }

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
