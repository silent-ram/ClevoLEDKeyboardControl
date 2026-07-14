using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text.RegularExpressions;
using System.Text;

namespace ColorfulLedKeyboard.Core;

public sealed class SettingsStore
{
    public const string LastGoodSuffix = ".last-good.bak";
    private static readonly JsonSerializerOptions SerializerOptions = new()
    {
        WriteIndented = true
    };
    private static readonly object SaveSync = new();

    static SettingsStore()
    {
        SerializerOptions.Converters.Add(new JsonStringEnumConverter());
    }

    public string SettingsPath { get; }

    public SettingsStore(string? settingsPath = null)
    {
        SettingsPath = settingsPath ?? AppPaths.SettingsPath;
    }

    public KeyboardSettings Load()
    {
        if (IsInteractiveDefaultStore &&
            ServiceIpc.TryRequest<object, KeyboardSettings>("GetSettings", new { }, out var remote, timeoutMs: 250) && remote is not null)
            return remote.Normalize();
        return LoadLocal();
    }

    public KeyboardSettings LoadLocal()
    {
        Directory.CreateDirectory(Path.GetDirectoryName(SettingsPath)!);

        if (!File.Exists(SettingsPath))
        {
            var defaults = new KeyboardSettings().Normalize();
            TrySaveDefaults(defaults);
            return defaults;
        }

        string json;
        try
        {
            json = File.ReadAllText(SettingsPath);
        }
        catch (IOException)
        {
            return new KeyboardSettings().Normalize();
        }
        catch (UnauthorizedAccessException)
        {
            return new KeyboardSettings().Normalize();
        }

        var migrateLegacyMode = !json.Contains("\"Effect\"", StringComparison.Ordinal);

        var automationVersion = DetectAutomationVersion(json);
        var migrateLegacyAutomation = automationVersion == 0;

        // ★ 旧版 EffectType.Music 预扫描：枚举值已删除，反序列化会抛 JsonException 导致重置默认（数据丢失）
        var legacyMusicDetected = DetectLegacyMusicMode(json);
        var legacyAppProfileMusic = ContainsLegacyAppProfileMusic(json);
        if (legacyMusicDetected || legacyAppProfileMusic)
        {
            json = SanitizeLegacyMusicStrings(json);
        }

        KeyboardSettings settings;
        try
        {
            settings = JsonSerializer.Deserialize<KeyboardSettings>(json, SerializerOptions) ?? new KeyboardSettings();
        }
        catch (JsonException)
        {
            return RecoverCorruptSettings();
        }

        if (legacyMusicDetected)
        {
            settings.OperatingMode = OperatingMode.Music;
        }

        settings.Normalize(migrateLegacyMode);
        if (migrateLegacyAutomation)
        {
            MigrateLegacyAutomation(settings);
        }
        if (automationVersion < AutomationSettings.CurrentVersion)
        {
            MigrateAutomationV1(settings);
            TryPersistAutomationMigration(settings);
        }

        return settings;
    }

    private static int DetectAutomationVersion(string json)
    {
        try
        {
            using var doc = JsonDocument.Parse(json);
            if (!doc.RootElement.TryGetProperty("Automation", out var automation)) return 0;
            return automation.TryGetProperty("Version", out var version) && version.TryGetInt32(out var value)
                ? value
                : 1;
        }
        catch (JsonException)
        {
            return 0;
        }
    }

    private static void MigrateAutomationV1(KeyboardSettings settings)
    {
        foreach (var legacy in settings.Automation.Rules)
        {
            var filter = new AutomationTimeFilter
            {
                TimeEnabled = legacy.Conditions.TimeEnabled,
                Start = legacy.Conditions.Start,
                End = legacy.Conditions.End,
                Days = [.. legacy.Conditions.Days]
            }.Normalize();

            if (legacy.Conditions.ApplicationsEnabled && legacy.Conditions.ProcessNames.Count > 0)
            {
                if (legacy.Action.Target == SceneTargetKind.MusicPreset)
                {
                    foreach (var processName in legacy.Conditions.ProcessNames)
                    {
                        settings.Automation.MusicApplications.Add(new MusicApplicationRule
                        {
                            Name = legacy.Conditions.ProcessNames.Count == 1 ? legacy.Name : $"{legacy.Name} - {processName}",
                            Enabled = legacy.Enabled,
                            ProcessName = processName,
                            TimeFilter = filter,
                            MusicPresetId = legacy.Action.PresetId,
                            BrightnessLimit = legacy.Action.BrightnessLimit
                        }.Normalize());
                    }
                }
                else
                {
                    settings.Automation.LightingApplications.Add(new LightingApplicationRule
                    {
                        Name = legacy.Name,
                        Enabled = legacy.Enabled,
                        ProcessNames = [.. legacy.Conditions.ProcessNames],
                        TimeFilter = filter,
                        Action = legacy.Action
                    }.Normalize());
                }
            }
            else
            {
                settings.Automation.ScheduleRules.Add(new AutomationScheduleRule
                {
                    Name = legacy.Name,
                    Enabled = legacy.Enabled,
                    TimeFilter = filter,
                    Action = legacy.Action
                }.Normalize());
            }
        }

        settings.Automation.Rules.Clear();
        settings.Automation.Version = AutomationSettings.CurrentVersion;
        settings.Automation.Normalize();
    }

