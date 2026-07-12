using ColorfulLedKeyboard.Core;

namespace ColorfulLedKeyboard.Tests;

public sealed class AutomationSettingsTests
{
    [Fact]
    public void Resolver_MusicAlwaysWinsAndForegroundMusicWinsWithinCategory()
    {
        var settings = new AutomationSettings
        {
            Enabled = true,
            MusicApplications =
            [
                new MusicApplicationRule { Name = "网易云", ProcessName = "cloudmusic" },
                new MusicApplicationRule { Name = "QQ音乐", ProcessName = "qqmusic" }
            ],
            LightingApplications =
            [
                new LightingApplicationRule { Name = "Word", ProcessNames = ["winword"] }
            ]
        }.Normalize();
        var states = new AudioApplicationState[]
        {
            new("cloudmusic", "", [10], 0.3f, true, false),
            new("qqmusic", "", [20], 0.2f, true, true)
        };

        var selected = AutomationResolver.Resolve(settings, DateTime.Now, "qqmusic", states);

        Assert.Equal("QQ音乐", selected.Music?.Name);
        Assert.Null(selected.Lighting);
    }

    [Fact]
    public void Resolver_BackgroundMusicUsesRuleOrderAndKeepsForegroundModifier()
    {
        var settings = new AutomationSettings
        {
            Enabled = true,
            MusicApplications =
            [
                new MusicApplicationRule { Name = "网易云", ProcessName = "cloudmusic" },
                new MusicApplicationRule { Name = "QQ音乐", ProcessName = "qqmusic" }
            ],
            LightingApplications =
            [
                new LightingApplicationRule { Name = "Word", ProcessNames = ["winword"] }
            ]
        }.Normalize();
        var states = new AudioApplicationState[]
        {
            new("cloudmusic", "", [10], 0.3f, true, false),
            new("qqmusic", "", [20], 0.2f, true, false)
        };

        var selected = AutomationResolver.Resolve(settings, DateTime.Now, "winword", states);

        Assert.Equal("网易云", selected.Music?.Name);
        Assert.Equal("Word", selected.Lighting?.Name);
    }
    [Fact]
    public void Conditions_CombineWeekdayOvernightAndAnyApplication()
    {
        var conditions = new SceneConditions
        {
            TimeEnabled = true,
            Start = "22:00",
            End = "02:00",
            Days = [DayOfWeek.Monday],
            ApplicationsEnabled = true,
            ProcessNames = ["game", "launcher.exe"]
        }.Normalize();

        Assert.True(conditions.Matches(new DateTime(2026, 7, 6, 23, 0, 0), "GAME.EXE"));
        Assert.True(conditions.Matches(new DateTime(2026, 7, 7, 1, 0, 0), "launcher"));
        Assert.False(conditions.Matches(new DateTime(2026, 7, 7, 3, 0, 0), "game"));
        Assert.False(conditions.Matches(new DateTime(2026, 7, 6, 23, 0, 0), "other"));
    }

    [Fact]
    public void Evaluator_UsesFirstValidMatchingRule()
    {
        var automation = new AutomationSettings
        {
            Enabled = true,
            Rules =
            [
                Rule("第一条", "game"),
                Rule("第二条", "game")
            ]
        }.Normalize();

        var result = SceneEvaluator.Evaluate(
            automation,
            new SceneEvaluationContext(new DateTime(2026, 7, 6, 20, 0, 0), "game.exe", true),
            _ => null);

        Assert.Equal("第一条", result.Rule?.Name);
    }

    [Fact]
    public void Evaluator_SkipsApplicationRuleWhenForegroundUnavailable()
    {
        var automation = new AutomationSettings
        {
            Enabled = true,
            Rules = [Rule("游戏", "game")]
        }.Normalize();

        var result = SceneEvaluator.Evaluate(
            automation,
            new SceneEvaluationContext(DateTime.Now, "game", false),
            _ => null);

        Assert.Null(result.Rule);
        Assert.Contains("应用检测不可用", result.InvalidReason);
    }

