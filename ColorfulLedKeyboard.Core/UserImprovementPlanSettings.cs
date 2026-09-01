namespace ColorfulLedKeyboard.Core;

/// <summary>
/// 用户可选的改进计划设置。默认开启，旧版本配置缺少此字段时也会保持开启。
/// </summary>
public sealed class UserImprovementPlanSettings
{
    public bool Enabled { get; set; } = true;

    public UserImprovementPlanSettings Normalize() => this;
}
