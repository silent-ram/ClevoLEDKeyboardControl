using ColorfulLedKeyboard.Core;
using System.Drawing;
using System.Security.Cryptography;
using Windows.Media.Control;

namespace ColorfulLedKeyboard.Tray;

internal sealed class MediaSessionMonitor : IDisposable
{
    private readonly System.Threading.Timer _timer;
    private GlobalSystemMediaTransportControlsSessionManager? _manager;
    private readonly Dictionary<string, List<string>> _colorCache = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, List<string>> _lastColorsBySource = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, (MediaSessionState State, DateTimeOffset Seen)> _lastSessions = new(StringComparer.OrdinalIgnoreCase);
    private int _refreshing;
    private bool _disposed;

    public MediaSessionMonitor()
    {
        _timer = new System.Threading.Timer(async _ => await RefreshAsync(), null, TimeSpan.Zero, TimeSpan.FromMilliseconds(500));
    }

    public async Task RefreshAsync()
    {
        if (_disposed || Interlocked.Exchange(ref _refreshing, 1) != 0) return;
        try
        {
            _manager ??= await GlobalSystemMediaTransportControlsSessionManager.RequestAsync();
            var now = DateTimeOffset.UtcNow;
            var state = new MediaPlaybackState { UpdatedUtc = now };
            foreach (var session in _manager.GetSessions())
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
                        IsPlaying = playback?.PlaybackStatus == GlobalSystemMediaTransportControlsSessionPlaybackStatus.Playing
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
                if (now - pair.Value.Seen > TimeSpan.FromSeconds(5))
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
                    DominantColor = previous.DominantColor,
                    Palette = [.. previous.Palette]
                });
            }
            state.Save();
        }
        catch (Exception ex)
        {
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
    }
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
