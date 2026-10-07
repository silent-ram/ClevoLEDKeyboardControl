using ColorfulLedKeyboard.Core;

namespace ColorfulLedKeyboard.Tray.Wpf;

/// <summary>
/// 托盘程序双语文案表（中/英）。与安装器 InstallerStrings 同模式：
/// key 对齐测试防漏翻；语言取 UiState.Language（"auto" 跟随系统，"zh-CN"/"en-US" 手动）。
/// 切换语言后触发 LanguageChanged： MainWindow 重建页面、托盘菜单刷新。
/// </summary>
internal static class AppStrings
{
    public const string Auto = "auto";
    public const string ZhCn = "zh-CN";
    public const string EnUs = "en-US";

    private static readonly Dictionary<string, (string Zh, string En)> Table = new()
    {
        // ---- 导航 / 窗口壳 ----
        ["nav.overview"] = ("当前状态", "Overview"),
        ["nav.effect"] = ("灯效设置", "Lighting"),
        ["nav.music"] = ("音乐模式", "Music"),
        ["nav.automation"] = ("场景自动化", "Scene automation"),
        ["nav.feedback"] = ("事件反馈", "Event feedback"),
        ["nav.diagnostics"] = ("诊断与恢复", "Diagnostics"),
        ["nav.settings"] = ("软件设置", "App settings"),
        ["nav.about"] = ("关于", "About"),
        ["title.base"] = ("ClevoLEDKeyboardControl 设置", "ClevoLEDKeyboardControl Settings"),
        ["title.dirty"] = (" - 有未应用的更改", " - Unapplied changes"),
        ["save.dirty"] = ("● 有尚未保存的修改", "● Unsaved changes"),
        ["save.clean"] = ("✓ 设置已保存", "✓ Settings saved"),
        ["save.apply"] = ("保存并应用", "Save & apply"),
        ["save.revert"] = ("恢复修改", "Revert"),
        ["save.discarded"] = ("设置已保存", "Settings saved"),
        ["save.musicPresetDirty"] = ("● 音乐预设尚未保存，请先点击音乐页内的“保存预设修改”", "● Music preset has unsaved changes — save it on the music page first"),

        // ---- 通用按钮 / 提示 ----
        ["common.confirm"] = ("确定", "OK"),
        ["common.cancel"] = ("取消", "Cancel"),
        ["common.close"] = ("关闭", "Close"),
        ["common.yes"] = ("是", "Yes"),
        ["common.no"] = ("否", "No"),

        // ---- 托盘菜单 ----
        ["menu.enableLighting"] = ("启用灯效", "Enable lighting"),
        ["menu.settings"] = ("设置...", "Settings..."),
        ["menu.exit"] = ("退出", "Exit"),
        ["menu.now.lighting"] = ("当前：灯效模式 · {0}", "Now: lighting · {0}"),
        ["menu.now.music"] = ("当前：音乐模式", "Now: music mode"),
        ["menu.now.musicApp"] = ("当前：{0} · 音乐模式", "Now: {0} · music mode"),
        ["menu.musicApps"] = ("音乐程序", "Music apps"),
        ["menu.lightingApps"] = ("灯效程序", "Lighting apps"),
        ["menu.openAutomation"] = ("打开场景自动化设置...", "Open scene automation settings..."),
        ["menu.openMusic"] = ("打开音乐设置...", "Open music settings..."),
        ["menu.openFeedback"] = ("打开事件反馈设置...", "Open event feedback settings..."),
        ["menu.lightingMode"] = ("灯效模式", "Lighting mode"),
        ["menu.musicMode"] = ("音乐模式", "Music mode"),
        ["menu.off"] = ("关闭灯光", "Lights off"),
        ["menu.brightness.base"] = ("基础亮度 ({0}%)", "Base brightness ({0}%)"),
        ["menu.brightness.peak"] = ("音乐峰值亮度 ({0}%)", "Music peak brightness ({0}%)"),
        ["menu.restartService"] = ("重启服务", "Restart service"),
        ["menu.checkUpdate"] = ("检查更新", "Check for updates"),
        ["menu.about"] = ("关于...", "About..."),
        ["menu.musicPresetColors"] = ("音乐预设颜色", "Music preset colors"),
        ["menu.albumDominant"] = ("封面主色", "Album dominant color"),
        ["menu.albumPalette"] = ("封面配色", "Album palette"),
        ["menu.beat.beat"] = ("鼓点响应", "Beat response"),
        ["menu.beat.rhythm"] = ("节奏律动", "Rhythm groove"),
        ["menu.musicResponse"] = ("音乐响应（{0}）", "Music response ({0})"),
        ["menu.autostart.confirm"] = ("确定要将开机自启动切换为{0}吗?该设置立即生效,可能需要管理员权限。", "Switch launch-at-startup to {0}? This takes effect immediately and may require administrator approval."),
        ["state.on"] = ("开启", "on"),
        ["state.off"] = ("关闭", "off"),
        ["tooltip.lighting"] = ("灯效模式", "Lighting mode"),
        ["tooltip.music"] = ("音乐：{0}", "Music: {0}"),
        ["tooltip.musicDetecting"] = ("音乐：检测中…", "Music: detecting…"),
        ["tooltip.scene"] = ("场景：{0}", "Scene: {0}"),
        ["tooltip.lightOff"] = ("灯光已关闭", "Lights are off"),
        ["tooltip.update"] = ("可更新：v{0}", "Update available: v{0}"),
    };

    /// <summary>当前生效语言（解析后的 "zh-CN"/"en-US"）。</summary>
    public static string Current { get; private set; } = ZhCn;

    public static bool IsEnglish => Current == EnUs;

    public static IReadOnlyCollection<string> AllKeys => Table.Keys;

    /// <summary>语言切换事件：MainWindow 重建页面、托盘刷新菜单。</summary>
    public static event EventHandler? LanguageChanged;

    /// <summary>按 UiState.Language 初始化/切换。"auto" 按 Windows UI 语言解析。</summary>
    public static void Apply(string language)
    {
        var resolved = language == ZhCn || language == EnUs
            ? language
            : (System.Globalization.CultureInfo.CurrentUICulture.Name.StartsWith("zh", StringComparison.OrdinalIgnoreCase)
                ? ZhCn
                : EnUs);
        if (Current == resolved) return;
        Current = resolved;
        LanguageChanged?.Invoke(null, EventArgs.Empty);
    }

    public static string Get(string key)
    {
        return Table.TryGetValue(key, out var entry) ? (IsEnglish ? entry.En : entry.Zh) : key;
    }

    public static string Format(string key, params object[] args) => string.Format(Get(key), args);
}