    private static void MigrateLegacyAutomation(KeyboardSettings settings)
    {
        var automation = new AutomationSettings
        {
            Enabled = settings.AppProfiles.Enabled || settings.Schedule.Enabled,
            Rules = []
        };

        var index = 0;
        foreach (var legacy in settings.AppProfiles.Rules)
        {
            var effect = legacy.BuildEffect();
            var preset = CreateMigratedPreset(settings, effect, legacy.Name, ++index);
            automation.Rules.Add(new SceneRule
            {
                Name = legacy.Name,
                Enabled = settings.AppProfiles.Enabled && legacy.Enabled,
                Conditions = new SceneConditions
                {
                    ApplicationsEnabled = true,
                    ProcessNames = [legacy.ProcessName]
                },
                Action = new SceneAction
                {
                    Target = SceneTargetKind.LightingPreset,
                    LightingEffectType = effect.Type,
                    PresetId = preset.Id,
                    BrightnessLimit = legacy.Brightness
                }
            }.Normalize());
        }

        foreach (var legacy in settings.Schedule.Rules)
        {
            var action = new SceneAction
            {
                Target = legacy.Effect.Type == EffectType.Off
                    ? SceneTargetKind.Off
                    : SceneTargetKind.LightingPreset,
                LightingEffectType = legacy.Effect.Type,
                BrightnessLimit = legacy.Brightness
            };
            if (action.Target == SceneTargetKind.LightingPreset)
            {
                action.PresetId = CreateMigratedPreset(settings, legacy.Effect, legacy.Name, ++index).Id;
            }

            automation.Rules.Add(new SceneRule
            {
                Name = legacy.Name,
                Enabled = settings.Schedule.Enabled && legacy.Enabled,
                Conditions = new SceneConditions
                {
                    TimeEnabled = true,
                    Start = legacy.Start,
                    End = legacy.End
                },
                Action = action
            }.Normalize());
        }

        settings.Automation = automation.Normalize();
        settings.AppProfiles.Enabled = false;
        settings.Schedule.Enabled = false;
    }

    private static EffectPreset CreateMigratedPreset(
        KeyboardSettings settings,
        LightingEffectSettings effect,
        string ruleName,
        int index)
    {
        var preset = new EffectPreset
        {
            Name = $"迁移-{(string.IsNullOrWhiteSpace(ruleName) ? "场景" : ruleName.Trim())}-{index}",
            Effect = KeyboardSettings.CloneEffect(effect)
        }.Normalize(effect.Type);
        settings.EffectPresets.ForType(effect.Type).Add(preset);
        settings.EffectPresets.Normalize();
        return preset;
    }

    private void TryPersistAutomationMigration(KeyboardSettings settings)
    {
        try
        {
            var backupPath = SettingsPath + ".pre-automation-v2.bak";
            if (!File.Exists(backupPath) && File.Exists(SettingsPath))
            {
                File.Copy(SettingsPath, backupPath);
            }
            Save(settings);
        }
        catch (IOException) { }
        catch (UnauthorizedAccessException) { }
    }

    private static bool DetectLegacyMusicMode(string json)
    {
        try
        {
            using var doc = JsonDocument.Parse(json);
            if (!doc.RootElement.TryGetProperty("Effect", out var effect)) return false;
            if (!effect.TryGetProperty("Type", out var type)) return false;
            return type.ValueKind == JsonValueKind.String &&
                string.Equals(type.GetString(), "Music", StringComparison.OrdinalIgnoreCase);
        }
        catch (JsonException)
        {
            return false;
        }
    }

    private static bool ContainsLegacyAppProfileMusic(string json)
    {
        // 即使 Effect.Type 不是 Music，AppProfile 规则也可能引用 "TargetEffect": "Music"
        return Regex.IsMatch(
            json,
            @"""TargetEffect""\s*:\s*""Music""",
            RegexOptions.IgnoreCase);
    }

    private static string SanitizeLegacyMusicStrings(string json)
    {
        // 把 JSON 中所有 "Type": "Music" 和 "TargetEffect": "Music" 替换为对应字段的 Static
        return System.Text.RegularExpressions.Regex.Replace(
            json,
            @"""(Type|TargetEffect)""\s*:\s*""Music""",
            @"""$1"":""Static""",
            System.Text.RegularExpressions.RegexOptions.IgnoreCase);
    }

    public void Save(KeyboardSettings settings)
    {
        if (IsInteractiveDefaultStore)
        {
            if (!ServiceIpc.TrySendWithRetry("SaveSettings", settings.Normalize(), attempts: 4, timeoutMs: 750, delayMs: 150))
                throw new IOException("服务通信不可用，配置处于只读状态。");
            return;
        }
        SaveLocal(settings);
    }

