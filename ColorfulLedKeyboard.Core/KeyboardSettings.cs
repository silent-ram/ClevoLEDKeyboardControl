namespace ColorfulLedKeyboard.Core;

public sealed class KeyboardSettings
{
    public bool Enabled { get; set; } = true;

    public KeyboardMode Mode { get; set; } = KeyboardMode.Rainbow;

    public string StaticColor { get; set; } = "#FF0000";

    public int RainbowStep { get; set; } = 3;

    public int RefreshIntervalMs { get; set; } = 40;

    public int Brightness { get; set; } = 70;

    public OperatingMode OperatingMode { get; set; } = OperatingMode.Lighting;

    public LightingEffectSettings Effect { get; set; } = new();

    public EffectMemorySettings SavedEffects { get; set; } = new();

    public EffectPresetSettings EffectPresets { get; set; } = new();

    public IdleDimSettings IdleDim { get; set; } = new();

    public ScheduleSettings Schedule { get; set; } = new();

    public AppProfileSettings AppProfiles { get; set; } = new();

    public TypingPulseSettings TypingPulse { get; set; } = new();

    public UpdateSettings Update { get; set; } = new();

    public NotificationFlashSettings NotificationFlash { get; set; } = new();

    public KeyboardSettings Normalize(bool migrateLegacyMode = false)
    {
        Brightness = Math.Clamp(Brightness, 0, 100);
        RainbowStep = Math.Clamp(RainbowStep, 1, 20);
        RefreshIntervalMs = Math.Clamp(RefreshIntervalMs, 20, 500);

        try
        {
            StaticColor = RgbColor.FromHex(StaticColor).Hex;
        }
        catch (FormatException)
        {
            StaticColor = "#FF0000";
        }

        if (!Enum.IsDefined(Mode))
        {
            Mode = KeyboardMode.Rainbow;
        }

        if (!Enum.IsDefined(OperatingMode))
        {
            OperatingMode = OperatingMode.Lighting;
        }

        Effect ??= new LightingEffectSettings();

        if (migrateLegacyMode && Effect.Type == EffectType.Rainbow && Mode != KeyboardMode.Rainbow)
        {
            Effect.Type = Mode switch
            {
                KeyboardMode.Static => EffectType.Static,
                KeyboardMode.Breathing => EffectType.Breathing,
                KeyboardMode.Sequence => EffectType.Sequence,
                KeyboardMode.Off => EffectType.Off,
                KeyboardMode.Pulse => EffectType.Pulse,
                KeyboardMode.Heartbeat => EffectType.Heartbeat,
                _ => Effect.Type
            };
        }

        // 旧版本可能保存了 EffectType.Music = 5（已删除）；
        // 这里精确识别值 5 把灯效降回 Static，并把顶层模式切到 Music，保留用户"在音乐模式"的语义。
        // 注意：旧 JSON 中 "Effect.Type": "Music" 字符串由 SettingsStore 预扫描负责（Task 6），
        // 此处仅兜底\"反序列化得到整数 5\" 这条路径。
        if ((int)Effect.Type == 5)
        {
            Effect.Type = EffectType.Static;
            OperatingMode = OperatingMode.Music;
        }
        else if (!Enum.IsDefined(Effect.Type))
        {
            // 其他未知值（未来扩展或被破坏的数据）仅静默回退灯效，不动 OperatingMode。
            Effect.Type = EffectType.Static;
        }

        if (migrateLegacyMode &&
            Effect.Type is EffectType.Static or EffectType.Breathing &&
            Effect.Color == "#FF0000" &&
            StaticColor != "#FF0000")
        {
            Effect.Color = StaticColor;
        }

        if (migrateLegacyMode && Effect.Type == EffectType.Rainbow)
        {
            Effect.Step = RainbowStep;
            Effect.IntervalMs = RefreshIntervalMs;
        }

        Effect.Normalize();
        SavedEffects ??= new EffectMemorySettings();
        SavedEffects.Normalize();
        EffectPresets ??= new EffectPresetSettings();
        EffectPresets.Normalize();
        IdleDim ??= new IdleDimSettings();
        IdleDim.Normalize();
        Schedule ??= new ScheduleSettings();
        Schedule.Normalize();
        AppProfiles ??= new AppProfileSettings();
        AppProfiles.Normalize();
        TypingPulse ??= new TypingPulseSettings();
        TypingPulse.Normalize();
        Update ??= new UpdateSettings();
        Update.Normalize();
        NotificationFlash ??= new NotificationFlashSettings();
        NotificationFlash.Normalize();
        Mode = Effect.Type switch
        {
            EffectType.Static => KeyboardMode.Static,
            EffectType.Rainbow => KeyboardMode.Rainbow,
            EffectType.Breathing => KeyboardMode.Breathing,
            EffectType.Sequence => KeyboardMode.Sequence,
            EffectType.Off => KeyboardMode.Off,
            EffectType.Pulse => KeyboardMode.Pulse,
            EffectType.Heartbeat => KeyboardMode.Heartbeat,
            _ => Mode
        };

        StaticColor = Effect.Color;
        RainbowStep = Effect.Step;
        RefreshIntervalMs = Effect.IntervalMs;

        return this;
    }

