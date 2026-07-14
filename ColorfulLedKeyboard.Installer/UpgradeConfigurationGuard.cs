namespace ColorfulLedKeyboard.Installer;

/// <summary>
/// 在覆盖安装期间保护服务配置。快照既保存在内存中，也持久化到独立备份目录；
/// 新服务启动前若发现原文件缺失或发生任何变化，恢复升级前的精确字节内容。
/// </summary>
internal sealed class UpgradeConfigurationGuard
{
    private readonly string _settingsPath;
    private readonly string _backupDirectory;
    private readonly Func<DateTimeOffset> _utcNow;

    internal UpgradeConfigurationGuard(
        string settingsPath,
        string backupDirectory,
        Func<DateTimeOffset>? utcNow = null)
    {
        _settingsPath = settingsPath;
        _backupDirectory = backupDirectory;
        _utcNow = utcNow ?? (() => DateTimeOffset.UtcNow);
    }

    internal UpgradeConfigurationSnapshot? Capture()
    {
        if (!File.Exists(_settingsPath))
        {
            return null;
        }

        return Capture(ReadAllBytesWithRetry(_settingsPath));
    }

    internal UpgradeConfigurationSnapshot Capture(byte[] contents)
    {
        ArgumentNullException.ThrowIfNull(contents);
        Directory.CreateDirectory(_backupDirectory);
        var timestamp = _utcNow().ToString("yyyyMMdd-HHmmssfff");
        var backupPath = Path.Combine(_backupDirectory, $"settings.pre-upgrade-{timestamp}.bak");
        WriteAtomically(backupPath, contents);
        return new UpgradeConfigurationSnapshot(contents, backupPath);
    }

    internal bool RestoreIfChanged(UpgradeConfigurationSnapshot? snapshot)
    {
        if (snapshot is null)
        {
            return false;
        }

        if (File.Exists(_settingsPath))
        {
            var current = ReadAllBytesWithRetry(_settingsPath);
            if (current.AsSpan().SequenceEqual(snapshot.Contents))
            {
                return false;
            }
        }

        WriteAtomically(_settingsPath, snapshot.Contents);
        return true;
    }

    private static byte[] ReadAllBytesWithRetry(string path)
    {
        Exception? lastError = null;
        for (var attempt = 0; attempt < 4; attempt++)
        {
            try
            {
                return File.ReadAllBytes(path);
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                lastError = ex;
                if (attempt < 3) Thread.Sleep(100);
            }
        }

        throw new IOException($"无法读取升级前配置：{path}", lastError);
    }

    private static void WriteAtomically(string destination, byte[] contents)
    {
        var directory = Path.GetDirectoryName(destination)
            ?? throw new InvalidOperationException($"配置路径无效：{destination}");
        Directory.CreateDirectory(directory);
        var temporary = Path.Combine(directory, $".{Path.GetFileName(destination)}.{Guid.NewGuid():N}.tmp");
        try
        {
            File.WriteAllBytes(temporary, contents);
            File.Move(temporary, destination, overwrite: true);
        }
        finally
        {
            try { if (File.Exists(temporary)) File.Delete(temporary); } catch { }
        }
    }
}

internal sealed record UpgradeConfigurationSnapshot(byte[] Contents, string BackupPath);