    public void SaveLocal(KeyboardSettings settings)
    {
        lock (SaveSync)
        {
            Directory.CreateDirectory(Path.GetDirectoryName(SettingsPath)!);
            var json = JsonSerializer.Serialize(settings.Normalize(), SerializerOptions);
            var tempPath = $"{SettingsPath}.{Environment.ProcessId}.{Guid.NewGuid():N}.tmp";
            try
            {
                File.WriteAllText(tempPath, json);
                if (File.Exists(SettingsPath)) TryPreserveLastGood(SettingsPath);
                File.Move(tempPath, SettingsPath, overwrite: true);
            }
            finally
            {
                try { if (File.Exists(tempPath)) File.Delete(tempPath); } catch { }
            }
        }
    }

    private bool IsInteractiveDefaultStore => Environment.UserInteractive &&
        string.Equals(Path.GetFullPath(SettingsPath), Path.GetFullPath(AppPaths.SettingsPath), StringComparison.OrdinalIgnoreCase);

    private KeyboardSettings RecoverCorruptSettings()
    {
        var corruptPath = $"{SettingsPath}.corrupt-{DateTimeOffset.UtcNow:yyyyMMdd-HHmmssfff}";
        try
        {
            File.Move(SettingsPath, corruptPath);
        }
        catch (IOException) { corruptPath = SettingsPath; }
        catch (UnauthorizedAccessException) { corruptPath = SettingsPath; }

        var backupPath = SettingsPath + LastGoodSuffix;
        if (TryLoadValidated(backupPath, out var recovered))
        {
            TryWriteRecoveryStatus("RecoveredBackup", corruptPath, backupPath);
            try { Save(recovered); } catch (IOException) { } catch (UnauthorizedAccessException) { }
            return recovered;
        }

        TryWriteRecoveryStatus("DefaultsUsed", corruptPath, "");
        var defaults = new KeyboardSettings().Normalize();
        TrySaveDefaults(defaults);
        return defaults;
    }

    private static bool TryLoadValidated(string path, out KeyboardSettings settings)
    {
        settings = new KeyboardSettings().Normalize();
        try
        {
            if (!File.Exists(path)) return false;
            settings = (JsonSerializer.Deserialize<KeyboardSettings>(File.ReadAllText(path), SerializerOptions)
                ?? new KeyboardSettings()).Normalize();
            return true;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or JsonException)
        {
            return false;
        }
    }

    public static bool TryParse(string json, out KeyboardSettings settings, out string error)
    {
        settings = new KeyboardSettings().Normalize();
        error = "";
        if (Encoding.UTF8.GetByteCount(json) > ServiceIpc.MaximumMessageBytes)
        {
            error = "配置文件超过 1 MB 限制";
            return false;
        }
        try
        {
            settings = (JsonSerializer.Deserialize<KeyboardSettings>(json, SerializerOptions)
                ?? throw new JsonException("配置内容为空")).Normalize();
            return true;
        }
        catch (JsonException ex)
        {
            error = ex.Message;
            return false;
        }
    }

    private void TryPreserveLastGood(string path)
    {
        try
        {
            using var _ = JsonDocument.Parse(File.ReadAllText(path));
            File.Copy(path, SettingsPath + LastGoodSuffix, overwrite: true);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or JsonException) { }
    }

    private void TryWriteRecoveryStatus(string result, string corruptPath, string backupPath)
    {
        try
        {
            var state = new SettingsRecoveryState
            {
                UpdatedUtc = DateTimeOffset.UtcNow,
                Result = result,
                CorruptFilePath = corruptPath,
                BackupFilePath = backupPath
            };
            var statePath = Path.Combine(Path.GetDirectoryName(SettingsPath)!, AppPaths.SettingsRecoveryStateFileName);
            File.WriteAllText(statePath, JsonSerializer.Serialize(state));
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { }
    }

    private void TrySaveDefaults(KeyboardSettings defaults)
    {
        try
        {
            Save(defaults);
        }
        catch (UnauthorizedAccessException)
        {
        }
        catch (IOException)
        {
        }
    }
}

public sealed class SettingsRecoveryState
{
    public DateTimeOffset UpdatedUtc { get; set; }
    public string Result { get; set; } = "";
    public string CorruptFilePath { get; set; } = "";
    public string BackupFilePath { get; set; } = "";

    public static SettingsRecoveryState? Load()
    {
        if (Environment.UserInteractive &&
            ServiceIpc.TryRequest<object, SettingsRecoveryState>("GetSettingsRecovery", new { }, out var remote, 350))
            return remote;
        try
        {
            return File.Exists(AppPaths.SettingsRecoveryStatePath)
                ? JsonSerializer.Deserialize<SettingsRecoveryState>(File.ReadAllText(AppPaths.SettingsRecoveryStatePath))
                : null;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or JsonException) { return null; }
    }
}
