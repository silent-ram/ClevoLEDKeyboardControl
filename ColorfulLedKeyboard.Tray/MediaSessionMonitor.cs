using ColorfulLedKeyboard.Core;
using System.Diagnostics;
using System.Drawing;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Text;
using Windows.Media.Control;

namespace ColorfulLedKeyboard.Tray;

internal sealed class MediaSessionMonitor : IDisposable
{
    private readonly System.Threading.Timer _timer;
    private GlobalSystemMediaTransportControlsSessionManager? _manager;
    private readonly Dictionary<string, List<string>> _colorCache = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, List<string>> _lastColorsBySource = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, (MediaSessionState State, DateTimeOffset Seen)> _lastSessions = new(StringComparer.OrdinalIgnoreCase);
    private Dictionary<string, List<string>> _processNamesBySource = new(StringComparer.OrdinalIgnoreCase);
    private HashSet<string> _processIdentitySources = new(StringComparer.OrdinalIgnoreCase);
    private DateTimeOffset _processIdentityUpdatedUtc = DateTimeOffset.MinValue;
    private int _rebuildManager = 1;
    private int _refreshing;
    private bool _disposed;

    public MediaSessionMonitor()
    {
        _timer = new System.Threading.Timer(async _ => await RefreshAsync(), null, TimeSpan.Zero, TimeSpan.FromMilliseconds(500));
    }

    public void ForceRefresh()
    {
        if (_disposed) return;
        Interlocked.Exchange(ref _rebuildManager, 1);
        _ = RefreshAsync();
    }

