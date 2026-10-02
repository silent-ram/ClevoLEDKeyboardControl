namespace ColorfulLedKeyboard.Core;

/// <summary>
/// MusicSettings 和 MusicPreset 共享的可调音频参数 + 颜色字段。
/// 用 interface 让 MusicSettingsNormalizer 同时归一化两个类型，避免重复 clamp 逻辑。
/// 类型本身保持原样（不引入继承），只声明实现该接口。
/// </summary>
internal interface IMusicTunable
{
    MusicResponseMode ResponseMode { get; set; }
    string LowColor { get; set; }
    string HighColor { get; set; }
    List<string> Colors { get; set; }
    double Sensitivity { get; set; }
    int AttackMs { get; set; }
    int ReleaseMs { get; set; }
    int BaseBrightness { get; set; }
    int PeakBrightness { get; set; }
    int IntervalMs { get; set; }
    double NoiseGate { get; set; }
    double BeatThreshold { get; set; }
    int PeakHoldMs { get; set; }
    bool FollowSystemVolume { get; set; }
    bool EqEnabled { get; set; }
    int EqLowHz { get; set; }
    int EqHighHz { get; set; }
}

/// <summary>
/// 共享的范围常量 + 归一化逻辑。MusicSettings.Normalize / MusicPreset.Normalize 都委托给此处，
/// 之后再各自处理独有字段（PresetName/CustomPresets 仅 MusicSettings 有；Name 仅 MusicPreset 有）。
/// </summary>
internal static class MusicSettingsNormalizer
{
    public const double SensitivityMin = 0.5;
    public const double SensitivityMax = 4.0;
    public const int AttackMsMin = 10;
    public const int AttackMsMax = 1000;
    public const int ReleaseMsMin = 20;
    public const int ReleaseMsMax = 3000;
    public const int BaseBrightnessMin = 0;
    public const int BaseBrightnessMax = 100;
    public const int PeakBrightnessMax = 100;
    public const int IntervalMsMin = 15;
    public const int IntervalMsMax = 200;
    public const double NoiseGateMin = 0;
    public const double NoiseGateMax = 0.5;
    public const double BeatThresholdMin = 0;
    public const double BeatThresholdMax = 1.0;
    public const double BeatThresholdAlgorithmScale = 0.10;
    public const int PeakHoldMsMin = 0;
    public const int PeakHoldMsMax = 500;
    public const int EqLowHzMin = 20;
    public const int EqLowHzMax = 1000;
    public const int EqHighHzMax = 16000;

    public const string FactoryLowColor = "#0040FF";
    public const string FactoryHighColor = "#FF0040";

    public static double ToAlgorithmBeatThreshold(double beatThreshold) =>
        Math.Clamp(beatThreshold, BeatThresholdMin, BeatThresholdMax) * BeatThresholdAlgorithmScale;

    public static bool IsNormalized(IMusicTunable target)
    {
        return Enum.IsDefined(target.ResponseMode) &&
            target.Colors is { Count: > 0 } &&
            target.Sensitivity is >= SensitivityMin and <= SensitivityMax &&
            target.AttackMs is >= AttackMsMin and <= AttackMsMax &&
            target.ReleaseMs is >= ReleaseMsMin and <= ReleaseMsMax &&
            target.BaseBrightness is >= BaseBrightnessMin and <= BaseBrightnessMax &&
            target.PeakBrightness >= target.BaseBrightness && target.PeakBrightness <= PeakBrightnessMax &&
            target.IntervalMs is >= IntervalMsMin and <= IntervalMsMax &&
            target.NoiseGate is >= NoiseGateMin and <= NoiseGateMax &&
            target.BeatThreshold is >= BeatThresholdMin and <= BeatThresholdMax &&
            target.PeakHoldMs is >= PeakHoldMsMin and <= PeakHoldMsMax &&
            target.EqLowHz is >= EqLowHzMin and <= EqLowHzMax &&
            target.EqHighHz >= target.EqLowHz + 10 && target.EqHighHz <= EqHighHzMax;
    }

    public static void Normalize(IMusicTunable target, Func<List<string>, string, string, List<string>> colorNormalizer,
        MusicResponseMode invalidResponseModeFallback)
    {
        if (!Enum.IsDefined(target.ResponseMode))
        {
            target.ResponseMode = invalidResponseModeFallback;
        }

        target.LowColor = LightingEffectSettings.NormalizeHex(target.LowColor, FactoryLowColor);
        target.HighColor = LightingEffectSettings.NormalizeHex(target.HighColor, FactoryHighColor);
        target.Colors = colorNormalizer(target.Colors, target.LowColor, target.HighColor);
        target.LowColor = target.Colors[0];
        target.HighColor = target.Colors[^1];

        target.Sensitivity = Math.Clamp(target.Sensitivity, SensitivityMin, SensitivityMax);
        target.AttackMs = Math.Clamp(target.AttackMs, AttackMsMin, AttackMsMax);
        target.ReleaseMs = Math.Clamp(target.ReleaseMs, ReleaseMsMin, ReleaseMsMax);
        target.BaseBrightness = Math.Clamp(target.BaseBrightness, BaseBrightnessMin, BaseBrightnessMax);
        target.PeakBrightness = Math.Clamp(target.PeakBrightness, target.BaseBrightness, PeakBrightnessMax);
        target.IntervalMs = Math.Clamp(target.IntervalMs, IntervalMsMin, IntervalMsMax);
        target.NoiseGate = Math.Clamp(target.NoiseGate, NoiseGateMin, NoiseGateMax);
        target.BeatThreshold = Math.Clamp(target.BeatThreshold, BeatThresholdMin, BeatThresholdMax);
        target.PeakHoldMs = Math.Clamp(target.PeakHoldMs, PeakHoldMsMin, PeakHoldMsMax);
        target.EqLowHz = Math.Clamp(target.EqLowHz, EqLowHzMin, EqLowHzMax);
        target.EqHighHz = Math.Clamp(target.EqHighHz, target.EqLowHz + 10, EqHighHzMax);
    }
}
