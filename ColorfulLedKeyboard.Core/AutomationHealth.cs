namespace ColorfulLedKeyboard.Core;

public enum RuleHealthSeverity { Information, Warning, Error }
public sealed record RuleHealthIssue(string RuleId, string RuleName, RuleHealthSeverity Severity, string Reason);

public static class AutomationHealthAnalyzer
{
    public static IReadOnlyList<RuleHealthIssue> Analyze(KeyboardSettings settings)
    {
        var issues = new List<RuleHealthIssue>();
        var automation = settings.Automation;
        for (var index = 0; index < automation.MusicApplications.Count; index++)
        {
            var rule = automation.MusicApplications[index];
            if (string.IsNullOrWhiteSpace(rule.ProcessName)) Add(rule.Id, rule.Name, RuleHealthSeverity.Error, "未配置进程");
            if (!MusicSettings.BuiltInPresets.Concat(settings.Effect.Music.CustomPresets).Any(p => p.Id == rule.MusicPresetId))
                Add(rule.Id, rule.Name, RuleHealthSeverity.Error, "引用的音乐预设不存在");
            if (rule.ColorSource != MusicColorSource.Preset && string.IsNullOrWhiteSpace(rule.MediaSessionId))
                Add(rule.Id, rule.Name, RuleHealthSeverity.Warning, "封面颜色使用自动媒体匹配，建议重新绑定播放器");
            if (automation.MusicApplications.Take(index).Any(previous => SameMusicScope(previous, rule)))
                Add(rule.Id, rule.Name, RuleHealthSeverity.Warning, "可能被前面的音乐规则完全覆盖");
        }
        for (var index = 0; index < automation.LightingApplications.Count; index++)
        {
            var rule = automation.LightingApplications[index];
            if (rule.ProcessNames.Count == 0) Add(rule.Id, rule.Name, RuleHealthSeverity.Error, "未配置前台进程");
            var actionError = ValidateAction(settings, rule.Action);
            if (actionError is not null) Add(rule.Id, rule.Name, RuleHealthSeverity.Error, actionError);
            if (automation.LightingApplications.Take(index).Any(previous => SameLightingScope(previous, rule)))
                Add(rule.Id, rule.Name, RuleHealthSeverity.Warning, "可能被前面的灯效规则完全覆盖");
        }
        foreach (var rule in automation.ScheduleRules)
        {
            var error = ValidateAction(settings, rule.Action);
            if (error is not null) Add(rule.Id, rule.Name, RuleHealthSeverity.Error, error);
        }
        return issues;

        void Add(string id, string name, RuleHealthSeverity severity, string reason) =>
            issues.Add(new RuleHealthIssue(id, name, severity, reason));
    }

    private static string? ValidateAction(KeyboardSettings settings, SceneAction action) => action.Target switch
    {
        SceneTargetKind.Off => null,
        SceneTargetKind.LightingPreset when action.PresetId == EffectPresetSettings.BuiltInId(action.LightingEffectType) ||
            settings.EffectPresets.ForType(action.LightingEffectType).Any(p => p.Id == action.PresetId) => null,
        SceneTargetKind.MusicPreset when MusicSettings.BuiltInPresets.Concat(settings.Effect.Music.CustomPresets)
            .Any(p => p.Id == action.PresetId) => null,
        _ => "引用的预设不存在"
    };

    private static bool SameMusicScope(MusicApplicationRule left, MusicApplicationRule right) => left.Enabled &&
        string.Equals(left.ProcessName, right.ProcessName, StringComparison.OrdinalIgnoreCase) &&
        string.Equals(left.ExecutablePath, right.ExecutablePath, StringComparison.OrdinalIgnoreCase) &&
        SameTime(left.TimeFilter, right.TimeFilter);

    private static bool SameLightingScope(LightingApplicationRule left, LightingApplicationRule right) => left.Enabled &&
        right.ProcessNames.All(name => left.ProcessNames.Contains(name, StringComparer.OrdinalIgnoreCase)) &&
        SameTime(left.TimeFilter, right.TimeFilter);

    private static bool SameTime(AutomationTimeFilter left, AutomationTimeFilter right) =>
        left.TimeEnabled == right.TimeEnabled && left.Start == right.Start && left.End == right.End &&
        left.Days.SequenceEqual(right.Days);
}

public sealed record AutomationSimulationInput(DateTime LocalTime, string ForegroundProcess,
    IReadOnlyList<AudioApplicationState> AudioApplications, bool Idle);
public sealed record AutomationSimulationResult(AutomationSelection Selection, IReadOnlyList<RuleHealthIssue> Health,
    int FinalBrightnessLimit, string PriorityTrace);

public static class AutomationSimulator
{
    public static AutomationSimulationResult Simulate(KeyboardSettings settings, AutomationSimulationInput input)
    {
        var health = AutomationHealthAnalyzer.Analyze(settings);
        string? ValidateMusic(MusicApplicationRule rule) => health.Any(i => i.RuleId == rule.Id && i.Severity == RuleHealthSeverity.Error)
            ? "规则无效" : null;
        string? ValidateAction(SceneAction action) => action.Target == SceneTargetKind.Off ||
            (action.Target == SceneTargetKind.LightingPreset && (action.PresetId == EffectPresetSettings.BuiltInId(action.LightingEffectType) || settings.EffectPresets.ForType(action.LightingEffectType).Any(p => p.Id == action.PresetId))) ||
            (action.Target == SceneTargetKind.MusicPreset && MusicSettings.BuiltInPresets.Concat(settings.Effect.Music.CustomPresets).Any(p => p.Id == action.PresetId))
                ? null : "引用的预设不存在";
        var selection = AutomationResolver.Resolve(settings.Automation, input.LocalTime, input.ForegroundProcess,
            input.AudioApplications, ValidateMusic, ValidateAction);
        var limits = new List<int> { 100 };
        if (selection.Music?.BrightnessLimit is int music) limits.Add(music);
        if (selection.Lighting?.Action.BrightnessLimit is int lighting) limits.Add(lighting);
        if (input.Idle && settings.IdleDim.Enabled) limits.Add(settings.IdleDim.TurnOff ? 0 : settings.IdleDim.Brightness);
        var trace = selection.Music is not null ? $"有声音乐程序：{selection.Music.Name}" :
            selection.Lighting is not null ? $"前台灯效程序：{selection.Lighting.Name}" :
            selection.Schedule is not null ? $"时间计划：{selection.Schedule.Name}" : "用户手动基础模式";
        return new AutomationSimulationResult(selection, health, limits.Min(), trace);
    }
}
