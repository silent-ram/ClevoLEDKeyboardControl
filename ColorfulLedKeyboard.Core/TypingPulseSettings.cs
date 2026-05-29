namespace ColorfulLedKeyboard.Core;

public sealed class TypingPulseSettings
{
    public bool Enabled { get; set; }

    public int BaseBrightness { get; set; } = 50;

    public int PeakBrightness { get; set; } = 100;

    public int HoldMs { get; set; } = 120;

    public int FadeMs { get; set; } = 650;

    public TypingPulseSettings Normalize()
    {
        BaseBrightness = Math.Clamp(BaseBrightness, 0, 100);
        PeakBrightness = Math.Clamp(PeakBrightness, BaseBrightness, 100);
        HoldMs = Math.Clamp(HoldMs, 20, 2000);
        FadeMs = Math.Clamp(FadeMs, 50, 5000);
        return this;
    }
}
