using System.Text.Json;

namespace ColorfulLedKeyboard.Core;

public sealed class MediaPlaybackState
{
    public DateTimeOffset UpdatedUtc { get; set; } = DateTimeOffset.UtcNow;
    public List<MediaSessionState> Sessions { get; set; } = [];
    public string LastError { get; set; } = "";
    public DateTimeOffset? LastErrorUtc { get; set; }

    public MediaSessionState? Find(MusicApplicationRule rule)
    {
        if (!string.IsNullOrWhiteSpace(rule.MediaSessionId))
            return BestSession(item => string.Equals(item.SourceId, rule.MediaSessionId, StringComparison.OrdinalIgnoreCase));
        return FindUnbound(rule.ProcessName);
    }

    public MediaSessionState? Find(MusicPlayerBinding binding)
    {
        if (!string.IsNullOrWhiteSpace(binding.MediaSessionId))
            return BestSession(item => string.Equals(item.SourceId, binding.MediaSessionId, StringComparison.OrdinalIgnoreCase));
        return FindUnbound(binding.ProcessName);
    }

    private MediaSessionState? FindUnbound(string processName)
    {
        if (string.IsNullOrWhiteSpace(processName)) return null;
        var normalized = AppProfileRule.NormalizeProcessName(processName);
        var matches = Sessions.Where(item =>
            item.SourceId.Contains(normalized, StringComparison.OrdinalIgnoreCase)).ToList();
        // 未绑定时只接受唯一候选；多个相似 SourceId 的置信度不足，避免套用错误封面。
        return matches.Count == 1 ? BestSession(item => ReferenceEquals(item, matches[0])) : null;
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
