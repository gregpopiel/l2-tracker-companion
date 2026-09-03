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
using Velopack;

namespace L2TrackerCompanion;

public partial class MainWindow : Window
{
    private readonly WindowCaptureService _windowCaptureService = new();
    private readonly SessionStore _sessionStore = new(SessionStore.GetDefaultPath());
    private readonly AuthService _auth = new(TokenStore.GetDefault());
    private readonly AppOptionsStore _options = AppOptionsStore.GetDefault();
    private readonly PollingLoop _polling = new();
    private readonly UpdateService _updates = new();
    private readonly DispatcherTimer _refreshTimer;
    private readonly DispatcherTimer _pollTimer;
    private readonly DispatcherTimer _updateTimer;
    private CancellationTokenSource _pollCts = new();
    private CancellationTokenSource _spotsCts = new();
    private readonly CancellationTokenSource _updateCts = new();
    private int _pollTickBusy;
    private bool _suppressPickerEvents;
    private bool _suppressModeEvents;
    private bool _ratePerHour = UserSettingsInfo.SchemaDefaults.RatePerHour;
    private LiveStatusSnapshot _liveStatus = LiveStatus.Idle();
    private PlayReport? _lastReport;
    private DateTimeOffset _lastReportAt;
    private MonotonicityOutcome? _lastComparison;
    private bool _saveInFlight;
    private bool _isAdmin;
    private readonly GameProcessWatch _gameWatch = new();
    private long? _savedPanelMinutes;
    private long? _savedPanelXp;
    private UpdateInfo? _pendingUpdate;
    private bool _updateCheckInFlight;

    public MainWindow()
    {
        InitializeComponent();
        Closed += OnClosed;
        Loaded += (_, _) => _ = RestoreAuthAsync();
        Loaded += (_, _) => _ = CheckForUpdatesAsync();

        // The buffer only exists to compare one reading against the previous
        // one within a run, and anything left over from the last run is stale
        // by definition — the panel may have been reset while we were closed.
        // The save lock is a separate table and deliberately survives this.
        _sessionStore.NewSession();

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

        // Long enough that a farming session isn't repeatedly hitting GitHub, short
        // enough that an update lands the same day it's published. A tick is a no-op
        // once _pendingUpdate is already set.
        _updateTimer = new DispatcherTimer
        {
            Interval = TimeSpan.FromHours(4),
        };
        _updateTimer.Tick += (_, _) => _ = CheckForUpdatesAsync();
        _updateTimer.Start();

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
        ApplyAdminGating();
    }

    /// <summary>
    /// The User/Debug toggle is admin-only — a non-admin account never sees it at all,
    /// not just a disabled Debug option, since User is the only mode it could ever pick.
    /// Not knowing yet (before sign-in resolves) is treated the same as not being admin.
    /// Any Debug mode saved locally from a previous, admin session is silently dropped
    /// back to User until the account re-proves itself an admin.
    /// </summary>
    private void ApplyAdminGating()
    {
        OptionsSection.Visibility = _isAdmin ? Visibility.Visible : Visibility.Collapsed;
        DebugModeRadio.IsEnabled = _isAdmin;
        if (!_isAdmin && _options.DebugMode)
        {
            _options.SetDebugMode(false);
        }

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

        // One radio group now, so WPF clears the sibling itself — nothing to mirror.
        _options.SetDebugMode(radio == DebugModeRadio);
        ApplyUiMode();
    }

    private RateUnit DisplayRateUnit => _ratePerHour ? RateUnit.Hour : RateUnit.Minute;