    public KeyboardSettings CloneForRuntime()
    {
        return new KeyboardSettings
        {
            Enabled = Enabled,
            OperatingMode = OperatingMode,
            Mode = Mode,
            StaticColor = StaticColor,
            RainbowStep = RainbowStep,
            RefreshIntervalMs = RefreshIntervalMs,
            Brightness = Brightness,
            Effect = CloneEffect(Effect),
            SavedEffects = new EffectMemorySettings
            {
                Static = CloneEffect(SavedEffects.Static),
                Rainbow = CloneEffect(SavedEffects.Rainbow),
                Breathing = CloneEffect(SavedEffects.Breathing),
                Sequence = CloneEffect(SavedEffects.Sequence),
                Pulse = CloneEffect(SavedEffects.Pulse),
                Heartbeat = CloneEffect(SavedEffects.Heartbeat),
                LastUsedLightingEffect = SavedEffects.LastUsedLightingEffect
            },
            EffectPresets = CloneEffectPresets(EffectPresets),
            IdleDim = new IdleDimSettings
            {
                Enabled = IdleDim.Enabled,
                AfterSeconds = IdleDim.AfterSeconds,
                Brightness = IdleDim.Brightness,
                TurnOff = IdleDim.TurnOff
            },
            Schedule = new ScheduleSettings
            {
                Enabled = Schedule.Enabled,
                Rules = Schedule.Rules.Select(rule => new ScheduleRule
                {
                    Name = rule.Name,
                    Start = rule.Start,
                    End = rule.End,
                    Enabled = rule.Enabled,
                    Brightness = rule.Brightness,
                    Effect = CloneEffect(rule.Effect)
                }).ToList()
            },
            AppProfiles = new AppProfileSettings
            {
                Enabled = AppProfiles.Enabled,
                Rules = AppProfiles.Rules.Select(rule => new AppProfileRule
                {
                    Name = rule.Name,
                    ProcessName = rule.ProcessName,
                    Enabled = rule.Enabled,
                    AutoColorEnabled = rule.AutoColorEnabled,
                    IconColor = rule.IconColor,
                    Brightness = rule.Brightness,
                    TargetEffect = rule.TargetEffect,
                    ManualColor = rule.ManualColor
                }).ToList()
            },
            TypingPulse = new TypingPulseSettings
            {
                Enabled = TypingPulse.Enabled,
                BaseBrightness = TypingPulse.BaseBrightness,
                PeakBrightness = TypingPulse.PeakBrightness,
                HoldMs = TypingPulse.HoldMs,
                FadeMs = TypingPulse.FadeMs
            },
            Update = new UpdateSettings
            {
                CheckInterval = Update.CheckInterval
            },
            NotificationFlash = new NotificationFlashSettings
            {
                Enabled = NotificationFlash.Enabled,
                Color = NotificationFlash.Color,
                Pulses = NotificationFlash.Pulses,
                PulseMs = NotificationFlash.PulseMs,
                CooldownSeconds = NotificationFlash.CooldownSeconds
            }
        }.Normalize();
    }

