using System.Collections.Concurrent;
using System.Net.Http.Headers;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace L2TrackerCompanion.Api;

/// <summary>
/// Native <see cref="HttpClient"/> calls — no browser <c>Origin</c> header.
/// <see cref="Create"/> reuses one client per base URL.
/// </summary>
public sealed class TrackerApiClient
{
    public static readonly TimeSpan Timeout = TimeSpan.FromSeconds(15);

    private static readonly ConcurrentDictionary<string, TrackerApiClient> ClientsByBaseUrl = new(StringComparer.Ordinal);

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
    };

    private readonly HttpClient _http;

    public TrackerApiClient(HttpClient http)
    {
        ArgumentNullException.ThrowIfNull(http);
        _http = http;
        _http.Timeout = Timeout;
        _http.DefaultRequestHeaders.Accept.Clear();
        _http.DefaultRequestHeaders.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));
    }

    public static TrackerApiClient Create(string baseUrl)
    {
        var key = TokenStore.NormalizeBaseUrl(baseUrl);
        return ClientsByBaseUrl.GetOrAdd(key, static url =>
        {
            var http = new HttpClient
            {
                BaseAddress = new Uri(url + "/", UriKind.Absolute),
                Timeout = Timeout,
            };
            return new TrackerApiClient(http);
        });
    }

    public Task<ApiCallResult<MeResponse>> GetMeAsync(
        string token,
        CancellationToken cancellationToken = default)
        => GetAsync<MeResponse>("api/me", token, cancellationToken);

    public Task<ApiCallResult<IReadOnlyList<CharacterInfo>>> GetCharactersAsync(
        string token,
        CancellationToken cancellationToken = default)
        => GetListAsync<CharacterInfo>("api/characters", token, cancellationToken);

    public Task<ApiCallResult<IReadOnlyList<SpotInfo>>> GetSpotsAsync(
        string token,
        int characterId,
        CancellationToken cancellationToken = default)
    {
        if (characterId <= 0)
        {
            return Task.FromResult(ApiCallResult<IReadOnlyList<SpotInfo>>.Fail("characterId is required"));
        }

        return GetListAsync<SpotInfo>($"api/spots?characterId={characterId}", token, cancellationToken);
    }

    public Task<ApiCallResult<IReadOnlyList<AreaInfo>>> GetAreasAsync(
        string token,
        CancellationToken cancellationToken = default)
        => GetListAsync<AreaInfo>("api/areas", token, cancellationToken);

    public async Task<ApiCallResult<SpotInfo>> PostSpotAsync(
        string token,
        string name,
        int areaId,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(token);
        ArgumentException.ThrowIfNullOrWhiteSpace(name);
        if (areaId <= 0)
        {
            return ApiCallResult<SpotInfo>.Fail("areaId is required");
        }

        using var request = new HttpRequestMessage(HttpMethod.Post, "api/spots");
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token.Trim());
        request.Content = new StringContent(
            JsonSerializer.Serialize(new SpotCreateRequest(name.Trim(), areaId), JsonOptions),
            System.Text.Encoding.UTF8,
            "application/json");

        HttpResponseMessage response;
        try
        {
            response = await _http.SendAsync(request, cancellationToken).ConfigureAwait(false);
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException)
        {
            return ApiCallResult<SpotInfo>.Fail(ex.Message);
        }

        var text = await response.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false);
        if (!response.IsSuccessStatusCode)
        {
            return ApiCallResult<SpotInfo>.Fail(ReadMessage(text, response.StatusCode), response.StatusCode);
        }

        try
        {
            var created = JsonSerializer.Deserialize<SpotCreatedResponse>(text, JsonOptions);
            if (created is null || created.Id <= 0)
            {
                return ApiCallResult<SpotInfo>.Fail("Empty JSON response.");
            }

            return ApiCallResult<SpotInfo>.Ok(
                new SpotInfo(created.Id, created.Name, created.AreaId, null));
        }
        catch (JsonException)
        {
            return ApiCallResult<SpotInfo>.Fail("Response was not JSON.");
        }
    }

    public async Task<ApiCallResult<bool>> DeleteSpotAsync(
        string token,
        int spotId,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(token);
        if (spotId <= 0)
        {
            return ApiCallResult<bool>.Fail("spotId is required");
        }

        using var request = new HttpRequestMessage(HttpMethod.Delete, $"api/spots/{spotId}");
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token.Trim());

        HttpResponseMessage response;
        try
        {
            response = await _http.SendAsync(request, cancellationToken).ConfigureAwait(false);
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException)
        {
            return ApiCallResult<bool>.Fail(ex.Message);
        }

        var text = await response.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false);
        if (!response.IsSuccessStatusCode)
        {
            return ApiCallResult<bool>.Fail(ReadMessage(text, response.StatusCode), response.StatusCode);
        }

        return ApiCallResult<bool>.Ok(true);
    }

    public Task<ApiCallResult<UserSettingsInfo>> GetSettingsAsync(
        string token,
        CancellationToken cancellationToken = default)
        => GetAsync<UserSettingsInfo>("api/settings", token, cancellationToken);

    public async Task<ApiCallResult<FarmLogResponse>> PostFarmLogAsync(
        string token,
        FarmLogRequest body,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(token);
        ArgumentNullException.ThrowIfNull(body);
        using var request = new HttpRequestMessage(HttpMethod.Post, "api/farm-logs");
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token.Trim());
        request.Content = new StringContent(
            JsonSerializer.Serialize(body, JsonOptions),
            System.Text.Encoding.UTF8,
            "application/json");

        HttpResponseMessage response;
        try
        {
            response = await _http.SendAsync(request, cancellationToken).ConfigureAwait(false);
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException)
        {
            return ApiCallResult<FarmLogResponse>.Fail(ex.Message);
        }

        var text = await response.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false);
        if (!response.IsSuccessStatusCode)
        {
            return ApiCallResult<FarmLogResponse>.Fail(ReadMessage(text, response.StatusCode), response.StatusCode);
        }

        try
        {
            var value = JsonSerializer.Deserialize<FarmLogResponse>(text, JsonOptions);
            return value is null
                ? ApiCallResult<FarmLogResponse>.Fail("Empty JSON response.")
                : ApiCallResult<FarmLogResponse>.Ok(value);
        }
        catch (JsonException)
        {
            return ApiCallResult<FarmLogResponse>.Fail("Response was not JSON.");
        }
    }

    private async Task<ApiCallResult<IReadOnlyList<T>>> GetListAsync<T>(
        string relativeUri,
        string token,
        CancellationToken cancellationToken)
    {
        var call = await GetAsync<List<T>>(relativeUri, token, cancellationToken).ConfigureAwait(false);
        if (!call.Success)
        {
            return ApiCallResult<IReadOnlyList<T>>.Fail(call.Error ?? "Request failed.", call.Status);
        }

        IReadOnlyList<T> list = call.Value ?? [];
        return ApiCallResult<IReadOnlyList<T>>.Ok(list);
    }

    private async Task<ApiCallResult<T>> GetAsync<T>(
        string relativeUri,
        string token,
        CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(token);
        using var request = new HttpRequestMessage(HttpMethod.Get, relativeUri);
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token.Trim());

        HttpResponseMessage response;
        try
        {
            response = await _http.SendAsync(request, cancellationToken).ConfigureAwait(false);
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException)
        {
            return ApiCallResult<T>.Fail(ex.Message);
        }

        var body = await response.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false);
        if (!response.IsSuccessStatusCode)
        {
            return ApiCallResult<T>.Fail(ReadMessage(body, response.StatusCode), response.StatusCode);
        }

        try
        {
            var value = JsonSerializer.Deserialize<T>(body, JsonOptions);
            return value is null
                ? ApiCallResult<T>.Fail("Empty JSON response.")
                : ApiCallResult<T>.Ok(value);
        }
        catch (JsonException)
        {
            return ApiCallResult<T>.Fail("Response was not JSON.");
        }
    }

    private static string ReadMessage(string body, System.Net.HttpStatusCode status)
    {
        try
        {
            using var doc = JsonDocument.Parse(body);
            if (doc.RootElement.TryGetProperty("message", out var message)
                && message.ValueKind == JsonValueKind.String)
            {
                return message.GetString() ?? status.ToString();
            }
        }
        catch (JsonException)
        {
            // Fall through to status + raw body.
        }

        return string.IsNullOrWhiteSpace(body) ? $"HTTP {(int)status}" : body;
    }
}

