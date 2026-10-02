namespace ColorfulLedKeyboard.Core;

/// <summary>灯效等价判定。从 WinForms 设置窗迁入 Core，供两个前端与测试共享。</summary>
public static class EffectComparison
{
    public static bool AreEquivalentEffects(LightingEffectSettings left, LightingEffectSettings right)
    {
        left = KeyboardSettings.CloneEffect(left).Normalize();
        right = KeyboardSettings.CloneEffect(right).Normalize();
        if (left.Type != right.Type)
        {
            return false;
        }

        if (left.Type == EffectType.Static)
        {
            return string.Equals(left.Color, right.Color, StringComparison.OrdinalIgnoreCase);
        }

        if (left.Type == EffectType.Breathing)
        {
            return string.Equals(left.Color, right.Color, StringComparison.OrdinalIgnoreCase) &&
                left.PeriodMs == right.PeriodMs &&
                left.MinimumBrightness == right.MinimumBrightness &&
                left.HardBlink == right.HardBlink;
        }

        if (left.PeriodMs != right.PeriodMs)
        {
            return false;
        }

        if (left.CustomSequenceColorsEnabled != right.CustomSequenceColorsEnabled ||
            left.Sequence.Count != right.Sequence.Count)
        {
            return false;
        }

        for (var i = 0; i < left.Sequence.Count; i++)
        {
            var leftItem = left.Sequence[i];
            var rightItem = right.Sequence[i];
            if (!string.Equals(leftItem.Color, rightItem.Color, StringComparison.OrdinalIgnoreCase) ||
                leftItem.HoldMs != rightItem.HoldMs ||
                leftItem.TransitionMs != rightItem.TransitionMs ||
                leftItem.Breathing != rightItem.Breathing)
            {
                return false;
            }
        }

        return true;
    }
}
