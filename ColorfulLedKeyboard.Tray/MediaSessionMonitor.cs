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
    private const int MaxArtworkBytes = 16 * 1024 * 1024;
    private readonly System.Threading.Timer _timer;
    private GlobalSystemMediaTransportControlsSessionManager? _manager;
    private readonly Dictionary<string, List<string>> _colorCache = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, List<string>> _lastColorsBySource = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, (MediaSessionState State, DateTimeOffset Seen)> _lastSessions = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, GlobalSystemMediaTransportControlsSession> _subscribedSessions =
        new(StringComparer.OrdinalIgnoreCase);
    private HashSet<string> _activeSessionSources = new(StringComparer.OrdinalIgnoreCase);
    private readonly object _subscriptionGate = new();
    private readonly MediaMetadataRefreshTracker _metadataRefresh = new();
    private Dictionary<string, List<string>> _processNamesBySource = new(StringComparer.OrdinalIgnoreCase);
    private HashSet<string> _processIdentitySources = new(StringComparer.OrdinalIgnoreCase);
    private DateTimeOffset _processIdentityUpdatedUtc = DateTimeOffset.MinValue;
    private int _rebuildManager = 1;
    private int _refreshing;
    private volatile bool _disposed;

    public MediaSessionMonitor()
    {
        _timer = new System.Threading.Timer(async _ => await RefreshAsync(), null, TimeSpan.Zero, TimeSpan.FromMilliseconds(500));
    }

    public void ForceRefresh()
    {
        if (_disposed) return;
        _metadataRefresh.MarkAllDirty();
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
            ReconcileSessionSubscriptions(sessions);
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
                    var playback = session.GetPlaybackInfo();
                    var sourceId = session.SourceAppUserModelId ?? "";
                    var isPlaying = playback?.PlaybackStatus == GlobalSystemMediaTransportControlsSessionPlaybackStatus.Playing;
                    _lastSessions.TryGetValue(sourceId, out var previous);
                    var plan = _metadataRefresh.Plan(sourceId, previous.State is not null, isPlaying, now);
                    MediaSessionState media;
                    if (plan.ReadMetadata)
                    {
                        media = await ReadMetadataAsync(session, sourceId, previous.State, plan, isPlaying, now);
                    }
                    else
                    {
                        media = previous.State is null
                            ? new MediaSessionState { SourceId = sourceId, TrackId = sourceId }
                            : CloneSession(previous.State);
                    }
                    media.IsPlaying = isPlaying;
                    media.IsCurrent = string.Equals(sourceId, currentSourceId, StringComparison.OrdinalIgnoreCase);
                    media.ProcessNames = _processNamesBySource.GetValueOrDefault(sourceId) ?? [];
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
                    RemoveSource(pair.Key);
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

    private async Task<MediaSessionState> ReadMetadataAsync(
        GlobalSystemMediaTransportControlsSession session,
        string sourceId,
        MediaSessionState? previous,
        MediaMetadataRefreshPlan plan,
        bool isPlaying,
        DateTimeOffset now)
    {
        try
        {
            var properties = await session.TryGetMediaPropertiesAsync();
            var title = properties?.Title ?? "";
            var artist = properties?.Artist ?? "";
            var metadataId = $"{sourceId}|{title}|{artist}|{properties?.AlbumTitle ?? ""}";
            var readArtwork = _metadataRefresh.ShouldReadArtwork(sourceId, metadataId, plan, now);
            var media = new MediaSessionState
            {
                SourceId = sourceId,
                Title = title,
                Artist = artist,
                TrackId = metadataId
            };
            var colors = PreviousColors(sourceId, previous);
            var artworkResolved = false;
            if (readArtwork && properties?.Thumbnail is not null)
            {
                try
                {
                    using var randomAccess = await properties.Thumbnail.OpenReadAsync();
                    if (randomAccess.Size is > 0 and <= MaxArtworkBytes)
                    {
                        using var stream = randomAccess.AsStreamForRead();
                        using var bytes = await ReadArtworkAsync(stream);
                        if (bytes is not null)
                        {
                            bytes.Position = 0;
                            media.TrackId += "|" + Convert.ToHexString(SHA256.HashData(bytes));
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
                            artworkResolved = true;
                            if (colors.Count > 0)
                            {
                                _colorCache[media.TrackId] = [.. colors];
                                _lastColorsBySource[sourceId] = [.. colors];
                                while (_colorCache.Count > 128) _colorCache.Remove(_colorCache.Keys.First());
                            }
                        }
                    }
                }
                catch
                {
                }
            }
            else if (!readArtwork && previous is not null)
            {
                media.TrackId = previous.TrackId;
            }
            media.DominantColor = colors.FirstOrDefault() ?? "";
            media.Palette = colors;
            _metadataRefresh.RecordMetadataResult(
                sourceId,
                metadataId,
                isPlaying,
                now,
                readArtwork,
                artworkResolved,
                plan);
            return media;
        }
        catch
        {
            _metadataRefresh.RecordMetadataFailure(sourceId, now, plan.ForceArtwork, plan);
            if (previous is not null) return CloneSession(previous);
            return new MediaSessionState { SourceId = sourceId, TrackId = sourceId };
        }
    }

    private static async Task<MemoryStream?> ReadArtworkAsync(Stream source)
    {
        var destination = new MemoryStream();
        var buffer = new byte[81920];
        while (true)
        {
            var read = await source.ReadAsync(buffer);
            if (read == 0) break;
            if (destination.Length + read > MaxArtworkBytes)
            {
                destination.Dispose();
                return null;
            }
            await destination.WriteAsync(buffer.AsMemory(0, read));
        }
        destination.Position = 0;
        return destination;
    }

    private List<string> PreviousColors(string sourceId, MediaSessionState? previous)
    {
        if (previous?.Palette.Count > 0) return [.. previous.Palette];
        return _lastColorsBySource.TryGetValue(sourceId, out var colors) ? [.. colors] : [];
    }

    private static MediaSessionState CloneSession(MediaSessionState previous) => new()
    {
        SourceId = previous.SourceId,
        Title = previous.Title,
        Artist = previous.Artist,
        TrackId = previous.TrackId,
        IsPlaying = previous.IsPlaying,
        IsCurrent = previous.IsCurrent,
        ProcessNames = [.. previous.ProcessNames],
        DominantColor = previous.DominantColor,
        Palette = [.. previous.Palette]
    };

    private void ReconcileSessionSubscriptions(IReadOnlyCollection<GlobalSystemMediaTransportControlsSession> sessions)
    {
        lock (_subscriptionGate)
        {
            if (_disposed) return;
            var currentSources = sessions
                .Select(item => item.SourceAppUserModelId ?? "")
                .Where(sourceId => !string.IsNullOrWhiteSpace(sourceId))
                .ToHashSet(StringComparer.OrdinalIgnoreCase);
            foreach (var sourceId in currentSources.Where(sourceId => !_activeSessionSources.Contains(sourceId)))
                _metadataRefresh.MarkDirty(sourceId, DateTimeOffset.UtcNow);
            _activeSessionSources = currentSources;

            var currentSourcesWithSessions = sessions
                .Select(session => (Session: session, SourceId: session.SourceAppUserModelId ?? ""))
                .Where(item => !string.IsNullOrWhiteSpace(item.SourceId))
                .ToList();
            var currentSessionSources = currentSourcesWithSessions
                .Select(item => item.SourceId)
                .ToHashSet(StringComparer.OrdinalIgnoreCase);
            foreach (var pair in _subscribedSessions.Where(item => !currentSessionSources.Contains(item.Key)).ToList())
            {
                Unsubscribe(pair.Value);
                _subscribedSessions.Remove(pair.Key);
            }
            foreach (var item in currentSourcesWithSessions.Where(item => !_subscribedSessions.ContainsKey(item.SourceId)))
            {
                var propertiesSubscribed = false;
                var playbackSubscribed = false;
                try
                {
                    item.Session.MediaPropertiesChanged += OnMediaPropertiesChanged;
                    propertiesSubscribed = true;
                    item.Session.PlaybackInfoChanged += OnPlaybackInfoChanged;
                    playbackSubscribed = true;
                    _subscribedSessions[item.SourceId] = item.Session;
                }
                catch
                {
                    // A session can disappear while its snapshot is being reconciled.
                    if (propertiesSubscribed)
                    {
                        try { item.Session.MediaPropertiesChanged -= OnMediaPropertiesChanged; } catch { }
                    }
                    if (playbackSubscribed)
                    {
                        try { item.Session.PlaybackInfoChanged -= OnPlaybackInfoChanged; } catch { }
                    }
                }
            }
        }
    }

    private void ClearSessionSubscriptions()
    {
        lock (_subscriptionGate)
        {
            foreach (var session in _subscribedSessions.Values) Unsubscribe(session);
            _subscribedSessions.Clear();
            _activeSessionSources.Clear();
        }
    }

    private void Unsubscribe(GlobalSystemMediaTransportControlsSession session)
    {
        try { session.MediaPropertiesChanged -= OnMediaPropertiesChanged; } catch { }
        try { session.PlaybackInfoChanged -= OnPlaybackInfoChanged; } catch { }
    }

    private void RemoveSource(string sourceId)
    {
        _lastSessions.Remove(sourceId);
        _lastColorsBySource.Remove(sourceId);
        _metadataRefresh.Remove(sourceId);
    }

    public void Dispose()
    {
        _timer.Dispose();
        lock (_subscriptionGate)
        {
            if (_disposed) return;
            _disposed = true;
            ClearSessionSubscriptions();
            if (_manager is not null)
            {
                try { _manager.SessionsChanged -= OnSessionsChanged; } catch { }
                try { _manager.CurrentSessionChanged -= OnCurrentSessionChanged; } catch { }
            }
        }
    }

    private void ReplaceManager(GlobalSystemMediaTransportControlsSessionManager manager)
    {
        lock (_subscriptionGate)
        {
            if (_disposed || ReferenceEquals(_manager, manager)) return;
            if (_manager is not null)
            {
                try { _manager.SessionsChanged -= OnSessionsChanged; } catch { }
                try { _manager.CurrentSessionChanged -= OnCurrentSessionChanged; } catch { }
            }
            ClearSessionSubscriptions();
            _manager = manager;
            var sessionsSubscribed = false;
            var currentSessionSubscribed = false;
            try
            {
                _manager.SessionsChanged += OnSessionsChanged;
                sessionsSubscribed = true;
                _manager.CurrentSessionChanged += OnCurrentSessionChanged;
                currentSessionSubscribed = true;
            }
            catch
            {
                if (sessionsSubscribed)
                {
                    try { _manager.SessionsChanged -= OnSessionsChanged; } catch { }
                }
                if (currentSessionSubscribed)
                {
                    try { _manager.CurrentSessionChanged -= OnCurrentSessionChanged; } catch { }
                }
                throw;
            }
        }
    }

    private void OnSessionsChanged(GlobalSystemMediaTransportControlsSessionManager sender, SessionsChangedEventArgs args) =>
        ForceRefresh();

    private void OnCurrentSessionChanged(GlobalSystemMediaTransportControlsSessionManager sender, CurrentSessionChangedEventArgs args) =>
        ForceRefresh();

    private void OnMediaPropertiesChanged(GlobalSystemMediaTransportControlsSession sender, MediaPropertiesChangedEventArgs args)
    {
        if (_disposed) return;
        try
        {
            _metadataRefresh.MarkDirty(sender.SourceAppUserModelId ?? "", DateTimeOffset.UtcNow);
        }
        catch
        {
            // The session may have been torn down before the event was delivered.
        }
        _ = RefreshAsync();
    }

    private void OnPlaybackInfoChanged(GlobalSystemMediaTransportControlsSession sender, PlaybackInfoChangedEventArgs args)
    {
        if (!_disposed) _ = RefreshAsync();
    }
}

