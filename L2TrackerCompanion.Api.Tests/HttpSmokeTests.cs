using System.Net;
using System.Text;
using L2TrackerCompanion.Api;
using Xunit;

namespace L2TrackerCompanion.Api.Tests;

public class HttpSmokeTests
{
    [Fact]
    public async Task NativeGetIs200JsonWithoutOrigin()
    {
        var inner = new StubHandler(_ => new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new StringContent("""[{"id":7,"name":"vild"}]""", Encoding.UTF8, "application/json"),
        });
        var probe = new HttpSmokeHandler(inner);
        var client = new TrackerApiClient(new HttpClient(probe)
        {
            BaseAddress = new Uri("https://l2tracker.cc/"),
        });

        var result = await client.GetCharactersAsync("stored.jwt");

        Assert.True(result.Success);
        Assert.Equal(HttpStatusCode.OK, probe.LastStatus);
        Assert.False(probe.SentOrigin);
        Assert.True(HttpSmoke.IsJson(probe.LastBody));
        Assert.True(HttpSmoke.Passed(probe));
        Assert.Equal("/api/characters", probe.LastRequest?.RequestUri?.AbsolutePath);
        Assert.Contains("HTTP 200", HttpSmoke.Format(probe, "characters"), StringComparison.Ordinal);
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
