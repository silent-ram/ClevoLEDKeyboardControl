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

    public void Dispose()
    {
        if (Directory.Exists(_directory)) Directory.Delete(_directory, recursive: true);
    }

    private sealed class RecordingHandler : HttpMessageHandler
    {
        private readonly HttpStatusCode _statusCode;

        public RecordingHandler(HttpStatusCode statusCode = HttpStatusCode.NoContent) => _statusCode = statusCode;

        public List<TelemetryRequest> Requests { get; } = [];

        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            var payload = JsonSerializer.Deserialize<TelemetryRequest>(
                await request.Content!.ReadAsStringAsync(cancellationToken),
                new JsonSerializerOptions { PropertyNameCaseInsensitive = true });
            Assert.NotNull(payload);
            Requests.Add(payload!);
            return new HttpResponseMessage(_statusCode);
        }
    }

    private sealed class TelemetryRequest
    {
        public string InstallId { get; set; } = "";
        public string Event { get; set; } = "";
        public string Version { get; set; } = "";
    }
}
