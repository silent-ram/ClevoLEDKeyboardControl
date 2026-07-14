using ColorfulLedKeyboard.Core;

namespace ColorfulLedKeyboard.Tests;

public sealed class SettingsBackupRestoreTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), $"clevo-restore-{Guid.NewGuid():N}");

    [Fact]
    public void RestoreLastGoodReplacesCurrentSettings()
    {
        var path = Path.Combine(_root, "settings.json");
        var store = new SettingsStore(path);
        store.SaveLocal(new KeyboardSettings { Brightness = 20 });
        File.Copy(path, path + SettingsStore.LastGoodSuffix, overwrite: true);
        store.SaveLocal(new KeyboardSettings { Brightness = 90 });

        var result = store.RestoreLastGoodLocal();

        Assert.True(result.Success, result.Message);
        Assert.Equal(20, store.LoadLocal().Brightness);
    }

    [Fact]
    public void RestoreLastGoodLeavesCurrentSettingsWhenBackupIsInvalid()
    {
        var path = Path.Combine(_root, "settings.json");
        var store = new SettingsStore(path);
        store.SaveLocal(new KeyboardSettings { Brightness = 65 });
        File.WriteAllText(path + SettingsStore.LastGoodSuffix, "not-json");

        var result = store.RestoreLastGoodLocal();

        Assert.False(result.Success);
        Assert.Equal(65, store.LoadLocal().Brightness);
    }

    [Fact]
    public void RestoreLastGoodReportsMissingBackup()
    {
        var store = new SettingsStore(Path.Combine(_root, "settings.json"));

        var result = store.RestoreLastGoodLocal();

        Assert.False(result.Success);
        Assert.Contains("没有可用", result.Message, StringComparison.Ordinal);
    }

    public void Dispose()
    {
        if (Directory.Exists(_root)) Directory.Delete(_root, recursive: true);
    }
}
