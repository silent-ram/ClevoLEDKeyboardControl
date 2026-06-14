using ColorfulLedKeyboard.Core;

namespace ColorfulLedKeyboard.Tray;

/// <summary>监听 audio-source-status.json 变化，含 50ms debounce + 1 秒轮询兜底。
///
/// 为什么需要轮询兜底：
/// AudioSourceStatusFile.Write 用 File.Move(overwrite=true) 做原子替换。
/// FileSystemWatcher 在 Windows 上对 Move 的行为不稳：有时触发 Changed，有时触发
/// Renamed，新文件被覆盖式 rename 时甚至可能两个事件都不触发。订阅 Renamed 是必须的，
/// 但仍不够可靠。所以加一道 1 秒轮询兜底——读 mtime，变了就 Refresh，开销可忽略。</summary>
internal sealed class AudioSourceStatusWatcher : IDisposable
{
    private readonly FileSystemWatcher _watcher;
    private readonly System.Windows.Forms.Timer _debounce;
    private readonly System.Windows.Forms.Timer _poll;
    private readonly Action<AudioSourceStatusInfo?> _onUpdate;
    private DateTime _lastSeenMtime = DateTime.MinValue;
    private bool _disposed;

    public AudioSourceStatusWatcher(Action<AudioSourceStatusInfo?> onUpdate)
    {
        _onUpdate = onUpdate;

        try { Directory.CreateDirectory(AppPaths.ProgramDataDirectory); }
        catch { /* watcher 自己会重试 */ }

        _watcher = new FileSystemWatcher(AppPaths.ProgramDataDirectory, AppPaths.AudioSourceStatusFileName)
        {
            NotifyFilter = NotifyFilters.LastWrite | NotifyFilters.CreationTime | NotifyFilters.Size | NotifyFilters.FileName,
            EnableRaisingEvents = true,
        };
        _watcher.Changed += (_, _) => ScheduleRefresh();
        _watcher.Created += (_, _) => ScheduleRefresh();
        _watcher.Renamed += (_, _) => ScheduleRefresh();

        _debounce = new System.Windows.Forms.Timer { Interval = 50 };
        _debounce.Tick += (_, _) =>
        {
            _debounce.Stop();
            Refresh();
        };

        // 1 秒轮询兜底 mtime 变化
        _poll = new System.Windows.Forms.Timer { Interval = 1000 };
        _poll.Tick += (_, _) => CheckMtime();
        _poll.Start();
    }

    public void RefreshNow() => Refresh();

    private void ScheduleRefresh()
    {
        if (_disposed) return;
        try { _debounce.Stop(); _debounce.Start(); }
        catch { /* UI 已退出 */ }
    }

    private void CheckMtime()
    {
        if (_disposed) return;
        try
        {
            var fi = new FileInfo(AppPaths.AudioSourceStatusPath);
            if (!fi.Exists) return;
            var mtime = fi.LastWriteTimeUtc;
            if (mtime != _lastSeenMtime)
            {
                _lastSeenMtime = mtime;
                Refresh();
            }
        }
        catch { /* swallow */ }
    }

    private void Refresh()
    {
        if (_disposed) return;
        AudioSourceStatusInfo? info = null;
        try { info = AudioSourceStatusFile.Read(); } catch { }
        try { _onUpdate(info); } catch { }
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        try { _watcher.Dispose(); } catch { }
        try { _debounce.Stop(); _debounce.Dispose(); } catch { }
        try { _poll.Stop(); _poll.Dispose(); } catch { }
    }
}
