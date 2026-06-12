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
    public void LightingFrameGenerator_Rainbow_UsesCustomSequenceColorsWhenEnabled()
    {
        var settings = new KeyboardSettings
        {
            Brightness = 100,
            Effect = new LightingEffectSettings
            {
                Type = EffectType.Rainbow,
                CustomSequenceColorsEnabled = true,
                Sequence =
                [
                    new SequenceColor { Color = "#00FF00", HoldMs = 30000, TransitionMs = 0, Breathing = false },
                    new SequenceColor { Color = "#0000FF", HoldMs = 30000, TransitionMs = 0, Breathing = false }
                ]
            }
        }.Normalize();

        var color = new LightingFrameGenerator(settings).Next();

        Assert.Equal("#00FF00", color.Hex);
    }

    [Fact]
    public void LightingFrameGenerator_Rainbow_CustomSequence_ReachesEveryConfiguredColor()
    {
        var generator = new LightingFrameGenerator(new KeyboardSettings
        {
            Brightness = 100,
            Effect = new LightingEffectSettings
            {
                Type = EffectType.Rainbow,
                CustomSequenceColorsEnabled = true,
                Step = 3,
                IntervalMs = 40,
                Sequence =
                [
                    new SequenceColor { Color = "#FF0000", HoldMs = 650, TransitionMs = 0, Breathing = false },
                    new SequenceColor { Color = "#00FF00", HoldMs = 650, TransitionMs = 0, Breathing = false },
                    new SequenceColor { Color = "#0000FF", HoldMs = 650, TransitionMs = 0, Breathing = false },
                    new SequenceColor { Color = "#FFFFFF", HoldMs = 650, TransitionMs = 0, Breathing = false }
                ]
            }
        }.Normalize());

        var colors = new[] { 100, 750, 1400, 2050 }
            .Select(elapsedMs => generator.NextAtElapsed(100, elapsedMs).Hex)
            .ToArray();

        Assert.Equal(["#FF0000", "#00FF00", "#0000FF", "#FFFFFF"], colors);
    }

    [Fact]
    public void LightingFrameGenerator_Rainbow_CustomSequence_CanCycleManyColorsQuickly()
    {
        var sequence = Enumerable.Range(0, 48)
            .Select(index => new SequenceColor
            {
                Color = RgbColor.FromHsv(index * 360d / 48, 1, 1).Hex,
                HoldMs = 100,
                TransitionMs = 0,
                Breathing = false
            })
            .ToList();
        var generator = new LightingFrameGenerator(new KeyboardSettings
        {
            Brightness = 100,
            Effect = new LightingEffectSettings
            {
                Type = EffectType.Rainbow,
                CustomSequenceColorsEnabled = true,
                Step = 3,
                IntervalMs = 40,
                Sequence = sequence
            }
        }.Normalize());

        var sampled = new[] { 50, 1050, 2050, 3050, 4050 }
            .Select(elapsedMs => generator.NextAtElapsed(100, elapsedMs).Hex)
            .Distinct()
            .Count();

        Assert.True(sampled >= 5);
    }

    [Fact]
    public void LightingFrameGenerator_Sequence_UsesEachColorPeriod()
    {
        var generator = new LightingFrameGenerator(new KeyboardSettings
        {
            Brightness = 100,
            Effect = new LightingEffectSettings
            {
                Type = EffectType.Sequence,
                Step = 3,
                IntervalMs = 40,
                Sequence =
                [
                    new SequenceColor { Color = "#FF0000", HoldMs = 1200, TransitionMs = 0, Breathing = true },
                    new SequenceColor { Color = "#00FF00", HoldMs = 1200, TransitionMs = 0, Breathing = true },
                    new SequenceColor { Color = "#0000FF", HoldMs = 1200, TransitionMs = 0, Breathing = true },
                    new SequenceColor { Color = "#FFFFFF", HoldMs = 1200, TransitionMs = 0, Breathing = true }
                ]
            }
        }.Normalize());

        var colors = new[] { 600, 1800, 3000, 4200 }
            .Select(elapsedMs => generator.NextAtElapsed(100, elapsedMs).Hex)
            .ToArray();

        Assert.Equal(["#FF0000", "#00FF00", "#0000FF", "#FFFFFF"], colors);
    }

    [Fact]
    public void LightingFrameGenerator_Pulse_UsesRiseDecayAndRestPhases()
    {
        var generator = new LightingFrameGenerator(BuildPatternSettings(
            EffectType.Pulse,
            periodMs: 2000,
            ["#FF0000"]));

        var start = generator.NextAtElapsed(100, 0);
        var peak = generator.NextAtElapsed(100, 400);
        var decay = generator.NextAtElapsed(100, 1000);
        var rest = generator.NextAtElapsed(100, 1800);

        Assert.Equal(RgbColor.Black, start);
        Assert.Equal("#FF0000", peak.Hex);
        Assert.InRange(decay.R, (byte)1, (byte)254);
        Assert.Equal(RgbColor.Black, rest);
    }

    [Fact]
    public void LightingFrameGenerator_Heartbeat_UsesStrongWeakAndRestPhases()
    {
        var generator = new LightingFrameGenerator(BuildPatternSettings(
            EffectType.Heartbeat,
            periodMs: 1500,
            ["#FF0000"]));

        var strong = generator.NextAtElapsed(100, 187.5);
        var gap = generator.NextAtElapsed(100, 450);
        var weak = generator.NextAtElapsed(100, 712.5);
        var rest = generator.NextAtElapsed(100, 1200);

        Assert.Equal("#FF0000", strong.Hex);
        Assert.Equal(RgbColor.Black, gap);
        Assert.InRange(weak.R, (byte)160, (byte)170);
        Assert.Equal(RgbColor.Black, rest);
    }

    [Fact]
    public void LightingFrameGenerator_PatternModes_AdvanceColorAfterCompletePeriod()
    {
        var pulse = new LightingFrameGenerator(BuildPatternSettings(
            EffectType.Pulse,
            periodMs: 2000,
            ["#FF0000", "#00FF00", "#0000FF"]));
        var heartbeat = new LightingFrameGenerator(BuildPatternSettings(
            EffectType.Heartbeat,
            periodMs: 1500,
            ["#FF0000", "#00FF00", "#0000FF"]));

        Assert.Equal(["#FF0000", "#00FF00", "#0000FF"], new[] { 400d, 2400d, 4400d }
            .Select(elapsed => pulse.NextAtElapsed(100, elapsed).Hex)
            .ToArray());
        Assert.Equal(["#FF0000", "#00FF00", "#0000FF"], new[] { 187.5, 1687.5, 3187.5 }
            .Select(elapsed => heartbeat.NextAtElapsed(100, elapsed).Hex)
            .ToArray());
    }

    [Fact]
    public void LightingFrameGenerator_Next_AllowsBrightnessOverride()
    {
        var settings = new KeyboardSettings
        {
            Brightness = 0,
            Effect = new LightingEffectSettings
            {
                Type = EffectType.Static,
                Color = "#00FF00"
            }
        }.Normalize();

        var color = new LightingFrameGenerator(settings).Next(100);

        Assert.Equal("#00FF00", color.Hex);
    }

    [Fact]
    public void LightingFrameGenerator_RainbowCustomSequence_UsesConfiguredHoldDuration()
    {
        var slow = new LightingFrameGenerator(BuildRainbowSequenceSettings(step: 1, intervalMs: 160));
        var fast = new LightingFrameGenerator(BuildRainbowSequenceSettings(step: 10, intervalMs: 20));

        var slowFirst = slow.NextAtElapsed(100, 1199).Hex;
        var fastFirst = fast.NextAtElapsed(100, 1199).Hex;
        var slowSecond = slow.NextAtElapsed(100, 1200).Hex;
        var fastSecond = fast.NextAtElapsed(100, 1200).Hex;

        Assert.Equal("#FF0000", slowFirst);
        Assert.Equal("#FF0000", fastFirst);
        Assert.Equal("#0000FF", slowSecond);
        Assert.Equal("#0000FF", fastSecond);
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

    private static KeyboardSettings BuildRainbowSequenceSettings(int step, int intervalMs) =>
        new KeyboardSettings
        {
            Brightness = 100,
            Effect = new LightingEffectSettings
            {
                Type = EffectType.Rainbow,
                CustomSequenceColorsEnabled = true,
                Step = step,
                IntervalMs = intervalMs,
                Sequence =
                [
                    new SequenceColor { Color = "#FF0000", HoldMs = 1200, TransitionMs = 0, Breathing = false },
                    new SequenceColor { Color = "#0000FF", HoldMs = 1200, TransitionMs = 0, Breathing = false }
                ]
            }
        }.Normalize();

    private static KeyboardSettings BuildPatternSettings(EffectType type, int periodMs, IReadOnlyList<string> colors) =>
        new KeyboardSettings
        {
            Brightness = 100,
            Effect = new LightingEffectSettings
            {
                Type = type,
                PeriodMs = periodMs,
                Sequence = colors.Select(color => new SequenceColor
                {
                    Color = color,
                    HoldMs = periodMs,
                    TransitionMs = 0,
                    Breathing = false
                }).ToList()
            }
        }.Normalize();

    [Fact]
    public void AppProfileRule_Normalize_StripsExeAndClampsBrightness()
    {
        var rule = new AppProfileRule
        {
            ProcessName = "Spotify.exe",
            Brightness = 200,
            TargetEffect = EffectType.Sequence
        }.Normalize();

        Assert.Equal("Spotify", rule.ProcessName);
        Assert.Equal(100, rule.Brightness);
        Assert.Equal(EffectType.Static, rule.TargetEffect);
    }

    [Fact]
    public void AppProfileRule_Normalize_RejectsMusicAndUnsupportedEffects()
    {
        // Music 已不再是合法的 TargetEffect（由 OperatingMode 表达）。
        // 任何非 Static/Breathing 的 TargetEffect 都应被规范化回 Static。
        var rule = new AppProfileRule
        {
            ProcessName = "Spotify.exe",
            TargetEffect = (EffectType)5
        }.Normalize();

        Assert.Equal(EffectType.Static, rule.TargetEffect);
    }

    [Fact]
    public void LightingPresets_DoNotOverrideGlobalBrightness()
    {
        var settings = new KeyboardSettings { Brightness = 22 }.Normalize();

        LightingPresets.ApplyWarmWhite(settings);
        Assert.Equal(22, settings.Brightness);

        LightingPresets.ApplySoftRainbow(settings);
        Assert.Equal(22, settings.Brightness);

        LightingPresets.ApplyRedBluePulse(settings);
        Assert.Equal(22, settings.Brightness);
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
    public void MusicSettings_Normalize_RemovesCustomPresetsThatConflictWithBuiltIns()
    {
        var settings = new MusicSettings
        {
            CustomPresets =
            [
                new MusicPreset { Name = "通用" },
                new MusicPreset { Name = "My Preset" }
            ]
        }.Normalize();

        Assert.DoesNotContain(settings.CustomPresets, preset => preset.Name == "通用");
        Assert.Contains(settings.CustomPresets, preset => preset.Name == "My Preset");
    }

    [Fact]
    public void MusicSettings_ApplyPreset_SynchronizesLegacyLevelColorFlag()
    {
        var settings = new MusicSettings();
        var preset = new MusicPreset
        {
            Name = "Pulse",
            ResponseMode = MusicResponseMode.BrightnessPulse,
            Colors = ["#123456", "#654321"],
            FollowSystemVolume = false,
            EqEnabled = true,
            EqLowHz = 70,
            EqHighHz = 210
        };

        settings.ApplyPreset(preset);

        Assert.Equal("Pulse", settings.PresetName);
        Assert.Equal(MusicResponseMode.BrightnessPulse, settings.ResponseMode);
        Assert.False(settings.LevelColorEnabled);
        Assert.Equal(["#123456", "#654321"], settings.Colors);
        Assert.False(settings.FollowSystemVolume);
        Assert.True(settings.EqEnabled);
        Assert.Equal(70, settings.EqLowHz);
        Assert.Equal(210, settings.EqHighHz);
    }

    [Fact]
    public void MusicSettings_Normalize_BackfillsColorListFromLegacyLowHighColors()
    {
        var settings = new MusicSettings
        {
            LowColor = "#112233",
            HighColor = "#445566",
            Colors = []
        }.Normalize();

        Assert.Equal(["#112233", "#445566"], settings.Colors);
        Assert.Equal("#112233", settings.LowColor);
        Assert.Equal("#445566", settings.HighColor);
        Assert.False(settings.FollowSystemVolume);
    }

    [Fact]
    public void MusicSettings_Normalize_UsesUniversalColorsForFactoryDefaults()
    {
        var settings = new MusicSettings().Normalize();

        Assert.Equal(46, settings.Colors.Count);
        Assert.DoesNotContain("#404040", settings.Colors);
        Assert.DoesNotContain("#000000", settings.Colors);
        Assert.Equal("#FF0000", settings.LowColor);
        Assert.Equal("#B4FFD2", settings.HighColor);
    }

    [Fact]
    public void MusicSettings_BuiltInPresets_UseUniversalPreset()
    {
        var names = MusicSettings.BuiltInPresets.Select(preset => preset.Name).ToArray();

        Assert.Equal(["通用"], names);
        Assert.DoesNotContain("自定义", names);
        Assert.DoesNotContain("流行", names);
        Assert.DoesNotContain("摇滚", names);
        Assert.DoesNotContain("电子", names);
        Assert.DoesNotContain("经典", names);

        var universal = MusicSettings.BuiltInPresets.Single();
        Assert.Equal(MusicResponseMode.LevelColor, universal.ResponseMode);
        Assert.Equal(2.0, universal.Sensitivity);
        Assert.Equal(35, universal.AttackMs);
        Assert.Equal(80, universal.ReleaseMs);
        Assert.Equal(0, universal.NoiseGate);
        Assert.Equal(0.02, universal.BeatThreshold);
        Assert.False(universal.EqEnabled);
        Assert.Equal(30, universal.EqLowHz);
        Assert.Equal(5000, universal.EqHighHz);
        Assert.False(universal.FollowSystemVolume);
        Assert.Equal(25, universal.BaseBrightness);
        Assert.Equal(100, universal.PeakBrightness);
        Assert.Equal(46, universal.Colors.Count);
        Assert.DoesNotContain("#404040", universal.Colors);
        Assert.DoesNotContain("#000000", universal.Colors);
    }

    [Fact]
    public void MusicSettings_Normalize_MigratesLegacyBuiltInPresetNamesToUniversal()
    {
        foreach (var legacyName in new[] { "", "自定义", "流行", "摇滚", "电子", "经典" })
        {
            var settings = new MusicSettings { PresetName = legacyName }.Normalize();

            Assert.Equal("通用", settings.PresetName);
        }
    }

    [Fact]
    public void MusicPulseController_TriggersPulseAndAdvancesColors()
    {
        var settings = new MusicSettings
        {
            Sensitivity = 1,
            NoiseGate = 0,
            BeatThreshold = 0.2,
            PeakHoldMs = 20,
            ReleaseMs = 160,
            FollowSystemVolume = true
        }.Normalize();
        var controller = new MusicPulseController();
        var start = new DateTimeOffset(2026, 6, 12, 12, 0, 0, TimeSpan.Zero);

        var first = controller.Next(settings, audioLevel: 0.9, systemVolumeScalar: 1, colorCount: 3, start);
        var decay = controller.Next(settings, audioLevel: 0, systemVolumeScalar: 1, colorCount: 3, start.AddMilliseconds(180));
        var second = controller.Next(settings, audioLevel: 0.95, systemVolumeScalar: 1, colorCount: 3, start.AddMilliseconds(420));

        Assert.True(first.BeatTriggered);
        Assert.Equal(0, first.ColorIndex);
        Assert.True(first.Envelope > 0.9);
        Assert.False(decay.BeatTriggered);
        Assert.InRange(decay.Envelope, 0, first.Envelope);
        Assert.True(second.BeatTriggered);
        Assert.Equal(1, second.ColorIndex);
    }

    [Fact]
    public void MusicPulseController_FollowSystemVolumeCanMuteInput()
    {
        var settings = new MusicSettings
        {
            Sensitivity = 1,
            NoiseGate = 0,
            BeatThreshold = 0.2,
            FollowSystemVolume = true
        }.Normalize();
        var controller = new MusicPulseController();
        var now = new DateTimeOffset(2026, 6, 12, 12, 0, 0, TimeSpan.Zero);

        var muted = controller.Next(settings, audioLevel: 1, systemVolumeScalar: 0, colorCount: 2, now);

        Assert.False(muted.BeatTriggered);
        Assert.Equal(0, muted.Envelope);
    }

    [Fact]
    public void SpotifySettings_DefaultsAlbumColorSourceToWindowsMediaSession()
    {
        var settings = new SpotifySettings().Normalize();

        Assert.Equal(AlbumColorSource.WindowsMediaSession, settings.AlbumColorSource);
    }

    [Fact]
    public void MusicSettings_Normalize_KeepsBaseBrightnessIndependentFromGlobalBrightness()
    {
        var settings = new KeyboardSettings
        {
            Brightness = 70,
            OperatingMode = OperatingMode.Music,
            Effect = new LightingEffectSettings
            {
                Type = EffectType.Static,
                Music = new MusicSettings
                {
                    BaseBrightness = 5,
                    PeakBrightness = 100
                }
            }
        }.Normalize();

        Assert.Equal(70, settings.Brightness);
        Assert.Equal(5, settings.Effect.Music.BaseBrightness);
    }

    [Fact]
    public void KeyboardSettings_CloneForRuntime_PreservesAdvancedSettings()
    {
        var settings = new KeyboardSettings
        {
            OperatingMode = OperatingMode.Music,
            Effect = new LightingEffectSettings
            {
                Type = EffectType.Static,
                CustomSequenceColorsEnabled = true,
                Music = new MusicSettings
                {
                    PresetName = "Custom",
                    ResponseMode = MusicResponseMode.BrightnessPulse,
                    Colors = ["#123456", "#654321", "#ABCDEF"],
                    FollowSystemVolume = false,
                    CustomPresets = [new MusicPreset { Name = "Custom", NoiseGate = 0.2, Colors = ["#010203", "#040506"], FollowSystemVolume = false }]
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
        Assert.True(clone.Effect.CustomSequenceColorsEnabled);
        Assert.Equal("Custom", clone.Effect.Music.PresetName);
        Assert.Equal(["#123456", "#654321", "#ABCDEF"], clone.Effect.Music.Colors);
        Assert.False(clone.Effect.Music.FollowSystemVolume);
        Assert.Single(clone.Effect.Music.CustomPresets);
        Assert.Equal(["#010203", "#040506"], clone.Effect.Music.CustomPresets[0].Colors);
        Assert.False(clone.Effect.Music.CustomPresets[0].FollowSystemVolume);
        Assert.True(clone.AppProfiles.Enabled);
        Assert.Equal("#00FF00", clone.AppProfiles.Rules[0].ManualColor);
    }

    [Fact]
    public void KeyboardSettings_Normalize_CreatesEffectMemoryDefaults()
    {
        var settings = new KeyboardSettings
        {
            SavedEffects = null!
        }.Normalize();

        Assert.NotNull(settings.SavedEffects);
        Assert.Equal(EffectType.Static, settings.SavedEffects.Static.Type);
        Assert.Equal(EffectType.Rainbow, settings.SavedEffects.Rainbow.Type);
        Assert.True(settings.SavedEffects.Rainbow.CustomSequenceColorsEnabled);
        Assert.Equal(EffectType.Sequence, settings.SavedEffects.Sequence.Type);
        Assert.Equal(EffectType.Pulse, settings.SavedEffects.Pulse.Type);
        Assert.Equal(EffectType.Heartbeat, settings.SavedEffects.Heartbeat.Type);
    }

    [Fact]
    public void KeyboardSettings_CloneForRuntime_PreservesEffectMemory()
    {
        var settings = new KeyboardSettings
        {
            SavedEffects = new EffectMemorySettings
            {
                Rainbow = new LightingEffectSettings
                {
                    Type = EffectType.Rainbow,
                    CustomSequenceColorsEnabled = true,
                    Sequence =
                    [
                        new SequenceColor { Color = "#123456", HoldMs = 900, TransitionMs = 0, Breathing = false },
                        new SequenceColor { Color = "#ABCDEF", HoldMs = 900, TransitionMs = 0, Breathing = false }
                    ]
                },
                Sequence = new LightingEffectSettings
                {
                    Type = EffectType.Sequence,
                    PeriodMs = 1800,
                    Sequence =
                    [
                        new SequenceColor { Color = "#112233", HoldMs = 1800, TransitionMs = 0, Breathing = true },
                        new SequenceColor { Color = "#445566", HoldMs = 1800, TransitionMs = 0, Breathing = true }
                    ]
                },
                Pulse = new LightingEffectSettings
                {
                    Type = EffectType.Pulse,
                    PeriodMs = 2100,
                    Sequence = [new SequenceColor { Color = "#00AAFF", HoldMs = 2100, TransitionMs = 0 }]
                },
                Heartbeat = new LightingEffectSettings
                {
                    Type = EffectType.Heartbeat,
                    PeriodMs = 1600,
                    Sequence = [new SequenceColor { Color = "#FF2050", HoldMs = 1600, TransitionMs = 0 }]
                }
            }
        }.Normalize();

        var clone = settings.CloneForRuntime();

        Assert.Equal("#123456", clone.SavedEffects.Rainbow.Sequence[0].Color);
        Assert.Equal(900, clone.SavedEffects.Rainbow.Sequence[0].HoldMs);
        Assert.Equal("#112233", clone.SavedEffects.Sequence.Sequence[0].Color);
        Assert.Equal(1800, clone.SavedEffects.Sequence.Sequence[0].HoldMs);
        Assert.Equal(2100, clone.SavedEffects.Pulse.PeriodMs);
        Assert.Equal("#00AAFF", clone.SavedEffects.Pulse.Sequence[0].Color);
        Assert.Equal(1600, clone.SavedEffects.Heartbeat.PeriodMs);
        Assert.Equal("#FF2050", clone.SavedEffects.Heartbeat.Sequence[0].Color);
    }

    [Fact]
    public void EffectPresetSettings_SoftwareDefaults_UseThreeSecondPeriod()
    {
        var rainbow = EffectPresetSettings.CreateSoftwareDefault(EffectType.Rainbow);
        var breathing = EffectPresetSettings.CreateSoftwareDefault(EffectType.Breathing);
        var sequence = EffectPresetSettings.CreateSoftwareDefault(EffectType.Sequence);
        var pulse = EffectPresetSettings.CreateSoftwareDefault(EffectType.Pulse);
        var heartbeat = EffectPresetSettings.CreateSoftwareDefault(EffectType.Heartbeat);

        Assert.All(rainbow.Sequence, item => Assert.Equal(3000, item.HoldMs));
        Assert.Equal(3000, breathing.PeriodMs);
        Assert.Equal(3000, sequence.PeriodMs);
        Assert.All(sequence.Sequence, item => Assert.Equal(3000, item.HoldMs));
        Assert.Equal(2000, pulse.PeriodMs);
        Assert.Equal(["#00FFFF", "#0060FF", "#8000FF"], pulse.Sequence.Select(item => item.Color).ToArray());
        Assert.Equal(1500, heartbeat.PeriodMs);
        Assert.Equal(["#FF0000", "#FF4080"], heartbeat.Sequence.Select(item => item.Color).ToArray());
    }

    [Fact]
    public void KeyboardSettings_CloneForRuntime_PreservesEffectPresets()
    {
        var settings = new KeyboardSettings
        {
            EffectPresets = new EffectPresetSettings
            {
                Rainbow =
                [
                    new EffectPreset
                    {
                        Name = "Long Loop",
                        Effect = new LightingEffectSettings
                        {
                            Type = EffectType.Rainbow,
                            CustomSequenceColorsEnabled = true,
                            Sequence =
                            [
                                new SequenceColor { Color = "#123456", HoldMs = 3456, TransitionMs = 0, Breathing = false }
                            ]
                        }
                    }
                ],
                Pulse =
                [
                    new EffectPreset
                    {
                        Name = "Cool Pulse",
                        Effect = new LightingEffectSettings
                        {
                            Type = EffectType.Pulse,
                            PeriodMs = 2400,
                            Sequence = [new SequenceColor { Color = "#00FFFF", HoldMs = 2400, TransitionMs = 0 }]
                        }
                    }
                ]
            }
        }.Normalize();

        var clone = settings.CloneForRuntime();

        Assert.Single(clone.EffectPresets.Rainbow);
        Assert.Equal("Long Loop", clone.EffectPresets.Rainbow[0].Name);
        Assert.Equal("#123456", clone.EffectPresets.Rainbow[0].Effect.Sequence[0].Color);
        Assert.Equal(3456, clone.EffectPresets.Rainbow[0].Effect.Sequence[0].HoldMs);
        Assert.Single(clone.EffectPresets.Pulse);
        Assert.Equal("Cool Pulse", clone.EffectPresets.Pulse[0].Name);
        Assert.Equal(2400, clone.EffectPresets.Pulse[0].Effect.PeriodMs);
    }

    [Fact]
    public void EffectPresetSettings_Normalize_DeduplicatesAndLimitsPerMode()
    {
        var presets = new EffectPresetSettings
        {
            Rainbow = Enumerable.Range(0, 20)
                .Select(index => new EffectPreset
                {
                    Name = index % 2 == 0 ? "Same" : $"Preset {index}",
                    Effect = new LightingEffectSettings { Type = EffectType.Static }
                })
                .ToList()
        }.Normalize();

        Assert.True(presets.Rainbow.Count <= EffectPresetSettings.MaxPresetsPerMode);
        Assert.Equal(presets.Rainbow.Count, presets.Rainbow.Select(item => item.Name.ToLowerInvariant()).Distinct().Count());
        Assert.All(presets.Rainbow, item => Assert.Equal(EffectType.Rainbow, item.Effect.Type));
    }

    [Fact]
    public void KeyboardSettings_EffectMemory_CanRestoreCustomSequenceAfterPreset()
    {
        var settings = new KeyboardSettings
        {
            Effect = new LightingEffectSettings
            {
                Type = EffectType.Rainbow,
                CustomSequenceColorsEnabled = true,
                Sequence =
                [
                    new SequenceColor { Color = "#123456", HoldMs = 777, TransitionMs = 0, Breathing = false },
                    new SequenceColor { Color = "#654321", HoldMs = 777, TransitionMs = 0, Breathing = false }
                ]
            }
        }.Normalize();

        settings.SavedEffects.Rainbow = KeyboardSettings.CloneEffect(settings.Effect);
        LightingPresets.ApplyWarmWhite(settings);

        settings.Effect = KeyboardSettings.CloneEffect(settings.SavedEffects.Rainbow);
        settings.Normalize();

        Assert.Equal(EffectType.Rainbow, settings.Effect.Type);
        Assert.Equal("#123456", settings.Effect.Sequence[0].Color);
        Assert.Equal(777, settings.Effect.Sequence[0].HoldMs);
    }
}