    public async Task RefreshAsync()
    {
        if (_disposed || Interlocked.Exchange(ref _refreshing, 1) != 0) return;
        try
        {
            var now = DateTimeOffset.UtcNow;
            if (_manager is null || Interlocked.Exchange(ref _rebuildManager, 0) != 0)
            {
                var manager = await GlobalSystemMediaTransportControlsSessionManager.RequestAsync();
                ReplaceManager(manager);
            }
            var activeManager = _manager ?? throw new InvalidOperationException("Windows media session manager is unavailable.");
            var currentSourceId = activeManager.GetCurrentSession()?.SourceAppUserModelId ?? "";
            var state = new MediaPlaybackState { UpdatedUtc = now };
            var sessions = activeManager.GetSessions().ToList();
            var identitySources = sessions.Select(item => item.SourceAppUserModelId ?? "")
                .Where(source => !string.IsNullOrWhiteSpace(source)).ToHashSet(StringComparer.OrdinalIgnoreCase);
            if (!_processIdentitySources.SetEquals(identitySources) ||
                now - _processIdentityUpdatedUtc >= TimeSpan.FromSeconds(5))
            {
                _processNamesBySource = MediaSessionProcessMapper.Resolve(identitySources);
                _processIdentitySources = identitySources;
                _processIdentityUpdatedUtc = now;
            }
            foreach (var session in sessions)
            {
                try
                {
                    var properties = await session.TryGetMediaPropertiesAsync();
                    var playback = session.GetPlaybackInfo();
                    var sourceId = session.SourceAppUserModelId ?? "";
                    var title = properties?.Title ?? "";
                    var artist = properties?.Artist ?? "";
                    var media = new MediaSessionState
                    {
                        SourceId = sourceId,
                        Title = title,
                        Artist = artist,
                        TrackId = $"{sourceId}|{title}|{artist}|{properties?.AlbumTitle ?? ""}",
                        IsPlaying = playback?.PlaybackStatus == GlobalSystemMediaTransportControlsSessionPlaybackStatus.Playing,
                        IsCurrent = string.Equals(sourceId, currentSourceId, StringComparison.OrdinalIgnoreCase),
                        ProcessNames = _processNamesBySource.GetValueOrDefault(sourceId) ?? []
                    };
                    List<string> colors = [];
                    if (properties?.Thumbnail is not null)
                    {
                        try
                        {
                            using var randomAccess = await properties.Thumbnail.OpenReadAsync();
                            using var stream = randomAccess.AsStreamForRead();
                            using var bytes = new MemoryStream();
                            await stream.CopyToAsync(bytes);
                            var imageBytes = bytes.ToArray();
                            media.TrackId += "|" + Convert.ToHexString(SHA256.HashData(imageBytes));
                            if (_colorCache.TryGetValue(media.TrackId, out var cached))
                            {
                                colors = [.. cached];
                            }
                            else
                            {
                                bytes.Position = 0;
                                using var bitmap = new Bitmap(bytes);
                                colors = AlbumColorExtractor.Extract(bitmap);
                            }
                            if (colors.Count > 0)
                            {
                                _colorCache[media.TrackId] = [.. colors];
                                _lastColorsBySource[sourceId] = [.. colors];
                                while (_colorCache.Count > 128) _colorCache.Remove(_colorCache.Keys.First());
                            }
                        }
                        catch { }
                    }
                    if (colors.Count == 0 && _lastColorsBySource.TryGetValue(sourceId, out var previous))
                        colors = [.. previous];
                    media.DominantColor = colors.FirstOrDefault() ?? "";
                    media.Palette = colors;
                    state.Sessions.Add(media);
                    _lastSessions[sourceId] = (media, now);
                }
                catch
                {
                }
            }
            foreach (var pair in _lastSessions.ToList())
            {
                var sourceProcessRunning = MediaSessionProcessMapper.IsSourceProcessRunning(pair.Value.State);
                if (now - pair.Value.Seen > TimeSpan.FromSeconds(5) && !sourceProcessRunning)
                {
                    _lastSessions.Remove(pair.Key);
                    continue;
                }
                if (state.Sessions.Any(item => string.Equals(item.SourceId, pair.Key, StringComparison.OrdinalIgnoreCase))) continue;
                var previous = pair.Value.State;
                state.Sessions.Add(new MediaSessionState
                {
                    SourceId = previous.SourceId,
                    Title = previous.Title,
                    Artist = previous.Artist,
                    TrackId = previous.TrackId,
                    IsPlaying = false,
                    IsCurrent = false,
                    ProcessNames = [.. previous.ProcessNames],
                    DominantColor = previous.DominantColor,
                    Palette = [.. previous.Palette]
                });
            }
            state.Save();
        }
        catch (Exception ex)
        {
            Interlocked.Exchange(ref _rebuildManager, 1);
            new MediaPlaybackState
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

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        _timer.Dispose();
        if (_manager is not null)
        {
            _manager.SessionsChanged -= OnSessionsChanged;
            _manager.CurrentSessionChanged -= OnCurrentSessionChanged;
        }
    }

    private void ReplaceManager(GlobalSystemMediaTransportControlsSessionManager manager)
    {
        if (ReferenceEquals(_manager, manager)) return;
        if (_manager is not null)
        {
            _manager.SessionsChanged -= OnSessionsChanged;
            _manager.CurrentSessionChanged -= OnCurrentSessionChanged;
        }
        _manager = manager;
        _manager.SessionsChanged += OnSessionsChanged;
        _manager.CurrentSessionChanged += OnCurrentSessionChanged;
    }

    private void OnSessionsChanged(GlobalSystemMediaTransportControlsSessionManager sender, SessionsChangedEventArgs args) =>
        ForceRefresh();

    private void OnCurrentSessionChanged(GlobalSystemMediaTransportControlsSessionManager sender, CurrentSessionChangedEventArgs args) =>
        ForceRefresh();
}

internal static class MediaSessionProcessMapper
{
    private const uint ProcessQueryLimitedInformation = 0x1000;
    private const int ErrorInsufficientBuffer = 122;

    public static Dictionary<string, List<string>> Resolve(IEnumerable<string> sourceIds)
    {
        var sources = sourceIds.Where(source => !string.IsNullOrWhiteSpace(source))
            .Distinct(StringComparer.OrdinalIgnoreCase).ToHashSet(StringComparer.OrdinalIgnoreCase);
        var result = sources.ToDictionary(source => source, _ => new List<string>(), StringComparer.OrdinalIgnoreCase);
        if (sources.Count == 0) return result;

        foreach (var process in Process.GetProcesses())
        using (process)
        {
            try
            {
                var processName = AppProfileRule.NormalizeProcessName(process.ProcessName);
                var appUserModelId = GetAppUserModelId(process.Id);
                foreach (var source in sources)
                {
                    if ((!string.IsNullOrWhiteSpace(appUserModelId) &&
                         string.Equals(source, appUserModelId, StringComparison.OrdinalIgnoreCase)) ||
                        MediaPlaybackState.SourceMatchesProcess(source, processName))
                    {
                        if (!result[source].Contains(processName, StringComparer.OrdinalIgnoreCase))
                            result[source].Add(processName);
                    }
                }
            }
            catch
            {
            }
        }
        return result;
    }

