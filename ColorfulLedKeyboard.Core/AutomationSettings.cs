namespace ColorfulLedKeyboard.Core;

public sealed class AutomationSettings
{
    public const int CurrentVersion = 2;
    public int Version { get; set; } = CurrentVersion;
    public bool Enabled { get; set; }
    public List<SceneRule> Rules { get; set; } = [];

    public List<MusicApplicationRule> MusicApplications { get; set; } = [];

    public List<LightingApplicationRule> LightingApplications { get; set; } = [];

    public List<AutomationScheduleRule> ScheduleRules { get; set; } = [];

    public AutomationSettings Normalize()
    {
        Version = CurrentVersion;
        Rules = (Rules ?? []).Select(rule => rule.Normalize()).ToList();
        MusicApplications = (MusicApplications ?? []).Select(rule => rule.Normalize()).ToList();
        LightingApplications = (LightingApplications ?? []).Select(rule => rule.Normalize()).ToList();
        ScheduleRules = (ScheduleRules ?? []).Select(rule => rule.Normalize()).ToList();
        return this;
    }
}

public enum EventPolicy { Inherit = 0, Enabled = 1, Disabled = 2 }

public enum MusicColorSource { Preset = 0, AlbumDominant = 1, AlbumPalette = 2 }

public sealed class AutomationTimeFilter
{
    public bool TimeEnabled { get; set; }
    public string Start { get; set; } = "00:00";
    public string End { get; set; } = "00:00";
    public List<DayOfWeek> Days { get; set; } = [];

    public AutomationTimeFilter Normalize()
    {
        Start = NormalizeTime(Start, "00:00");
        End = NormalizeTime(End, "00:00");
        Days = (Days ?? []).Where(Enum.IsDefined).Distinct().OrderBy(day => day).ToList();
        return this;
    }

    public bool Matches(DateTime localNow)
    {
        if (Days.Count > 0 && !Days.Contains(EffectiveDay(localNow))) return false;
        if (!TimeEnabled) return true;
        var start = TimeOnly.Parse(Start);
        var end = TimeOnly.Parse(End);
        var now = TimeOnly.FromDateTime(localNow);
        if (start == end) return true;
        return start < end ? now >= start && now < end : now >= start || now < end;
    }

    private DayOfWeek EffectiveDay(DateTime localNow)
    {
        if (!TimeEnabled) return localNow.DayOfWeek;
        var start = TimeOnly.Parse(Start);
        var end = TimeOnly.Parse(End);
        return start > end && TimeOnly.FromDateTime(localNow) < end
            ? localNow.AddDays(-1).DayOfWeek
            : localNow.DayOfWeek;
    }

    private static string NormalizeTime(string value, string fallback) =>
        TimeOnly.TryParse(value, out var time) ? time.ToString("HH:mm") : fallback;
}

public sealed class MusicApplicationRule
{
    public string Id { get; set; } = Guid.NewGuid().ToString("N");
    public string Name { get; set; } = "音乐程序";
    public bool Enabled { get; set; } = true;
    public string ProcessName { get; set; } = "";
    public string ExecutablePath { get; set; } = "";
    public string ApplicationId { get; set; } = "";
    public string MediaSessionId { get; set; } = "";
    public bool IncludeChildProcesses { get; set; } = true;
    public AutomationTimeFilter TimeFilter { get; set; } = new();
    public string MusicPresetId { get; set; } = MusicSettings.BuiltInDefaultPresetId;
    public MusicColorSource ColorSource { get; set; } = MusicColorSource.AlbumPalette;
    public int? BrightnessLimit { get; set; }
    public EventPolicy TypingPolicy { get; set; }
    public EventPolicy NotificationPolicy { get; set; }

