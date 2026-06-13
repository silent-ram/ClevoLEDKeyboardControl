namespace ColorfulLedKeyboard.Core;

/// <summary>跨进程序列化用 DTO。Service 写入，Tray 读取。
/// JSON 中 Status 序列化为字符串（不是数字），未来加枚举值不破坏兼容性。</summary>
public sealed class AudioSourceStatusInfo
{
    public AudioSourceStatus Status { get; set; } = AudioSourceStatus.Unavailable;
    public string DeviceFriendlyName { get; set; } = "";
    public string DeviceId { get; set; } = "";
    public DateTimeOffset UpdatedAt { get; set; }
}