    internal static bool IsSourceProcessRunning(MediaSessionState session)
    {
        var names = (session.ProcessNames ?? []).Select(AppProfileRule.NormalizeProcessName)
            .Where(name => !string.IsNullOrWhiteSpace(name)).ToHashSet(StringComparer.OrdinalIgnoreCase);
        names.Add(AppProfileRule.NormalizeProcessName(session.SourceId));
        foreach (var name in names)
        {
            Process[] processes;
            try { processes = Process.GetProcessesByName(name); }
            catch { continue; }
            try
            {
                if (processes.Length > 0) return true;
            }
            finally
            {
                foreach (var process in processes) process.Dispose();
            }
        }
        return false;
    }

    internal static string GetAppUserModelId(int processId)
    {
        var handle = OpenProcess(ProcessQueryLimitedInformation, false, processId);
        if (handle == IntPtr.Zero) return "";
        try
        {
            uint length = 0;
            if (GetApplicationUserModelId(handle, ref length, null) != ErrorInsufficientBuffer || length == 0) return "";
            var value = new StringBuilder((int)length);
            return GetApplicationUserModelId(handle, ref length, value) == 0 ? value.ToString() : "";
        }
        finally
        {
            CloseHandle(handle);
        }
    }

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern IntPtr OpenProcess(uint desiredAccess, bool inheritHandle, int processId);

    [DllImport("kernel32.dll")]
    private static extern int GetApplicationUserModelId(IntPtr process, ref uint applicationUserModelIdLength, StringBuilder? applicationUserModelId);

    [DllImport("kernel32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool CloseHandle(IntPtr handle);
}

internal static class AlbumColorExtractor
{
    public static List<string> Extract(Bitmap source)
    {
        using var small = new Bitmap(source, new Size(48, 48));
        var buckets = new Dictionary<int, (long R, long G, long B, int Count)>();
        for (var y = 0; y < small.Height; y += 2)
        for (var x = 0; x < small.Width; x += 2)
        {
            var color = small.GetPixel(x, y);
            if (color.A < 128) continue;
            var max = Math.Max(color.R, Math.Max(color.G, color.B));
            var min = Math.Min(color.R, Math.Min(color.G, color.B));
            if (max < 24 || min > 235 || max - min < 12) continue;
            var key = (color.R / 32 << 6) | (color.G / 32 << 3) | color.B / 32;
            var bucket = buckets.GetValueOrDefault(key);
            buckets[key] = (bucket.R + color.R, bucket.G + color.G, bucket.B + color.B, bucket.Count + 1);
        }

        return buckets.Values.OrderByDescending(item => item.Count).Take(5).Select(item =>
        {
            var r = (int)(item.R / item.Count);
            var g = (int)(item.G / item.Count);
            var b = (int)(item.B / item.Count);
            var max = Math.Max(r, Math.Max(g, b));
            var min = Math.Min(r, Math.Min(g, b));
            var saturation = max == 0 ? 0 : (max - min) / (double)max;
            if (saturation is > 0 and < 0.38)
            {
                var gray = (r + g + b) / 3d;
                var factor = Math.Min(2.5, 0.38 / saturation);
                r = Math.Clamp((int)Math.Round(gray + (r - gray) * factor), 0, 255);
                g = Math.Clamp((int)Math.Round(gray + (g - gray) * factor), 0, 255);
                b = Math.Clamp((int)Math.Round(gray + (b - gray) * factor), 0, 255);
            }
            max = Math.Max(r, Math.Max(g, b));
            if (max < 180)
            {
                var scale = 180d / Math.Max(1, max);
                r = Math.Min(255, (int)Math.Round(r * scale));
                g = Math.Min(255, (int)Math.Round(g * scale));
                b = Math.Min(255, (int)Math.Round(b * scale));
            }
            return new RgbColor((byte)r, (byte)g, (byte)b).Hex;
        }).Distinct(StringComparer.OrdinalIgnoreCase).ToList();
    }
}