/// <summary>
/// <c>GET /api/me</c>. <c>DesktopAppEnabled</c> gates this app only — a user with it
/// off keeps full access to the website.
/// </summary>
public sealed record MeResponse(
    string Id,
    string Username,
    string? Avatar,
    bool IsAdmin,
    bool DesktopAppEnabled);

public sealed record CharacterInfo(
    int Id,
    string Name,
    string? CharacterClass,
    int Level,
    double Percentage,
    int TargetLevel);

public sealed record AreaInfo(int Id, string Name);

public static class WorldArea
{
    public const string Name = "World";

    public static AreaInfo? Find(IEnumerable<AreaInfo>? areas)
    {
        if (areas is null)
        {
            return null;
        }

        return areas.FirstOrDefault(area =>
            !string.IsNullOrWhiteSpace(area.Name)
            && string.Equals(area.Name.Trim(), Name, StringComparison.OrdinalIgnoreCase));
    }
}

public sealed record SpotAreaInfo(int Id, string Name);

public sealed record SpotCreateRequest(string Name, int AreaId);

public sealed record SpotCreatedResponse(int Id, string Name, int AreaId);

/// <summary>
/// <c>GET /api/spots?characterId=</c>. The three hourly figures are averages of
/// <em>that character's</em> logs only (the endpoint filters the relation by
/// <c>characterId</c>), and are null when the character has never logged here.
/// <see cref="LogCount"/> is the one field that is not character-scoped — it
/// counts every character on the account, because it gates spot deletion, which
/// cascades account-wide. Never read it as a sample count for these averages.
/// Amounts follow the API's thousands convention — see <see cref="LegacyThousands"/>.
/// </summary>
public sealed record SpotInfo(
    int Id,
    string Name,
    int AreaId,
    SpotAreaInfo? Area,
    long? FarmXpHourly = null,
    long? AdenaHourly = null,
    long? AverageXpHourly = null,
    int LogCount = 0)
{
    public string Label
    {
        get
        {
            var areaName = Area?.Name;
            return string.IsNullOrEmpty(areaName) ? Name : $"{Name} ({areaName})";
        }
    }
}

