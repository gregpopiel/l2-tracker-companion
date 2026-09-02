using System.Net;
using System.Text;
using System.Text.Json;

namespace L2TrackerCompanion.Api;

/// <summary>
/// Plan step 19: record whether a native GET sent <c>Origin</c> and what
/// status/body came back. Does not change auth or CORS.
/// </summary>
public sealed class HttpSmokeHandler : DelegatingHandler
{
    public HttpSmokeHandler(HttpMessageHandler inner)
        : base(inner)
    {
        ArgumentNullException.ThrowIfNull(inner);
    }

    public HttpRequestMessage? LastRequest { get; private set; }

    public HttpStatusCode LastStatus { get; private set; }

    public string LastBody { get; private set; } = "";

    public bool SentOrigin => LastRequest?.Headers.Contains("Origin") == true;

    protected override async Task<HttpResponseMessage> SendAsync(
        HttpRequestMessage request,
        CancellationToken cancellationToken)
    {
        LastRequest = request;
        var response = await base.SendAsync(request, cancellationToken).ConfigureAwait(false);
        LastStatus = response.StatusCode;
        LastBody = await response.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false);
        response.Content = new StringContent(LastBody, Encoding.UTF8, "application/json");
        return response;
    }
}

public static class HttpSmoke
{
    public static bool IsJson(string body)
    {
        if (string.IsNullOrWhiteSpace(body))
        {
            return false;
        }

        try
        {
            using var doc = JsonDocument.Parse(body);
            return doc.RootElement.ValueKind is JsonValueKind.Object or JsonValueKind.Array;
        }
        catch (JsonException)
        {
            return false;
        }
    }

    public static bool Passed(HttpSmokeHandler probe)
        => probe.LastStatus == HttpStatusCode.OK && !probe.SentOrigin && IsJson(probe.LastBody);

    public static string Format(HttpSmokeHandler probe, string label)
    {
        var uri = probe.LastRequest?.RequestUri;
        var origin = probe.SentOrigin ? "present" : "(none)";
        var json = IsJson(probe.LastBody) ? "yes" : "no";
        return
            $"{label}\n"
            + $"  {probe.LastRequest?.Method} {uri}\n"
            + $"  Origin: {origin}\n"
            + $"  HTTP {(int)probe.LastStatus} {probe.LastStatus}\n"
            + $"  JSON: {json}";
    }
}
