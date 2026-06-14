namespace ColorfulLedKeyboard.Core;

/// <summary>当前音频源的状态。决定灯效是按节拍跳还是压到底色。</summary>
public enum AudioSourceStatus
{
    /// <summary>设备正常、采样率合法、有有效样本流。</summary>
    Active,

    /// <summary>HFP/通话端点（采样率 ≤ 16000 Hz 且单声道）。loopback 不开启。</summary>
    Hfp,

    /// <summary>默认设备刚切换、capture 重建中（≤2 秒过渡窗口）。</summary>
    Switching,

    /// <summary>无可用 render 设备 / 已开 capture 但 1.5 秒无有效样本。</summary>
    Unavailable,
}
