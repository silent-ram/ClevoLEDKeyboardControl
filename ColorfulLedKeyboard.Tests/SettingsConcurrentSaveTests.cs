using ColorfulLedKeyboard.Core;

namespace ColorfulLedKeyboard.Tests;

public sealed class SettingsConcurrentSaveTests : IDisposable
{
    private readonly string _directory = Path.Combine(Path.GetTempPath(), $"clevo-concurrent-save-{Guid.NewGuid():N}");
    private string SettingsPath => Path.Combine(_directory, AppPaths.SettingsFileName);

    [Fact]
    public async Task ConcurrentSavesRemainValidAndLeaveNoTemporaryFiles()
    {
        var store = new SettingsStore(SettingsPath);
        store.SaveLocal(new KeyboardSettings());

        await Task.WhenAll(Enumerable.Range(1, 20).Select(value => Task.Run(() =>
            store.SaveLocal(new KeyboardSettings { Brightness = value }))));

        var loaded = store.LoadLocal();
        Assert.InRange(loaded.Brightness, 1, 20);
        Assert.Empty(Directory.GetFiles(_directory, "*.tmp"));
        Assert.True(SettingsStore.TryParse(File.ReadAllText(SettingsPath + SettingsStore.LastGoodSuffix), out _, out _));
    }

    public void Dispose()
    {
        if (Directory.Exists(_directory)) Directory.Delete(_directory, recursive: true);
    }
}