    public static LightingEffectSettings CloneEffect(LightingEffectSettings effect)
    {
        return new LightingEffectSettings
        {
            Type = effect.Type,
            Color = effect.Color,
            Step = effect.Step,
            IntervalMs = effect.IntervalMs,
            PeriodMs = effect.PeriodMs,
            MinimumBrightness = effect.MinimumBrightness,
            HardBlink = effect.HardBlink,
            CustomSequenceColorsEnabled = effect.CustomSequenceColorsEnabled,
            Music = new MusicSettings
            {
                LevelColorEnabled = effect.Music.LevelColorEnabled,
                PresetName = effect.Music.PresetName,
                ResponseMode = effect.Music.ResponseMode,
                LowColor = effect.Music.LowColor,
                HighColor = effect.Music.HighColor,
                Colors = [.. effect.Music.Colors],
                Sensitivity = effect.Music.Sensitivity,
                AttackMs = effect.Music.AttackMs,
                ReleaseMs = effect.Music.ReleaseMs,
                BaseBrightness = effect.Music.BaseBrightness,
                PeakBrightness = effect.Music.PeakBrightness,
                IntervalMs = effect.Music.IntervalMs,
                NoiseGate = effect.Music.NoiseGate,
                BeatThreshold = effect.Music.BeatThreshold,
                PeakHoldMs = effect.Music.PeakHoldMs,
                FollowSystemVolume = effect.Music.FollowSystemVolume,
                EqEnabled = effect.Music.EqEnabled,
                EqLowHz = effect.Music.EqLowHz,
                EqHighHz = effect.Music.EqHighHz,
                Spotify = new SpotifySettings
                {
                    AlbumColorEnabled = effect.Music.Spotify.AlbumColorEnabled,
                    AlbumColorSource = effect.Music.Spotify.AlbumColorSource,
                    ClientId = effect.Music.Spotify.ClientId,
                    RefreshToken = effect.Music.Spotify.RefreshToken,
                    LastAlbumColor = effect.Music.Spotify.LastAlbumColor
                },
                CustomPresets = effect.Music.CustomPresets.Select(preset => new MusicPreset
                {
                    Name = preset.Name,
                    ResponseMode = preset.ResponseMode,
                    LowColor = preset.LowColor,
                    HighColor = preset.HighColor,
                    Colors = [.. preset.Colors],
                    Sensitivity = preset.Sensitivity,
                    AttackMs = preset.AttackMs,
                    ReleaseMs = preset.ReleaseMs,
                    BaseBrightness = preset.BaseBrightness,
                    PeakBrightness = preset.PeakBrightness,
                    IntervalMs = preset.IntervalMs,
                    NoiseGate = preset.NoiseGate,
                    BeatThreshold = preset.BeatThreshold,
                    PeakHoldMs = preset.PeakHoldMs,
                    FollowSystemVolume = preset.FollowSystemVolume,
                    EqEnabled = preset.EqEnabled,
                    EqLowHz = preset.EqLowHz,
                    EqHighHz = preset.EqHighHz
                }).ToList()
            },
            Sequence = effect.Sequence.Select(item => new SequenceColor
            {
                Color = item.Color,
                HoldMs = item.HoldMs,
                TransitionMs = item.TransitionMs,
                Breathing = item.Breathing
            }).ToList()
        };
    }

    public static EffectPresetSettings CloneEffectPresets(EffectPresetSettings presets)
    {
        return new EffectPresetSettings
        {
            Static = presets.Static.Select(CloneEffectPreset).ToList(),
            Rainbow = presets.Rainbow.Select(CloneEffectPreset).ToList(),
            Breathing = presets.Breathing.Select(CloneEffectPreset).ToList(),
            Sequence = presets.Sequence.Select(CloneEffectPreset).ToList(),
            Pulse = presets.Pulse.Select(CloneEffectPreset).ToList(),
            Heartbeat = presets.Heartbeat.Select(CloneEffectPreset).ToList()
        }.Normalize();
    }

    public static EffectPreset CloneEffectPreset(EffectPreset preset)
    {
        return new EffectPreset
        {
            Name = preset.Name,
            Effect = CloneEffect(preset.Effect)
        };
    }
}

public sealed class EffectMemorySettings
{
    public LightingEffectSettings Static { get; set; } =
        new() { Type = EffectType.Static, Color = "#FF0000" };

    public LightingEffectSettings Rainbow { get; set; } =
        new()
        {
            Type = EffectType.Rainbow,
            PeriodMs = EffectPresetSettings.DefaultPeriodMs,
            CustomSequenceColorsEnabled = true,
            Sequence =
            [
                new SequenceColor { Color = "#FF0000", HoldMs = EffectPresetSettings.DefaultPeriodMs, TransitionMs = 0, Breathing = false },
                new SequenceColor { Color = "#0000FF", HoldMs = EffectPresetSettings.DefaultPeriodMs, TransitionMs = 0, Breathing = false }
            ]
        };

