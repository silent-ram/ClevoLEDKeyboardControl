namespace ColorfulLedKeyboard.Core;

/// <summary>界面布局度量。从 WinForms 设置窗迁入 Core，两个前端共享常量与 DPI 换算。</summary>
public static class UiMetrics
{
    public const int ContentWidth = 800;
    public const int LabelWidth = 150;
    public const int ControlLeft = 165;
    public const int RowHeight = 48;
    public const int ButtonHeight = 34;

    public static int ScaleForDpi(int logicalPixels, int dpi) =>
        Math.Max(1, (int)Math.Round(logicalPixels * Math.Max(96, dpi) / 96d));
}

/// <summary>音乐响应模式与 ComboBox 索引的双向映射。从 WinForms 设置窗迁入 Core，
/// 让映射逻辑可以脱离具体前端被测试。</summary>
public static class MusicResponseMapping
{
    public static int Index(bool beatDetectionEnabled) => beatDetectionEnabled ? 1 : 0;

    public static bool UsesBeatDetection(int selectedIndex) => selectedIndex == 1;
}
