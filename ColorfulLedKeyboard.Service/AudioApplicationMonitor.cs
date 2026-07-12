using ColorfulLedKeyboard.Core;
using NAudio.CoreAudioApi;
using System.Diagnostics;

namespace ColorfulLedKeyboard.Service;

internal sealed class AudioApplicationMonitor : IDisposable
{
    internal static readonly TimeSpan StartDelay = TimeSpan.FromMilliseconds(300);
    internal static readonly TimeSpan StopDelay = TimeSpan.FromSeconds(2);
    private const float ActivityThreshold = 0.001f;

    private readonly MMDeviceEnumerator _enumerator = new();
    private readonly Dictionary<string, PlaybackLatch> _latches = new(StringComparer.OrdinalIgnoreCase);
    private IReadOnlyList<AudioApplicationStatus> _lastSnapshot = [];
    private DateTimeOffset _lastPollAt = DateTimeOffset.MinValue;
    private bool _disposed;

    public IReadOnlyList<AudioApplicationStatus> Poll(string foregroundProcessName, DateTimeOffset now)
    {
        if (_disposed) return [];
        if (now - _lastPollAt < TimeSpan.FromMilliseconds(100)) return _lastSnapshot;
        _lastPollAt = now;
        var observed = new Dictionary<string, ObservedApplication>(StringComparer.OrdinalIgnoreCase);
        try
        {
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
                            var processName = AppProfileRule.NormalizeProcessName(process.ProcessName);
                            if (string.IsNullOrWhiteSpace(processName)) continue;
                            var peak = session.AudioMeterInformation.MasterPeakValue;
                            var path = TryGetPath(process);
                            var key = processName + "|" + path;
                            if (!observed.TryGetValue(key, out var app))
                            {
                                app = new ObservedApplication(processName, path);
                                observed.Add(key, app);
                            }
                            app.ProcessIds.Add(pid);
                            app.PeakLevel = Math.Max(app.PeakLevel, peak);
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
        }
        catch (Exception ex) when (ex is InvalidOperationException or UnauthorizedAccessException or System.Runtime.InteropServices.COMException)
        {
        }

        foreach (var name in _latches.Keys.Union(observed.Keys, StringComparer.OrdinalIgnoreCase).ToList())
        {
            if (!_latches.TryGetValue(name, out var latch))
            {
                latch = new PlaybackLatch();
                _latches.Add(name, latch);
            }
            var peak = observed.TryGetValue(name, out var app) ? app.PeakLevel : 0f;
            UpdateLatch(latch, peak, now);
        }

        _lastSnapshot = observed.Values.Select(app => new AudioApplicationStatus
        {
            ProcessName = app.ProcessName,
            ExecutablePath = app.ExecutablePath,
            ProcessIds = app.ProcessIds.Distinct().OrderBy(pid => pid).ToList(),
            PeakLevel = app.PeakLevel,
            IsPlaying = _latches[app.ProcessName + "|" + app.ExecutablePath].IsPlaying,
            IsForeground = string.Equals(app.ProcessName, AppProfileRule.NormalizeProcessName(foregroundProcessName), StringComparison.OrdinalIgnoreCase)
        }).OrderByDescending(app => app.IsPlaying).ThenByDescending(app => app.PeakLevel).ToList();
        return _lastSnapshot;
    }

    internal static void UpdateLatch(PlaybackLatch latch, float peak, DateTimeOffset now)
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
        _enumerator.Dispose();
    }

    internal sealed class PlaybackLatch
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