    public MusicApplicationRule Normalize()
    {
        Id = NormalizeId(Id);
        Name = string.IsNullOrWhiteSpace(Name) ? "音乐程序" : Name.Trim();
        ProcessName = AppProfileRule.NormalizeProcessName(ProcessName);
        ExecutablePath = (ExecutablePath ?? "").Trim();
        ApplicationId = (ApplicationId ?? "").Trim();
        MediaSessionId = (MediaSessionId ?? "").Trim();
        MusicPresetId = (MusicPresetId ?? "").Trim();
        TimeFilter ??= new AutomationTimeFilter();
        TimeFilter.Normalize();
        if (!Enum.IsDefined(ColorSource)) ColorSource = MusicColorSource.AlbumPalette;
        if (!Enum.IsDefined(TypingPolicy)) TypingPolicy = EventPolicy.Inherit;
        if (!Enum.IsDefined(NotificationPolicy)) NotificationPolicy = EventPolicy.Inherit;
        BrightnessLimit = ClampNullable(BrightnessLimit);
        return this;
    }

    internal static string NormalizeId(string value) =>
        string.IsNullOrWhiteSpace(value) ? Guid.NewGuid().ToString("N") : value.Trim();
    internal static int? ClampNullable(int? value) => value.HasValue ? Math.Clamp(value.Value, 0, 100) : null;
}

public sealed class LightingApplicationRule
{
    public string Id { get; set; } = Guid.NewGuid().ToString("N");
    public string Name { get; set; } = "灯效程序";
    public bool Enabled { get; set; } = true;
    public List<string> ProcessNames { get; set; } = [];
    public AutomationTimeFilter TimeFilter { get; set; } = new();
    public SceneAction Action { get; set; } = new();
    public EventPolicy TypingPolicy { get; set; }
    public EventPolicy NotificationPolicy { get; set; }

    public LightingApplicationRule Normalize()
    {
        Id = MusicApplicationRule.NormalizeId(Id);
        Name = string.IsNullOrWhiteSpace(Name) ? "灯效程序" : Name.Trim();
        ProcessNames = (ProcessNames ?? []).Select(AppProfileRule.NormalizeProcessName)
            .Where(name => !string.IsNullOrWhiteSpace(name)).Distinct(StringComparer.OrdinalIgnoreCase).ToList();
        TimeFilter ??= new AutomationTimeFilter();
        TimeFilter.Normalize();
        Action ??= new SceneAction();
        Action.Normalize();
        if (!Enum.IsDefined(TypingPolicy)) TypingPolicy = EventPolicy.Inherit;
        if (!Enum.IsDefined(NotificationPolicy)) NotificationPolicy = EventPolicy.Inherit;
        return this;
    }

    public bool Matches(string processName, DateTime localNow) => Enabled && TimeFilter.Matches(localNow) &&
        ProcessNames.Contains(AppProfileRule.NormalizeProcessName(processName), StringComparer.OrdinalIgnoreCase);
}

public sealed class AutomationScheduleRule
{
    public string Id { get; set; } = Guid.NewGuid().ToString("N");
    public string Name { get; set; } = "时间计划";
    public bool Enabled { get; set; } = true;
    public AutomationTimeFilter TimeFilter { get; set; } = new() { TimeEnabled = true };
    public SceneAction Action { get; set; } = new();

    public AutomationScheduleRule Normalize()
    {
        Id = MusicApplicationRule.NormalizeId(Id);
        Name = string.IsNullOrWhiteSpace(Name) ? "时间计划" : Name.Trim();
        TimeFilter ??= new AutomationTimeFilter { TimeEnabled = true };
        TimeFilter.Normalize();
        Action ??= new SceneAction();
        Action.Normalize();
        return this;
    }
}

public sealed record AudioApplicationState(
    string ProcessName,
    string ExecutablePath,
    IReadOnlyList<int> ProcessIds,
    float PeakLevel,
    bool IsPlaying,
    bool IsForeground);

public sealed record AutomationSelection(
    MusicApplicationRule? Music,
    AudioApplicationState? Audio,
    LightingApplicationRule? Lighting,
    AutomationScheduleRule? Schedule,
    string? InvalidReason = null);