    private void ApplyUiMode()
    {
        var debug = _options.DebugMode;
        var debugVisibility = debug ? Visibility.Visible : Visibility.Collapsed;
        DebugToolsPanel.Visibility = debugVisibility;
        Title = debug ? "L2 Tracker Companion (Debug)" : "L2 Tracker Companion";
        AuthHintLabel.Text = debug
            ? "Paste the JWT from the website (browser localStorage key l2_jwt_token)."
            : "Paste the token from the website.";

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

    private async Task RestoreAuthAsync()
    {
        if (!_auth.HasStoredToken)
        {
            // Nothing has happened yet — the hint above the form already says to
            // paste a token, so a status line here would only repeat it.
            ShowLogin(string.Empty);
            return;
        }

        // The form stays hidden for the round-trip: the stored token is being checked,
        // so inviting a paste (and losing it to the result below) would be a trap.
        ShowLogin("Checking stored token…", checking: true);
        SignInButton.IsEnabled = false;
        RetryButton.IsEnabled = false;
        try
        {
            var result = await _auth.TryRestoreAsync();
            ShowAuthResult(result);
        }
        finally
        {
            SignInButton.IsEnabled = true;
            RetryButton.IsEnabled = true;
            GateForm.Visibility = Visibility.Visible;
        }
    }

    private void RetryButton_Click(object sender, RoutedEventArgs e) => _ = RestoreAuthAsync();

    private void WebsiteLink_RequestNavigate(object sender, System.Windows.Navigation.RequestNavigateEventArgs e)
    {
        System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo(e.Uri.AbsoluteUri) { UseShellExecute = true });
        e.Handled = true;
    }

    private void TokenBox_KeyDown(object sender, System.Windows.Input.KeyEventArgs e)
    {
        if (e.Key == System.Windows.Input.Key.Enter && SignInButton.IsEnabled)
        {
            e.Handled = true;
            SignInButton_Click(SignInButton, new RoutedEventArgs());
        }
    }

    private async void SignInButton_Click(object sender, RoutedEventArgs e)
    {
        SignInButton.IsEnabled = false;
        SetAuthStatus("Validating token…", isError: false);
        try
        {
            // The base URL is whatever AuthService loaded from api-base-url.txt;
            // there is no longer any in-app surface that reads or writes it.
            var result = await _auth.SignInAsync(TokenBox.Password);
            if (result.Success)
            {
                TokenBox.Clear();
            }

            ShowAuthResult(result);
        }
        finally
        {
            SignInButton.IsEnabled = true;
        }
    }

    private void SignOutButton_Click(object sender, RoutedEventArgs e)
    {
        if (_saveInFlight)
        {
            // The pending save still writes its lock row when it returns; wiping
            // the store underneath it would hand that lock to the next account.
            PickerStatusLabel.Text = "A save is still in progress — try again in a moment.";
            return;
        }

        var gate = CurrentGate();
        if (gate.CanSave && gate.Totals is not null && !ConfirmDiscardSession(gate.Totals))
        {
            return;
        }

        _auth.SignOut();
        TokenBox.Clear();
        ApplyRateUnit(UserSettingsInfo.SchemaDefaults);
        ClearPickers(SessionPickers.SignInToLoad);

        // Unsaved snapshots belong to the account that produced them — the next
        // token pasted at the gate may be a different one. Confirmed above.
        _sessionStore.NewSession();
        _sessionStore.ClearSaveLock();
        _savedPanelMinutes = null;
        _savedPanelXp = null;
        _lastReport = null;
        _lastComparison = null;
        ShowLiveStatus(LiveStatus.Idle());
        RefreshSessionStatus();
        // Sign Out is a deliberate click on a button labeled "Sign out" — the gate
        // reappearing with its own hint already confirms it happened.
        ShowLogin(string.Empty);
    }

    private bool ConfirmDiscardSession(SessionTotals totals, string action = "Signing out")
        => MessageBox.Show(
            this,
            $"This session has {totals.XpFarmed}k XP, {totals.Adena}k Adena and {totals.Minutes} min "
            + $"that have not been saved yet. {action} discards them for good.\n\n{action} anyway?",
            "Unsaved session",
            MessageBoxButton.YesNo,
            MessageBoxImage.Warning,
            MessageBoxResult.No) == MessageBoxResult.Yes;

    private void ShowAuthResult(AuthResult result)
    {
        if (result.Success)
        {
            _isAdmin = result.IsAdmin;
            ApplyAdminGating();
            ShowWorkspace(result.Message);
            BindCharacters(result.Characters);
            _ = LoadSettingsAsync();
            return;
        }

        ApplyRateUnit(UserSettingsInfo.SchemaDefaults);
        ClearPickers(SessionPickers.SignInToLoad);
        ShowLogin(result.Message, isError: true);
    }

