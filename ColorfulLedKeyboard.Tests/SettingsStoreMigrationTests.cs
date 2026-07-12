using ColorfulLedKeyboard.Core;

namespace ColorfulLedKeyboard.Tests;

public sealed class SettingsStoreMigrationTests : IDisposable
{
    private readonly string _tempSettingsPath;
    private readonly SettingsStore _store;

    public SettingsStoreMigrationTests()
    {
        _tempSettingsPath = Path.Combine(Path.GetTempPath(), $"settings-migration-{Guid.NewGuid():N}.json");
        _store = new SettingsStore(_tempSettingsPath);
    }

    public void Dispose()
    {
        if (File.Exists(_tempSettingsPath)) File.Delete(_tempSettingsPath);
        if (File.Exists(_tempSettingsPath + ".pre-automation-v2.bak"))
            File.Delete(_tempSettingsPath + ".pre-automation-v2.bak");
    }

    [Fact]
    public void Load_LegacyMusicJson_MigratesToOperatingModeMusicAndPreservesPresets()
    {
        var legacyJson = """
        {
          "Enabled": true,
          "Brightness": 75,
          "Mode": "Static",
          "Effect": {
            "Type": "Music",
            "Color": "#FF0000",
            "Music": {
              "PresetName": "通用",
              "BaseBrightness": 20,
              "PeakBrightness": 95,
              "CustomPresets": [
                { "Name": "我的预设1", "BaseBrightness": 10, "PeakBrightness": 80 },
                { "Name": "我的预设2", "BaseBrightness": 30, "PeakBrightness": 100 }
              ]
            }
          }
        }
        """;
        File.WriteAllText(_tempSettingsPath, legacyJson);

        var loaded = _store.Load();

        Assert.Equal(OperatingMode.Music, loaded.OperatingMode);
        Assert.Equal(EffectType.Static, loaded.Effect.Type);
        Assert.Equal("通用", loaded.Effect.Music.PresetName);
        Assert.Equal(20, loaded.Effect.Music.BaseBrightness);
        Assert.Equal(95, loaded.Effect.Music.PeakBrightness);
        Assert.Equal(2, loaded.Effect.Music.CustomPresets.Count);
        Assert.Equal("我的预设1", loaded.Effect.Music.CustomPresets[0].Name);
    }

    [Fact]
    public void Load_LegacyAppProfileMusicTarget_DowngradedToStatic()
    {
        var legacyJson = """
        {
          "Enabled": true,
          "Effect": { "Type": "Static", "Color": "#FF0000" },
          "AppProfiles": {
            "Enabled": true,
            "Rules": [
              { "Name": "音乐播放器", "ProcessName": "spotify", "TargetEffect": "Music" }
            ]
          }
        }
        """;
        File.WriteAllText(_tempSettingsPath, legacyJson);

        var loaded = _store.Load();

        Assert.Single(loaded.AppProfiles.Rules);
        Assert.Equal(EffectType.Static, loaded.AppProfiles.Rules[0].TargetEffect);
    }

    [Fact]
    public void Load_NewFormatJson_DoesNotTriggerMigration()
    {
        var newJson = """
        {
          "Enabled": true,
          "OperatingMode": "Lighting",
          "Effect": { "Type": "Rainbow", "Color": "#FF0000" },
          "Automation": { "Version": 2, "Enabled": false, "Rules": [] }
        }
        """;
        File.WriteAllText(_tempSettingsPath, newJson);

        var loaded = _store.Load();

        Assert.Equal(OperatingMode.Lighting, loaded.OperatingMode);
        Assert.Equal(EffectType.Rainbow, loaded.Effect.Type);
    }

    [Fact]
    public void Load_LegacyAutomation_MigratesOnceAndCreatesBackup()
    {
        var legacyJson = """
        {
          "Enabled": true,
          "Effect": { "Type": "Static", "Color": "#FF0000" },
          "IdleDim": { "Enabled": true, "AfterSeconds": 300, "Brightness": 12 },
          "AppProfiles": {
            "Enabled": true,
            "Rules": [
              { "Name": "游戏", "ProcessName": "game.exe", "Enabled": true,
                "Brightness": 80, "TargetEffect": "Static", "ManualColor": "#00FF00",
                "AutoColorEnabled": false }
            ]
          },
          "Schedule": {
            "Enabled": true,
            "Rules": [
              { "Name": "夜间", "Start": "23:00", "End": "07:00", "Enabled": true,
                "Brightness": 10, "Effect": { "Type": "Off" } }
            ]
          }
        }
        """;
        File.WriteAllText(_tempSettingsPath, legacyJson);

        var first = _store.Load();
        var second = _store.Load();

        Assert.True(first.Automation.Enabled);
        Assert.Single(first.Automation.LightingApplications);
        Assert.Single(first.Automation.ScheduleRules);
        Assert.Equal("游戏", first.Automation.LightingApplications[0].Name);
        Assert.Equal(SceneTargetKind.Off, first.Automation.ScheduleRules[0].Action.Target);
        Assert.Equal(12, first.IdleDim.Brightness);
        Assert.True(File.Exists(_tempSettingsPath + ".pre-automation-v2.bak"));
        Assert.Empty(second.Automation.Rules);
        Assert.Single(second.EffectPresets.Static);
    }

    [Fact]
    public void Load_AutomationV1_SplitsRulesIntoMusicLightingAndSchedule()
    {
        var json = """
        {
          "Effect": {
            "Type": "Static",
            "Music": { "CustomPresets": [ { "Id": "music-one", "Name": "播放器" } ] }
          },
          "Automation": {
            "Version": 1,
            "Enabled": true,
            "Rules": [
              { "Name": "音乐", "Conditions": { "ApplicationsEnabled": true, "ProcessNames": ["player"] },
                "Action": { "Target": "MusicPreset", "PresetId": "music-one" } },
              { "Name": "办公", "Conditions": { "ApplicationsEnabled": true, "ProcessNames": ["word"] },
                "Action": { "Target": "LightingPreset", "LightingEffectType": "Static", "PresetId": "builtin:lighting:static" } },
              { "Name": "夜间", "Conditions": { "TimeEnabled": true, "Start": "23:00", "End": "07:00" },
                "Action": { "Target": "Off" } }
            ]
          }
        }
        """;
        File.WriteAllText(_tempSettingsPath, json);

        var loaded = _store.Load();

        Assert.Empty(loaded.Automation.Rules);
        Assert.Equal("player", Assert.Single(loaded.Automation.MusicApplications).ProcessName);
        Assert.Equal("word", Assert.Single(loaded.Automation.LightingApplications).ProcessNames[0]);
        Assert.Equal(SceneTargetKind.Off, Assert.Single(loaded.Automation.ScheduleRules).Action.Target);
    }
}