internal readonly record struct MediaMetadataRefreshPlan(bool ReadMetadata, bool ForceArtwork, long DirtyVersion)
{
    public MediaMetadataRefreshPlan(bool readMetadata, bool forceArtwork)
        : this(readMetadata, forceArtwork, 0)
    {
    }
}

internal sealed class MediaMetadataRefreshTracker
{
    internal static readonly TimeSpan PlayingMetadataInterval = TimeSpan.FromSeconds(15);
    internal static readonly TimeSpan PausedMetadataInterval = TimeSpan.FromMinutes(2);
    internal static readonly TimeSpan MissingArtworkRetryInterval = TimeSpan.FromSeconds(30);
    internal static readonly TimeSpan EventArtworkThrottleInterval = TimeSpan.FromSeconds(5);
    internal static readonly TimeSpan ArtworkValidationInterval = TimeSpan.FromMinutes(30);
    private readonly object _gate = new();
    private readonly Dictionary<string, Entry> _entries = new(StringComparer.OrdinalIgnoreCase);

    public MediaMetadataRefreshPlan Plan(string sourceId, bool hasPrevious, bool isPlaying, DateTimeOffset now)
    {
        lock (_gate)
        {
            var entry = GetOrCreate(sourceId);
            var firstAttempt = !hasPrevious && !entry.HasMetadataAttempt;
            var playbackChanged = entry.HasPlaybackState && entry.IsPlaying != isPlaying;
            var playbackStarted = playbackChanged && isPlaying;
            entry.HasPlaybackState = true;
            entry.IsPlaying = isPlaying;
            var artworkDue = now >= entry.NextArtworkRefresh;
            var readMetadata = firstAttempt || entry.Dirty || playbackChanged ||
                now >= entry.NextMetadataRefresh || artworkDue;
            return new MediaMetadataRefreshPlan(
                readMetadata,
                firstAttempt || playbackStarted || artworkDue ||
                (entry.Dirty && entry.EventNeedsArtwork),
                entry.DirtyVersion);
        }
    }

