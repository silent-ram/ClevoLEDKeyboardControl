namespace ColorfulLedKeyboard.Core;

public sealed class MusicSettings : IMusicTunable
{
    private static readonly string[] DefaultColorValues =
    [
        "#FF0000", "#FF8000", "#FFFF00", "#80FF00", "#00FF00", "#00FF80",
        "#00FFFF", "#0080FF", "#0000FF", "#8000FF", "#FF00FF", "#FF0080",
        "#B80000", "#B85C00", "#B8B800", "#5CB800", "#00B800", "#00B85C",
        "#00B8B8", "#005CB8", "#0000B8", "#5C00B8", "#B800B8", "#B8005C",
        "#750000", "#753B00", "#757500", "#3B7500", "#007500", "#00753B",
        "#007575", "#003B75", "#000075", "#3B0075", "#750075", "#75003B",
        "#FFFFFF", "#E0E0E0", "#C0C0C0", "#A0A0A0", "#808080", "#606060",
        "#FFD2A1", "#CFE8FF", "#FFB4DC", "#B4FFD2"
    ];
    public const string DefaultPresetName = "通用";
    public const string BuiltInDefaultPresetId = "builtin:music:general";
    public const double BeatThresholdAlgorithmScale = 0.10;

    public static double ToAlgorithmBeatThreshold(double beatThreshold) =>
        MusicSettingsNormalizer.ToAlgorithmBeatThreshold(beatThreshold);

    public string PresetName { get; set; } = DefaultPresetName;

    public MusicResponseMode ResponseMode { get; set; } = MusicResponseMode.LevelColor;

    public bool LevelColorEnabled { get; set; }

    public string LowColor { get; set; } = "#0040FF";

    public string HighColor { get; set; } = "#FF0040";

    public List<string> Colors { get; set; } = [];

    public double Sensitivity { get; set; } = 2.0;

    public int AttackMs { get; set; } = 35;

    public int ReleaseMs { get; set; } = 80;

    public int BaseBrightness { get; set; } = 25;

    public int PeakBrightness { get; set; } = 100;

    public int IntervalMs { get; set; } = 25;

    public double NoiseGate { get; set; } = 0;

    public double BeatThreshold { get; set; } = 0.02;

    public int PeakHoldMs { get; set; } = 90;

    public bool FollowSystemVolume { get; set; }

    public bool EqEnabled { get; set; }
    public bool AllowSystemMixFallback { get; set; } = true;

    public int EqLowHz { get; set; } = 30;

    public int EqHighHz { get; set; } = 5000;

    public SpotifySettings Spotify { get; set; } = new();

    /// <summary>
    /// 音乐模式自己的播放器绑定。它不依赖场景自动化，仅在用户手动选择音乐模式时生效。
    /// PID 只用于绑定时确认，持久化时保存稳定的进程身份与媒体会话身份。
    /// </summary>
    public MusicPlayerBinding PlayerBinding { get; set; } = new();

    public List<MusicPreset> CustomPresets { get; set; } = [];

    public MusicSettings Normalize()
    {
        PresetName = NormalizePresetName(PresetName);
        var fallbackResponseMode = LevelColorEnabled ? MusicResponseMode.LevelColor : MusicResponseMode.BrightnessPulse;
        MusicSettingsNormalizer.Normalize(this, NormalizeColors, fallbackResponseMode);
        Spotify ??= new SpotifySettings();
        Spotify.Normalize();
        PlayerBinding ??= new MusicPlayerBinding();
        PlayerBinding.Normalize();
        LevelColorEnabled = ResponseMode == MusicResponseMode.LevelColor;
        CustomPresets = (CustomPresets ?? [])
            .Select(preset => preset.Normalize())
            .Where(preset => !string.IsNullOrWhiteSpace(preset.Name))
            .Where(preset => !IsBuiltInPresetName(preset.Name))
            .GroupBy(preset => preset.Name, StringComparer.OrdinalIgnoreCase)
            .Select(group => group.Last())
            .Take(8)
            .ToList();
        return this;
    }

    public void ApplyPreset(MusicPreset preset)
    {
        PresetName = preset.Name;
        ResponseMode = preset.ResponseMode;
        LevelColorEnabled = ResponseMode == MusicResponseMode.LevelColor;
        LowColor = preset.LowColor;
        HighColor = preset.HighColor;
        Colors = [.. preset.Colors];
        Sensitivity = preset.Sensitivity;
        AttackMs = preset.AttackMs;
        ReleaseMs = preset.ReleaseMs;
        BaseBrightness = preset.BaseBrightness;
        PeakBrightness = preset.PeakBrightness;
        IntervalMs = preset.IntervalMs;
        NoiseGate = preset.NoiseGate;
        BeatThreshold = preset.BeatThreshold;
        PeakHoldMs = preset.PeakHoldMs;
        FollowSystemVolume = preset.FollowSystemVolume;
        EqEnabled = preset.EqEnabled;
        EqLowHz = preset.EqLowHz;
        EqHighHz = preset.EqHighHz;
        Normalize();
    }

