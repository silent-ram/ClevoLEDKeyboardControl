namespace ColorfulLedKeyboard.Core;

public enum KeyboardMode
{
    Static = 0,
    Rainbow = 1,
    Breathing = 2,
    Sequence = 3,
    Off = 4,
    // 5 = 已废弃的 Music 模式（以前 KeyboardMode.Music）。
    // 现在音乐由 OperatingMode.Music 表示。迁移逻辑见 KeyboardSettings.Normalize。
    Pulse = 6,
    Heartbeat = 7
}
