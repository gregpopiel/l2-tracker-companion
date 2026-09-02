namespace L2TrackerCompanion.Api;

/// <summary>
/// Paste a website JWT, validate it with <c>GET /api/characters</c>, persist
/// only on success (DPAPI). A failed call clears any stored token.
/// </summary>
public sealed class AuthService
{
    private readonly TokenStore _store;
    private readonly Func<string, TrackerApiClient> _clientFactory;

    public AuthService(TokenStore store, Func<string, TrackerApiClient>? clientFactory = null)
    {
        ArgumentNullException.ThrowIfNull(store);
        _store = store;
        _clientFactory = clientFactory ?? TrackerApiClient.Create;
        BaseUrl = _store.LoadBaseUrl();
    }

    public string BaseUrl { get; private set; }

    public bool HasStoredToken => _store.HasToken;

    public string TokenPath => _store.TokenPath;

    /// <summary>
    /// JWT for later GETs/POSTs. <see cref="AuthResult"/> does not carry the token.
    /// </summary>
    public string? TryLoadToken() => _store.TryLoadToken();

    public void SetBaseUrl(string baseUrl)
    {
        BaseUrl = TokenStore.NormalizeBaseUrl(baseUrl);
        _store.SaveBaseUrl(BaseUrl);
    }

    public async Task<AuthResult> SignInAsync(string token, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(token))
        {
            _store.ClearToken();
            return AuthResult.Fail("Paste a token from the website (localStorage l2_jwt_token).");
        }

        return await ValidateAndStoreAsync(token.Trim(), cancellationToken).ConfigureAwait(false);
    }

    public async Task<AuthResult> TryRestoreAsync(CancellationToken cancellationToken = default)
    {
        var token = _store.TryLoadToken();
        if (token is null)
        {
            return AuthResult.Fail("No stored token.");
        }

        return await ValidateAndStoreAsync(token, cancellationToken).ConfigureAwait(false);
    }

    public void SignOut() => _store.ClearToken();

    private async Task<AuthResult> ValidateAndStoreAsync(string token, CancellationToken cancellationToken)
    {
        var client = _clientFactory(BaseUrl);
        var call = await client.GetCharactersAsync(token, cancellationToken).ConfigureAwait(false);
        if (!call.Success || call.Value is null)
        {
            _store.ClearToken();
            return AuthResult.Fail(call.Error ?? "Token was rejected.");
        }

        _store.SaveToken(token);
        var names = call.Value.Select(c => c.Name).ToArray();
        var summary = call.Value.Count == 0
            ? "Signed in. No characters on this account yet."
            : $"Signed in. {call.Value.Count} character{(call.Value.Count == 1 ? "" : "s")}: {string.Join(", ", names)}.";
        return AuthResult.Ok(summary, call.Value, BaseUrl);
    }
}

public sealed record AuthResult(
    bool Success,
    string Message,
    IReadOnlyList<CharacterInfo> Characters,
    string BaseUrl)
{
    public static AuthResult Ok(string message, IReadOnlyList<CharacterInfo> characters, string baseUrl)
        => new(true, message, characters, baseUrl);

    public static AuthResult Fail(string message)
        => new(false, message, [], TokenStore.DefaultBaseUrl);
}
