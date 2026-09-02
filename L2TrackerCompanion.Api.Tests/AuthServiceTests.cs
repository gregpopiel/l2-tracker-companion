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
    public async Task EmptyPasteClearsStoredToken()
    {
        var dir = NewTempDir();
        try
        {
            var store = new TokenStore(dir);
            store.SaveToken("previous");
            var auth = new AuthService(store, _ => throw new InvalidOperationException("must not call the API"));
            var result = await auth.SignInAsync("   ");
            Assert.False(result.Success);
            Assert.False(store.HasToken);
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

    private static TrackerApiClient ClientThatReturns(HttpStatusCode status, string json)
    {
        var handler = new StubHandler(_ => new HttpResponseMessage(status)
        {
            Content = new StringContent(json, Encoding.UTF8, "application/json"),
        });
        return new TrackerApiClient(new HttpClient(handler)
        {
            BaseAddress = new Uri("https://l2tracker.cc/"),
        });
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
