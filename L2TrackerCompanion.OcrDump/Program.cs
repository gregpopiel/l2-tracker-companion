using L2TrackerCompanion.Api;
using L2TrackerCompanion.Ocr;
using L2TrackerCompanion.Parsing;
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

if (args.Length >= 1 && string.Equals(args[0], "--auth-garbage", StringComparison.OrdinalIgnoreCase))
{
    var auth = new AuthService(TokenStore.GetDefault());
    var garbage = await auth.SignInAsync("not-a-real-jwt");
    Console.WriteLine(garbage.Message);
    Console.WriteLine(auth.HasStoredToken
        ? $"Token still on disk: {auth.TokenPath}"
        : $"No token on disk ({auth.TokenPath})");
    return garbage.Success ? 2 : 0;
}

if (args.Length >= 1 && string.Equals(args[0], "--auth-status", StringComparison.OrdinalIgnoreCase))
{
    var auth = new AuthService(TokenStore.GetDefault());
    if (!auth.HasStoredToken)
    {
        Console.WriteLine("No stored token.");
        return 1;
    }

    var restored = await auth.TryRestoreAsync();
    Console.WriteLine(restored.Message);
    Console.WriteLine(auth.HasStoredToken
        ? $"Token on disk (DPAPI): {auth.TokenPath}"
        : $"Token cleared: {auth.TokenPath}");
    return restored.Success ? 0 : 2;
}

if (args.Length >= 1 && string.Equals(args[0], "--auth", StringComparison.OrdinalIgnoreCase))
{
    if (args.Length < 2)
    {
        Console.Error.WriteLine("Usage: L2TrackerCompanion.OcrDump --auth <jwt>");
        return 1;
    }

    var auth = new AuthService(TokenStore.GetDefault());
    if (args.Length >= 4 && string.Equals(args[2], "--base-url", StringComparison.OrdinalIgnoreCase))
    {
        auth.SetBaseUrl(args[3]);
    }

    var signedIn = await auth.SignInAsync(args[1]);
    Console.WriteLine(signedIn.Message);
    Console.WriteLine(auth.HasStoredToken
        ? $"Token saved (DPAPI): {auth.TokenPath}"
        : $"No token on disk ({auth.TokenPath})");
    return signedIn.Success ? 0 : 2;
}

if (args.Length >= 1 && string.Equals(args[0], "--spots", StringComparison.OrdinalIgnoreCase))
{
    return await RunSpotsAsync();
}

if (args.Length >= 1 && string.Equals(args[0], "--http-smoke", StringComparison.OrdinalIgnoreCase))
{
    return await RunHttpSmokeAsync();
}

if (args.Length >= 1 && string.Equals(args[0], "--save", StringComparison.OrdinalIgnoreCase))
{
    return await RunSaveAsync(args);
}

if (args.Length >= 1 && string.Equals(args[0], "--match-hint", StringComparison.OrdinalIgnoreCase))
{
    if (args.Length < 2)
    {
        Console.Error.WriteLine("Usage: L2TrackerCompanion.OcrDump --match-hint <image.png|hint>");
        return 1;
    }

    return await RunMatchHintAsync(args[1]);
}