public static class AutomationResolver
{
    public static AutomationSelection Resolve(
        AutomationSettings settings,
        DateTime localNow,
        string foregroundProcessName,
        IReadOnlyList<AudioApplicationState> audioStates,
        Func<MusicApplicationRule, string?>? validateMusic = null,
        Func<SceneAction, string?>? validateAction = null)
    {
        if (!settings.Enabled) return new AutomationSelection(null, null, null, null);
        string? firstInvalidReason = null;
        var candidates = settings.MusicApplications
            .Where(rule => rule.Enabled && rule.TimeFilter.Matches(localNow))
            .Where(rule =>
            {
                var error = string.IsNullOrWhiteSpace(rule.ProcessName)
                    ? "未配置有效进程"
                    : validateMusic?.Invoke(rule);
                if (error is null) return true;
                firstInvalidReason ??= $"{rule.Name}：{error}";
                return false;
            })
            .Select(rule => (Rule: rule, Audio: audioStates.FirstOrDefault(state => state.IsPlaying &&
                string.Equals(state.ProcessName, rule.ProcessName, StringComparison.OrdinalIgnoreCase) &&
                PathMatches(rule.ExecutablePath, state.ExecutablePath))))
            .Where(item => item.Audio is not null)
            .ToList();
        var music = candidates.FirstOrDefault(item => item.Audio!.IsForeground);
        if (music.Rule is null) music = candidates.FirstOrDefault();

        var lighting = settings.LightingApplications.FirstOrDefault(rule =>
        {
            if (!rule.Enabled) return false;
            if (rule.ProcessNames.Count == 0)
            {
                firstInvalidReason ??= $"{rule.Name}：未配置有效进程";
                return false;
            }
            var error = validateAction?.Invoke(rule.Action);
            if (error is not null)
            {
                firstInvalidReason ??= $"{rule.Name}：{error}";
                return false;
            }
            return rule.Matches(foregroundProcessName, localNow);
        });
        var schedule = settings.ScheduleRules.FirstOrDefault(rule =>
        {
            if (!rule.Enabled) return false;
            var error = validateAction?.Invoke(rule.Action);
            if (error is not null)
            {
                firstInvalidReason ??= $"{rule.Name}：{error}";
                return false;
            }
            return rule.TimeFilter.Matches(localNow);
        });
        return new AutomationSelection(music.Rule, music.Audio, lighting, schedule, firstInvalidReason);
    }

    private static bool PathMatches(string expected, string actual)
    {
        if (string.IsNullOrWhiteSpace(expected)) return true;
        if (string.IsNullOrWhiteSpace(actual)) return false;
        try { return string.Equals(Path.GetFullPath(expected), Path.GetFullPath(actual), StringComparison.OrdinalIgnoreCase); }
        catch { return false; }
    }
}

public sealed class SceneRule
{
    public string Id { get; set; } = Guid.NewGuid().ToString("N");
    public string Name { get; set; } = "新场景";
    public bool Enabled { get; set; } = true;
    public SceneConditions Conditions { get; set; } = new();
    public SceneAction Action { get; set; } = new();

    public SceneRule Normalize()
    {
        Id = string.IsNullOrWhiteSpace(Id) ? Guid.NewGuid().ToString("N") : Id.Trim();
        Name = string.IsNullOrWhiteSpace(Name) ? "新场景" : Name.Trim();
        Conditions ??= new SceneConditions();
        Conditions.Normalize();
        Action ??= new SceneAction();
        Action.Normalize();
        return this;
    }
}

public sealed class SceneConditions
{
    public bool TimeEnabled { get; set; }
    public string Start { get; set; } = "00:00";
    public string End { get; set; } = "00:00";
    public List<DayOfWeek> Days { get; set; } = [];
    public bool ApplicationsEnabled { get; set; }
    public List<string> ProcessNames { get; set; } = [];

    public SceneConditions Normalize()
    {
        Start = NormalizeTime(Start, "00:00");
        End = NormalizeTime(End, "00:00");
        Days = (Days ?? []).Where(Enum.IsDefined).Distinct().OrderBy(day => day).ToList();
        ProcessNames = (ProcessNames ?? [])
            .Select(AppProfileRule.NormalizeProcessName)
            .Where(name => !string.IsNullOrWhiteSpace(name))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();
        return this;
    }

