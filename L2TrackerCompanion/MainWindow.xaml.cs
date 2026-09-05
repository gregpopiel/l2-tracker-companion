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
    private readonly LastCharacterStore _lastCharacter = LastCharacterStore.GetDefault();
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
    private LiveStatusSnapshot _liveStatus = LiveStatus.Idle();
    private bool _saveInFlight;
    private readonly SaveConfirmationHold _saveConfirmation = new();
    private bool _holdEmptySpot;
    private bool _spotsLoaded;
    private AreaInfo? _worldArea;
    private IReadOnlyList<AreaInfo>? _areas;
    private readonly LocationChangeWatch _locationWatch = new();
    private bool _isAdmin;
    private string? _userId;
    private readonly GameProcessWatch _gameWatch = new();
    private readonly Dictionary<string, ImageSource> _statusDotIcons = new();
    private readonly ImageSource? _appIcon = StatusDotIcon.TryLoadAppIcon();
    private UpdateInfo? _pendingUpdate;
    private bool _updateCheckInFlight;

    public MainWindow()
    {
        InitializeComponent();
        Closed += OnClosed;
        Loaded += (_, _) => _ = RestoreAuthAsync();
        Loaded += (_, _) => _ = CheckForUpdatesAsync(autoApply: true);

        // The buffer only exists to compare one read against the previous
        // one within a run, and anything left over from the last run is stale
        // by definition — the panel may have been reset while we were closed.
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
        // "All" from launch; the real areas arrive with the first sign-in.
        FillAreaFilter(null);
        ShowLiveStatus(LiveStatus.Idle());
        ApplyLoadedMode();
        AppVersionLabel.Text = $"Version {_updates.CurrentVersion}";
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
        ShowLiveStatus(_liveStatus);
        ClearPickers(SessionPickers.SignInToLoad);

        // Unsaved snapshots belong to the account that produced them — the next
        // token pasted at the gate may be a different one. Confirmed above.
        _sessionStore.NewSession();
        _saveConfirmation.Release();
        HideLocationChange();
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
            _userId = result.UserId;
            ShowWorkspace(result.Message);
            BindCharacters(result.Characters);
            _ = LoadSettingsAsync();
            return;
        }

        ShowLiveStatus(_liveStatus);
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
        // The stored pick survives on disk; only the account it belongs to is forgotten,
        // so signing back in as the same user still restores it.
        _userId = null;
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

    // Resolved from the theme rather than a literal color, so a palette edit
    // changes this surface too. Collapsed when empty rather than left as a
    // blank line.
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
        // The remembered pick, when the account still has that character — otherwise
        // the first one, which is also what a fresh install and a deleted character get.
        var remembered = _lastCharacter.TryLoad(_userId);
        var selected = characters.FirstOrDefault(c => c.Id == remembered) ?? characters.FirstOrDefault();

        _suppressPickerEvents = true;
        try
        {
            CharacterCombo.ItemsSource = characters;
            CharacterCombo.IsEnabled = characters.Count > 0;
            CharacterCombo.SelectedItem = selected;
            SpotCombo.ItemsSource = null;
            SpotCombo.SelectedItem = null;
            SpotCombo.IsEnabled = false;
            ClearSpotButton.IsEnabled = false;
            BonusBox.IsEnabled = true;
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
        _holdEmptySpot = false;
        _spotsLoaded = false;
        _worldArea = null;
        _areas = null;
        _suppressPickerEvents = true;
        try
        {
            CharacterCombo.ItemsSource = null;
            CharacterCombo.SelectedItem = null;
            CharacterCombo.IsEnabled = false;
            SpotCombo.ItemsSource = null;
            SpotCombo.SelectedItem = null;
            SpotCombo.IsEnabled = false;
            ClearSpotButton.IsEnabled = false;
            HideSpotResolveHint();
            BonusBox.Text = string.Empty;
            BonusBox.IsEnabled = false;
            HideBonusHint();
        }
        finally
        {
            _suppressPickerEvents = false;
        }

        // Sets the flag itself, so it cannot run inside the block above.
        FillAreaFilter(null);
        PickerStatusLabel.Text = message;
    }

    private void CharacterCombo_SelectionChanged(object sender, System.Windows.Controls.SelectionChangedEventArgs e)
    {
        if (_suppressPickerEvents)
        {
            return;
        }

        // Guarded above, so only a user's own pick is remembered — never the
        // programmatic selection BindCharacters and ClearPickers make.
        if (SelectedCharacter is not null)
        {
            _lastCharacter.Save(_userId, SelectedCharacter.Id);
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

        if (SelectedSpot is not null)
        {
            _holdEmptySpot = false;
        }

        RefreshSaveEnabled();
    }

    private void ClearSpotButton_Click(object sender, RoutedEventArgs e)
    {
        _holdEmptySpot = true;
        _suppressPickerEvents = true;
        try
        {
            SpotCombo.SelectedItem = null;
        }
        finally
        {
            _suppressPickerEvents = false;
        }

        RefreshSaveEnabled();
    }

    /// <summary>
    /// Whether a verified read may be saved, and what it would post.
    /// A rejected tick falls back to the last frame that itself passed the gate.
    /// </summary>
    private SaveGateDecision CurrentGate()
    {
        var last = _sessionStore.Last();
        var held = _sessionStore.LastSavable();
        return SaveGate.EvaluateWithHold(
            last?.Report,
            last?.CapturedAt ?? default,
            lastComparison: null,
            held?.Report,
            held?.CapturedAt ?? default,
            currentAccepted: last is not null);
    }

    private LocationStabilityDecision CurrentLocationStability()
        => LocationStability.Evaluate(_sessionStore.List().Select(row => row.Report.LocationHint));

    private SpotResolveDecision CurrentSpotResolve(SaveGateDecision? gate = null)
    {
        var stability = CurrentLocationStability();
        gate ??= CurrentGate();
        return SpotResolve.Evaluate(
            SelectedSpot,
            stability.IsStable ? stability.CanonicalName : null,
            (gate.Source ?? _sessionStore.Last()?.Report)?.LocationHint,
            SpotCombo.ItemsSource as IEnumerable<SpotInfo>,
            _spotsLoaded,
            _worldArea);
    }

    private void ShowSpotResolveHint(SpotResolveDecision resolve, LocationStabilityDecision stability)
    {
        // A picked spot leaves nothing to resolve, but the redesign's single
        // hint slot under the field still has a job: confirm what the panel
        // itself is reading, since a stale/mismatched pick would otherwise be
        // silent here (the actual save-target warning already lives in
        // PickerStatusLabel via SpotLocationWarning).
        var text = resolve.Kind == SpotResolveKind.UseSelected
            ? DetectedLocationHint()
            : resolve.Hint(stability.SampleCount, LocationStability.WindowSize);
        SpotResolveHintLabel.Text = text;
        SpotResolveHintRow.Visibility = string.IsNullOrEmpty(text)
            ? Visibility.Collapsed
            : Visibility.Visible;
    }

    private string DetectedLocationHint()
    {
        if (!_polling.IsRunning)
        {
            return string.Empty;
        }

        var hint = CurrentGate().Source?.LocationHint;
        return string.IsNullOrWhiteSpace(hint) ? string.Empty : $"Detected in-game: {hint}";
    }

    private void HideSpotResolveHint()
    {
        SpotResolveHintLabel.Text = string.Empty;
        SpotResolveHintRow.Visibility = Visibility.Collapsed;
    }

    private void RefreshSaveEnabled()
    {
        ClearSpotButton.IsEnabled = SelectedSpot is not null && SpotCombo.IsEnabled;
        var stability = CurrentLocationStability();
        var gate = CurrentGate();
        var resolve = CurrentSpotResolve(gate);
        ShowSpotResolveHint(resolve, stability);

        var pickersReady = SessionPickers.SaveReady(SelectedCharacter, resolve);

        // Save mode whenever tracking is on (even before the first read) or
        // there's a held frame from an earlier Stop — otherwise Start.
        var saveMode = _polling.IsRunning || gate.CanSave;
        MainActionButton.Content = saveMode ? "Save & send session" : "Start tracking";
        // A poll tick lands here every 10s, including while a save is awaiting
        // its response — without this the button would re-arm mid-POST and a
        // second click would duplicate the log.
        MainActionButton.IsEnabled = saveMode
            ? pickersReady && gate.CanSave && !_saveInFlight
            : true;

        // Save mode can leave the button disabled for reasons the player
        // cannot clear from here (no character on the account, a location
        // that never settles), and Stop no longer discards the held frame —
        // so save mode always keeps a second, always-enabled way out: Stop
        // while a run is on, and a fresh run once it is not.
        SecondaryActionLink.Content = _polling.IsRunning ? "Stop tracking" : "Start a new session";
        SecondaryActionLink.Visibility = saveMode ? Visibility.Visible : Visibility.Collapsed;

        // Button enablement always updates. The status line does not: a tick
        // during POST used to replace "Saving session…" with "Ready to save…",
        // and after a 2xx the lock reason replaced the confirmation.
        if (_saveConfirmation.FreezePickerStatus(_saveInFlight))
        {
            return;
        }

        if (!SessionPickers.CharacterChosen(SelectedCharacter))
        {
            return;
        }

        if (!gate.CanSave)
        {
            PickerStatusLabel.Text = gate.BlockReason ?? string.Empty;
            return;
        }

        if (!resolve.CanSave)
        {
            // Same message ShowSpotResolveHint just put under the Spot field
            // above — no need to repeat it a second time under Save.
            PickerStatusLabel.Text = string.Empty;
            return;
        }

        // Readiness itself doesn't need a sentence — the Save button already
        // shows that by unlocking. This line is only for warnings worth a
        // second look even though Save is enabled.
        //
        // A spot picked earlier in the session (or auto-picked once) always
        // wins over a later location change — see SpotLocationWarning. That
        // is correct for where the save goes, but the player still needs a
        // nudge that it happened.
        var warnings = gate.Warnings.ToList();
        var spotWarning = SpotLocationWarning.Evaluate(
            SelectedSpot,
            stability.IsStable ? stability.CanonicalName : null);
        if (spotWarning is not null)
        {
            warnings.Add(spotWarning);
        }

        PickerStatusLabel.Text = string.Join(" · ", warnings);
    }

    private void ApplyLocationHint(string? hint)
    {
        if (_holdEmptySpot || SelectedSpot is not null)
        {
            return;
        }

        var stability = CurrentLocationStability();
        if (!stability.IsStable || !SpotMatch.SameName(hint, stability.CanonicalName))
        {
            return;
        }

        var spots = SpotCombo.ItemsSource as IEnumerable<SpotInfo>;
        var match = SpotMatch.ExactName(stability.CanonicalName, spots);
        if (match is null)
        {
            return;
        }

        _suppressPickerEvents = true;
        try
        {
            SpotCombo.SelectedItem = match;
        }
        finally
        {
            _suppressPickerEvents = false;
        }
    }

    private async Task LoadSpotsAsync(CharacterInfo? character)
    {
        _spotsCts.Cancel();
        _spotsCts = new CancellationTokenSource();
        var cancellationToken = _spotsCts.Token;
        _holdEmptySpot = false;
        _spotsLoaded = false;

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

        // Drop the old character's ranking now rather than at the end: every
        // early return below (no character, expired token, failed fetch) would
        // otherwise leave it on screen as if it described the new one.
        ShowLiveStatus(_liveStatus);

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

        var call = await Api.GetSpotsAsync(token, character.Id, cancellationToken);
        if (cancellationToken.IsCancellationRequested)
        {
            return;
        }

        if (!call.Success || call.Value is null)
        {
            _spotsLoaded = false;
            RefreshSaveEnabled();
            PickerStatusLabel.Text = $"Could not load spots: {call.Error}";
            return;
        }

        await EnsureAreasAsync(token, cancellationToken);
        if (cancellationToken.IsCancellationRequested)
        {
            return;
        }

        _spotsLoaded = true;
        _suppressPickerEvents = true;
        try
        {
            SpotCombo.ItemsSource = call.Value;
            SpotCombo.SelectedItem = null;
            SpotCombo.IsEnabled = true;
        }
        finally
        {
            _suppressPickerEvents = false;
        }

        ApplyLocationHint(_sessionStore.Last()?.Report.LocationHint);
        RefreshSaveEnabled();

        // The benchmark ranks against this list, so it can only be right once
        // the list is loaded.
        ShowLiveStatus(_liveStatus);
    }

    /// <summary>
    /// Fetches the account's areas once: World is what auto-created spots are
    /// filed under, and the whole list fills the benchmark's area picker.
    /// A failed fetch leaves the picker at "All" — the ranking itself
    /// needs no areas at all.
    /// </summary>
    private async Task EnsureAreasAsync(string token, CancellationToken cancellationToken)
    {
        if (_areas is not null)
        {
            return;
        }

        var areas = await Api.GetAreasAsync(token, cancellationToken);
        if (!areas.Success || areas.Value is null)
        {
            return;
        }

        _areas = areas.Value;
        _worldArea = WorldArea.Find(_areas);
        FillAreaFilter(_areas);
    }

    /// <summary>
    /// The areas belong to the account, not to one character, so the picker is
    /// built once and a chosen area survives switching characters.
    /// </summary>
    private void FillAreaFilter(IEnumerable<AreaInfo>? areas)
    {
        var chosen = (AreaFilterCombo.SelectedItem as AreaChoice)?.AreaId;
        var choices = AreaChoice.Build(areas);
        _suppressPickerEvents = true;
        try
        {
            AreaFilterCombo.ItemsSource = choices;
            AreaFilterCombo.SelectedItem =
                choices.FirstOrDefault(choice => choice.AreaId == chosen) ?? AreaChoice.All;
        }
        finally
        {
            _suppressPickerEvents = false;
        }
    }

    private void AreaFilterCombo_SelectionChanged(object sender, System.Windows.Controls.SelectionChangedEventArgs e)
    {
        if (_suppressPickerEvents)
        {
            return;
        }

        // Same read, narrower field — no re-parse needed.
        ShowLiveStatus(_liveStatus);
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
            ShowLiveStatus(_liveStatus);
            ShowBonusHint(
                $"Could not load default bonus ({call.Error ?? "empty response"}). Using {fallback.DefaultBonus}.");
            return;
        }

        BonusBox.Text = call.Value.DefaultBonus.ToString(CultureInfo.InvariantCulture);
        BonusBox.IsEnabled = true;
        ShowLiveStatus(_liveStatus);
        HideBonusHint();
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

    private async Task SaveSessionAsync()
    {
        if (_saveInFlight)
        {
            return;
        }

        if (!SessionPickers.SaveReady(SelectedCharacter, CurrentSpotResolve()))
        {
            RefreshSaveEnabled();
            return;
        }

        var gate = CurrentGate();
        if (!gate.CanSave || gate.Totals is null)
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
        _saveConfirmation.BeginSave();
        MainActionButton.IsEnabled = false;
        PickerStatusLabel.Text = "Saving session…";
        try
        {
            var resolve = CurrentSpotResolve();
            var ensured = await EnsureSpotForSaveAsync(token, resolve);
            if (ensured is null)
            {
                return;
            }

            var spot = ensured.Spot;
            var totals = gate.Totals;
            var request = new FarmLogRequest(
                CharacterId: SelectedCharacter!.Id,
                SpotId: spot.Id,
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
                PickerStatusLabel.Text = await FormatSaveFailureAsync(token, call.Error, ensured);
                return;
            }

            // The log exists on the server from here on. Nothing below may put
            // the button back or throw out of this handler.
            MainActionButton.IsEnabled = false;

            var created = ensured.Created;
            var wasTracking = _polling.IsRunning;
            _saveConfirmation.Saved();
            ResetLocalSessionAfterSave();
            PickerStatusLabel.Text =
                $"Saved farm log #{call.Value!.Id} for {SelectedCharacter.Name} at {spot.Label}"
                + (created ? " (new World spot)" : "")
                + $" ({totals.XpFarmed}k XP, {totals.Minutes} min). "
                + (wasTracking
                    ? "Tracking stopped. Start tracking to save another log."
                    : "Start tracking to save another log.");

            if (SaveConfirmationHold.ShouldStopTracking(wasTracking, saved: true))
            {
                StopTracking("Session saved.");
            }
        }
        finally
        {
            _saveInFlight = false;
            RefreshSaveEnabled();
        }
    }

    /// <summary>
    /// After a 2xx, drop every companion-side read. Character, spot and
    /// bonus stay. Start tracking to capture the panel again.
    /// </summary>
    private void ResetLocalSessionAfterSave()
    {
        _sessionStore.NewSession();
        HideLocationChange();
        ShowLiveStatus(LiveStatus.Idle());
        SessionStatusLabel.Text = SessionStore.FormatInspect(_sessionStore.List(), _sessionStore.Path);
    }

    /// <summary>
    /// The spot row the farm log should attach to — the picker, an exact
    /// name match, or a newly created World spot. A failed create retries
    /// GET spots in case the name landed from a race.
    /// </summary>
    private async Task<EnsuredSpot?> EnsureSpotForSaveAsync(string token, SpotResolveDecision resolve)
    {
        if (resolve.Kind is SpotResolveKind.UseSelected or SpotResolveKind.UseExisting)
        {
            return resolve.Spot is null ? null : new EnsuredSpot(resolve.Spot, Created: false);
        }

        if (resolve.Kind != SpotResolveKind.CreateWorld
            || string.IsNullOrWhiteSpace(resolve.Name)
            || resolve.WorldArea is null)
        {
            RefreshSaveEnabled();
            return null;
        }

        var world = resolve.WorldArea;
        var created = await Api.PostSpotAsync(token, resolve.Name, world.Id);
        if (created.Success && created.Value is not null)
        {
            var spot = WithWorldArea(created.Value, world);
            await MergeCreatedSpotAsync(spot);
            return new EnsuredSpot(spot, Created: true);
        }

        var spots = await Api.GetSpotsAsync(token, SelectedCharacter!.Id);
        if (spots.Success)
        {
            var match = SpotMatch.ExactName(resolve.Name, spots.Value);
            if (match is not null)
            {
                await MergeCreatedSpotAsync(match);
                return new EnsuredSpot(match, Created: false);
            }
        }

        PickerStatusLabel.Text = $"Could not create spot: {created.Error}";
        return null;
    }

    private async Task<string> FormatSaveFailureAsync(string token, string? error, EnsuredSpot ensured)
    {
        var message = $"Save failed: {error}";
        if (!ensured.Created)
        {
            return message;
        }

        var undone = await Api.DeleteSpotAsync(token, ensured.Spot.Id);
        if (undone.Success)
        {
            return message + " The new spot was not kept.";
        }

        return message
            + $" Spot \"{ensured.Spot.Name}\" was created — delete it on the website if you do not want it.";
    }

    private sealed record EnsuredSpot(SpotInfo Spot, bool Created);

    private static SpotInfo WithWorldArea(SpotInfo spot, AreaInfo world)
        => new(spot.Id, spot.Name, spot.AreaId, new SpotAreaInfo(world.Id, world.Name));

    private async Task MergeCreatedSpotAsync(SpotInfo spot)
    {
        var token = _auth.TryLoadToken();
        if (token is null || SelectedCharacter is null)
        {
            return;
        }

        var call = await Api.GetSpotsAsync(token, SelectedCharacter.Id);
        if (!call.Success || call.Value is null)
        {
            return;
        }

        var hold = _holdEmptySpot;
        var selectedId = SelectedSpot?.Id;
        _suppressPickerEvents = true;
        try
        {
            SpotCombo.ItemsSource = call.Value;
            SpotCombo.IsEnabled = true;
            if (hold || selectedId is null)
            {
                SpotCombo.SelectedItem = null;
            }
            else
            {
                SpotCombo.SelectedItem = call.Value.FirstOrDefault(s => s.Id == selectedId) ?? spot;
            }
        }
        finally
        {
            _suppressPickerEvents = false;
        }

        _holdEmptySpot = hold;
        ShowLiveStatus(_liveStatus);
    }

    private void OnClosed(object? sender, EventArgs e)
    {
        _polling.Stop();
        _pollTimer.Stop();
        _updateTimer.Stop();
        _pollCts.Cancel();
        _spotsCts.Cancel();
        _updateCts.Cancel();

        // Only safe to dispose here: mid-run these are replaced rather than
        // disposed, because a tick still in flight holds the old token and
        // registering on a disposed source throws.
        _pollCts.Dispose();
        _spotsCts.Dispose();
        _updateCts.Dispose();

        _sessionStore.Dispose();
    }

    /// <summary>
    /// Silent, background check — never surfaces a failure (offline, GitHub hiccup),
    /// never restarts on its own except for <paramref name="autoApply"/>. Otherwise,
    /// once an update is downloaded, <see cref="UpdateAvailableButton"/> appears and
    /// the actual apply/restart waits for an explicit click, since the app can be
    /// mid-poll or holding an unsaved farm-log delta.
    /// </summary>
    /// <param name="autoApply">
    /// True only for the launch-time call: at that point the session was just reset
    /// (see constructor) and nothing can be mid-poll or mid-save yet, so a found
    /// update is applied and the app restarts immediately with no click needed.
    /// Every later (periodic) call passes false and falls back to the button.
    /// </param>
    private async Task CheckForUpdatesAsync(bool autoApply = false)
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

            if (autoApply)
            {
                try
                {
                    _updates.ApplyAndRestart(updateInfo);
                }
                catch (Exception ex)
                {
                    System.Diagnostics.Trace.WriteLine(ex);
                }

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
                : "Game window not found";
        }
        else
        {
            // One line, not four: this label lives in the status bar now, and
            // a multi-line string there grows the bar and squeezes the tab
            // above it. StatusBarText's tooltip carries whatever gets trimmed.
            GameWindowStatusLabel.Text = _options.DebugMode
                ? $"HWND 0x{gameWindow.Hwnd.ToInt64():X} · "
                  + $"{gameWindow.Title} · "
                  + $"{gameWindow.Width} x {gameWindow.Height} · "
                  + $"PID {gameWindow.ProcessId}"
                : "Lineage II detected.";
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

        _sessionStore.NewSession();
        _saveConfirmation.Release();
        HideLocationChange();
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
            ? $"Tracking every {(int)PollingLoop.Interval.TotalSeconds}s"
            : "Not tracking.";
        PollStatusLabel.Text = string.IsNullOrWhiteSpace(message)
            ? prefix
            : prefix + " · " + message;
    }

    private void ShowLiveStatus(LiveStatusSnapshot status)
    {
        // Same report Save would post. Nothing savable → no totals.
        status = LiveStatus.ForDisplay(status, CurrentGate().Source);

        _liveStatus = status;
        UpdateTitleBarStatusIcon(status.Light);

        // Any read/capture problem while tracking gets the banner treatment
        // — a closed Lamp panel, an unread field, "game not running"
        // mid-session, a contradicting read, all read the same way to the
        // player: something needs attention. Nothing else in the card shows
        // status.Detail, so a Green/Idle tick has nothing further to say.
        if (_polling.IsRunning && status.Light is TrafficLight.Red or TrafficLight.Orange)
        {
            ShowReadProblem(status.Detail);
        }
        else
        {
            HideReadProblem();
        }

        var report = status.Report;
        var showData = report is not null;
        long? xpPerHour = null;
        long? adenaPerHour = null;
        if (report is not null)
        {
            xpPerHour = LiveRates.PerHour(report.Xp, report.Minutes);
            adenaPerHour = LiveRates.PerHour(report.Adena, report.Minutes);
        }

        XpPerHourValue.Text = Amt(xpPerHour);
        AdenaPerHourValue.Text = Amt(adenaPerHour);

        var benchmark = showData && _spotsLoaded
            ? SpotBenchmark.Evaluate(
                xpPerHour,
                adenaPerHour,
                SpotCombo.ItemsSource as IEnumerable<SpotInfo>,
                (AreaFilterCombo.SelectedItem as AreaChoice)?.AreaId)
            : null;
        SetRankBadge(XpRankBadge, XpRankBadgeLabel, benchmark?.XpRank, benchmark?.RankedSpots);
        SetRankBadge(AdenaRankBadge, AdenaRankBadgeLabel, benchmark?.AdenaRank, benchmark?.RankedSpots);

        SessionXpValue.Text = Amt(report?.Xp);
        SessionAdenaValue.Text = Amt(report?.Adena);
        SessionPlayTimeValue.Text = report?.Minutes is int minutes ? $"{minutes} min" : "0 min";
        RedLampValue.Text = Amt(report?.RedLampXp);
        PurpleLampValue.Text = Amt(report?.PurpleLampXp);
        BlueLampValue.Text = Amt(report?.BlueLampXp);
        GreenLampValue.Text = Amt(report?.GreenLampXp);

        LiveValuesLabel.Text = showData ? LiveStatus.FormatValues(report) : string.Empty;
    }

    /// <summary>
    /// "#N of M spots", hidden entirely rather than showing "#0 of 0" until
    /// there is something real to place — see the handoff's idle-badge
    /// decision.
    /// </summary>
    private static void SetRankBadge(
        System.Windows.Controls.Border badge,
        System.Windows.Controls.TextBlock label,
        int? rank,
        int? total)
    {
        if (rank is null || total is null)
        {
            badge.Visibility = Visibility.Collapsed;
            return;
        }

        label.Text = $"#{rank} of {total} spot{(total == 1 ? string.Empty : "s")}";
        badge.Visibility = Visibility.Visible;
    }

    private static string Amt(long? value)
        => value?.ToString("N0", CultureInfo.InvariantCulture) ?? "0";

    /// <summary>
    /// The title bar's status dot moved here from the old Live status card —
    /// a swapped <see cref="Window.Icon"/> rather than custom window chrome,
    /// so the rest of the title bar (drag, minimize/maximize/close) stays
    /// entirely native. The dot is badged onto the app's own mark rather than
    /// replacing it, because this icon is also the taskbar button and the
    /// Alt+Tab entry. Orange (a non-fatal read warning) reads as the same
    /// "problem" red as a hard error: the handoff's title-bar dot only has
    /// three states, and both already mean "look at the card."
    /// </summary>
    private void UpdateTitleBarStatusIcon(TrafficLight light)
    {
        // Resolved from the theme rather than re-spelled as hex here, so a
        // palette edit changes this surface too — same pattern as
        // SetAuthStatus. Cached per brush: reassigning the same frozen source
        // is a no-op, but rendering a fresh one every tick would not be.
        var brushKey = light switch
        {
            TrafficLight.Green => "ConfirmGreenBrush",
            TrafficLight.Red or TrafficLight.Orange => "AlarmRedBrush",
            _ => "IdleGrayBrush",
        };

        if (!_statusDotIcons.TryGetValue(brushKey, out var icon))
        {
            icon = StatusDotIcon.Render(_appIcon, ((SolidColorBrush)FindResource(brushKey)).Color);
            _statusDotIcons[brushKey] = icon;
        }

        Icon = icon;
    }

    /// <summary>
    /// A settled move to another location, shown once. Only a reminder to
    /// restart the in-game Play Report — nothing is blocked or hidden.
    /// </summary>
    private void ShowLocationChange()
    {
        var stability = CurrentLocationStability();
        var message = _locationWatch.Notice(stability.IsStable ? stability.CanonicalName : null);
        if (message is null)
        {
            return;
        }

        LocationChangeLabel.Text = message;
        LocationChangeBanner.Visibility = Visibility.Visible;
    }

    private void HideLocationChange()
    {
        _locationWatch.Reset();
        LocationChangeLabel.Text = string.Empty;
        LocationChangeBanner.Visibility = Visibility.Collapsed;
    }

    /// <summary>
    /// The handoff's second banner: something is wrong with the current read
    /// while tracking is on — a contradicting read, a closed Lamp panel, an
    /// unread field, "game not running" mid-session — so the card is holding
    /// the last verified frame instead. Driven entirely from ShowLiveStatus.
    /// </summary>
    private void ShowReadProblem(string message)
    {
        ReadProblemLabel.Text = message;
        ReadProblemBanner.Visibility = Visibility.Visible;
    }

    private void HideReadProblem()
    {
        ReadProblemLabel.Text = string.Empty;
        ReadProblemBanner.Visibility = Visibility.Collapsed;
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

            // A capture taken just now is a live read of the panel.
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

    /// <summary>
    /// The merged action button: Save whenever tracking is on or there's a
    /// held frame worth saving (see RefreshSaveEnabled), Start otherwise —
    /// same condition the button's own Content/IsEnabled are driven by.
    /// </summary>
    private async void MainActionButton_Click(object sender, RoutedEventArgs e)
    {
        if (_polling.IsRunning || CurrentGate().CanSave)
        {
            await SaveSessionAsync();
        }
        else
        {
            await StartTrackingAsync();
        }
    }

    /// <summary>
    /// Stop while a run is on; otherwise start a fresh one, discarding the
    /// frame the previous run left behind (StartTrackingAsync's NewSession is
    /// the discard) — confirmed first, since that read is still savable.
    /// </summary>
    private async void SecondaryActionLink_Click(object sender, RoutedEventArgs e)
    {
        if (_polling.IsRunning)
        {
            StopTracking("Stopped.");
            return;
        }

        // Same rationale as SignOutButton_Click: a fresh run clears the store
        // the pending save is still working from, and that save then clears it
        // a second time on its way out — wiping the new run's first reads and
        // leaving its every later tick discarded (SaveConfirmationHold).
        if (_saveInFlight)
        {
            PickerStatusLabel.Text = "A save is still in progress — try again in a moment.";
            return;
        }

        var gate = CurrentGate();
        if (gate.CanSave
            && gate.Totals is not null
            && !ConfirmDiscardSession(gate.Totals, "Starting a new session"))
        {
            return;
        }

        await StartTrackingAsync();
    }

    private async Task StartTrackingAsync()
    {
        // Starting a tracking run means starting fresh comparisons. Save and
        // the live totals only hold verified frames from this run.
        _saveConfirmation.Release();
        _sessionStore.NewSession();
        // Where the player is now is a first sighting for this run, not a move
        // from wherever the previous run ended — they may have restarted the
        // Play Report themselves in between.
        HideLocationChange();
        ShowLiveStatus(LiveStatus.Idle());
        _polling.Start();
        _pollCts = new CancellationTokenSource();
        RefreshSaveEnabled();
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
        // ShowLiveStatus gates the read-problem banner on a running loop, so
        // it has to be re-run here: a banner raised by the last tick ("Game
        // not running.", a contradicting read) would otherwise stay up over a
        // stopped session until the next run started.
        ShowLiveStatus(_liveStatus);
        RefreshSaveEnabled();
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
                // Drop back to the bare "Tracking every 10s": leaving
                // "Capturing…" up would claim work is in progress for as long
                // as capture keeps failing. The reason itself is not the
                // bottom bar's job — ShowLiveStatus below surfaces it through
                // ReadProblemBanner in the messages section.
                RefreshPollStatus(string.Empty);
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
            // silently doing nothing every 10s. Not the bottom bar — reported
            // through ReadProblemBanner via ShowLiveStatus below, with the
            // bar dropped back off "Capturing…" so it stops implying work.
            RefreshPollStatus(string.Empty);
            ShowLiveStatus(LiveStatus.ParseFailed(ex.Message));
        }
        finally
        {
            Interlocked.Exchange(ref _pollTickBusy, 0);
        }
    }

    /// <param name="inspectOnly">
    /// The image is a file the user picked, not a read of the panel as it is
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
            // Capture once / Parse last / a poll tick finishing after Save
            // would otherwise put the live card back as if the session were
            // still open. Debug parse text above is enough.
            if (_saveConfirmation.IgnoreIncomingReads)
            {
                return;
            }

            if (!result.Success || result.Report is null)
            {
                // Not the bottom bar — ShowLiveStatus already surfaces this
                // through ReadProblemBanner in the messages section. The bar
                // only drops back off "Capturing…" so it stops implying work.
                if (fromPoll)
                {
                    RefreshPollStatus(string.Empty);
                }

                ShowLiveStatus(LiveStatus.ParseFailed(result.ErrorMessage ?? "Parse failed"));
                return;
            }

            if (inspectOnly)
            {
                ParseStatusLabel.Text += "\n\nInspected only — the live session was not touched.";
                return;
            }

            var rejected = (string?)null;
            var appended = false;
            if (fromPoll)
            {
                var tick = _polling.Tick(_sessionStore, result.Report);
                // A tick that finished after Stop compared nothing, so the
                // previous verdict must not carry over onto this frame — and
                // must not replace StopTracking's poll-status line either.
                if (!tick.Tracking)
                {
                    return;
                }

                ShowLocationChange();
                appended = tick.Appended;
                if (!tick.Appended)
                {
                    // Not the bottom bar — a rejected tick already shows the
                    // same message via ReadProblemBanner (ShowLiveStatus
                    // below, with LiveStatus.TickRejected). The bar keeps
                    // showing its last accepted tick instead.
                    rejected = tick.Message;
                    ParseStatusLabel.Text += "\n\n" + tick.Message;
                }
                else
                {
                    RefreshPollStatus(tick.Message);
                    if (tick.Outcome == MonotonicityOutcome.Reset)
                    {
                        ParseStatusLabel.Text += "\n\n" + tick.Message;
                    }
                }
            }
            else
            {
                var accepted = _sessionStore.TryAccept(result.Report);
                appended = accepted.Appended;
                if (!accepted.Appended)
                {
                    rejected = $"Discarded: {accepted.Reason}";
                    ParseStatusLabel.Text += $"\n\n{rejected}";
                }
            }

            if (appended)
            {
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
            }

            // Light/detail describe this tick. XP / Adena / rates are the
            // last verified frame — the same numbers Save would post.
            ShowLiveStatus(
                rejected is null
                    ? LiveStatus.FromReport(result.Report)
                    : LiveStatus.TickRejected(rejected));
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