    public MusicPreset ToPreset(string name)
    {
        return new MusicPreset
        {
            Name = name,
            ResponseMode = ResponseMode,
            LowColor = LowColor,
            HighColor = HighColor,
            Colors = [.. Colors],
            Sensitivity = Sensitivity,
            AttackMs = AttackMs,
            ReleaseMs = ReleaseMs,
            BaseBrightness = BaseBrightness,
            PeakBrightness = PeakBrightness,
            IntervalMs = IntervalMs,
            NoiseGate = NoiseGate,
            BeatThreshold = BeatThreshold,
            PeakHoldMs = PeakHoldMs,
            FollowSystemVolume = FollowSystemVolume,
            EqEnabled = EqEnabled,
            EqLowHz = EqLowHz,
            EqHighHz = EqHighHz
        }.Normalize();
    }

    public static bool IsBuiltInPresetName(string? name)
    {
        return !string.IsNullOrWhiteSpace(name) &&
            (BuiltInPresets.Any(preset => string.Equals(preset.Name, name.Trim(), StringComparison.OrdinalIgnoreCase)) ||
             IsLegacyBuiltInPresetName(name));
    }

    private static string NormalizePresetName(string? name)
    {
        if (string.IsNullOrWhiteSpace(name) || IsLegacyBuiltInPresetName(name))
        {
            return DefaultPresetName;
        }

        return name.Trim();
    }

    private static bool IsLegacyBuiltInPresetName(string? name)
    {
        var trimmed = name?.Trim();
        return string.Equals(trimmed, "自定义", StringComparison.OrdinalIgnoreCase) ||
            string.Equals(trimmed, "流行", StringComparison.OrdinalIgnoreCase) ||
            string.Equals(trimmed, "摇滚", StringComparison.OrdinalIgnoreCase) ||
            string.Equals(trimmed, "电子", StringComparison.OrdinalIgnoreCase) ||
            string.Equals(trimmed, "经典", StringComparison.OrdinalIgnoreCase);
    }

    public static List<string> DefaultColors() => [.. DefaultColorValues];

    private static List<string> NormalizeColors(List<string>? colors, string lowColor, string highColor)
    {
        var normalized = (colors ?? [])
            .Select(color => LightingEffectSettings.NormalizeHex(color, ""))
            .Where(color => !string.IsNullOrWhiteSpace(color))
            .ToList();

        if (normalized.Count == 0)
        {
            if (IsFactoryLegacyColorPair(lowColor, highColor))
            {
                normalized.AddRange(DefaultColorValues);
            }
            else
            {
                normalized.Add(lowColor);
                if (!string.Equals(lowColor, highColor, StringComparison.OrdinalIgnoreCase))
                {
                    normalized.Add(highColor);
                }
            }
        }

        if (normalized.Count == 0)
        {
            normalized.AddRange(DefaultColorValues);
        }

        return normalized.Take(48).ToList();
    }

    private static bool IsFactoryLegacyColorPair(string lowColor, string highColor) =>
        string.Equals(lowColor, "#0040FF", StringComparison.OrdinalIgnoreCase) &&
        string.Equals(highColor, "#FF0040", StringComparison.OrdinalIgnoreCase);

