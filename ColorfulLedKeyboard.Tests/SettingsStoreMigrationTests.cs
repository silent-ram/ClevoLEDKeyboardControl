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
          "Effect": { "Type": "Rainbow", "Color": "#FF0000" }
        }
        """;
        File.WriteAllText(_tempSettingsPath, newJson);

        var loaded = _store.Load();

        Assert.Equal(OperatingMode.Lighting, loaded.OperatingMode);
        Assert.Equal(EffectType.Rainbow, loaded.Effect.Type);
    }
}
