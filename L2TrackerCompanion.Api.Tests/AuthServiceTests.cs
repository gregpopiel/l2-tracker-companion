using System.Net;
using System.Net.Http.Headers;
using System.Text;
using L2TrackerCompanion.Api;
using Xunit;

namespace L2TrackerCompanion.Api.Tests;

public class AuthServiceTests
{
    [Fact]
    public void TokenStoreRoundTripsThroughDpapiAndIsNotPlainText()
    {
        var dir = NewTempDir();
        try
        {
            var store = new TokenStore(dir);
            store.SaveToken("header.payload.signature");
            Assert.True(File.Exists(store.TokenPath));
            var onDisk = File.ReadAllText(store.TokenPath, Encoding.Latin1);
            Assert.DoesNotContain("header.payload.signature", onDisk, StringComparison.Ordinal);
            Assert.Equal("header.payload.signature", store.TryLoadToken());
        }
        finally
        {
            Directory.Delete(dir, recursive: true);
        }
    }

    [Fact]
    public async Task GarbageTokenIsNotLeftOnDisk()
    {
        var dir = NewTempDir();
        try
        {
            var store = new TokenStore(dir);
            store.SaveToken("previous-good-token");
            Assert.True(store.HasToken);

            var auth = new AuthService(store, _ => ClientThatReturns(HttpStatusCode.Unauthorized, """{"message":"Invalid token"}"""));
            var result = await auth.SignInAsync("not-a-real-jwt");

            Assert.False(result.Success);
            Assert.False(store.HasToken);
            Assert.False(File.Exists(store.TokenPath));
            Assert.Contains("Invalid token", result.Message, StringComparison.Ordinal);
        }
        finally
        {
            Directory.Delete(dir, recursive: true);
        }
    }

    [Fact]
    public async Task ValidTokenIsSavedAndListsCharacters()
    {
        var dir = NewTempDir();
        try
        {
            var store = new TokenStore(dir);
            var auth = new AuthService(store, _ => ClientThatReturns(
                HttpStatusCode.OK,
                """[{"id":1,"name":"TestChar","characterClass":"S","level":80,"percentage":12.5,"targetLevel":85}]"""));

            var result = await auth.SignInAsync("  real.jwt.value  ");

            Assert.True(result.Success);
            Assert.True(store.HasToken);
            Assert.Equal("real.jwt.value", store.TryLoadToken());
            Assert.Equal("TestChar", result.Characters.Single().Name);
            Assert.Contains("TestChar", result.Message, StringComparison.Ordinal);
            Assert.False(result.IsAdmin);
        }
        finally
        {
            Directory.Delete(dir, recursive: true);
        }
    }

    [Fact]
    public async Task AdminFlagFromMeIsCarriedOntoTheResult()
    {
        var dir = NewTempDir();
        try
        {
            var store = new TokenStore(dir);
            var auth = new AuthService(store, _ => ClientThatReturns(
                HttpStatusCode.OK,
                """[{"id":1,"name":"TestChar","characterClass":"S","level":80,"percentage":12.5,"targetLevel":85}]""",
                MeAdminJson));

            var result = await auth.SignInAsync("real.jwt.value");

            Assert.True(result.Success);
            Assert.True(result.IsAdmin);
        }
        finally
        {
            Directory.Delete(dir, recursive: true);
        }
    }

    [Fact]
    public async Task DesktopAccessDisabledIsRefusedAndLeavesNoToken()
    {
        var dir = NewTempDir();
        try
        {
            var store = new TokenStore(dir);
            store.SaveToken("previous-good-token");

            var auth = new AuthService(store, _ => ClientThatReturns(
                HttpStatusCode.OK,
                """[{"id":1,"name":"TestChar","characterClass":"S","level":80,"percentage":12.5,"targetLevel":85}]""",
                MeDesktopDisabledJson));

            var result = await auth.SignInAsync("real.jwt.value");

            Assert.False(result.Success);
            Assert.False(store.HasToken);
            Assert.False(File.Exists(store.TokenPath));
            Assert.Contains("Desktop access is not enabled", result.Message, StringComparison.Ordinal);
        }
        finally
        {
            Directory.Delete(dir, recursive: true);
        }
    }

    [Fact]
    public async Task GetCharactersDoesNotSendOriginHeader()
    {
        HttpRequestMessage? seen = null;
        var handler = new StubHandler(request =>
        {
            seen = request;
            return new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent("[]", Encoding.UTF8, "application/json"),
            };
        });
        var http = new HttpClient(handler) { BaseAddress = new Uri("https://l2tracker.cc/") };
        var client = new TrackerApiClient(http);
        await client.GetCharactersAsync("abc.def.ghi");

