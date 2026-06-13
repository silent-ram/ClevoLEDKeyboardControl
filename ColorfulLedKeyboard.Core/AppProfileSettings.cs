namespace ColorfulLedKeyboard.Core;

public sealed class AppProfileSettings
{
    public bool Enabled { get; set; }

    public List<AppProfileRule> Rules { get; set; } = [];

    public AppProfileSettings Normalize()
    {
        Rules = (Rules ?? [])
            .Select(rule => rule.Normalize())
            .Where(rule => !string.IsNullOrWhiteSpace(rule.ProcessName))
            .ToList();

        return this;
    }
}

public sealed class AppProfileRule
{
    public string Name { get; set; } = "新场景";

    public string ProcessName { get; set; } = "";

    public bool Enabled { get; set; } = true;

    public bool AutoColorEnabled { get; set; } = true;

    public string IconColor { get; set; } = "#FFFFFF";

    public int Brightness { get; set; } = 70;

    public EffectType TargetEffect { get; set; } = EffectType.Static;

    public string ManualColor { get; set; } = "#FFFFFF";

    public AppProfileRule Normalize()
    {
        Name = string.IsNullOrWhiteSpace(Name) ? "新场景" : Name.Trim();
        ProcessName = NormalizeProcessName(ProcessName);
        IconColor = LightingEffectSettings.NormalizeHex(IconColor, "#FFFFFF");
        ManualColor = LightingEffectSettings.NormalizeHex(ManualColor, "#FFFFFF");
        Brightness = Math.Clamp(Brightness, 0, 100);
        if (TargetEffect is not EffectType.Static and not EffectType.Breathing)
        {
            TargetEffect = EffectType.Static;
        }
        return this;
    }

    public LightingEffectSettings BuildEffect()
    {
        return new LightingEffectSettings
        {
            Type = TargetEffect,
            Color = AutoColorEnabled ? IconColor : ManualColor,
            PeriodMs = EffectPresetSettings.DefaultPeriodMs,
            MinimumBrightness = 0,
            HardBlink = false
        }.Normalize();
    }

    public bool Matches(string processName)
    {
        return Enabled &&
            !string.IsNullOrWhiteSpace(ProcessName) &&
            string.Equals(ProcessName, NormalizeProcessName(processName), StringComparison.OrdinalIgnoreCase);
    }

    public static string NormalizeProcessName(string value)
    {
        var name = Path.GetFileNameWithoutExtension((value ?? string.Empty).Trim());
        return name;
    }
}