    /// <summary>
    /// Sign-in gate: nothing but the token form is reachable until the JWT validates.
    /// <paramref name="isError"/> distinguishes a rejected/unreachable token from a
    /// neutral gate state (idle, checking, just signed out) — without it every message
    /// here rendered in the same gray, so a rejection read no differently than "signed out".
    /// </summary>
    private void ShowLogin(string status, bool checking = false, bool isError = false)
    {
        // Stop tracking here, not at each caller: the gate hides the Stop button,
        // so a loop left running could not be stopped without signing back in.
        if (_polling.IsRunning)
        {
            StopTracking("Stopped: not signed in.");
        }

        // Signed out (or never proven admin yet) — Debug mode is admin-only.
        _isAdmin = false;
        ApplyAdminGating();

        SetAuthStatus(status, isError);
        GateForm.Visibility = checking ? Visibility.Collapsed : Visibility.Visible;
        RetryButton.Visibility = _auth.HasStoredToken ? Visibility.Visible : Visibility.Collapsed;
        MainTabs.Visibility = Visibility.Collapsed;
        StatusBar.Visibility = Visibility.Collapsed;
        LoginView.Visibility = Visibility.Visible;
        if (!checking)
        {
            TokenBox.Focus();
        }
    }

    private void ShowWorkspace(string status)
    {
        SetAuthStatus(status, "ConfirmGreenBrush");
        LoginView.Visibility = Visibility.Collapsed;
        MainTabs.Visibility = Visibility.Visible;
        StatusBar.Visibility = Visibility.Visible;
        MainTabs.SelectedIndex = 0;
    }

    private void SetAuthStatus(string status, bool isError)
        => SetAuthStatus(status, isError ? "AlarmRedBrush" : "StaticGrayBrush");

