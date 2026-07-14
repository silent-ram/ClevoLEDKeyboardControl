using ColorfulLedKeyboard.Core;
using NAudio.CoreAudioApi;
using System.Diagnostics;

namespace ColorfulLedKeyboard.Tray;

/// <summary>
/// 必须运行在交互用户会话中；LocalSystem 服务位于 Session 0，无法看到用户播放器的音频会话。
/// </summary>
internal sealed class AudioSessionMonitor : IDisposable
{
    private static readonly TimeSpan StartDelay = TimeSpan.FromMilliseconds(300);
    private static readonly TimeSpan StopDelay = TimeSpan.FromSeconds(2);
    private const float ActivityThreshold = 0.001f;
    private readonly MMDeviceEnumerator _enumerator = new();
    private readonly Dictionary<string, PlaybackLatch> _latches = new(StringComparer.OrdinalIgnoreCase);
    private readonly System.Threading.Timer _timer;
    private int _refreshing;
    private bool _disposed;

    public AudioSessionMonitor()
    {
        _timer = new System.Threading.Timer(_ => Refresh(), null, TimeSpan.Zero, TimeSpan.FromMilliseconds(100));
    }

    private void Refresh()
    {
        if (_disposed || Interlocked.Exchange(ref _refreshing, 1) != 0) return;
        try
        {
            var now = DateTimeOffset.UtcNow;
            var observed = new Dictionary<string, ObservedApplication>(StringComparer.OrdinalIgnoreCase);
            var devices = _enumerator.EnumerateAudioEndPoints(DataFlow.Render, DeviceState.Active);
            for (var deviceIndex = 0; deviceIndex < devices.Count; deviceIndex++)
            {
                using var device = devices[deviceIndex];
                try
                {
                    device.AudioSessionManager.RefreshSessions();
                    var sessions = device.AudioSessionManager.Sessions;
                    for (var index = 0; index < sessions.Count; index++)
                    {
                        using var session = sessions[index];
                        var pid = unchecked((int)session.GetProcessID);
                        if (pid <= 0) continue;
                        try
                        {
                            using var process = Process.GetProcessById(pid);
                            var name = AppProfileRule.NormalizeProcessName(process.ProcessName);
                            if (string.IsNullOrWhiteSpace(name)) continue;
                            var path = TryGetPath(process);
                            var key = name + "|" + path;
                            if (!observed.TryGetValue(key, out var app))
                            {
                                app = new ObservedApplication(name, path);
                                observed.Add(key, app);
                            }
                            app.ProcessIds.Add(pid);
                            app.PeakLevel = Math.Max(app.PeakLevel, session.AudioMeterInformation.MasterPeakValue);
                        }
                        catch (Exception ex) when (ex is ArgumentException or InvalidOperationException or System.ComponentModel.Win32Exception)
                        {
                        }
                    }
                }
                catch (Exception ex) when (ex is InvalidOperationException or UnauthorizedAccessException or System.Runtime.InteropServices.COMException)
                {
                }
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
}
