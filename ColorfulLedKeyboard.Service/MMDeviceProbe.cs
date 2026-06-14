using NAudio.CoreAudioApi;
using NAudio.CoreAudioApi.Interfaces;

namespace ColorfulLedKeyboard.Service;

/// <summary>NAudio 实现的 IAudioDeviceProbe，封装 MMDeviceEnumerator 与
/// IMMNotificationClient 通知。Provider 通过 SetCallback 注册回调，所有 NAudio 异常被吞下。</summary>
internal sealed class MMDeviceProbe : IAudioDeviceProbe, IMMNotificationClient, IDisposable
{
    private readonly object _lock = new();
    private readonly MMDeviceEnumerator _enumerator = new();
    private MMDevice? _currentDevice;
    private Action<string>? _onDefaultDeviceChanged;
    private bool _registered;
    private bool _disposed;

    public MMDeviceProbe()
    {
        try
        {
            _enumerator.RegisterEndpointNotificationCallback(this);
            _registered = true;
        }
        catch
        {
            _registered = false;
        }
    }

    public void SetCallback(Action<string> onDefaultDeviceChanged)
    {
        _onDefaultDeviceChanged = onDefaultDeviceChanged;
    }

    public DeviceSnapshot? GetDefaultRenderDevice()
    {
        if (_disposed) return null;
        try
        {
            var device = _enumerator.GetDefaultAudioEndpoint(DataFlow.Render, Role.Multimedia);
            return CacheAndSnapshot(device);
        }
        catch
        {
            return null;
        }
    }

    public DeviceSnapshot? GetDevice(string id)
    {
        if (_disposed || string.IsNullOrEmpty(id)) return null;
        try
        {
            var device = _enumerator.GetDevice(id);
            return CacheAndSnapshot(device);
        }
        catch
        {
            return null;
        }
    }

    public MMDevice? GetCurrentMMDevice()
    {
        lock (_lock) return _currentDevice;
    }

    private DeviceSnapshot? CacheAndSnapshot(MMDevice device)
    {
        if (device is null) return null;

        int sampleRate = 0;
        int channels = 0;
        try
        {
            var mix = device.AudioClient.MixFormat;
            sampleRate = mix.SampleRate;
            channels = mix.Channels;
        }
        catch
        {
            // 拿不到 MixFormat 的设备，按非 HFP 处理
        }

        var snapshot = new DeviceSnapshot(device.ID, device.FriendlyName, sampleRate, channels);

        lock (_lock)
        {
            if (_currentDevice is { } previous && !ReferenceEquals(previous, device))
            {
                try { previous.Dispose(); } catch { }
            }
            _currentDevice = device;
        }

        return snapshot;
    }

    public void OnDefaultDeviceChanged(DataFlow flow, Role role, string defaultDeviceId)
    {
        if (_disposed) return;
        if (flow != DataFlow.Render) return;
        if (role != Role.Multimedia) return;

        // 关键：COM 回调线程上调 MMDeviceEnumerator 任何 API 都会 STA 死锁。
        // 把整个 Provider 回调链扔到 ThreadPool，本回调仅触发 + 立刻返回。
        var callback = _onDefaultDeviceChanged;
        if (callback is null) return;

        var snapshot = defaultDeviceId;
        System.Threading.ThreadPool.QueueUserWorkItem(_ =>
        {
            try { callback(snapshot); }
            catch { /* 别让 ThreadPool 线程异常崩进程 */ }
        });
    }

    public void OnDeviceAdded(string deviceId) { }
    public void OnDeviceRemoved(string deviceId) { }
    public void OnDeviceStateChanged(string deviceId, DeviceState newState) { }
    public void OnPropertyValueChanged(string pwstrDeviceId, PropertyKey key) { }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;

        if (_registered)
        {
            try { _enumerator.UnregisterEndpointNotificationCallback(this); } catch { }
        }

        lock (_lock)
        {
            try { _currentDevice?.Dispose(); } catch { }
            _currentDevice = null;
        }

        try { _enumerator.Dispose(); } catch { }
    }
}
