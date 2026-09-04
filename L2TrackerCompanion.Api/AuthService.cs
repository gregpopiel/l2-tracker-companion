namespace L2TrackerCompanion.Api;

/// <summary>
/// Paste a website JWT, validate it with <c>GET /api/me</c> (which must also report
/// desktop access as enabled) then <c>GET /api/characters</c>, persist only on success
/// (DPAPI). A *rejected* token is cleared; a call that never reached the server
/// (offline, DNS, timeout, 5xx) leaves the stored token alone — see <see cref="Reject"/>.
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
        // An empty box is a slip, not a decision to sign out: the stored token stays.
        // Only Sign Out and an actual 401/403 remove it.
        if (string.IsNullOrWhiteSpace(token))
        {
            return AuthResult.Fail("Paste a token before signing in.");
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

        // Runs on every sign-in AND on every restore at startup, so revoking desktop access
        // takes effect the next time the app launches, not only on the next paste.
        var me = await client.GetMeAsync(token, cancellationToken).ConfigureAwait(false);
        if (!me.Success || me.Value is null)
        {
            return Reject(me.Status, me.Error);
        }

        if (!me.Value.DesktopAppEnabled)
        {
            _store.ClearToken();
            return AuthResult.Fail("Desktop access is not enabled for this account.");
        }

        var call = await client.GetCharactersAsync(token, cancellationToken).ConfigureAwait(false);
        if (!call.Success || call.Value is null)
        {
            return Reject(call.Status, call.Error);
        }

        _store.SaveToken(token);
        var names = call.Value.Select(c => c.Name).ToArray();
        var summary = call.Value.Count == 0
            ? "Signed in. No characters on this account yet."
            : $"Signed in. {call.Value.Count} character{(call.Value.Count == 1 ? "" : "s")}: {string.Join(", ", names)}.";
        return AuthResult.Ok(summary, call.Value, BaseUrl, me.Value.IsAdmin, me.Value.Id);
    }

    /// <summary>
    /// Only a 401/403 proves the token itself is bad. Anything else — offline, DNS,
    /// timeout, a 502 from the edge — must keep the stored token, or a network blip
    /// would sign the user out and force them to dig the JWT out of the browser again.
    /// </summary>
    private AuthResult Reject(System.Net.HttpStatusCode? status, string? error)
    {
        var tokenIsBad = status is System.Net.HttpStatusCode.Unauthorized
            or System.Net.HttpStatusCode.Forbidden;
        if (tokenIsBad)
        {
            _store.ClearToken();
            return AuthResult.Fail(error ?? "Token was rejected.");
        }

        var kept = _store.HasToken ? " The stored token was kept — retry once the server is reachable." : string.Empty;
        return AuthResult.Fail($"Could not reach {BaseUrl}: {error ?? "request failed"}.{kept}");
    }
}

public sealed record AuthResult(
    bool Success,
    string Message,
    IReadOnlyList<CharacterInfo> Characters,
    string BaseUrl,
    bool IsAdmin,
    string UserId)
{
    public static AuthResult Ok(
        string message,
        IReadOnlyList<CharacterInfo> characters,
        string baseUrl,
        bool isAdmin,
        string userId)
        => new(true, message, characters, baseUrl, isAdmin, userId);

    public static AuthResult Fail(string message)
        => new(false, message, [], TokenStore.DefaultBaseUrl, false, string.Empty);
}
