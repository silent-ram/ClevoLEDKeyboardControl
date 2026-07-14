using ColorfulLedKeyboard.Core;
using ColorfulLedKeyboard.Service;

namespace ColorfulLedKeyboard.Tests;

public sealed class AutomationBrightnessStatusTests
{
    [Fact]
    public void LightingStatusUsesActualBrightnessInsteadOfDefaultLimit()
    {
        var settings = new KeyboardSettings
        {
            Enabled = true,
            OperatingMode = OperatingMode.Lighting,
            Brightness = 70,
            OutputBrightnessLimit = 100
        }.Normalize();
        var status = new AutomationStatus();

        Worker.ApplyBrightnessStatus(settings, status);

        Assert.Equal(70, status.FinalBrightnessLimit);
        Assert.Equal("70%", status.BrightnessDisplay);
        Assert.Equal("灯效实际亮度", status.BrightnessDescription);
    }

    [Fact]
    public void MusicStatusShowsDynamicRangeCappedByRule()
    {
        var settings = new KeyboardSettings
        {
            Enabled = true,
            OperatingMode = OperatingMode.Music,
            OutputBrightnessLimit = 80,
            Effect = new LightingEffectSettings
            {
                Music = new MusicSettings { BaseBrightness = 25, PeakBrightness = 100 }
            }
        }.Normalize();
        var status = new AutomationStatus();

        Worker.ApplyBrightnessStatus(settings, status);

        Assert.Equal(80, status.FinalBrightnessLimit);
        Assert.Equal("25–80%", status.BrightnessDisplay);
        Assert.Equal("音乐动态范围 · 输出上限 80%", status.BrightnessDescription);
    }

    [Fact]
    public void IdleTurnOffIsReportedAsClosed()
    {
        var settings = new KeyboardSettings
        {
            Enabled = true,
            OperatingMode = OperatingMode.Lighting,
            OutputBrightnessLimit = 0
        }.Normalize();
        var status = new AutomationStatus { IdleOverrideActive = true };

        Worker.ApplyBrightnessStatus(settings, status);

        Assert.Equal(0, status.FinalBrightnessLimit);
        Assert.Equal("已关闭", status.BrightnessDisplay);
        Assert.Equal("空闲关灯覆盖", status.BrightnessDescription);
    }
}