    public bool HasAnyCondition => TimeEnabled || Days.Count > 0 || ApplicationsEnabled;
    public bool IsValid => HasAnyCondition && (!ApplicationsEnabled || ProcessNames.Count > 0);

    public bool Matches(DateTime localNow, string? foregroundProcessName)
    {
        if (!IsValid || (Days.Count > 0 && !Days.Contains(EffectiveDay(localNow)))) return false;
        if (TimeEnabled && !IsWithinTimeRange(TimeOnly.FromDateTime(localNow))) return false;
        if (ApplicationsEnabled)
        {
            var normalized = AppProfileRule.NormalizeProcessName(foregroundProcessName ?? "");
            if (string.IsNullOrWhiteSpace(normalized) ||
                !ProcessNames.Contains(normalized, StringComparer.OrdinalIgnoreCase)) return false;
        }
        return true;
    }

    private DayOfWeek EffectiveDay(DateTime localNow)
    {
        if (!TimeEnabled) return localNow.DayOfWeek;
        var start = TimeOnly.Parse(Start);
        var end = TimeOnly.Parse(End);
        var now = TimeOnly.FromDateTime(localNow);
        return start > end && now < end
            ? localNow.AddDays(-1).DayOfWeek
            : localNow.DayOfWeek;
    }

    private bool IsWithinTimeRange(TimeOnly now)
    {
        var start = TimeOnly.Parse(Start);
        var end = TimeOnly.Parse(End);
        if (start == end) return true;
        return start < end ? now >= start && now < end : now >= start || now < end;
    }

    private static string NormalizeTime(string value, string fallback) =>
        TimeOnly.TryParse(value, out var time) ? time.ToString("HH:mm") : fallback;
}

public enum SceneTargetKind { LightingPreset = 0, MusicPreset = 1, Off = 2 }

public sealed class SceneAction
{
    public SceneTargetKind Target { get; set; } = SceneTargetKind.LightingPreset;
    public EffectType LightingEffectType { get; set; } = EffectType.Static;
    public string PresetId { get; set; } = EffectPresetSettings.BuiltInId(EffectType.Static);
    public int? BrightnessLimit { get; set; }

    public SceneAction Normalize()
    {
        if (!Enum.IsDefined(Target)) Target = SceneTargetKind.LightingPreset;
        if (!Enum.IsDefined(LightingEffectType) || LightingEffectType == EffectType.Off)
            LightingEffectType = EffectType.Static;
        PresetId = (PresetId ?? string.Empty).Trim();
        BrightnessLimit = BrightnessLimit.HasValue ? Math.Clamp(BrightnessLimit.Value, 0, 100) : null;
        return this;
    }
}

public sealed record SceneEvaluationContext(DateTime LocalNow, string? ForegroundProcessName, bool ForegroundAvailable);
public sealed record SceneEvaluationResult(SceneRule? Rule, string? InvalidReason);

public static class SceneEvaluator
{
    public static SceneEvaluationResult Evaluate(
        AutomationSettings automation,
        SceneEvaluationContext context,
        Func<SceneAction, string?> validateAction)
    {
        if (!automation.Enabled) return new SceneEvaluationResult(null, null);
        string? firstInvalidReason = null;
        foreach (var rule in automation.Rules)
        {
            if (!rule.Enabled) continue;
            if (!rule.Conditions.IsValid)
            {
                firstInvalidReason ??= $"{rule.Name}：未配置有效触发条件";
                continue;
            }
            if (rule.Conditions.ApplicationsEnabled && !context.ForegroundAvailable)
            {
                firstInvalidReason ??= $"{rule.Name}：应用检测不可用";
                continue;
            }
            var actionError = validateAction(rule.Action);
            if (actionError is not null)
            {
                firstInvalidReason ??= $"{rule.Name}：{actionError}";
                continue;
            }
            if (rule.Conditions.Matches(context.LocalNow, context.ForegroundProcessName))
                return new SceneEvaluationResult(rule, firstInvalidReason);
        }
        return new SceneEvaluationResult(null, firstInvalidReason);
    }
}