public sealed record UserSettingsInfo(int DefaultBonus, string? RateUnit)
{
    public const string MinuteValue = "minute";
    public const string HourValue = "hour";

    /// <summary>
    /// Prisma <c>UserSettings</c> defaults: <c>defaultBonus</c> 25,
    /// <c>rateUnit</c> <c>hour</c>.
    /// </summary>
    public static UserSettingsInfo SchemaDefaults { get; } = new(25, HourValue);

    /// <summary>
    /// Website <c>user_settings.rate_unit</c> is <c>hour</c> or <c>minute</c>
    /// (Prisma default <c>hour</c>). Missing/blank follows that default;
    /// any other value shows XP/min.
    /// </summary>
    public bool RatePerHour
    {
        get
        {
            if (string.IsNullOrWhiteSpace(RateUnit))
            {
                return true;
            }

            return string.Equals(RateUnit.Trim(), HourValue, StringComparison.OrdinalIgnoreCase);
        }
    }
}

public sealed record FarmLogRequest(
    int CharacterId,
    int SpotId,
    long XpFarmed,
    long Adena,
    int Minutes,
    double AcquiredXpSp,
    long RedLampXP,
    long PurpleLampXP,
    long BlueLampXP,
    long GreenLampXP,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    DateTimeOffset? Date = null);

public sealed record FarmLogResponse(int Id, int CharacterId, int SpotId);

/// <summary>
/// <see cref="Status"/> is the HTTP status of a rejected response, or null when the
/// call never got one (transport error, DNS, timeout). Callers that decide whether a
/// token is bad must check it: only a real 401/403 means "bad token".
/// </summary>
public sealed record ApiCallResult<T>(
    bool Success,
    T? Value,
    string? Error,
    System.Net.HttpStatusCode? Status = null)
{
    public static ApiCallResult<T> Ok(T value) => new(true, value, null);

    public static ApiCallResult<T> Fail(string error, System.Net.HttpStatusCode? status = null)
        => new(false, default, error, status);
}

public static class SessionPickers
{
    public const string SignInToLoad = "Sign in to load characters.";

    public const string SignInToSave = "Sign in to save.";

    public static bool CharacterChosen(CharacterInfo? character)
        => character is not null && character.Id > 0;

    public static bool SaveEnabled(CharacterInfo? character, SpotInfo? spot)
        => CharacterChosen(character) && spot is not null && spot.Id > 0;

    public static bool SaveReady(CharacterInfo? character, SpotResolveDecision resolve)
        => CharacterChosen(character) && resolve.CanSave;
}

/// <summary>
/// Exact case-insensitive match of a minimap <c>locationHint</c> against
/// spot <see cref="SpotInfo.Name"/> — never fuzzy, never the area label.
/// A miss returns null so the picker can stay as it was.
/// </summary>
public static class SpotMatch
{
    /// <summary>
    /// Trimmed, case-insensitive equality of two location / spot names.
    /// Blank values never match, including two blanks.
    /// </summary>
    public static bool SameName(string? left, string? right)
    {
        if (string.IsNullOrWhiteSpace(left) || string.IsNullOrWhiteSpace(right))
        {
            return false;
        }

        return string.Equals(left.Trim(), right.Trim(), StringComparison.OrdinalIgnoreCase);
    }

    public static IReadOnlyList<SpotInfo> ExactNames(string? locationHint, IEnumerable<SpotInfo>? spots)
    {
        if (string.IsNullOrWhiteSpace(locationHint) || spots is null)
        {
            return [];
        }

        var needle = locationHint.Trim();
        return spots
            .Where(spot =>
                !string.IsNullOrWhiteSpace(spot.Name)
                && string.Equals(spot.Name.Trim(), needle, StringComparison.OrdinalIgnoreCase))
            .ToList();
    }

    /// <summary>
    /// A unique exact name hit, or null when there is none or more than one.
    /// </summary>
    public static SpotInfo? ExactName(string? locationHint, IEnumerable<SpotInfo>? spots)
    {
        var matches = ExactNames(locationHint, spots);
        return matches.Count == 1 ? matches[0] : null;
    }
}