    public bool ShouldReadArtwork(
        string sourceId,
        string metadataId,
        MediaMetadataRefreshPlan plan,
        DateTimeOffset now)
    {
        lock (_gate)
        {
            var entry = GetOrCreate(sourceId);
            return plan.ForceArtwork ||
                entry.DirtyVersion != plan.DirtyVersion ||
                !string.Equals(entry.MetadataId, metadataId, StringComparison.Ordinal) ||
                now >= entry.NextArtworkRefresh;
        }
    }

    public void RecordMetadataResult(
        string sourceId,
        string metadataId,
        bool isPlaying,
        DateTimeOffset now,
        bool artworkAttempted,
        bool artworkResolved,
        MediaMetadataRefreshPlan? plan = null)
    {
        lock (_gate)
        {
            var entry = GetOrCreate(sourceId);
            entry.HasMetadataAttempt = true;
            entry.MetadataId = metadataId;
            var hasNewerEvent = plan is not null && entry.DirtyVersion != plan.Value.DirtyVersion;
            if (!hasNewerEvent)
            {
                entry.Dirty = false;
                entry.EventNeedsArtwork = false;
            }
            entry.NextMetadataRefresh = now + (isPlaying ? PlayingMetadataInterval : PausedMetadataInterval);
            if (!artworkAttempted) return;
            entry.NextArtworkRefresh = now +
                (artworkResolved ? ArtworkValidationInterval : MissingArtworkRetryInterval);
        }
    }

