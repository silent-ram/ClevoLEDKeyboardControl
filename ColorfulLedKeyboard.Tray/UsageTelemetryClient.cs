using System.Net;
using System.Net.Http.Json;
using System.Text.Json.Serialization;
using ColorfulLedKeyboard.Core;

namespace ColorfulLedKeyboard.Tray;

/// <summary>
/// 向用户改进计划服务发送最小化、匿名的运行统计。
/// 网络或状态文件异常均静默忽略，不影响灯效和设置功能。
/// </summary>
internal sealed class UsageTelemetryClient : IDisposable
{
    internal const string Endpoint = "https://clevo-usage-api.yycc1936.workers.dev/v1/telemetry";
    private static readonly TimeSpan RequestTimeout = TimeSpan.FromSeconds(3);
    private readonly HttpClient _httpClient;
    private readonly string _statePath;
    private readonly string _endpoint;
    private readonly Func<DateTimeOffset> _utcNow;
    private readonly SemaphoreSlim _sync = new(1, 1);
    private DateTimeOffset _nextAttemptAt = DateTimeOffset.MinValue;
    private int _consecutiveFailures;
    private bool _disposed;

    public UsageTelemetryClient(
        HttpClient? httpClient = null,
        string? statePath = null,
        string? endpoint = null,
        Func<DateTimeOffset>? utcNow = null)
    {
        _httpClient = httpClient ?? new HttpClient { Timeout = RequestTimeout };
        _statePath = statePath ?? AppPaths.UsageTelemetryStatePath;
        _endpoint = endpoint ?? Endpoint;
        _utcNow = utcNow ?? (() => DateTimeOffset.UtcNow);
    }

    public async Task SyncAsync(KeyboardSettings settings)
    {
        if (_disposed || settings.UserImprovementPlan?.Enabled != true)
        {
            return;
        }

        await _sync.WaitAsync().ConfigureAwait(false);
        try
        {
            var now = _utcNow();
            if (now < _nextAttemptAt)
            {
                return;
            }

            var state = UsageTelemetryState.Load(_statePath);
            var previousInstallId = state.InstallId;
            state.EnsureInstallId();
            if (!string.Equals(previousInstallId, state.InstallId, StringComparison.OrdinalIgnoreCase))
            {
                state.Save(_statePath);
            }

            var version = GetCurrentVersion();
            var today = _utcNow().ToString("yyyy-MM-dd");
            var telemetryEvent = !state.InstallSent
                ? "install"
                : !string.Equals(state.LastVersionSent, version, StringComparison.OrdinalIgnoreCase)
                    ? "version"
                    : !string.Equals(state.LastHeartbeatDate, today, StringComparison.Ordinal)
                        ? "heartbeat"
                        : null;

            if (telemetryEvent is null)
            {
                ScheduleNextDailyCheck(now);
                return;
            }

            var request = new UsageTelemetryRequest(state.InstallId, telemetryEvent, version);
            try
            {
                using var response = await _httpClient.PostAsJsonAsync(_endpoint, request).ConfigureAwait(false);
                // Worker 成功响应固定为 204。这样旧的默认 Hello World 页面（200）不会被误记为成功。
                if (response.StatusCode != HttpStatusCode.NoContent)
                {
                    ScheduleRetry(now);
                    return;
                }
            }
            catch (HttpRequestException)
            {
                ScheduleRetry(now);
                return;
            }
            catch (TaskCanceledException)
            {
                ScheduleRetry(now);
                return;
            }

            _consecutiveFailures = 0;
            _nextAttemptAt = DateTimeOffset.MinValue;

            switch (telemetryEvent)
            {
                case "install":
                    state.InstallSent = true;
                    state.LastVersionSent = version;
                    state.LastHeartbeatDate = today;
                    break;
                case "version":
                    state.LastVersionSent = version;
                    state.LastHeartbeatDate = today;
                    break;
                case "heartbeat":
                    state.LastHeartbeatDate = today;
                    break;
            }

            state.Save(_statePath);
            ScheduleNextDailyCheck(now);
        }
        catch
        {
            // 统计属于非关键功能，任何本地异常都不应打扰用户。
        }
        finally
        {
            _sync.Release();
        }
    }

    private void ScheduleRetry(DateTimeOffset now)
    {
        _consecutiveFailures = Math.Min(_consecutiveFailures + 1, 5);
        var delay = _consecutiveFailures switch
        {
            1 => TimeSpan.FromSeconds(15),
            2 => TimeSpan.FromMinutes(1),
            3 => TimeSpan.FromMinutes(5),
            4 => TimeSpan.FromMinutes(30),
            _ => TimeSpan.FromHours(6),
        };
        _nextAttemptAt = now + delay;
    }

    internal TimeSpan GetNextCheckDelay()
    {
        var now = _utcNow();
        if (_nextAttemptAt <= now)
        {
            return TimeSpan.FromSeconds(1);
        }

        return _nextAttemptAt - now;
    }

    private void ScheduleNextDailyCheck(DateTimeOffset now)
    {
        var nextUtcDate = now.UtcDateTime.Date.AddDays(1);
        _nextAttemptAt = new DateTimeOffset(nextUtcDate, TimeSpan.Zero);
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        _httpClient.Dispose();
    }

    private static string GetCurrentVersion() =>
        typeof(UsageTelemetryClient).Assembly.GetName().Version?.ToString(3) ?? "0.0.0";

    private sealed record UsageTelemetryRequest(
        [property: JsonPropertyName("installId")] string InstallId,
        [property: JsonPropertyName("event")] string Event,
        [property: JsonPropertyName("version")] string Version);
}
