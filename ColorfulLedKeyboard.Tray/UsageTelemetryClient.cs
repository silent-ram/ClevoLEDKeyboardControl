using System.Net;
using System.Net.Http;
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
    // 上报端点:仅腾讯云函数(广州,直写 COS),国内外用户统一入口。
    // Cloudflare Worker 已退役(workers.dev 国内不可达)。
    // 若将来增加端点,按数组顺序排列即可——客户端会按系统时区自动排序,
    // 并在失败时依次 fallback;服务端按 installId 幂等去重,重复上报不虚增统计。
    internal static readonly string[] DefaultEndpoints =
    [
        "https://1417250850-29682anb03.ap-guangzhou.tencentscf.com/v1/telemetry",
    ];

    // 10 秒:函数 URL 偶发冷启动 + 单实例排队可能耗 2~3 秒,3 秒会把偶发慢请求误判为失败。
    private static readonly TimeSpan RequestTimeout = TimeSpan.FromSeconds(10);
    private readonly HttpClient _httpClient;
    private readonly string[] _endpoints;
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
        : this(httpClient, statePath, endpoint is null ? DefaultEndpoints : [endpoint], utcNow)
    {
    }

    internal UsageTelemetryClient(
        HttpClient? httpClient,
        string? statePath,
        string[] endpoints,
        Func<DateTimeOffset>? utcNow = null,
        TimeZoneInfo? timeZone = null)
    {
        _httpClient = httpClient ?? new HttpClient { Timeout = RequestTimeout };
        _statePath = statePath ?? AppPaths.UsageTelemetryStatePath;
        _endpoints = OrderEndpointsForLocalZone(endpoints is { Length: > 0 } ? endpoints : DefaultEndpoints, timeZone ?? TimeZoneInfo.Local);
        _endpoint = _endpoints[0];
        _utcNow = utcNow ?? (() => DateTimeOffset.UtcNow);
    }

    // workers.dev（Cloudflare 直连）在中国大陆基本不可达；自建/云函数中转端点则相反，
    // 海外直连它们反而绕路。按系统时区预排序：中国时区用户中转在前，其余用户 CF 直连在前。
    // 排序只是优化——fallback 保证任何顺序下都能找到可达端点。
    internal static string[] OrderEndpointsForLocalZone(string[] endpoints, TimeZoneInfo timeZone)
    {
        static bool IsCloudflareDirect(string endpoint) =>
            endpoint.Contains("workers.dev", StringComparison.OrdinalIgnoreCase);

        static bool IsChinaLocalZone(TimeZoneInfo zone) =>
            zone.Id is "China Standard Time" or "Asia/Shanghai" or "Asia/Urumqi" ||
            (zone.BaseUtcOffset == TimeSpan.FromHours(8) && zone.DisplayName?.Contains("China") == true);

        var preferRelay = IsChinaLocalZone(timeZone);
        return endpoints
            .Select((endpoint, index) => (Endpoint: endpoint, Index: index))
            .OrderBy(item =>
            {
                var isCloudflare = IsCloudflareDirect(item.Endpoint);
                // 期望的端点类型得 0 分排在前面，不匹配的得 1 分；分数相同保持原数组顺序。
                return (isCloudflare != preferRelay ? 0 : 1, item.Index);
            })
            .Select(item => item.Endpoint)
            .ToArray();
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
            var delivered = false;
            foreach (var endpoint in _endpoints)
            {
                try
                {
                    using var response = await _httpClient.PostAsJsonAsync(endpoint, request).ConfigureAwait(false);
                    // Worker 成功响应固定为 204：旧的默认 Hello World 页面（200）或中转函数异常页
                    // （200）都不能算成功，否则国内用户会被误记为上报成功。
                    if (response.StatusCode != HttpStatusCode.NoContent) continue;
                    delivered = true;
                    break;
                }
                catch (HttpRequestException)
                {
                }
                catch (TaskCanceledException)
                {
                }
            }

            if (!delivered)
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
