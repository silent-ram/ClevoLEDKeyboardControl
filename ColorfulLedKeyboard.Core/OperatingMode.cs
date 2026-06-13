namespace ColorfulLedKeyboard.Core;

/// <summary>
/// 顶层运行模式：表示用户当前希望键盘走灯效路径还是音乐响应路径。
/// 与 <see cref="EffectType"/> 是不同维度——前者是"模式"，后者是"灯效"。
/// 当 <see cref="Music"/> 时 Worker 走 RunMusicAsync；当 <see cref="Lighting"/> 时走 RunEffectAsync。
/// </summary>
public enum OperatingMode
{
    /// <summary>灯效模式（默认）：键盘按 EffectType 显示静态色/呼吸/RGB 循环等动画。</summary>
    Lighting = 0,

    /// <summary>音乐模式：键盘根据系统音频电平动态变化，参数由 EffectSettings.Music 配置。</summary>
    Music = 1
}
