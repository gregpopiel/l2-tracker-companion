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
            return ApiCallResult<FarmLogResponse>.Fail(ReadMessage(text, response.StatusCode));
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
            return ApiCallResult<IReadOnlyList<T>>.Fail(call.Error ?? "Request failed.");
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
            return ApiCallResult<T>.Fail(ReadMessage(body, response.StatusCode));
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

public sealed record CharacterInfo(
    int Id,
    string Name,
    string? CharacterClass,
    int Level,
    double Percentage,
    int TargetLevel);

public sealed record SpotAreaInfo(int Id, string Name);

public sealed record SpotInfo(int Id, string Name, int AreaId, SpotAreaInfo? Area)
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

public sealed record UserSettingsInfo(int DefaultBonus, int DefaultMinutes)
{
    public static UserSettingsInfo SchemaDefaults { get; } = new(25, 60);
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

public sealed record ApiCallResult<T>(bool Success, T? Value, string? Error)
{
    public static ApiCallResult<T> Ok(T value) => new(true, value, null);

    public static ApiCallResult<T> Fail(string error) => new(false, default, error);
}

public static class SessionPickers
{
    public static bool SaveEnabled(CharacterInfo? character, SpotInfo? spot)
        => character is not null && character.Id > 0 && spot is not null && spot.Id > 0;
}

/// <summary>
/// Exact case-insensitive match of a minimap <c>locationHint</c> against
/// spot <see cref="SpotInfo.Name"/> — never fuzzy, never the area label.
/// A miss returns null so the picker can stay as it was.
/// </summary>
public static class SpotMatch
{
    public static SpotInfo? ExactName(string? locationHint, IEnumerable<SpotInfo>? spots)
    {
        if (string.IsNullOrWhiteSpace(locationHint) || spots is null)
        {
            return null;
        }

        var needle = locationHint.Trim();
        return spots.FirstOrDefault(spot =>
            !string.IsNullOrWhiteSpace(spot.Name)
            && string.Equals(spot.Name.Trim(), needle, StringComparison.OrdinalIgnoreCase));
    }
}
