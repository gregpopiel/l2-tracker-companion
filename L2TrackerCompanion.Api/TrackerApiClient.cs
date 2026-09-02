using System.Net.Http.Headers;
using System.Text.Json;

namespace L2TrackerCompanion.Api;

/// <summary>
/// Native <see cref="HttpClient"/> calls — no browser <c>Origin</c> header.
/// </summary>
public sealed class TrackerApiClient
{
    public static readonly TimeSpan Timeout = TimeSpan.FromSeconds(15);

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
        var http = new HttpClient
        {
            BaseAddress = new Uri(TokenStore.NormalizeBaseUrl(baseUrl) + "/", UriKind.Absolute),
            Timeout = Timeout,
        };
        return new TrackerApiClient(http);
    }

    public async Task<ApiCallResult<IReadOnlyList<CharacterInfo>>> GetCharactersAsync(
        string token,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(token);
        using var request = new HttpRequestMessage(HttpMethod.Get, "api/characters");
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token.Trim());

        HttpResponseMessage response;
        try
        {
            response = await _http.SendAsync(request, cancellationToken).ConfigureAwait(false);
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException)
        {
            return ApiCallResult<IReadOnlyList<CharacterInfo>>.Fail(ex.Message);
        }

        var body = await response.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false);
        if (!response.IsSuccessStatusCode)
        {
            return ApiCallResult<IReadOnlyList<CharacterInfo>>.Fail(ReadMessage(body, response.StatusCode));
        }

        try
        {
            var characters = JsonSerializer.Deserialize<List<CharacterInfo>>(body, JsonOptions) ?? [];
            return ApiCallResult<IReadOnlyList<CharacterInfo>>.Ok(characters);
        }
        catch (JsonException)
        {
            return ApiCallResult<IReadOnlyList<CharacterInfo>>.Fail("Characters response was not JSON.");
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

public sealed record ApiCallResult<T>(bool Success, T? Value, string? Error)
{
    public static ApiCallResult<T> Ok(T value) => new(true, value, null);

    public static ApiCallResult<T> Fail(string error) => new(false, default, error);
}
