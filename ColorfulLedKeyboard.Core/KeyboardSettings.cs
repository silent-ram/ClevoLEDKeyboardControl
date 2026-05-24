namespace ColorfulLedKeyboard.Core;

public sealed class KeyboardSettings
{
    public bool Enabled { get; set; } = true;

    public KeyboardMode Mode { get; set; } = KeyboardMode.Rainbow;

    public string StaticColor { get; set; } = "#FF0000";

    public int RainbowStep { get; set; } = 3;

    public int RefreshIntervalMs { get; set; } = 40;

    public int Brightness { get; set; } = 70;

    public LightingEffectSettings Effect { get; set; } = new();

    public IdleDimSettings IdleDim { get; set; } = new();

    public ScheduleSettings Schedule { get; set; } = new();

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

        Effect ??= new LightingEffectSettings();

        if (migrateLegacyMode && Effect.Type == EffectType.Rainbow && Mode != KeyboardMode.Rainbow)
        {
            Effect.Type = Mode switch
            {
                KeyboardMode.Static => EffectType.Static,
                KeyboardMode.Breathing => EffectType.Breathing,
                KeyboardMode.Sequence => EffectType.Sequence,
                KeyboardMode.Off => EffectType.Off,
                KeyboardMode.Music => EffectType.Music,
                _ => Effect.Type
            };
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
        IdleDim ??= new IdleDimSettings();
        IdleDim.Normalize();
        Schedule ??= new ScheduleSettings();
        Schedule.Normalize();
        Mode = Effect.Type switch
        {
            EffectType.Static => KeyboardMode.Static,
            EffectType.Rainbow => KeyboardMode.Rainbow,
            EffectType.Breathing => KeyboardMode.Breathing,
            EffectType.Sequence => KeyboardMode.Sequence,
            EffectType.Off => KeyboardMode.Off,
            EffectType.Music => KeyboardMode.Music,
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
            Mode = Mode,
            StaticColor = StaticColor,
            RainbowStep = RainbowStep,
            RefreshIntervalMs = RefreshIntervalMs,
            Brightness = Brightness,
            Effect = CloneEffect(Effect),
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
            }
        }.Normalize();
    }

    private static LightingEffectSettings CloneEffect(LightingEffectSettings effect)
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
            Music = new MusicSettings
            {
                LevelColorEnabled = effect.Music.LevelColorEnabled,
                LowColor = effect.Music.LowColor,
                HighColor = effect.Music.HighColor,
                Sensitivity = effect.Music.Sensitivity,
                AttackMs = effect.Music.AttackMs,
                ReleaseMs = effect.Music.ReleaseMs,
                BaseBrightness = effect.Music.BaseBrightness,
                PeakBrightness = effect.Music.PeakBrightness,
                IntervalMs = effect.Music.IntervalMs
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
}