if (args.Length >= 1 && string.Equals(args[0], "--new-session", StringComparison.OrdinalIgnoreCase))
{
    using var wiped = new SessionStore(SessionStore.GetDefaultPath());
    wiped.NewSession();
    wiped.ClearSaveLock();
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
        Console.WriteLine();
        Console.WriteLine(LiveStatus.Format(LiveStatus.FromReport(parsed.Report)));
        using var store = new SessionStore(SessionStore.GetDefaultPath());
        var accepted = store.TryAccept(parsed.Report);
        Console.WriteLine();
        if (!accepted.Appended)
        {
            Console.WriteLine($"Discarded: {accepted.Reason}");
        }

        Console.WriteLine(SessionStore.FormatInspect(store.List(), store.Path));
    }
    else
    {
        Console.WriteLine();
        Console.WriteLine(LiveStatus.Format(LiveStatus.ParseFailed(parsed.ErrorMessage ?? "Parse failed")));
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
    Console.Error.WriteLine("       L2TrackerCompanion.OcrDump --auth <jwt>");
    Console.Error.WriteLine("       L2TrackerCompanion.OcrDump --auth-garbage");
    Console.Error.WriteLine("       L2TrackerCompanion.OcrDump --auth-status");
    Console.Error.WriteLine("       L2TrackerCompanion.OcrDump --spots");
    Console.Error.WriteLine("       L2TrackerCompanion.OcrDump --http-smoke");
    Console.Error.WriteLine("       L2TrackerCompanion.OcrDump --save --character-id <id> --spot-id <id>");
    Console.Error.WriteLine("       L2TrackerCompanion.OcrDump --match-hint <image.png|hint>");
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

static async Task<int> RunSpotsAsync()
{
    var auth = new AuthService(TokenStore.GetDefault());
    if (!auth.HasStoredToken)
    {
        Console.WriteLine("No stored token. Sign in first: ./scripts/auth.sh --token '<jwt>'");
        return 1;
    }

    var restored = await auth.TryRestoreAsync();
    Console.WriteLine(restored.Message);
    if (!restored.Success)
    {
        return 2;
    }

    var token = auth.TryLoadToken();
    if (token is null)
    {
        Console.WriteLine("Token missing after restore.");
        return 2;
    }

    var client = TrackerApiClient.Create(auth.BaseUrl);
    if (restored.Characters.Count == 0)
    {
        Console.WriteLine("No characters to load spots for.");
        return 0;
    }

    foreach (var character in restored.Characters)
    {
        var spots = await client.GetSpotsAsync(token, character.Id);
        if (!spots.Success || spots.Value is null)
        {
            Console.WriteLine($"{character.Name} ({character.Id}): failed — {spots.Error}");
            return 2;
        }

        Console.WriteLine($"{character.Name} ({character.Id}): {spots.Value.Count} spot(s)");
        var preview = spots.Value.Take(8).ToArray();
        foreach (var spot in preview)
        {
            Console.WriteLine($"  {spot.Id}\t{spot.Label}");
        }

        if (spots.Value.Count > preview.Length)
        {
            Console.WriteLine($"  … {spots.Value.Count - preview.Length} more");
        }
    }

    var settings = await client.GetSettingsAsync(token);
    if (!settings.Success || settings.Value is null)
    {
        Console.WriteLine($"settings: failed — {settings.Error} (using schema default bonus {UserSettingsInfo.SchemaDefaults.DefaultBonus})");
        return 0;
    }

    Console.WriteLine($"settings: defaultBonus={settings.Value.DefaultBonus} rateUnit={settings.Value.RateUnit ?? UserSettingsInfo.HourValue}");
    return 0;
}

static async Task<int> RunHttpSmokeAsync()
{
    var auth = new AuthService(TokenStore.GetDefault());
    if (!auth.HasStoredToken)
    {
        Console.WriteLine("No stored token. Sign in first: ./scripts/auth.sh --token '<jwt>'");
        return 1;
    }

    var token = auth.TryLoadToken();
    if (token is null)
    {
        Console.WriteLine("Stored token could not be read.");
        return 2;
    }

    var probe = new HttpSmokeHandler(new HttpClientHandler());
    var http = new HttpClient(probe)
    {
        BaseAddress = new Uri(auth.BaseUrl + "/", UriKind.Absolute),
    };
    var client = new TrackerApiClient(http);

    Console.WriteLine($"Base URL: {auth.BaseUrl}");
    Console.WriteLine("(native HttpClient — no Origin header)");
    Console.WriteLine();

    var characters = await client.GetCharactersAsync(token);
    Console.WriteLine(HttpSmoke.Format(probe, "GET /api/characters"));
    if (!HttpSmoke.Passed(probe) || !characters.Success)
    {
        Console.WriteLine(characters.Success ? "Smoke failed." : $"Call failed: {characters.Error}");
        return 2;
    }

    var list = characters.Value ?? [];
    var names = list.Select(c => c.Name).ToArray();
    Console.WriteLine($"  {list.Count} character(s): {(names.Length == 0 ? "(none)" : string.Join(", ", names))}");
    Console.WriteLine();

    var settings = await client.GetSettingsAsync(token);
    Console.WriteLine(HttpSmoke.Format(probe, "GET /api/settings"));
    if (!HttpSmoke.Passed(probe) || !settings.Success)
    {
        Console.WriteLine(settings.Success ? "Smoke failed." : $"Call failed: {settings.Error}");
        return 2;
    }

    Console.WriteLine($"  defaultBonus={settings.Value!.DefaultBonus} rateUnit={settings.Value.RateUnit ?? UserSettingsInfo.HourValue}");
    Console.WriteLine();
    Console.WriteLine("CORS/auth middleware accepted a native GET with Bearer and no Origin.");
    return 0;
}

static int ReadFlag(string[] args, string name, out int value)
{
    value = 0;
    for (var i = 1; i < args.Length - 1; i++)
    {
        if (string.Equals(args[i], name, StringComparison.OrdinalIgnoreCase)
            && int.TryParse(args[i + 1], System.Globalization.NumberStyles.Integer, System.Globalization.CultureInfo.InvariantCulture, out value))
        {
            return i;
        }
    }

    return -1;
}

static double ReadBonusFlag(string[] args)
{
    for (var i = 1; i < args.Length - 1; i++)
    {
        if (string.Equals(args[i], "--bonus", StringComparison.OrdinalIgnoreCase)
            && BonusText.TryParse(args[i + 1], out var bonus))
        {
            return bonus;
        }
    }

    return 0;
}

static FarmLogRequest ToFarmLogRequest(SessionTotals totals, int characterId, int spotId, double bonus)
    => new(
        CharacterId: characterId,
        SpotId: spotId,
        XpFarmed: totals.XpFarmed,
        Adena: totals.Adena,
        Minutes: totals.Minutes,
        AcquiredXpSp: bonus,
        RedLampXP: totals.RedLampXP,
        PurpleLampXP: totals.PurpleLampXP,
        BlueLampXP: totals.BlueLampXP,
        GreenLampXP: totals.GreenLampXP,
        Date: totals.EndedAt);

static async Task<int> RunSaveAsync(string[] args)
{
    if (ReadFlag(args, "--character-id", out var characterId) < 0)
    {
        Console.Error.WriteLine(
            "Usage: L2TrackerCompanion.OcrDump --save --character-id <id> [--spot-id <id>] [--bonus <n>]");
        return 1;
    }

    var hasSpotId = ReadFlag(args, "--spot-id", out var spotId) >= 0;

    var auth = new AuthService(TokenStore.GetDefault());
    var token = auth.TryLoadToken();
    if (token is null)
    {
        Console.WriteLine("No stored token. Sign in first: ./scripts/auth.sh --token '<jwt>'");
        return 1;
    }

    using var store = new SessionStore(SessionStore.GetDefaultPath());
    var latest = store.Last();
    if (latest is null)
    {
        Console.WriteLine("No reading stored yet. Parse or track first.");
        return 2;
    }

    var gate = SaveGate.Evaluate(
        latest.Report,
        latest.CapturedAt,
        saveLocked: store.IsSaveLocked(latest.Report));
    if (!gate.CanSave || gate.Totals is null)
    {
        Console.WriteLine(gate.BlockReason);
        return 2;
    }

    foreach (var warning in gate.Warnings)
    {
        Console.WriteLine($"Warning: {warning}");
    }

    var bonus = ReadBonusFlag(args);
    var client = TrackerApiClient.Create(auth.BaseUrl);
    var createdSpotId = (int?)null;
    if (!hasSpotId)
    {
        var resolved = await ResolveSpotFromLocationAsync(client, token, characterId, store);
        if (resolved is null)
        {
            return 2;
        }

        spotId = resolved.Value.Id;
        if (resolved.Value.Created)
        {
            createdSpotId = resolved.Value.Id;
        }
    }

    var call = await client.PostFarmLogAsync(token, ToFarmLogRequest(gate.Totals, characterId, spotId, bonus));
    if (!call.Success)
    {
        var extra = "";
        if (createdSpotId is int id)
        {
            var undone = await client.DeleteSpotAsync(token, id);
            extra = undone.Success
                ? " The new spot was not kept."
                : " The new World spot was left on the account.";
        }

        Console.WriteLine($"Save failed: {call.Error}.{extra}");
        return 2;
    }

    store.MarkSaved(latest.Report);
    Console.WriteLine(
        $"Saved farm log #{call.Value!.Id} ({gate.Totals.XpFarmed}k XP, {gate.Totals.Adena}k Adena, "
        + $"{gate.Totals.Minutes} min from the Play Report). Reset the panel in-game to start a new session.");
    return 0;
}

static async Task<(int Id, bool Created)?> ResolveSpotFromLocationAsync(
    TrackerApiClient client,
    string token,
    int characterId,
    SessionStore store)
{
    var spotsCall = await client.GetSpotsAsync(token, characterId);
    if (!spotsCall.Success || spotsCall.Value is null)
    {
        Console.WriteLine($"Could not load spots: {spotsCall.Error}");
        return null;
    }

    var areasCall = await client.GetAreasAsync(token);
    var world = areasCall.Success ? WorldArea.Find(areasCall.Value) : null;
    var stability = LocationStability.Evaluate(store.List().Select(row => row.Report.LocationHint));
    var latest = store.Last();
    var resolve = SpotResolve.Evaluate(
        selected: null,
        stability.IsStable ? stability.CanonicalName : null,
        latest?.Report.LocationHint,
        spotsCall.Value,
        spotsLoaded: true,
        world);
    if (!resolve.CanSave)
    {
        Console.WriteLine(resolve.Hint(stability.SampleCount, LocationStability.WindowSize));
        return null;
    }

    if (resolve.Kind is SpotResolveKind.UseExisting or SpotResolveKind.UseSelected)
    {
        Console.WriteLine($"Using existing spot: {resolve.Spot!.Name}.");
        return (resolve.Spot.Id, Created: false);
    }

    var created = await client.PostSpotAsync(token, resolve.Name!, resolve.WorldArea!.Id);
    if (created.Success && created.Value is not null)
    {
        Console.WriteLine($"Created World spot: {created.Value.Name}.");
        return (created.Value.Id, Created: true);
    }

    var retry = await client.GetSpotsAsync(token, characterId);
    var match = SpotMatch.ExactName(resolve.Name, retry.Value);
    if (match is not null)
    {
        Console.WriteLine($"Using existing spot: {match.Name}.");
        return (match.Id, Created: false);
    }

    Console.WriteLine($"Could not create spot: {created.Error}");
    return null;
}

static async Task<int> RunMatchHintAsync(string input)
{
    string? hint;
    if (File.Exists(input))
    {
        var parsed = await PlayReportPipeline.RunFileAsync(input, CancellationToken.None);
        Console.WriteLine(PlayReportPipeline.FormatWindow(parsed));
        if (!parsed.Success || parsed.Report is null)
        {
            return 2;
        }

        hint = parsed.Report.LocationHint;
    }
    else
    {
        hint = input;
        Console.WriteLine($"Hint: {hint}");
    }

    if (string.IsNullOrWhiteSpace(hint))
    {
        Console.WriteLine("No location hint. Picker unchanged.");
        return 0;
    }

    var auth = new AuthService(TokenStore.GetDefault());
    var token = auth.TryLoadToken();
    if (token is null)
    {
        Console.WriteLine("No stored token — cannot load spots. Matcher itself does not need the API.");
        return 1;
    }

    var restored = await auth.TryRestoreAsync();
    if (!restored.Success || restored.Characters.Count == 0)
    {
        Console.WriteLine(restored.Message);
        return 2;
    }

    var client = TrackerApiClient.Create(auth.BaseUrl);
    var spots = await client.GetSpotsAsync(token, restored.Characters[0].Id);
    if (!spots.Success || spots.Value is null)
    {
        Console.WriteLine($"Could not load spots: {spots.Error}");
        return 2;
    }

    var previous = spots.Value[0];
    var match = SpotMatch.ExactName(hint, spots.Value);
    if (match is null)
    {
        Console.WriteLine($"Hint \"{hint}\" did not match a spot. Picker would stay at {previous.Label}.");
        return 0;
    }

    Console.WriteLine($"Preselected {match.Label} (id {match.Id}). Did not save.");
    return 0;
}

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