    public LightingEffectSettings Breathing { get; set; } =
        new() { Type = EffectType.Breathing, Color = "#FF0000", PeriodMs = EffectPresetSettings.DefaultPeriodMs };

    public LightingEffectSettings Sequence { get; set; } =
        new()
        {
            Type = EffectType.Sequence,
            PeriodMs = EffectPresetSettings.DefaultPeriodMs,
            Sequence =
            [
                new SequenceColor { Color = "#FF0000", HoldMs = EffectPresetSettings.DefaultPeriodMs, TransitionMs = 0, Breathing = true },
                new SequenceColor { Color = "#0000FF", HoldMs = EffectPresetSettings.DefaultPeriodMs, TransitionMs = 0, Breathing = true }
            ]
        };

    public LightingEffectSettings Pulse { get; set; } =
        EffectPresetSettings.CreateSoftwareDefault(EffectType.Pulse);

    public LightingEffectSettings Heartbeat { get; set; } =
        EffectPresetSettings.CreateSoftwareDefault(EffectType.Heartbeat);

    /// <summary>
    /// 最后一次用户切到的灯效类型（不含 Off）。
    /// 当用户从音乐模式切回灯效模式时，UI 会从此字段恢复 Effect.Type，让"切回"体验连贯。
    /// 老版本升级时缺失此字段，默认为 Static。
    /// </summary>
    public EffectType LastUsedLightingEffect { get; set; } = EffectType.Static;

    public EffectMemorySettings Normalize()
    {
        Static = NormalizeForType(Static, EffectType.Static);
        Rainbow = NormalizeForType(Rainbow, EffectType.Rainbow);
        Rainbow.CustomSequenceColorsEnabled = true;
        Breathing = NormalizeForType(Breathing, EffectType.Breathing);
        Sequence = NormalizeForType(Sequence, EffectType.Sequence);
        Pulse = NormalizeForType(Pulse, EffectType.Pulse);
        Heartbeat = NormalizeForType(Heartbeat, EffectType.Heartbeat);
        // 旧版本升级或损坏的配置可能落到无效值（如已废弃的 Music=5），统一回退到 Static。
        if (!Enum.IsDefined(LastUsedLightingEffect) || LastUsedLightingEffect == EffectType.Off)
        {
            LastUsedLightingEffect = EffectType.Static;
        }
        return this;
    }

    private static LightingEffectSettings NormalizeForType(LightingEffectSettings? effect, EffectType type)
    {
        effect ??= type is EffectType.Pulse or EffectType.Heartbeat
            ? EffectPresetSettings.CreateSoftwareDefault(type)
            : new LightingEffectSettings();
        effect.Type = type;
        return effect.Normalize();
    }
}

public sealed class EffectPresetSettings
{
    public const int DefaultPeriodMs = 3000;
    public const int DefaultPulsePeriodMs = 2000;
    public const int DefaultHeartbeatPeriodMs = 1500;
    public const int MaxPresetsPerMode = 16;

    public List<EffectPreset> Static { get; set; } = [];

    public List<EffectPreset> Rainbow { get; set; } = [];

    public List<EffectPreset> Breathing { get; set; } = [];

    public List<EffectPreset> Sequence { get; set; } = [];

    public List<EffectPreset> Pulse { get; set; } = [];

    public List<EffectPreset> Heartbeat { get; set; } = [];

    public EffectPresetSettings Normalize()
    {
        Static = NormalizeList(Static, EffectType.Static);
        Rainbow = NormalizeList(Rainbow, EffectType.Rainbow);
        Breathing = NormalizeList(Breathing, EffectType.Breathing);
        Sequence = NormalizeList(Sequence, EffectType.Sequence);
        Pulse = NormalizeList(Pulse, EffectType.Pulse);
        Heartbeat = NormalizeList(Heartbeat, EffectType.Heartbeat);
        return this;
    }

    public List<EffectPreset> ForType(EffectType type) => type switch
    {
        EffectType.Static => Static,
        EffectType.Rainbow => Rainbow,
        EffectType.Breathing => Breathing,
        EffectType.Sequence => Sequence,
        EffectType.Pulse => Pulse,
        EffectType.Heartbeat => Heartbeat,
        _ => []
    };

