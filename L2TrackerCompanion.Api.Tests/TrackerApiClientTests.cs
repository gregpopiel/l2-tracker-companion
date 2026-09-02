using System.Net;
using System.Net.Http.Headers;
using System.Text;
using L2TrackerCompanion.Api;
using Xunit;

namespace L2TrackerCompanion.Api.Tests;

public class TrackerApiClientTests
{
    [Fact]
    public async Task GetSpotsDoesNotSendOriginAndRequiresCharacterId()
    {
        HttpRequestMessage? seen = null;
        var handler = new StubHandler(request =>
        {
            seen = request;
            return Json(HttpStatusCode.OK, """
                [{"id":10,"name":"Dragon Valley (east)","areaId":1,"area":{"id":1,"name":"World"},"logCount":0}]
                """);
        });
        var client = new TrackerApiClient(new HttpClient(handler)
        {
            BaseAddress = new Uri("https://l2tracker.cc/"),
        });

        var missing = await client.GetSpotsAsync("tok", 0);
        Assert.False(missing.Success);
        Assert.Contains("characterId", missing.Error, StringComparison.Ordinal);
        Assert.Null(seen);

        var ok = await client.GetSpotsAsync("abc.def.ghi", 7);
        Assert.True(ok.Success);
        Assert.NotNull(seen);
        Assert.False(seen!.Headers.Contains("Origin"));
        Assert.Equal("abc.def.ghi", seen.Headers.Authorization?.Parameter);
        Assert.Equal(AuthenticationHeaderValue.Parse("Bearer x").Scheme, seen.Headers.Authorization?.Scheme);
        Assert.Equal("/api/spots", seen.RequestUri?.AbsolutePath);
        Assert.Equal("?characterId=7", seen.RequestUri?.Query);
        var spot = Assert.Single(ok.Value!);
        Assert.Equal(10, spot.Id);
        Assert.Equal("Dragon Valley (east)", spot.Name);
        Assert.Equal("Dragon Valley (east) (World)", spot.Label);
    }

    [Fact]
    public async Task GetSettingsReadsBonusAndMinutesAndIgnoresLampFields()
    {
        HttpRequestMessage? seen = null;
        var handler = new StubHandler(request =>
        {
            seen = request;
            return Json(HttpStatusCode.OK, """
                {"id":1,"userId":"u1","redLampValue":325,"purpleLampValue":250,"blueLampValue":225,"greenLampValue":200,"defaultBonus":30,"defaultMinutes":90,"rateUnit":"hour"}
                """);
        });
        var client = new TrackerApiClient(new HttpClient(handler)
        {
            BaseAddress = new Uri("https://l2tracker.cc/"),
        });

        var result = await client.GetSettingsAsync("jwt");
        Assert.True(result.Success);
        Assert.Equal(30, result.Value!.DefaultBonus);
        Assert.Equal(90, result.Value.DefaultMinutes);
        Assert.False(seen!.Headers.Contains("Origin"));
        Assert.Equal("/api/settings", seen.RequestUri?.AbsolutePath);
    }

    [Fact]
    public async Task PostFarmLogSendsThousandsAndCapitalXpLampFieldsWithoutOrigin()
    {
        HttpRequestMessage? seen = null;
        string? body = null;
        var handler = new StubHandler(request =>
        {
            seen = request;
            body = request.Content!.ReadAsStringAsync().GetAwaiter().GetResult();
            return Json(HttpStatusCode.OK, """{"id":99,"characterId":7,"spotId":160}""");
        });
        var client = new TrackerApiClient(new HttpClient(handler)
        {
            BaseAddress = new Uri("https://l2tracker.cc/"),
        });

        var call = await client.PostFarmLogAsync(
            "jwt",
            new FarmLogRequest(
                CharacterId: 7,
                SpotId: 160,
                XpFarmed: 13587,
                Adena: 1000,
                Minutes: 12,
                AcquiredXpSp: 567,
                RedLampXP: 1000,
                PurpleLampXP: 0,
                BlueLampXP: 0,
                GreenLampXP: 0,
                Date: DateTimeOffset.Parse("2026-09-02T10:12:20Z")));

        Assert.True(call.Success);
        Assert.Equal(99, call.Value!.Id);
        Assert.NotNull(seen);
        Assert.Equal(HttpMethod.Post, seen!.Method);
        Assert.Equal("/api/farm-logs", seen.RequestUri?.AbsolutePath);
        Assert.False(seen.Headers.Contains("Origin"));
        Assert.Equal("jwt", seen.Headers.Authorization?.Parameter);
        Assert.Contains("\"xpFarmed\":13587", body, StringComparison.Ordinal);
        Assert.Contains("\"redLampXP\":1000", body, StringComparison.Ordinal);
        Assert.Contains("\"purpleLampXP\":0", body, StringComparison.Ordinal);
        Assert.Contains("\"blueLampXP\":0", body, StringComparison.Ordinal);
        Assert.Contains("\"greenLampXP\":0", body, StringComparison.Ordinal);
        Assert.Contains("\"acquiredXpSp\":567", body, StringComparison.Ordinal);
        Assert.Contains("\"minutes\":12", body, StringComparison.Ordinal);
        Assert.DoesNotContain("redLampXp\":", body, StringComparison.Ordinal);
    }

    [Fact]
    public void SaveIsDisabledUntilCharacterAndSpotAreChosen()
    {
        var character = new CharacterInfo(1, "Player174", "S", 80, 0, 85);
        var spot = new SpotInfo(10, "Dragon Valley (east)", 1, new SpotAreaInfo(1, "World"));
        Assert.False(SessionPickers.SaveEnabled(null, null));
        Assert.False(SessionPickers.SaveEnabled(character, null));
        Assert.False(SessionPickers.SaveEnabled(null, spot));
        Assert.True(SessionPickers.SaveEnabled(character, spot));
    }

    private static HttpResponseMessage Json(HttpStatusCode status, string json)
        => new(status)
        {
            Content = new StringContent(json, Encoding.UTF8, "application/json"),
        };

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