    public static IReadOnlyList<MusicPreset> BuiltInPresets { get; } =
    [
        new MusicPreset
        {
            Id = BuiltInDefaultPresetId,
            Name = DefaultPresetName,
            ResponseMode = MusicResponseMode.LevelColor,
            LowColor = "#FFD2A1",
            HighColor = "#007500",
            Colors =
            [
                "#FFD2A1",
                "#000075",
                "#0080FF",
                "#00FF00",
                "#00B85C",
                "#0000B8",
                "#E0E0E0",
                "#606060",
                "#757500",
                "#3B0075",
                "#B4FFD2",
                "#FF0000",
                "#FF0080",
                "#FFB4DC",
                "#007575",
                "#FF00FF",
                "#00B8B8",
                "#750000",
                "#00753B",
                "#003B75",
                "#005CB8",
                "#75003B",
                "#B8B800",
                "#FFFF00",
                "#B85C00",
                "#FFFFFF",
                "#80FF00",
                "#8000FF",
                "#00B800",
                "#753B00",
                "#750075",
                "#B800B8",
                "#808080",
                "#B80000",
                "#00FF80",
                "#C0C0C0",
                "#0000FF",
                "#A0A0A0",
                "#00FFFF",
                "#CFE8FF",
                "#B8005C",
                "#FF8000",
                "#5CB800",
                "#5C00B8",
                "#3B7500",
                "#007500"
            ],
            Sensitivity = 2.0,
            AttackMs = 20,
            ReleaseMs = 80,
            BaseBrightness = 30,
            PeakBrightness = 100,
            NoiseGate = 0,
            BeatThreshold = 0.02,
            PeakHoldMs = 90,
            FollowSystemVolume = false,
            EqEnabled = false,
            EqLowHz = 50,
            EqHighHz = 10999
        }.Normalize()
    ];
}

public sealed class MusicPlayerBinding
{
    public bool Enabled { get; set; }
    public string ProcessName { get; set; } = "";
    public string ExecutablePath { get; set; } = "";
    public bool IncludeChildProcesses { get; set; } = true;
    public string MediaSessionId { get; set; } = "";
    public MusicColorSource ColorSource { get; set; } = MusicColorSource.Preset;

    public MusicPlayerBinding Normalize()
    {
        ProcessName = AppProfileRule.NormalizeProcessName(ProcessName);
        ExecutablePath = (ExecutablePath ?? "").Trim();
        MediaSessionId = (MediaSessionId ?? "").Trim();
        if (!Enum.IsDefined(ColorSource)) ColorSource = MusicColorSource.Preset;
        if (string.IsNullOrWhiteSpace(ProcessName)) Enabled = false;
        return this;
    }
}

public enum MusicResponseMode
{
    LevelColor = 0,
    BrightnessPulse = 1
}

public sealed class MusicPreset : IMusicTunable
{
    public string Id { get; set; } = Guid.NewGuid().ToString("N");

    public string Name { get; set; } = MusicSettings.DefaultPresetName;

    public MusicResponseMode ResponseMode { get; set; } = MusicResponseMode.LevelColor;

    public string LowColor { get; set; } = "#0040FF";

    public string HighColor { get; set; } = "#FF0040";

    public List<string> Colors { get; set; } = [];

    public double Sensitivity { get; set; } = 2.0;

    public int AttackMs { get; set; } = 35;

    public int ReleaseMs { get; set; } = 80;

    public int BaseBrightness { get; set; } = 25;

    public int PeakBrightness { get; set; } = 100;

    public int IntervalMs { get; set; } = 25;

    public double NoiseGate { get; set; } = 0;

    public double BeatThreshold { get; set; } = 0.02;

    public int PeakHoldMs { get; set; } = 90;

    public bool FollowSystemVolume { get; set; }

    public bool EqEnabled { get; set; }

    public int EqLowHz { get; set; } = 30;

    public int EqHighHz { get; set; } = 5000;

    public MusicPreset Normalize()
    {
        Id = string.IsNullOrWhiteSpace(Id) ? Guid.NewGuid().ToString("N") : Id.Trim();
        Name = string.IsNullOrWhiteSpace(Name) ? "" : Name.Trim();
        MusicSettingsNormalizer.Normalize(this, NormalizeColors, MusicResponseMode.LevelColor);
        return this;
    }

    private static List<string> NormalizeColors(List<string>? colors, string lowColor, string highColor)
    {
        var normalized = (colors ?? [])
            .Select(color => LightingEffectSettings.NormalizeHex(color, ""))
            .Where(color => !string.IsNullOrWhiteSpace(color))
            .ToList();

        if (normalized.Count == 0)
        {
            if (IsFactoryLegacyColorPair(lowColor, highColor))
            {
                normalized.AddRange(MusicSettings.DefaultColors());
            }
            else
            {
                normalized.Add(lowColor);
                if (!string.Equals(lowColor, highColor, StringComparison.OrdinalIgnoreCase))
                {
                    normalized.Add(highColor);
                }
            }
        }

        if (normalized.Count == 0)
        {
            normalized.AddRange(MusicSettings.DefaultColors());
        }

        return normalized.Take(48).ToList();
    }

    private static bool IsFactoryLegacyColorPair(string lowColor, string highColor) =>
        string.Equals(lowColor, "#0040FF", StringComparison.OrdinalIgnoreCase) &&
        string.Equals(highColor, "#FF0040", StringComparison.OrdinalIgnoreCase);
}