    // Resolved from the theme rather than a literal color — same pattern as
    // LightBrush below — so a palette edit changes this surface too. Collapsed
    // when empty rather than left as a blank line, same as LiveRatesLabel below.
    private void SetAuthStatus(string status, string brushKey)
    {
        var brush = (Brush)FindResource(brushKey);
        var visibility = string.IsNullOrEmpty(status) ? Visibility.Collapsed : Visibility.Visible;
        AuthStatusLabel.Text = status;
        AuthStatusLabel.Foreground = brush;
        AuthStatusLabel.Visibility = visibility;
        AccountStatusLabel.Text = status;
        AccountStatusLabel.Foreground = brush;
        AccountStatusLabel.Visibility = visibility;
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

    /// <summary>
    /// Whether the latest reading may be saved, and what it would post.
    /// </summary>
    private SaveGateDecision CurrentGate()
        => SaveGate.Evaluate(
            _lastReport,
            _lastReportAt,
            _lastComparison,
            _lastReport is not null && IsSaveLocked(_lastReport));

    /// <summary>
    /// The stored lock, plus an in-process copy of it.
    /// </summary>
    /// <remarks>
    /// Writing the lock row can fail (a locked database) <em>after</em> the log
    /// has already reached the server. Without the in-process copy the next
    /// poll tick would find no lock and put Save back within ten seconds, ready
    /// to duplicate a log that already exists.
    /// </remarks>
    private bool IsSaveLocked(PlayReport current)
    {
        if (_savedPanelMinutes is not null
            && SaveLock.Covers(_savedPanelMinutes.Value, _savedPanelXp, current))
        {
            return true;
        }

        return _sessionStore.IsSaveLocked(current);
    }

    private void RefreshSaveEnabled()
    {
        var pickersReady = SessionPickers.SaveEnabled(SelectedCharacter, SelectedSpot);
        var gate = CurrentGate();

        // A poll tick lands here every 10s, including while a save is awaiting
        // its response — without this the button would re-arm mid-POST and a
        // second click would duplicate the log.
        SaveButton.IsEnabled = pickersReady && gate.CanSave && !_saveInFlight;
        if (!pickersReady)
        {
            return;
        }

        if (!gate.CanSave)
        {
            PickerStatusLabel.Text = gate.BlockReason;
            return;
        }

        var totals = gate.Totals!;
        var text =
            $"Ready to save {SelectedCharacter!.Name} at {SelectedSpot!.Label}: "
            + $"{totals.XpFarmed}k XP, {totals.Adena}k Adena, "
            + $"{totals.Minutes} min from the Play Report.";
        // Separated, not stacked: this lands in the status bar, and a newline
        // there grows the bar and pushes the content above it up.
        if (gate.Warnings.Count > 0)
        {
            text += " · " + string.Join(" · ", gate.Warnings);
        }

        PickerStatusLabel.Text = text;
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
            ShowLogin("Session expired. Paste a token to continue.", isError: true);
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

    private async Task LoadSettingsAsync(bool keepExistingOnFailure = false)
    {
        var token = _auth.TryLoadToken();
        if (token is null)
        {
            return;
        }

        var call = await Api.GetSettingsAsync(token);
        if (!call.Success || call.Value is null)
        {
            if (keepExistingOnFailure)
            {
                return;
            }

            var fallback = UserSettingsInfo.SchemaDefaults;
            BonusBox.Text = fallback.DefaultBonus.ToString(CultureInfo.InvariantCulture);
            BonusBox.IsEnabled = true;
            ApplyRateUnit(fallback);
            ShowBonusHint(
                $"Could not load default bonus ({call.Error ?? "empty response"}). Using {fallback.DefaultBonus}.");
            return;
        }

        BonusBox.Text = call.Value.DefaultBonus.ToString(CultureInfo.InvariantCulture);
        BonusBox.IsEnabled = true;
        ApplyRateUnit(call.Value);
        HideBonusHint();
    }

    private void ApplyRateUnit(UserSettingsInfo settings)
    {
        _ratePerHour = settings.RatePerHour;
        ShowLiveStatus(_liveStatus);
    }

    private void ShowBonusHint(string message)
    {
        BonusHintLabel.Text = message;
        BonusHintLabelRow.Visibility = Visibility.Visible;
    }

    private void HideBonusHint()
    {
        BonusHintLabel.Text = string.Empty;
        BonusHintLabelRow.Visibility = Visibility.Collapsed;
    }

    private async void SaveButton_Click(object sender, RoutedEventArgs e)
    {
        // Saving no longer ends anything: the log is built from the latest
        // reading, so tracking keeps running and the panel keeps counting.
        if (_saveInFlight)
        {
            return;
        }

        if (!SessionPickers.SaveEnabled(SelectedCharacter, SelectedSpot))
        {
            RefreshSaveEnabled();
            return;
        }

        var gate = CurrentGate();
        var report = _lastReport;
        if (!gate.CanSave || gate.Totals is null || report is null)
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
            ShowLogin("Session expired. Paste a token to continue.", isError: true);
            return;
        }

        _saveInFlight = true;
        SaveButton.IsEnabled = false;
        PickerStatusLabel.Text = "Saving session…";
        var saved = false;
        try
        {
            var totals = gate.Totals;
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

            // The log exists on the server from here on. Nothing below may put
            // the button back or throw out of this async void handler.
            saved = true;
            SaveButton.IsEnabled = false;

            // Held in memory as well as on disk, so a failed write cannot let
            // the next tick re-arm the button.
            _savedPanelMinutes = report.Minutes;
            _savedPanelXp = report.Xp;

            var lockNote = string.Empty;
            try
            {
                // Records the panel state this log covered, so a later frame
                // cannot post the same minutes again.
                _sessionStore.MarkSaved(report);
                SessionStatusLabel.Text = SessionStore.FormatInspect(_sessionStore.List(), _sessionStore.Path);
            }
            catch (Exception ex)
            {
                lockNote = $" (warning: could not record it locally — {ex.Message})";
            }

            PickerStatusLabel.Text =
                $"Saved farm log #{call.Value!.Id} for {SelectedCharacter.Name} at {SelectedSpot.Label} "
                + $"({totals.XpFarmed}k XP, {totals.Minutes} min). "
                + "Reset the Play Report in-game to start a new session."
                + lockNote;
        }
        finally
        {
            _saveInFlight = false;
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
        _updateTimer.Stop();
        _pollCts.Cancel();
        _spotsCts.Cancel();
        _updateCts.Cancel();
        _sessionStore.Dispose();
    }

    /// <summary>
    /// Silent, background check — never surfaces a failure (offline, GitHub hiccup),
    /// never restarts on its own. Once an update is downloaded, <see cref="UpdateAvailableButton"/>
    /// appears and the actual apply/restart waits for that explicit click, since the
    /// app can be mid-poll or holding an unsaved farm-log delta.
    /// </summary>
    private async Task CheckForUpdatesAsync()
    {
        if (_pendingUpdate is not null || _updateCheckInFlight)
        {
            return;
        }

        _updateCheckInFlight = true;
        try
        {
            var updateInfo = await _updates.CheckAndDownloadAsync(_updateCts.Token);
            if (updateInfo is null)
            {
                return;
            }

            _pendingUpdate = updateInfo;
            // Nothing left to poll for once a version is already downloaded and
            // waiting on the user's click.
            _updateTimer.Stop();
            UpdateAvailableButton.Content = $"Update available (v{updateInfo.TargetFullRelease.Version}) — restart to install";
            UpdateAvailableButton.Visibility = Visibility.Visible;
        }
        finally
        {
            _updateCheckInFlight = false;
        }
    }

    private void UpdateAvailableButton_Click(object sender, RoutedEventArgs e)
    {
        if (_pendingUpdate is null)
        {
            return;
        }

        if (_saveInFlight)
        {
            // Same rationale as SignOutButton_Click: the pending save still writes
            // its lock row when it returns, and restarting out from under it would
            // leave the client unsure whether the log ever reached the server.
            PickerStatusLabel.Text = "A save is still in progress — try again in a moment.";
            return;
        }

        var gate = CurrentGate();
        if (gate.CanSave && gate.Totals is not null && !ConfirmDiscardSession(gate.Totals, "Restarting to update"))
        {
            return;
        }

        UpdateAvailableButton.IsEnabled = false;
        try
        {
            _updates.ApplyAndRestart(_pendingUpdate);
        }
        catch (Exception ex)
        {
            System.Diagnostics.Trace.WriteLine(ex);
            UpdateAvailableButton.IsEnabled = true;
            PickerStatusLabel.Text = "Update failed to apply — try again later.";
        }
    }

    private void RefreshGameWindowStatus()
    {
        var gameWindow = _windowCaptureService.TryFindGameWindow();
        NoticeGameProcess(gameWindow?.ProcessId);
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

    /// <summary>
    /// A restarted client always comes back with a zeroed Play Report, so an
    /// observed restart is proof of a reset — no inference from the figures,
    /// and no dependence on a tick landing in any particular window. The
    /// decision itself lives in <see cref="GameProcessWatch"/>, which is
    /// testable without Windows.
    /// </summary>
    private void NoticeGameProcess(int? processId)
    {
        // Only asked for when no window was found, and only to tell an exited
        // client from one that is briefly without a usable window.
        var followedAlive = true;
        if (processId is null && _gameWatch.FollowedProcessId is int followed)
        {
            followedAlive = _windowCaptureService.IsGameProcessRunning(followed);
        }

        if (!_gameWatch.Notice(processId, followedAlive))
        {
            return;
        }

        // The lock is left to SaveLock: the restarted panel reads below the
        // saved XP, so it releases itself. Clearing it here would only add a
        // way to lose it wrongly.
        _sessionStore.NewSession();
        _lastReport = null;
        _lastComparison = null;
        ShowLiveStatus(LiveStatus.Idle());
        RefreshSessionStatus();
        RefreshPollStatus("Game restarted — the Play Report is counting from zero again.");
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
        _liveStatus = status;
        LiveLight.Fill = LightBrush(status.Light);
        LiveLightLabel.Text = status.Light == TrafficLight.Idle ? "Idle" : status.Light.ToString();
        LiveDetailLabel.Text = status.Detail;
        var rates = LiveRates.Format(status.Report, DisplayRateUnit);
        LiveRatesLabel.Text = rates;
        LiveRatesLabel.Visibility = string.IsNullOrEmpty(rates)
            ? Visibility.Collapsed
            : Visibility.Visible;
        LiveValuesLabel.Text = LiveStatus.FormatValues(status.Report);
    }

    // Resolved from the theme rather than rebuilt from literals: these were the
    // palette's own Confirm Green and Alarm Red spelled a second way, so a
    // palette edit used to change every surface except this one.
    private Brush LightBrush(TrafficLight light) => (Brush)FindResource(light switch
    {
        TrafficLight.Green => "ConfirmGreenBrush",
        TrafficLight.Orange => "CautionAmberBrush",
        TrafficLight.Red => "AlarmRedBrush",
        _ => "IdleGrayBrush",
    });

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

            // A capture taken just now is a live reading of the panel.
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

        // Re-reading whatever is on disk: it may be minutes or hours old.
        await RunParseAsync(capturePath, fromPoll: false, inspectOnly: true);
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

        await RunParseAsync(dialog.FileName, fromPoll: false, inspectOnly: true);
    }

    private void NewSessionButton_Click(object sender, RoutedEventArgs e)
    {
        // Also clears the comparison baseline, which the version of this handler
        // deleted in f05353e did not: _lastReport/_lastComparison arrived with
        // that same commit, and leaving them behind would start the next session
        // comparing against the previous one's last frame. The game-restart path
        // resets exactly this set for the same reason.
        _sessionStore.NewSession();
        _lastReport = null;
        _lastComparison = null;
        ShowLiveStatus(LiveStatus.Idle());
        RefreshSessionStatus();
    }

    private async void StartStopButton_Click(object sender, RoutedEventArgs e)
    {
        if (_polling.IsRunning)
        {
            StopTracking("Stopped.");
            return;
        }

        // Starting a reading run means starting fresh comparisons. Safe because
        // the buffer no longer feeds the save (one frame does) and the save lock
        // lives in saved_logs, so this cannot unlock anything. It is also the
        // manual way out if the baseline ever goes stale.
        _sessionStore.NewSession();
        _lastComparison = null;
        _polling.Start();
        _pollCts = new CancellationTokenSource();
        StartStopButton.Content = "Stop reading";
        RefreshPollStatus("Starting…");
        _pollTimer.Start();
        await LoadSettingsAsync(keepExistingOnFailure: true);
        await RunPollTickAsync();
    }

    private void StopTracking(string message)
    {
        _polling.Stop();
        _pollTimer.Stop();
        _pollCts.Cancel();
        StartStopButton.Content = "Start reading";
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
            await RunParseAsync(outputPath, fromPoll: true, cancellationToken: _pollCts.Token);
        }
        catch (OperationCanceledException)
        {
            // Stop was pressed mid-tick; nothing to report.
        }
        catch (Exception ex)
        {
            // The timer discards this Task, so without a catch a throw here
            // (a locked session.db, a capture that failed inside the pipeline)
            // would be swallowed whole and tracking would look healthy while
            // silently doing nothing every 10s.
            RefreshPollStatus($"Tick failed: {ex.Message}");
            ShowLiveStatus(LiveStatus.ParseFailed(ex.Message));
        }
        finally
        {
            Interlocked.Exchange(ref _pollTickBusy, 0);
        }
    }

    /// <param name="inspectOnly">
    /// The image is a file the user picked, not a reading of the panel as it is
    /// right now. An old screenshot is indistinguishable from a fresh reset, so
    /// it must never reach the buffer or the save.
    /// </param>
    private async Task RunParseAsync(
        string imagePath,
        bool fromPoll,
        bool inspectOnly = false,
        CancellationToken cancellationToken = default)
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
                // A tick that finished after Stop compared nothing, so the
                // previous verdict must not carry over onto this frame.
                _lastComparison = tick.Tracking ? tick.Outcome : null;

                if (tick.Appended && tick.Outcome == MonotonicityOutcome.Reset)
                {
                    ParseStatusLabel.Text += "\n\n" + tick.Message;
                }
                else if (!tick.Appended && tick.Tracking)
                {
                    ParseStatusLabel.Text += "\n\n" + tick.Message;
                }
            }
            else if (inspectOnly)
            {
                ParseStatusLabel.Text += "\n\nInspected only — the live session was not touched.";
                return;
            }
            else
            {
                var accepted = _sessionStore.TryAccept(result.Report);
                _lastComparison = accepted.Outcome;
                if (!accepted.Appended)
                {
                    ParseStatusLabel.Text += $"\n\nDiscarded: {accepted.Reason}";
                }
            }

            // The save is built from this frame, not from the buffer, so the
            // latest parse is kept even when the buffer rejected it — the gate
            // is what decides whether it may be posted.
            _lastReport = result.Report;
            _lastReportAt = DateTimeOffset.UtcNow;

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