    [Fact]
    public void Evaluator_SkipsMissingPresetAndReportsReason()
    {
        var rule = Rule("游戏", "game");
        rule.Action.PresetId = "missing";
        var result = SceneEvaluator.Evaluate(
            new AutomationSettings { Enabled = true, Rules = [rule] }.Normalize(),
            new SceneEvaluationContext(DateTime.Now, "game", true),
            _ => "引用的预设不存在");

        Assert.Null(result.Rule);
        Assert.Contains("引用的预设不存在", result.InvalidReason);
    }

    [Fact]
    public void PresetStableId_SurvivesRenameAndClone()
    {
        var preset = new EffectPreset
        {
            Name = "旧名称",
            Effect = new LightingEffectSettings { Type = EffectType.Static }
        }.Normalize(EffectType.Static);
        var id = preset.Id;
        preset.Name = "新名称";
        preset.Normalize(EffectType.Static);

        var settings = new EffectPresetSettings { Static = [preset] }.Normalize();
        var clone = KeyboardSettings.CloneEffectPresets(settings);

        Assert.Equal(id, clone.Static[0].Id);
        Assert.Equal("新名称", clone.Static[0].Name);
    }

    [Fact]
    public void ManualMusicPlayerBinding_SurvivesRuntimeCloneWithoutAutomation()
    {
        var settings = new KeyboardSettings
        {
            OperatingMode = OperatingMode.Music,
            Automation = new AutomationSettings { Enabled = false }
        };
        settings.Effect.Music.PlayerBinding = new MusicPlayerBinding
        {
            Enabled = true,
            ProcessName = "QQMusic.exe",
            ExecutablePath = @"C:\QQMusic\QQMusic.exe",
            MediaSessionId = "QQMusic",
            ColorSource = MusicColorSource.AlbumPalette
        };

        var clone = settings.Normalize().CloneForRuntime();

        Assert.False(clone.Automation.Enabled);
        Assert.True(clone.Effect.Music.PlayerBinding.Enabled);
        Assert.Equal("QQMusic", clone.Effect.Music.PlayerBinding.ProcessName);
        Assert.Equal("QQMusic", clone.Effect.Music.PlayerBinding.MediaSessionId);
        Assert.Equal(MusicColorSource.AlbumPalette, clone.Effect.Music.PlayerBinding.ColorSource);
    }

    [Fact]
    public void ManualMusicPlayerBinding_UsesOnlyBoundMediaSession()
    {
        var state = new MediaPlaybackState
        {
            Sessions =
            [
                new MediaSessionState { SourceId = "cloudmusic", Title = "错误歌曲", IsPlaying = true },
                new MediaSessionState { SourceId = "QQMusic", Title = "正确歌曲", IsPlaying = true }
            ]
        };
        var binding = new MusicPlayerBinding
        {
            Enabled = true,
            ProcessName = "qqmusic",
            MediaSessionId = "QQMusic"
        };

        Assert.Equal("正确歌曲", state.Find(binding)?.Title);
    }

    [Fact]
    public void ManualMusicPlayerBinding_KeepsCachedColorDuringTrackTransition()
    {
        var state = new MediaPlaybackState
        {
            Sessions =
            [
                new MediaSessionState
                {
                    SourceId = "QQMusic.exe",
                    Title = "切歌中",
                    IsPlaying = false,
                    DominantColor = "#0296DA",
                    Palette = ["#0296DA", "#CCBAAE"]
                }
            ]
        };
        var binding = new MusicPlayerBinding
        {
            Enabled = true,
            ProcessName = "QQMusic",
            MediaSessionId = "QQMusic.exe",
            ColorSource = MusicColorSource.AlbumPalette
        };

        var media = state.Find(binding);

        Assert.Equal("切歌中", media?.Title);
        Assert.Equal(["#0296DA", "#CCBAAE"], media?.Palette);
    }

    private static SceneRule Rule(string name, string process) => new()
    {
        Name = name,
        Conditions = new SceneConditions
        {
            ApplicationsEnabled = true,
            ProcessNames = [process]
        },
        Action = new SceneAction
        {
            Target = SceneTargetKind.LightingPreset,
            LightingEffectType = EffectType.Static,
            PresetId = EffectPresetSettings.BuiltInId(EffectType.Static)
        }
    };
}
