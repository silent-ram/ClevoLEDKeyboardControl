using ColorfulLedKeyboard.Core;
using NAudio.CoreAudioApi;
using NAudio.CoreAudioApi.Interfaces;
using System.Diagnostics;
using System.Runtime.InteropServices;

namespace ColorfulLedKeyboard.Tray;

/// <summary>
/// 必须运行在交互用户会话中；LocalSystem 服务位于 Session 0，无法看到用户播放器的音频会话。
/// </summary>
internal sealed class AudioSessionMonitor : IDisposable
{
    private static readonly TimeSpan StartDelay = TimeSpan.FromMilliseconds(300);
    private static readonly TimeSpan StopDelay = TimeSpan.FromSeconds(2);
    private const float ActivityThreshold = 0.001f;

    // WASAPI 端点枚举（EnumAudioEndpoints/GetSessionEnumerator）在交互会话下每次调用都会在原生堆
    // 残留约 1.5 KB（VMMap 实测，GC 不可回收），百毫秒级全量轮询会累积成肉眼可见的泄漏。
    // 因此枚举/Activate 只发生在：启动、30 秒兜底刷新、以及 OnSessionCreated 通知（新会话即时感知）；
    // 日常 100ms 采样复用缓存的 AudioMeterInformation 句柄做纯读数，律动响应粒度与轮询方案一致。
    private static readonly TimeSpan PollInterval = TimeSpan.FromMilliseconds(100);
    private static readonly TimeSpan FullRefreshInterval = TimeSpan.FromSeconds(30);

    private readonly MMDeviceEnumerator _enumerator = new();
    private readonly Dictionary<string, PlaybackLatch> _latches = new(StringComparer.OrdinalIgnoreCase);
    private readonly List<CachedSessionMeter> _cachedMeters = [];
    private readonly List<MMDevice> _cachedDevices = [];
    private readonly System.Threading.Timer _timer;
    private DateTimeOffset _nextFullRefreshUtc = DateTimeOffset.MinValue;
    private int _structureDirty = 1;
    private int _refreshing;
    private bool _disposed;

    public AudioSessionMonitor()
    {
        _timer = new System.Threading.Timer(_ => Refresh(), null, TimeSpan.Zero, PollInterval);
    }

    private void Refresh()
    {
        if (_disposed || Interlocked.Exchange(ref _refreshing, 1) != 0) return;
        try
        {
            var now = DateTimeOffset.UtcNow;
            if (now >= _nextFullRefreshUtc || Interlocked.Exchange(ref _structureDirty, 0) != 0)
            {
                RebuildSessionMeters();
                _nextFullRefreshUtc = now + FullRefreshInterval;
            }

            var observed = SampleCachedMeters(now);

            var foreground = ForegroundWindowProcessName.GetName() ?? "";
            new AudioApplicationsState
            {
                UpdatedUtc = now,
                Applications = observed.Select(pair => new AudioApplicationStatus
                {
                    ProcessName = pair.Value.ProcessName,
                    ExecutablePath = pair.Value.ExecutablePath,
                    ProcessIds = pair.Value.ProcessIds.Distinct().OrderBy(pid => pid).ToList(),
                    PeakLevel = pair.Value.PeakLevel,
                    IsPlaying = _latches[pair.Key].IsPlaying,
                    IsForeground = string.Equals(pair.Value.ProcessName, AppProfileRule.NormalizeProcessName(foreground), StringComparison.OrdinalIgnoreCase)
                }).OrderByDescending(app => app.IsPlaying).ThenByDescending(app => app.PeakLevel).ToList()
            }.Save();
        }
        catch (Exception ex)
        {
            new AudioApplicationsState
            {
                UpdatedUtc = DateTimeOffset.UtcNow,
                LastError = ex.GetType().Name + ": " + ex.Message,
                LastErrorUtc = DateTimeOffset.UtcNow
            }.Save();
        }
        finally
        {
            Interlocked.Exchange(ref _refreshing, 0);
        }
    }

    private void RebuildSessionMeters()
    {
        UnsubscribeCachedDevices();
        _cachedMeters.Clear();
        try
        {
            var devices = _enumerator.EnumerateAudioEndPoints(DataFlow.Render, DeviceState.Active);
            for (var deviceIndex = 0; deviceIndex < devices.Count; deviceIndex++)
            {
                // 设备与其 AudioSessionManager 长期缓存：订阅 OnSessionCreated 获得新会话的即时通知，
                // 避免高频重新 Activate。下一次全量刷新时退订并释放。
                var device = devices[deviceIndex];
                try
                {
                    var manager = device.AudioSessionManager;
                    manager.OnSessionCreated += OnDeviceSessionCreated;
                    _cachedDevices.Add(device);
                    manager.RefreshSessions();
                    var sessions = manager.Sessions;
                    for (var index = 0; index < sessions.Count; index++)
                    {
                        // 缓存的 AudioSessionControl 持有到下一次全量刷新；会话结束后其 COM 调用会
                        // 抛 COMException，SampleCachedMeters 会把失效项剔除。不主动 Dispose，
                        // 由 GC 回收包装对象，避免触碰失效的 COM 指针。
                        var session = sessions[index];
                        var pid = unchecked((int)session.GetProcessID);
                        if (pid <= 0)
                        {
                            session.Dispose();
                            continue;
                        }
                        _cachedMeters.Add(new CachedSessionMeter(session, pid));
                    }
                }
                catch (Exception ex) when (ex is InvalidOperationException or UnauthorizedAccessException or COMException)
                {
                    device.Dispose();
                }
            }
        }
        catch (Exception ex) when (ex is InvalidOperationException or UnauthorizedAccessException or COMException)
        {
        }
    }

