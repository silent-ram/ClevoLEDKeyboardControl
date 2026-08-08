namespace ColorfulLedKeyboard.Core;

public sealed class LightingEffectSettings
{
    public EffectType Type { get; set; } = EffectType.Rainbow;

    public string Color { get; set; } = "#FF0000";

    public int Step { get; set; } = 3;

    public int IntervalMs { get; set; } = 40;

    public int PeriodMs { get; set; } = EffectPresetSettings.DefaultPeriodMs;

    public int MinimumBrightness { get; set; } = 0;

    public bool HardBlink { get; set; }

    public bool CustomSequenceColorsEnabled { get; set; }

    public List<SequenceColor> Sequence { get; set; } =
        CreateDefaultSequence();

    public MusicSettings Music { get; set; } = new();

    public LightingEffectSettings Normalize()
    {
        if (!Enum.IsDefined(Type))
        {
            Type = EffectType.Rainbow;
        }

        Color = NormalizeHex(Color, "#FF0000");
        Step = Math.Clamp(Step, 1, 20);
        IntervalMs = Math.Clamp(IntervalMs, 20, 500);
        PeriodMs = Math.Clamp(PeriodMs, 300, 30000);
        MinimumBrightness = Math.Clamp(MinimumBrightness, 0, 100);
        Sequence = NormalizeSequence(Sequence);
        Music ??= new MusicSettings();
        Music.Normalize();
        return this;
    }

    private static List<SequenceColor> NormalizeSequence(List<SequenceColor>? sequence)
    {
        var normalized = (sequence ?? [])
            .Select(item => item.Normalize())
            .Where(item => !string.IsNullOrWhiteSpace(item.Color))
            .ToList();

        if (normalized.Count > 0)
        {
            return normalized;
        }

        return CreateDefaultSequence();
    }

    private static List<SequenceColor> CreateDefaultSequence() =>
        EffectPresetSettings.DefaultSequenceColors.Select(color => new SequenceColor
        {
            Color = color,
            HoldMs = EffectPresetSettings.DefaultPeriodMs,
            TransitionMs = 0,
            Breathing = false
        }).ToList();

    internal static string NormalizeHex(string value, string fallback)
    {
        try
        {
            return RgbColor.FromHex(value).Hex;
        }
        catch (FormatException)
        {
            return fallback;
        }
    }
}

public sealed class SequenceColor
{
    public string Color { get; set; } = "#FF0000";

    public int HoldMs { get; set; } = EffectPresetSettings.DefaultPeriodMs;

    public int TransitionMs { get; set; } = 1200;

    public bool Breathing { get; set; } = true;

    public SequenceColor Normalize()
    {
        Color = LightingEffectSettings.NormalizeHex(Color, "#FF0000");
        HoldMs = Math.Clamp(HoldMs, 0, 30000);
        TransitionMs = Math.Clamp(TransitionMs, 0, 30000);
        return this;
    }
}
