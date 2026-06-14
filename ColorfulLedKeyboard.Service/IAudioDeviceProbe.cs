namespace ColorfulLedKeyboard.Service;

/// <summary>把"如何枚举默认 render 设备"抽出来，方便单测脱离 NAudio。
/// 生产实现 MMDeviceProbe 包 NAudio MMDeviceEnumerator；测试用 FakeAudioDeviceProbe。</summary>
internal interface IAudioDeviceProbe
{
    /// <summary>当前默认 render 设备的快照。null = 无可用设备。</summary>
    DeviceSnapshot? GetDefaultRenderDevice();

    /// <summary>按设备 id 拿快照。null = 设备已消失或无效。</summary>
    DeviceSnapshot? GetDevice(string id);
}

/// <summary>设备快照：仅包含 Provider 状态机所需字段。
/// SampleRate ≤ 16000 且 Channels == 1 → 判 HFP。</summary>
internal sealed record DeviceSnapshot(string Id, string FriendlyName, int SampleRate, int Channels);