    public static LightingEffectSettings CreateSoftwareDefault(EffectType type)
    {
        var effect = type switch
        {
            EffectType.Static => new LightingEffectSettings
            {
                Type = EffectType.Static,
                Color = "#FF0000"
            },
            EffectType.Breathing => new LightingEffectSettings
            {
                Type = EffectType.Breathing,
                Color = "#FF0000",
                PeriodMs = DefaultPeriodMs,
                MinimumBrightness = 0,
                HardBlink = false
            },
            EffectType.Sequence => new LightingEffectSettings
            {
                Type = EffectType.Sequence,
                PeriodMs = DefaultPeriodMs,
                Sequence =
                [
                    new SequenceColor { Color = "#FF0000", HoldMs = DefaultPeriodMs, TransitionMs = 0, Breathing = true },
                    new SequenceColor { Color = "#0000FF", HoldMs = DefaultPeriodMs, TransitionMs = 0, Breathing = true }
                ]
            },
            EffectType.Pulse => new LightingEffectSettings
            {
                Type = EffectType.Pulse,
                PeriodMs = DefaultPulsePeriodMs,
                Sequence =
                [
                    new SequenceColor { Color = "#00FFFF", HoldMs = DefaultPulsePeriodMs, TransitionMs = 0, Breathing = false },
                    new SequenceColor { Color = "#0060FF", HoldMs = DefaultPulsePeriodMs, TransitionMs = 0, Breathing = false },
                    new SequenceColor { Color = "#8000FF", HoldMs = DefaultPulsePeriodMs, TransitionMs = 0, Breathing = false }
                ]
            },
            EffectType.Heartbeat => new LightingEffectSettings
            {
                Type = EffectType.Heartbeat,
                PeriodMs = DefaultHeartbeatPeriodMs,
                Sequence =
                [
                    new SequenceColor { Color = "#FF0000", HoldMs = DefaultHeartbeatPeriodMs, TransitionMs = 0, Breathing = false },
                    new SequenceColor { Color = "#FF4080", HoldMs = DefaultHeartbeatPeriodMs, TransitionMs = 0, Breathing = false }
                ]
            },
            _ => new LightingEffectSettings
            {
                Type = EffectType.Rainbow,
                PeriodMs = DefaultPeriodMs,
                CustomSequenceColorsEnabled = true,
                Sequence =
                [
                    new SequenceColor { Color = "#FF0000", HoldMs = DefaultPeriodMs, TransitionMs = 0, Breathing = false },
                    new SequenceColor { Color = "#FFFF00", HoldMs = DefaultPeriodMs, TransitionMs = 0, Breathing = false },
                    new SequenceColor { Color = "#00FF00", HoldMs = DefaultPeriodMs, TransitionMs = 0, Breathing = false },
                    new SequenceColor { Color = "#00FFFF", HoldMs = DefaultPeriodMs, TransitionMs = 0, Breathing = false },
                    new SequenceColor { Color = "#0000FF", HoldMs = DefaultPeriodMs, TransitionMs = 0, Breathing = false },
                    new SequenceColor { Color = "#FF00FF", HoldMs = DefaultPeriodMs, TransitionMs = 0, Breathing = false }
                ]
            }
        };

        return effect.Normalize();
    }

    private static List<EffectPreset> NormalizeList(List<EffectPreset>? presets, EffectType type)
    {
        var result = new List<EffectPreset>();
        foreach (var preset in presets ?? [])
        {
            var normalized = preset.Normalize(type);
            if (string.IsNullOrWhiteSpace(normalized.Name) ||
                result.Any(item => string.Equals(item.Name, normalized.Name, StringComparison.OrdinalIgnoreCase)))
            {
                continue;
            }

            result.Add(normalized);
            if (result.Count >= MaxPresetsPerMode)
            {
                break;
            }
        }

        return result;
    }
}

public sealed class EffectPreset
{
    public string Name { get; set; } = "";

    public LightingEffectSettings Effect { get; set; } = new();

    public EffectPreset Normalize(EffectType type)
    {
        Name = Name.Trim();
        Effect ??= EffectPresetSettings.CreateSoftwareDefault(type);
        Effect.Type = type;
        Effect.Normalize();
        if (type == EffectType.Rainbow)
        {
            Effect.CustomSequenceColorsEnabled = true;
        }

        return this;
    }
}
