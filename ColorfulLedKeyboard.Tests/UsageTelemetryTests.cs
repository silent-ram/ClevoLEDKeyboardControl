using System.Net;
using System.Text.Json;
using ColorfulLedKeyboard.Core;
using ColorfulLedKeyboard.Tray;

namespace ColorfulLedKeyboard.Tests;

public sealed class UsageTelemetryTests : IDisposable
{
    private readonly string _directory = Path.Combine(Path.GetTempPath(), $"clevo-usage-{Guid.NewGuid():N}");
    private string StatePath => Path.Combine(_directory, AppPaths.UsageTelemetryStateFileName);

    [Fact]
    public void LegacySettingsDefaultToEnabled()
    {
        var settings = new KeyboardSettings().Normalize();

        Assert.True(settings.UserImprovementPlan.Enabled);
        Assert.True(settings.CloneForRuntime().UserImprovementPlan.Enabled);
    }

    [Fact]
    public void StateCreatesAndPreservesStableInstallId()
    {
        var state = UsageTelemetryState.Load(StatePath);
        state.EnsureInstallId();
        state.Save(StatePath);

        var reloaded = UsageTelemetryState.Load(StatePath);

        Assert.True(Guid.TryParse(reloaded.InstallId, out _));
        Assert.Equal(state.InstallId, reloaded.InstallId);
    }

    [Fact]
    public async Task ClientSendsInstallOnlyOncePerSuccessfulState()
    {
        var handler = new RecordingHandler();
        using var client = new UsageTelemetryClient(
            new HttpClient(handler),
            StatePath,
            "https://example.test/v1/telemetry",
            () => new DateTimeOffset(2026, 9, 1, 12, 0, 0, TimeSpan.Zero));
        var settings = new KeyboardSettings().Normalize();

        await client.SyncAsync(settings);
        await client.SyncAsync(settings);

        Assert.Single(handler.Requests);
        Assert.Equal("install", handler.Requests[0].Event);
        Assert.True(UsageTelemetryState.Load(StatePath).InstallSent);
    }

    [Fact]
    public async Task ClientSendsVersionWhenVersionChanges()
    {
        var state = new UsageTelemetryState
        {
            InstallId = Guid.NewGuid().ToString("D"),
            InstallSent = true,
            LastVersionSent = "0.0.1",
            LastHeartbeatDate = "2026-09-01"
        };
        state.Save(StatePath);

        var handler = new RecordingHandler();
        using var client = new UsageTelemetryClient(
            new HttpClient(handler),
            StatePath,
            "https://example.test/v1/telemetry",
            () => new DateTimeOffset(2026, 9, 1, 12, 0, 0, TimeSpan.Zero));

        await client.SyncAsync(new KeyboardSettings().Normalize());

        Assert.Single(handler.Requests);
        Assert.Equal("version", handler.Requests[0].Event);
        Assert.Equal(typeof(UsageTelemetryClient).Assembly.GetName().Version?.ToString(3), handler.Requests[0].Version);
    }

    [Fact]
    public async Task ClientSendsAtMostOneHeartbeatPerUtcDay()
    {
        var now = new DateTimeOffset(2026, 9, 1, 12, 0, 0, TimeSpan.Zero);
        var state = new UsageTelemetryState
        {
            InstallId = Guid.NewGuid().ToString("D"),
            InstallSent = true,
            LastVersionSent = typeof(UsageTelemetryClient).Assembly.GetName().Version?.ToString(3) ?? "0.0.0",
            LastHeartbeatDate = "2026-08-31"
        };
        state.Save(StatePath);

        var handler = new RecordingHandler();
        using var client = new UsageTelemetryClient(
            new HttpClient(handler),
            StatePath,
            "https://example.test/v1/telemetry",
            () => now);
        var settings = new KeyboardSettings().Normalize();

        await client.SyncAsync(settings);
        await client.SyncAsync(settings);

        Assert.Single(handler.Requests);
        Assert.Equal("heartbeat", handler.Requests[0].Event);

        now = now.AddDays(1);
        await client.SyncAsync(settings);

        Assert.Equal(2, handler.Requests.Count);
        Assert.All(handler.Requests, request => Assert.Equal("heartbeat", request.Event));
    }

    [Fact]
    public async Task ClientDoesNotTreatDefaultHelloWorldResponseAsSuccess()
    {
        var handler = new RecordingHandler(HttpStatusCode.OK);
        using var client = new UsageTelemetryClient(
            new HttpClient(handler),
            StatePath,
            "https://example.test/v1/telemetry",
            () => new DateTimeOffset(2026, 9, 1, 12, 0, 0, TimeSpan.Zero));

        await client.SyncAsync(new KeyboardSettings().Normalize());

        Assert.False(UsageTelemetryState.Load(StatePath).InstallSent);
    }

