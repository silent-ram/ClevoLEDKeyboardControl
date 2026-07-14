using ColorfulLedKeyboard.Installer;
using System.Text;

namespace ColorfulLedKeyboard.Tests;

public sealed class UpgradeConfigurationGuardTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), $"clevo-upgrade-{Guid.NewGuid():N}");

    [Fact]
    public void CaptureCreatesIndependentExactBackup()
    {
        var settingsPath = CreateSettings("{\"Brightness\":70,\"Name\":\"用户配置\"}");
        var guard = CreateGuard(settingsPath);

        var snapshot = guard.Capture();

        Assert.NotNull(snapshot);
        Assert.True(File.Exists(snapshot.BackupPath));
        Assert.Equal(File.ReadAllBytes(settingsPath), File.ReadAllBytes(snapshot.BackupPath));
        Assert.StartsWith(Path.Combine(_root, "Backups"), snapshot.BackupPath, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void ServicePayloadCanBeCapturedWhenSettingsFileCannotBeRead()
    {
        var settingsPath = Path.Combine(_root, "Data", "settings.json");
        var servicePayload = Encoding.UTF8.GetBytes("{\"Brightness\":42,\"OperatingMode\":1}");
        var guard = CreateGuard(settingsPath);

        var snapshot = guard.Capture(servicePayload);
        var restored = guard.RestoreIfChanged(snapshot);

        Assert.True(restored);
        Assert.Equal(servicePayload, File.ReadAllBytes(settingsPath));
    }

    [Fact]
    public void RestoreReplacesDefaultsWrittenDuringUpgrade()
    {
        var original = "{\"Brightness\":70,\"Automation\":{\"MusicApplications\":[{\"Name\":\"QQ音乐\"}]}}";
        var settingsPath = CreateSettings(original);
        var guard = CreateGuard(settingsPath);
        var snapshot = guard.Capture();
        File.WriteAllText(settingsPath, "{\"Brightness\":100}", Encoding.UTF8);

        var restored = guard.RestoreIfChanged(snapshot);

        Assert.True(restored);
        Assert.Equal(original, File.ReadAllText(settingsPath, Encoding.UTF8));
    }

    [Fact]
    public void RestoreRecreatesConfigurationDeletedDuringUpgrade()
    {
        var settingsPath = CreateSettings("{\"Brightness\":35}");
        var guard = CreateGuard(settingsPath);
        var snapshot = guard.Capture();
        File.Delete(settingsPath);

        var restored = guard.RestoreIfChanged(snapshot);

        Assert.True(restored);
        Assert.Equal("{\"Brightness\":35}", File.ReadAllText(settingsPath, Encoding.UTF8));
    }

    [Fact]
    public void UnchangedConfigurationIsNotRewritten()
    {
        var settingsPath = CreateSettings("{\"Brightness\":55}");
        var guard = CreateGuard(settingsPath);
        var snapshot = guard.Capture();
        var lastWrite = File.GetLastWriteTimeUtc(settingsPath);

        var restored = guard.RestoreIfChanged(snapshot);

        Assert.False(restored);
        Assert.Equal(lastWrite, File.GetLastWriteTimeUtc(settingsPath));
    }

    [Fact]
    public void FreshInstallWithoutSettingsDoesNothing()
    {
        var settingsPath = Path.Combine(_root, "Data", "settings.json");
        var guard = CreateGuard(settingsPath);

        var snapshot = guard.Capture();
        var restored = guard.RestoreIfChanged(snapshot);

        Assert.Null(snapshot);
        Assert.False(restored);
        Assert.False(File.Exists(settingsPath));
    }

    private UpgradeConfigurationGuard CreateGuard(string settingsPath) =>
        new(settingsPath, Path.Combine(_root, "Backups"), () => new DateTimeOffset(2026, 7, 14, 12, 0, 0, TimeSpan.Zero));

    private string CreateSettings(string contents)
    {
        var path = Path.Combine(_root, "Data", "settings.json");
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllText(path, contents, Encoding.UTF8);
        return path;
    }

    public void Dispose()
    {
        if (Directory.Exists(_root)) Directory.Delete(_root, recursive: true);
    }
}
