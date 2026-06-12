using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text.RegularExpressions;

namespace ColorfulLedKeyboard.Core;

public sealed class SettingsStore
{
    private static readonly JsonSerializerOptions SerializerOptions = new()
    {
        WriteIndented = true
    };

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
            var defaults = new KeyboardSettings().Normalize();
            TrySaveDefaults(defaults);
            return defaults;
        }

        if (legacyMusicDetected)
        {
            settings.OperatingMode = OperatingMode.Music;
        }

        return settings.Normalize(migrateLegacyMode);
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
        Directory.CreateDirectory(Path.GetDirectoryName(SettingsPath)!);
        var json = JsonSerializer.Serialize(settings.Normalize(), SerializerOptions);
        var tempPath = $"{SettingsPath}.{Environment.ProcessId}.tmp";
        File.WriteAllText(tempPath, json);

        if (File.Exists(SettingsPath))
        {
            File.Replace(tempPath, SettingsPath, destinationBackupFileName: null);
        }
        else
        {
            File.Move(tempPath, SettingsPath);
        }
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