    public void RecordMetadataFailure(
        string sourceId,
        DateTimeOffset now,
        bool artworkWasDue,
        MediaMetadataRefreshPlan? plan = null)
    {
        lock (_gate)
        {
            var entry = GetOrCreate(sourceId);
            entry.HasMetadataAttempt = true;
            var hasNewerEvent = plan is not null && entry.DirtyVersion != plan.Value.DirtyVersion;
            if (!hasNewerEvent)
                entry.Dirty = false;
            else
                entry.EventNeedsArtwork = false;
            entry.NextMetadataRefresh = now + MissingArtworkRetryInterval;
            if (artworkWasDue)
            {
                entry.NextArtworkRefresh = now + MissingArtworkRetryInterval;
            }
        }
    }

    public void MarkDirty(string sourceId) => MarkDirty(sourceId, DateTimeOffset.MinValue);

    public void MarkDirty(string sourceId, DateTimeOffset markedAt)
    {
        if (string.IsNullOrWhiteSpace(sourceId)) return;
        lock (_gate)
        {
            var entry = GetOrCreate(sourceId);
            entry.Dirty = true;
            entry.DirtyVersion++;
            if (entry.LastEventAt == DateTimeOffset.MinValue ||
                markedAt == DateTimeOffset.MinValue ||
                markedAt >= entry.LastEventAt + EventArtworkThrottleInterval)
            {
                entry.EventNeedsArtwork = true;
            }
            entry.LastEventAt = markedAt;
        }
    }

    public void MarkAllDirty()
    {
        lock (_gate)
        {
            foreach (var entry in _entries.Values)
            {
                entry.Dirty = true;
                entry.DirtyVersion++;
                entry.LastEventAt = DateTimeOffset.MinValue;
                entry.EventNeedsArtwork = true;
            }
        }
    }

    public void Remove(string sourceId)
    {
        lock (_gate) _entries.Remove(sourceId);
    }

    private Entry GetOrCreate(string sourceId)
    {
        if (_entries.TryGetValue(sourceId, out var entry)) return entry;
        entry = new Entry();
        _entries[sourceId] = entry;
        return entry;
    }

    private sealed class Entry
    {
        public bool Dirty { get; set; } = true;
        public bool HasMetadataAttempt { get; set; }
        public bool HasPlaybackState { get; set; }
        public bool IsPlaying { get; set; }
        public string MetadataId { get; set; } = "";
        public DateTimeOffset NextMetadataRefresh { get; set; } = DateTimeOffset.MinValue;
        public DateTimeOffset NextArtworkRefresh { get; set; } = DateTimeOffset.MinValue;
        public DateTimeOffset LastEventAt { get; set; } = DateTimeOffset.MinValue;
        public bool EventNeedsArtwork { get; set; }
        public long DirtyVersion { get; set; }
    }
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
