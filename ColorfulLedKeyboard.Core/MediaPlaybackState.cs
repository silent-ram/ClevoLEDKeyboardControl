using System.Text.Json;

namespace ColorfulLedKeyboard.Core;

public sealed class MediaPlaybackState
{
    public DateTimeOffset UpdatedUtc { get; set; } = DateTimeOffset.UtcNow;
    public List<MediaSessionState> Sessions { get; set; } = [];

    public MediaSessionState? Find(MusicApplicationRule rule)
    {
        if (!string.IsNullOrWhiteSpace(rule.MediaSessionId))
            return BestSession(item => string.Equals(item.SourceId, rule.MediaSessionId, StringComparison.OrdinalIgnoreCase));
        return BestSession(item => item.SourceId.Contains(rule.ProcessName, StringComparison.OrdinalIgnoreCase));
    }

    public MediaSessionState? Find(MusicPlayerBinding binding)
    {
        if (!string.IsNullOrWhiteSpace(binding.MediaSessionId))
            return BestSession(item => string.Equals(item.SourceId, binding.MediaSessionId, StringComparison.OrdinalIgnoreCase));
        return BestSession(item => item.SourceId.Contains(binding.ProcessName, StringComparison.OrdinalIgnoreCase));
    }

    private MediaSessionState? BestSession(Func<MediaSessionState, bool> matches) =>
        Sessions.Where(matches)
            .OrderByDescending(item => item.IsPlaying)
            .ThenByDescending(item => item.Palette.Count > 0 || !string.IsNullOrWhiteSpace(item.DominantColor))
            .FirstOrDefault();

    public static MediaPlaybackState? Load()
    {
        try
        {
            return File.Exists(AppPaths.MediaPlaybackStatePath)
                ? JsonSerializer.Deserialize<MediaPlaybackState>(File.ReadAllText(AppPaths.MediaPlaybackStatePath))
                : null;
        }
        catch { return null; }
    }

    public void Save()
    {
        try
        {
            Directory.CreateDirectory(AppPaths.ProgramDataDirectory);
            var temp = $"{AppPaths.MediaPlaybackStatePath}.{Environment.ProcessId}.tmp";
            File.WriteAllText(temp, JsonSerializer.Serialize(this));
            if (File.Exists(AppPaths.MediaPlaybackStatePath)) File.Replace(temp, AppPaths.MediaPlaybackStatePath, null);
            else File.Move(temp, AppPaths.MediaPlaybackStatePath);
        }
        catch (IOException) { }
        catch (UnauthorizedAccessException) { }
    }
}

public sealed class MediaSessionState
{
    public string SourceId { get; set; } = "";
    public string Title { get; set; } = "";
    public string Artist { get; set; } = "";
    public string TrackId { get; set; } = "";
    public bool IsPlaying { get; set; }
    public string DominantColor { get; set; } = "";
    public List<string> Palette { get; set; } = [];
}
