namespace ColorfulLedKeyboard.Core;

public enum EffectType
{
    Static = 0,
    Rainbow = 1,
    Breathing = 2,
    Sequence = 3,
    Off = 4,
    // 5 = 已废弃的 Music 灯效（以前 EffectType.Music）。
    // 现在音乐由 OperatingMode.Music 表示，不再属于 EffectType。迁移逻辑见 KeyboardSettings.Normalize。
    Pulse = 6,
    Heartbeat = 7
}