        Assert.NotNull(seen);
        Assert.False(seen!.Headers.Contains("Origin"));
        Assert.Equal("abc.def.ghi", seen.Headers.Authorization?.Parameter);
        Assert.Equal(AuthenticationHeaderValue.Parse("Bearer abc.def.ghi").Scheme, seen.Headers.Authorization?.Scheme);
        Assert.Equal("/api/characters", seen.RequestUri?.AbsolutePath);
    }

    [Fact]
    public async Task UnreachableServerKeepsStoredToken()
    {
        var dir = NewTempDir();
        try
        {
            var store = new TokenStore(dir);
            store.SaveToken("still.good.token");

            var auth = new AuthService(store, _ => ClientThatThrows(new HttpRequestException("No such host is known.")));
            var result = await auth.TryRestoreAsync();

            Assert.False(result.Success);
            Assert.True(store.HasToken);
            Assert.Equal("still.good.token", store.TryLoadToken());
            Assert.Contains("Could not reach", result.Message, StringComparison.Ordinal);
        }
        finally
        {
            Directory.Delete(dir, recursive: true);
        }
    }

    [Fact]
    public async Task ServerErrorKeepsStoredToken()
    {
        var dir = NewTempDir();
        try
        {
            var store = new TokenStore(dir);
            store.SaveToken("still.good.token");

            var auth = new AuthService(store, _ => ClientThatReturns(
                HttpStatusCode.BadGateway,
                """{"message":"Bad gateway"}"""));
            var result = await auth.TryRestoreAsync();

            Assert.False(result.Success);
            Assert.True(store.HasToken);
        }
        finally
        {
            Directory.Delete(dir, recursive: true);
        }
    }

    [Fact]
    public async Task ForbiddenTokenIsClearedFromDisk()
    {
        var dir = NewTempDir();
        try
        {
            var store = new TokenStore(dir);
            store.SaveToken("revoked.token");

            var auth = new AuthService(store, _ => ClientThatReturns(
                HttpStatusCode.Forbidden,
                """{"message":"Forbidden"}"""));
            var result = await auth.TryRestoreAsync();

            Assert.False(result.Success);
            Assert.False(store.HasToken);
        }
        finally
        {
            Directory.Delete(dir, recursive: true);
        }
    }

    [Fact]
    public async Task EmptyPasteKeepsStoredToken()
    {
        var dir = NewTempDir();
        try
        {
            var store = new TokenStore(dir);
            store.SaveToken("previous");
            var auth = new AuthService(store, _ => throw new InvalidOperationException("must not call the API"));
            var result = await auth.SignInAsync("   ");
            Assert.False(result.Success);
            Assert.True(store.HasToken);
            Assert.Equal("previous", store.TryLoadToken());
        }
        finally
        {
            Directory.Delete(dir, recursive: true);
        }
    }

    private static string NewTempDir()
    {
        var dir = Path.Combine(Path.GetTempPath(), "l2-auth-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(dir);
        return dir;
    }

    private const string MeDesktopEnabledJson =
        """{"id":"u1","username":"Tester","avatar":null,"isAdmin":false,"desktopAppEnabled":true}""";

    private const string MeDesktopDisabledJson =
        """{"id":"u1","username":"Tester","avatar":null,"isAdmin":false,"desktopAppEnabled":false}""";

    private const string MeAdminJson =
        """{"id":"u1","username":"Tester","avatar":null,"isAdmin":true,"desktopAppEnabled":true}""";

    /// <summary>
    /// <c>ValidateAndStoreAsync</c> calls <c>/api/me</c> before <c>/api/characters</c>, so the
    /// stub answers per endpoint. A non-OK <paramref name="status"/> applies to both: the
    /// <c>/api/me</c> failure short-circuits before the characters GET is ever made.
    /// </summary>
    private static TrackerApiClient ClientThatReturns(
        HttpStatusCode status,
        string json,
        string meJson = MeDesktopEnabledJson)
    {
        var handler = new StubHandler(request =>
        {
            var body = status == HttpStatusCode.OK && request.RequestUri?.AbsolutePath == "/api/me"
                ? meJson
                : json;
            return new HttpResponseMessage(status)
            {
                Content = new StringContent(body, Encoding.UTF8, "application/json"),
            };
        });
        return new TrackerApiClient(new HttpClient(handler)
        {
            BaseAddress = new Uri("https://l2tracker.cc/"),
        });
    }

    private static TrackerApiClient ClientThatThrows(Exception exception)
    {
        var handler = new ThrowingHandler(exception);
        return new TrackerApiClient(new HttpClient(handler)
        {
            BaseAddress = new Uri("https://l2tracker.cc/"),
        });
    }

    private sealed class ThrowingHandler : HttpMessageHandler
    {
        private readonly Exception _exception;

        public ThrowingHandler(Exception exception)
        {
            _exception = exception;
        }

        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken)
            => Task.FromException<HttpResponseMessage>(_exception);
    }

    private sealed class StubHandler : HttpMessageHandler
    {
        private readonly Func<HttpRequestMessage, HttpResponseMessage> _respond;

        public StubHandler(Func<HttpRequestMessage, HttpResponseMessage> respond)
        {
            _respond = respond;
        }

        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken)
            => Task.FromResult(_respond(request));
    }
}
