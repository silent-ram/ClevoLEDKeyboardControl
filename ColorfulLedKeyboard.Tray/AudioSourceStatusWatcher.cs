using ColorfulLedKeyboard.Core;

namespace ColorfulLedKeyboard.Tray;

/// <summary>监听 audio-source-status.json 变化，含 50ms debounce。
/// 主要解决 Windows 一次写文件触发多次 Changed 事件的抖动。</summary>
internal sealed class AudioSourceStatusWatcher : IDisposable
{
    private readonly FileSystemWatcher _watcher;
    private readonly System.Windows.Forms.Timer _debounce;
    private readonly Action<AudioSourceStatusInfo?> _onUpdate;
    private bool _disposed;

    public AudioSourceStatusWatcher(Action<AudioSourceStatusInfo?> onUpdate)
    {
        _onUpdate = onUpdate;

        try { Directory.CreateDirectory(AppPaths.ProgramDataDirectory); }
        catch { /* watcher 自己会重试 */ }

        _watcher = new FileSystemWatcher(AppPaths.ProgramDataDirectory, AppPaths.AudioSourceStatusFileName)
        {
            NotifyFilter = NotifyFilters.LastWrite | NotifyFilters.CreationTime | NotifyFilters.Size,
            EnableRaisingEvents = true,
        };
        _watcher.Changed += (_, _) => ScheduleRefresh();
        _watcher.Created += (_, _) => ScheduleRefresh();

        _debounce = new System.Windows.Forms.Timer { Interval = 50 };
        _debounce.Tick += (_, _) =>
        {
            _debounce.Stop();
            Refresh();
        };
    }

    public void RefreshNow() => Refresh();

    private void ScheduleRefresh()
    {
        if (_disposed) return;
        try { _debounce.Stop(); _debounce.Start(); }
        catch { /* UI 已退出 */ }
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
    }
}
