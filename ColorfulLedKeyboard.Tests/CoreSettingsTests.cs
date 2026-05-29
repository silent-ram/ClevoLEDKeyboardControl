using ColorfulLedKeyboard.Core;

namespace ColorfulLedKeyboard.Tests;

public sealed class CoreSettingsTests
{
    [Fact]
    public void RgbColor_FromHsv_UsesContinuousWheel()
    {
        Assert.Equal("#FF0000", RgbColor.FromHsv(0, 1, 1).Hex);
        Assert.Equal("#FFFF00", RgbColor.FromHsv(60, 1, 1).Hex);
        Assert.Equal("#00FF00", RgbColor.FromHsv(120, 1, 1).Hex);
        Assert.Equal("#00FFFF", RgbColor.FromHsv(180, 1, 1).Hex);
        Assert.Equal("#0000FF", RgbColor.FromHsv(240, 1, 1).Hex);
        Assert.Equal("#FF00FF", RgbColor.FromHsv(300, 1, 1).Hex);
        Assert.Equal("#FF0000", RgbColor.FromHsv(360, 1, 1).Hex);
    }

    [Fact]
    public void LightingFrameGenerator_Rainbow_DoesNotRestartAtRedForEachGenerator()
    {
        var settings = new KeyboardSettings
        {
            Brightness = 100,
            Effect = new LightingEffectSettings
            {
                Type = EffectType.Rainbow,
                Step = 3,
                IntervalMs = 40
            }
        }.Normalize();

        var first = new LightingFrameGenerator(settings).Next();
        Thread.Sleep(80);
        var second = new LightingFrameGenerator(settings).Next();

        Assert.NotEqual(first, second);
    }

    [Fact]
    public void AppProfileRule_BuildEffect_UsesManualColorWhenAutoColorDisabled()
    {
        var rule = new AppProfileRule
        {
            ProcessName = "spotify",
            AutoColorEnabled = false,
            IconColor = "#FFFFFF",
            ManualColor = "#00FF00",
            TargetEffect = EffectType.Static
        }.Normalize();

        var effect = rule.BuildEffect();

        Assert.Equal(EffectType.Static, effect.Type);
        Assert.Equal("#00FF00", effect.Color);
    }

    [Fact]
    public void AppProfileRule_Normalize_StripsExeAndClampsBrightness()
    {
        var rule = new AppProfileRule
        {
            ProcessName = "Spotify.exe",
            Brightness = 200,
            TargetEffect = EffectType.Music
        }.Normalize();

        Assert.Equal("Spotify", rule.ProcessName);
        Assert.Equal(100, rule.Brightness);
        Assert.Equal(EffectType.Static, rule.TargetEffect);
    }

    [Fact]
    public void MusicSettings_Normalize_DeduplicatesAndLimitsCustomPresets()
    {
        var settings = new MusicSettings
        {
            CustomPresets = Enumerable.Range(0, 12)
                .Select(index => new MusicPreset { Name = index % 2 == 0 ? "Same" : $"Preset {index}" })
                .ToList()
        }.Normalize();

        Assert.True(settings.CustomPresets.Count <= 8);
        Assert.Equal(settings.CustomPresets.Count, settings.CustomPresets.Select(item => item.Name.ToLowerInvariant()).Distinct().Count());
    }

    [Fact]
    public void MusicSettings_ApplyPreset_SynchronizesLegacyLevelColorFlag()
    {
        var settings = new MusicSettings();
        var preset = new MusicPreset
        {
            Name = "Pulse",
            ResponseMode = MusicResponseMode.BrightnessPulse
        };

        settings.ApplyPreset(preset);

        Assert.Equal("Pulse", settings.PresetName);
        Assert.Equal(MusicResponseMode.BrightnessPulse, settings.ResponseMode);
        Assert.False(settings.LevelColorEnabled);
    }

    [Fact]
    public void KeyboardSettings_CloneForRuntime_PreservesAdvancedSettings()
    {
        var settings = new KeyboardSettings
        {
            Effect = new LightingEffectSettings
            {
                Type = EffectType.Music,
                Music = new MusicSettings
                {
                    PresetName = "Custom",
                    ResponseMode = MusicResponseMode.BrightnessPulse,
                    CustomPresets = [new MusicPreset { Name = "Custom", NoiseGate = 0.2 }]
                }
            },
            TypingPulse = new TypingPulseSettings
            {
                Enabled = true,
                BaseBrightness = 30,
                PeakBrightness = 90
            },
            AppProfiles = new AppProfileSettings
            {
                Enabled = true,
                Rules =
                [
                    new AppProfileRule
                    {
                        ProcessName = "QQ",
                        AutoColorEnabled = false,
                        ManualColor = "#00FF00"
                    }
                ]
            }
        }.Normalize();

        var clone = settings.CloneForRuntime();

        Assert.True(clone.TypingPulse.Enabled);
        Assert.Equal("Custom", clone.Effect.Music.PresetName);
        Assert.Single(clone.Effect.Music.CustomPresets);
        Assert.True(clone.AppProfiles.Enabled);
        Assert.Equal("#00FF00", clone.AppProfiles.Rules[0].ManualColor);
    }
}