    // COM 事件回调线程：只置脏标志，重建留给下一个采样 tick（≤100ms），避免并发与重活。
    private void OnDeviceSessionCreated(object? sender, IAudioSessionControl newSession) =>
        Interlocked.Exchange(ref _structureDirty, 1);

    private void UnsubscribeCachedDevices()
    {
        foreach (var device in _cachedDevices)
        {
            try { device.AudioSessionManager.OnSessionCreated -= OnDeviceSessionCreated; } catch { }
            try { device.Dispose(); } catch { }
        }
        _cachedDevices.Clear();
    }

    private Dictionary<string, ObservedApplication> SampleCachedMeters(DateTimeOffset now)
    {
        var observed = new Dictionary<string, ObservedApplication>(StringComparer.OrdinalIgnoreCase);
        List<CachedSessionMeter>? expired = null;
        foreach (var cached in _cachedMeters)
        {
            float peak;
            try
            {
                cached.Meter ??= cached.Control.AudioMeterInformation;
                peak = cached.Meter.MasterPeakValue;
            }
            catch (Exception ex) when (ex is COMException or InvalidOperationException or ArgumentException)
            {
                (expired ??= []).Add(cached);
                continue;
            }

            try
            {
                using var process = Process.GetProcessById(cached.ProcessId);
                var name = AppProfileRule.NormalizeProcessName(process.ProcessName);
                if (string.IsNullOrWhiteSpace(name)) continue;
                var path = TryGetPath(process);
                var key = name + "|" + path;
                if (!observed.TryGetValue(key, out var app))
                {
                    app = new ObservedApplication(name, path);
                    observed.Add(key, app);
                }
                app.ProcessIds.Add(cached.ProcessId);
                app.PeakLevel = Math.Max(app.PeakLevel, peak);
            }
            catch (Exception ex) when (ex is ArgumentException or InvalidOperationException or System.ComponentModel.Win32Exception)
            {
                // 进程已退出：峰值记 0，会话句柄留到下轮全量刷新时清理。
            }
        }

        if (expired is not null)
        {
            foreach (var stale in expired) _cachedMeters.Remove(stale);
        }

        foreach (var key in _latches.Keys.Union(observed.Keys, StringComparer.OrdinalIgnoreCase).ToList())
        {
            if (!_latches.TryGetValue(key, out var latch))
            {
                latch = new PlaybackLatch();
                _latches.Add(key, latch);
            }
            UpdateLatch(latch, observed.TryGetValue(key, out var app) ? app.PeakLevel : 0, now);
        }

        return observed;
    }

    private static void UpdateLatch(PlaybackLatch latch, float peak, DateTimeOffset now)
    {
        if (peak > ActivityThreshold)
        {
            latch.FirstSoundAt ??= now;
            latch.LastSoundAt = now;
            if (!latch.IsPlaying && now - latch.FirstSoundAt.Value >= StartDelay) latch.IsPlaying = true;
            return;
        }
        latch.FirstSoundAt = null;
        if (latch.IsPlaying && latch.LastSoundAt.HasValue && now - latch.LastSoundAt.Value >= StopDelay)
            latch.IsPlaying = false;
    }

    private static string TryGetPath(Process process)
    {
        try { return process.MainModule?.FileName ?? ""; }
        catch { return ""; }
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        _timer.Dispose();
        UnsubscribeCachedDevices();
        _enumerator.Dispose();
    }

    private sealed class PlaybackLatch
    {
        public DateTimeOffset? FirstSoundAt { get; set; }
        public DateTimeOffset? LastSoundAt { get; set; }
        public bool IsPlaying { get; set; }
    }

    private sealed class ObservedApplication(string processName, string executablePath)
    {
        public string ProcessName { get; } = processName;
        public string ExecutablePath { get; } = executablePath;
        public List<int> ProcessIds { get; } = [];
        public float PeakLevel { get; set; }
    }

    private sealed class CachedSessionMeter(AudioSessionControl control, int processId)
    {
        public AudioSessionControl Control { get; } = control;
        public int ProcessId { get; } = processId;
        public AudioMeterInformation? Meter { get; set; }
    }
}
