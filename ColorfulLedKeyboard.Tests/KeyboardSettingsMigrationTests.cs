using ColorfulLedKeyboard.Core;

namespace ColorfulLedKeyboard.Tests;

public sealed class KeyboardSettingsMigrationTests
{
    [Fact]
    public void Normalize_DefaultOperatingMode_IsLighting()
    {
        var settings = new KeyboardSettings();
        settings.Normalize();
        Assert.Equal(OperatingMode.Lighting, settings.OperatingMode);
    }

    [Fact]
    public void Normalize_InvalidEffectTypeValueFive_RestoresStaticAndSetsMusicMode()
    {
        var settings = new KeyboardSettings
        {
            Effect = new LightingEffectSettings { Type = (EffectType)5 }
        };
        settings.Normalize(migrateLegacyMode: true);

        Assert.Equal(OperatingMode.Music, settings.OperatingMode);
        Assert.Equal(EffectType.Static, settings.Effect.Type);
    }

    [Fact]
    public void Normalize_PreservesMusicSubsettingsDuringMigration()
    {
        var settings = new KeyboardSettings
        {
            Effect = new LightingEffectSettings
            {
                Type = (EffectType)5,
                Music = new MusicSettings
                {
                    PresetName = "我的预设",
                    BaseBrightness = 30,
                    PeakBrightness = 90,
                    CustomPresets =
                    [
                        new MusicPreset { Name = "自定义A", BaseBrightness = 20, PeakBrightness = 80 }
                    ]
                }
            }
        };
        settings.Normalize(migrateLegacyMode: true);

        Assert.Equal("我的预设", settings.Effect.Music.PresetName);
        Assert.Equal(30, settings.Effect.Music.BaseBrightness);
        Assert.Equal(90, settings.Effect.Music.PeakBrightness);
        Assert.Single(settings.Effect.Music.CustomPresets);
        Assert.Equal("自定义A", settings.Effect.Music.CustomPresets[0].Name);
    }

    [Fact]
    public void Normalize_DefaultLastUsedLightingEffectIsStatic()
    {
        var settings = new KeyboardSettings();
        settings.Normalize();
        Assert.Equal(EffectType.Static, settings.SavedEffects.LastUsedLightingEffect);
    }

    [Fact]
    public void Normalize_AppProfileTargetEffectMusicLegacyValue_DowngradedToStatic()
    {
        var settings = new KeyboardSettings
        {
            AppProfiles = new AppProfileSettings
            {
                Rules =
                [
                    new AppProfileRule
                    {
                        Name = "测试",
                        ProcessName = "test.exe",
                        TargetEffect = (EffectType)5
                    }
                ]
            }
        };
        settings.Normalize(migrateLegacyMode: true);

        Assert.Equal(EffectType.Static, settings.AppProfiles.Rules[0].TargetEffect);
    }

    [Fact]
    public void Normalize_LastUsedLightingEffectOff_FallsBackToStatic()
    {
        var settings = new KeyboardSettings
        {
            SavedEffects = new EffectMemorySettings
            {
                LastUsedLightingEffect = EffectType.Off
            }
        };
        settings.Normalize();

        Assert.Equal(EffectType.Static, settings.SavedEffects.LastUsedLightingEffect);
    }

    [Fact]
    public void Normalize_LastUsedLightingEffectInvalidValue_FallsBackToStatic()
    {
        var settings = new KeyboardSettings
        {
            SavedEffects = new EffectMemorySettings
            {
                LastUsedLightingEffect = (EffectType)5
            }
        };
        settings.Normalize();

        Assert.Equal(EffectType.Static, settings.SavedEffects.LastUsedLightingEffect);
    }

    [Fact]
    public void Normalize_UnknownEffectTypeOtherThanFive_DoesNotForceMusicMode()
    {
        var settings = new KeyboardSettings
        {
            Effect = new LightingEffectSettings { Type = (EffectType)99 }
        };
        settings.Normalize(migrateLegacyMode: true);

        Assert.Equal(OperatingMode.Lighting, settings.OperatingMode);
        Assert.Equal(EffectType.Static, settings.Effect.Type);
    }

    [Fact]
    public void CloneForRuntime_PreservesOperatingModeAndLastUsedLightingEffect()
    {
        var settings = new KeyboardSettings
        {
            OperatingMode = OperatingMode.Music,
            SavedEffects = new EffectMemorySettings
            {
                LastUsedLightingEffect = EffectType.Heartbeat
            }
        };
        settings.Normalize();

        var clone = settings.CloneForRuntime();

        Assert.Equal(OperatingMode.Music, clone.OperatingMode);
        Assert.Equal(EffectType.Heartbeat, clone.SavedEffects.LastUsedLightingEffect);
    }
}
