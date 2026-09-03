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
    public async Task GetSettingsReadsBonusAndRateUnitAndIgnoresLampAndMinutesFields()
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
        Assert.Equal("hour", result.Value.RateUnit);
        Assert.True(result.Value.RatePerHour);
        Assert.False(seen!.Headers.Contains("Origin"));
        Assert.Equal("/api/settings", seen.RequestUri?.AbsolutePath);
    }

    [Theory]
    [InlineData("hour", true)]
    [InlineData("HOUR", true)]
    [InlineData(" hour ", true)]
    [InlineData(null, true)]
    [InlineData("", true)]
    [InlineData("  ", true)]
    [InlineData("minute", false)]
    [InlineData("MINUTE", false)]
    [InlineData("minutes", false)]
    [InlineData("nope", false)]
    public void RatePerHourFollowsHourOrSchemaDefault(string? rateUnit, bool perHour)
    {
        Assert.Equal(perHour, new UserSettingsInfo(25, rateUnit).RatePerHour);
    }

    [Fact]
    public void SchemaDefaultRateUnitIsHour()
    {
        Assert.Equal(UserSettingsInfo.HourValue, UserSettingsInfo.SchemaDefaults.RateUnit);
        Assert.Equal(25, UserSettingsInfo.SchemaDefaults.DefaultBonus);
        Assert.True(UserSettingsInfo.SchemaDefaults.RatePerHour);
        Assert.Equal("minute", UserSettingsInfo.MinuteValue);
    }

    [Fact]
    public async Task GetSettingsMissingRateUnitUsesSchemaDefaultHour()
    {
        var handler = new StubHandler(_ => Json(HttpStatusCode.OK, """
            {"id":1,"userId":"u1","defaultBonus":25}
            """));
        var client = new TrackerApiClient(new HttpClient(handler)
        {
            BaseAddress = new Uri("https://l2tracker.cc/"),
        });

        var result = await client.GetSettingsAsync("jwt");
        Assert.True(result.Success);
        Assert.Null(result.Value!.RateUnit);
        Assert.True(result.Value.RatePerHour);
    }

    [Fact]
    public async Task GetSettingsReadsMinuteRateUnit()
    {
        var handler = new StubHandler(_ => Json(HttpStatusCode.OK, """
            {"id":1,"userId":"u1","defaultBonus":25,"rateUnit":"minute"}
            """));
        var client = new TrackerApiClient(new HttpClient(handler)
        {
            BaseAddress = new Uri("https://l2tracker.cc/"),
        });

        var result = await client.GetSettingsAsync("jwt");
        Assert.True(result.Success);
        Assert.Equal("minute", result.Value!.RateUnit);
        Assert.False(result.Value.RatePerHour);
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
            return Json(HttpStatusCode.OK, """{"id":99,"characterId":1,"spotId":10}""");
        });
        var client = new TrackerApiClient(new HttpClient(handler)
        {
            BaseAddress = new Uri("https://l2tracker.cc/"),
        });

        var call = await client.PostFarmLogAsync(
            "jwt",
            new FarmLogRequest(
                CharacterId: 1,
                SpotId: 10,
                XpFarmed: 1000,
                Adena: 100,
                Minutes: 15,
                AcquiredXpSp: 25,
                RedLampXP: 200,
                PurpleLampXP: 0,
                BlueLampXP: 0,
                GreenLampXP: 0,
                Date: DateTimeOffset.Parse("2026-01-01T12:00:00Z")));

        Assert.True(call.Success);
        Assert.Equal(99, call.Value!.Id);
        Assert.NotNull(seen);
        Assert.Equal(HttpMethod.Post, seen!.Method);
        Assert.Equal("/api/farm-logs", seen.RequestUri?.AbsolutePath);
        Assert.False(seen.Headers.Contains("Origin"));
        Assert.Equal("jwt", seen.Headers.Authorization?.Parameter);
        Assert.Contains("\"xpFarmed\":1000", body, StringComparison.Ordinal);
        Assert.Contains("\"redLampXP\":200", body, StringComparison.Ordinal);
        Assert.Contains("\"purpleLampXP\":0", body, StringComparison.Ordinal);
        Assert.Contains("\"blueLampXP\":0", body, StringComparison.Ordinal);
        Assert.Contains("\"greenLampXP\":0", body, StringComparison.Ordinal);
        Assert.Contains("\"acquiredXpSp\":25", body, StringComparison.Ordinal);
        Assert.Contains("\"minutes\":15", body, StringComparison.Ordinal);
        Assert.DoesNotContain("redLampXp\":", body, StringComparison.Ordinal);
    }

    [Fact]
    public async Task GetAreasDoesNotSendOrigin()
    {
        HttpRequestMessage? seen = null;
        var handler = new StubHandler(request =>
        {
            seen = request;
            return Json(HttpStatusCode.OK, """
                [{"id":1,"name":"World","spots":[]},{"id":2,"name":"Special Zone","spots":[]}]
                """);
        });
        var client = new TrackerApiClient(new HttpClient(handler)
        {
            BaseAddress = new Uri("https://l2tracker.cc/"),
        });

        var result = await client.GetAreasAsync("jwt");
        Assert.True(result.Success);
        Assert.Equal("/api/areas", seen!.RequestUri?.AbsolutePath);
        Assert.False(seen.Headers.Contains("Origin"));
        var world = WorldArea.Find(result.Value);
        Assert.NotNull(world);
        Assert.Equal(1, world.Id);
        Assert.Equal("World", world.Name);
    }

    [Fact]
    public async Task PostSpotSendsNameAndAreaIdWithoutOrigin()
    {
        HttpRequestMessage? seen = null;
        string? body = null;
        var handler = new StubHandler(request =>
        {
            seen = request;
            body = request.Content!.ReadAsStringAsync().GetAwaiter().GetResult();
            return Json(HttpStatusCode.OK, """{"id":50,"userId":"u1","name":"Brand New Camp","areaId":1}""");
        });
        var client = new TrackerApiClient(new HttpClient(handler)
        {
            BaseAddress = new Uri("https://l2tracker.cc/"),
        });

        var call = await client.PostSpotAsync("jwt", "Brand New Camp", 1);
        Assert.True(call.Success);
        Assert.Equal(50, call.Value!.Id);
        Assert.Equal("Brand New Camp", call.Value.Name);
        Assert.Equal(1, call.Value.AreaId);
        Assert.Equal(HttpMethod.Post, seen!.Method);
        Assert.Equal("/api/spots", seen.RequestUri?.AbsolutePath);
        Assert.False(seen.Headers.Contains("Origin"));
        Assert.Contains("\"name\":\"Brand New Camp\"", body, StringComparison.Ordinal);
        Assert.Contains("\"areaId\":1", body, StringComparison.Ordinal);
    }

    [Fact]
    public async Task DeleteSpotDoesNotSendOrigin()
    {
        HttpRequestMessage? seen = null;
        var handler = new StubHandler(request =>
        {
            seen = request;
            return new HttpResponseMessage(HttpStatusCode.OK);
        });
        var client = new TrackerApiClient(new HttpClient(handler)
        {
            BaseAddress = new Uri("https://l2tracker.cc/"),
        });

        var call = await client.DeleteSpotAsync("jwt", 50);
        Assert.True(call.Success);
        Assert.Equal(HttpMethod.Delete, seen!.Method);
        Assert.Equal("/api/spots/50", seen.RequestUri?.AbsolutePath);
        Assert.False(seen.Headers.Contains("Origin"));
    }

    [Fact]
    public void SaveIsDisabledUntilCharacterAndSpotAreChosen()
    {
        var character = new CharacterInfo(1, "TestChar", "S", 80, 0, 85);
        var spot = new SpotInfo(10, "Dragon Valley (east)", 1, new SpotAreaInfo(1, "World"));
        Assert.False(SessionPickers.SaveEnabled(null, null));
        Assert.False(SessionPickers.SaveEnabled(character, null));
        Assert.False(SessionPickers.SaveEnabled(null, spot));
        Assert.True(SessionPickers.SaveEnabled(character, spot));
        var fromLocation = SpotResolve.Evaluate(
            null,
            "Dragon Valley (east)",
            "Dragon Valley (east)",
            [spot],
            spotsLoaded: true,
            new AreaInfo(1, "World"));
        Assert.True(SessionPickers.SaveReady(character, fromLocation));
        Assert.False(SessionPickers.SaveReady(
            character,
            SpotResolve.Evaluate(null, null, null, [spot], spotsLoaded: true, null)));
        Assert.False(SessionPickers.SaveReady(
            character,
            SpotResolve.Evaluate(
                null,
                "Brand New Camp",
                "Brand New Camp",
                spots: null,
                spotsLoaded: false,
                new AreaInfo(1, "World"))));
        Assert.Contains("Sign in", SessionPickers.SignInToLoad, StringComparison.Ordinal);
        Assert.Contains("Sign in", SessionPickers.SignInToSave, StringComparison.Ordinal);
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