    [Fact]
    public async Task ClientRetriesAfterTransientFailureWithBackoff()
    {
        var now = new DateTimeOffset(2026, 9, 1, 12, 0, 0, TimeSpan.Zero);
        var handler = new RecordingHandler(HttpStatusCode.ServiceUnavailable);
        using var client = new UsageTelemetryClient(
            new HttpClient(handler),
            StatePath,
            "https://example.test/v1/telemetry",
            () => now);
        var settings = new KeyboardSettings().Normalize();

        await client.SyncAsync(settings);
        Assert.False(UsageTelemetryState.Load(StatePath).InstallSent);

        // 第一次失败后不会立即忙循环；超过 15 秒后允许下一次补偿上报。
        handler.StatusCode = HttpStatusCode.NoContent;
        await client.SyncAsync(settings);
        Assert.Single(handler.Requests);

        now = now.AddSeconds(16);
        await client.SyncAsync(settings);

        Assert.Equal(2, handler.Requests.Count);
        Assert.True(UsageTelemetryState.Load(StatePath).InstallSent);
    }

    [Fact]
    public void EndpointOrderPrefersRelayForChinaTimeZoneAndDirectForOthers()
    {
        var cloudflare = "https://clevo-usage-api.yycc1936.workers.dev/v1/telemetry";
        var relayTencent = "https://example.gz.tencentscf.com/v1/telemetry";
        var relayAliyun = "https://example.cn-hangzhou.fcapp.run/v1/telemetry";
        var china = TimeZoneInfo.FindSystemTimeZoneById("China Standard Time");

        // 中国时区：中转在前，CF 直连殿后
        var forChina = UsageTelemetryClient.OrderEndpointsForLocalZone([cloudflare, relayTencent, relayAliyun], china);
        Assert.Equal([relayTencent, relayAliyun, cloudflare], forChina);

        // 其他时区：CF 直连在前
        var forOverseas = UsageTelemetryClient.OrderEndpointsForLocalZone([cloudflare, relayTencent], TimeZoneInfo.Utc);
        Assert.Equal([cloudflare, relayTencent], forOverseas);

        // 同类端点保持原有相对顺序
        var sameType = UsageTelemetryClient.OrderEndpointsForLocalZone([relayTencent, relayAliyun], china);
        Assert.Equal([relayTencent, relayAliyun], sameType);
    }

    [Fact]
    public async Task ClientFallsBackToNextEndpointWhenPrimaryFails()
    {
        var primary = "https://primary.test/v1/telemetry";
        var secondary = "https://secondary.test/v1/telemetry";
        var handler = new RecordingHandler
        {
            StatusCodeSelector = url => url.StartsWith("https://primary.test", StringComparison.Ordinal)
                ? HttpStatusCode.InternalServerError
                : HttpStatusCode.NoContent
        };
        using var client = new UsageTelemetryClient(
            new HttpClient(handler),
            StatePath,
            [primary, secondary],
            () => new DateTimeOffset(2026, 9, 1, 12, 0, 0, TimeSpan.Zero));
        var settings = new KeyboardSettings().Normalize();

        await client.SyncAsync(settings);

        Assert.Equal(2, handler.Endpoints.Count);
        Assert.Equal(primary, handler.Endpoints[0]);
        Assert.Equal(secondary, handler.Endpoints[1]);
        Assert.True(UsageTelemetryState.Load(StatePath).InstallSent);
    }

    [Fact]
    public async Task ClientStopsAfterFirstSuccessfulEndpoint()
    {
        // 主端点成功时不得向备用端点重复发送；服务端虽按 installId 幂等，客户端也应省流量。
        var primary = "https://primary.test/v1/telemetry";
        var secondary = "https://secondary.test/v1/telemetry";
        var handler = new RecordingHandler
        {
            StatusCodeSelector = _ => HttpStatusCode.NoContent
        };
        using var client = new UsageTelemetryClient(
            new HttpClient(handler),
            StatePath,
            [primary, secondary],
            () => new DateTimeOffset(2026, 9, 1, 12, 0, 0, TimeSpan.Zero));
        var settings = new KeyboardSettings().Normalize();

        await client.SyncAsync(settings);

        Assert.Single(handler.Requests);
        Assert.True(UsageTelemetryState.Load(StatePath).InstallSent);
    }

    public void Dispose()
    {
        if (Directory.Exists(_directory)) Directory.Delete(_directory, recursive: true);
    }

    private sealed class RecordingHandler : HttpMessageHandler
    {
        public HttpStatusCode StatusCode { get; set; }

        public Func<string, HttpStatusCode>? StatusCodeSelector { get; set; }

        public RecordingHandler(HttpStatusCode statusCode = HttpStatusCode.NoContent) => StatusCode = statusCode;

        public List<TelemetryRequest> Requests { get; } = [];

        public List<string> Endpoints { get; } = [];

        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            var endpoint = request.RequestUri?.ToString() ?? "";
            var payload = JsonSerializer.Deserialize<TelemetryRequest>(
                await request.Content!.ReadAsStringAsync(cancellationToken),
                new JsonSerializerOptions { PropertyNameCaseInsensitive = true });
            Assert.NotNull(payload);
            Endpoints.Add(endpoint);
            Requests.Add(payload!);
            var status = StatusCodeSelector is not null ? StatusCodeSelector(endpoint) : StatusCode;
            return new HttpResponseMessage(status);
        }
    }

    private sealed class TelemetryRequest
    {
        public string InstallId { get; set; } = "";
        public string Event { get; set; } = "";
        public string Version { get; set; } = "";
    }
}
