using System.Text.Json;

namespace ColorfulLedKeyboard.Core;

public sealed class AutomationStatus
{
    public DateTimeOffset UpdatedUtc { get; set; }
    public string ForegroundProcessName { get; set; } = "";
    public bool ForegroundAvailable { get; set; }
    public string ActiveRuleId { get; set; } = "";
    public string ActiveRuleName { get; set; } = "";
    public string TargetDescription { get; set; } = "基础设置";
    public bool IdleOverrideActive { get; set; }
    public int FinalBrightnessLimit { get; set; } = 100;
    public string BrightnessDisplay { get; set; } = "";
    public string BrightnessDescription { get; set; } = "";
    public string InvalidReason { get; set; } = "";
    public string ActiveMusicApplication { get; set; } = "";
    public List<int> ActiveProcessIds { get; set; } = [];
    public string AudioCaptureMode { get; set; } = "系统混音";
    public string TrackTitle { get; set; } = "";
    public string TrackArtist { get; set; } = "";
    public string AlbumColor { get; set; } = "";
    public List<AudioApplicationStatus> AudioApplications { get; set; } = [];

    public static AutomationStatus? Load()
    {
        if (Environment.UserInteractive &&
            ServiceIpc.TryRequest<object, AutomationStatus>("GetAutomationStatus", new { }, out var remote, 350))
            return remote;
        try
        {
            return File.Exists(AppPaths.AutomationStatusPath)
                ? JsonSerializer.Deserialize<AutomationStatus>(File.ReadAllText(AppPaths.AutomationStatusPath))
                : null;
        }
        catch (IOException) { return null; }
        catch (UnauthorizedAccessException) { return null; }
        catch (JsonException) { return null; }
    }

    public void Save()
    {
        try
        {
            Directory.CreateDirectory(AppPaths.ProgramDataDirectory);
            var temp = $"{AppPaths.AutomationStatusPath}.{Environment.ProcessId}.tmp";
            File.WriteAllText(temp, JsonSerializer.Serialize(this));
            File.Move(temp, AppPaths.AutomationStatusPath, overwrite: true);
        }
        catch (IOException) { }
        catch (UnauthorizedAccessException) { }
    }
}

public sealed class AudioApplicationStatus
{
    public string ProcessName { get; set; } = "";
    public string ExecutablePath { get; set; } = "";
    public List<int> ProcessIds { get; set; } = [];
    public float PeakLevel { get; set; }
    public bool IsPlaying { get; set; }
    public bool IsForeground { get; set; }
}

public sealed class AudioApplicationsState
{
    public DateTimeOffset UpdatedUtc { get; set; } = DateTimeOffset.UtcNow;
    public List<AudioApplicationStatus> Applications { get; set; } = [];
    public string LastError { get; set; } = "";
    public DateTimeOffset? LastErrorUtc { get; set; }

    public static AudioApplicationsState? Load()
    {
        if (Environment.UserInteractive &&
            ServiceIpc.TryRequest<object, AudioApplicationsState>("GetAudioApplications", new { }, out var remote, 350))
            return remote;
        try
        {
            return File.Exists(AppPaths.AudioApplicationsStatePath)
                ? JsonSerializer.Deserialize<AudioApplicationsState>(File.ReadAllText(AppPaths.AudioApplicationsStatePath))
                : null;
        }
        catch { return null; }
    }

    public void Save()
    {
        if (Environment.UserInteractive) { ServiceIpc.TrySend("AudioApplications", this); return; }
        try
        {
            Directory.CreateDirectory(AppPaths.ProgramDataDirectory);
            var temp = $"{AppPaths.AudioApplicationsStatePath}.{Environment.ProcessId}.tmp";
            File.WriteAllText(temp, JsonSerializer.Serialize(this));
            File.Move(temp, AppPaths.AudioApplicationsStatePath, overwrite: true);
        }
        catch (IOException) { }
        catch (UnauthorizedAccessException) { }
    }
}
