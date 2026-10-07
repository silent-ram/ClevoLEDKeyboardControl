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

        // ---- 灯效设置页 ----
        ["eff.preset.default"] = ("软件默认配置", "Software default"),
        ["eff.preset.unsaved"] = ("当前设置（未保存为预设）", "Current settings (not saved as a preset)"),
        ["eff.type.static"] = ("固定颜色", "Static color"),
        ["eff.type.rainbow"] = ("RGB 循环", "RGB cycle"),
        ["eff.type.breathing"] = ("单色呼吸", "Breathing"),
        ["eff.type.sequence"] = ("循环呼吸", "Color sequence"),
        ["eff.type.pulse"] = ("脉冲", "Pulse"),
        ["eff.type.heartbeat"] = ("心跳", "Heartbeat"),
        ["eff.mode.lighting"] = ("灯效模式", "Effect mode"),
        ["eff.mode.music"] = ("音乐模式", "Music mode"),
        ["eff.mode.off"] = ("关闭", "Off"),
        ["eff.brightness"] = ("亮度", "Brightness"),
        ["eff.period.breathing"] = ("呼吸周期", "Breathing period"),
        ["eff.period.minBrightness"] = ("最低亮度", "Minimum brightness"),
        ["eff.hardBlink"] = ("硬闪烁", "Hard blink"),
        ["eff.customColors"] = ("自定义颜色", "Custom colors"),
        ["eff.preset.saveChanges"] = ("保存灯效预设修改", "Save preset changes"),
        ["eff.preset.saveAs"] = ("新建/另存为", "New / save as"),
        ["eff.preset.delete"] = ("删除预设", "Delete preset"),
        ["eff.section.sequenceColors"] = ("自定义循环颜色", "Custom sequence colors"),
        ["eff.card.mode"] = ("模式", "Mode"),
        ["eff.row.effect"] = ("当前效果", "Effect"),
        ["eff.card.params"] = ("灯效参数", "Effect parameters"),
        ["eff.card.presets"] = ("配置预设", "Presets"),
        ["eff.row.preset"] = ("当前预设", "Preset"),
        ["eff.row.presetName"] = ("预设名称", "Preset name"),
        ["eff.hold"] = ("停留时长", "Dwell time"),
        ["eff.period.pulse"] = ("脉冲周期", "Pulse period"),
        ["eff.period.heartbeat"] = ("心跳周期", "Heartbeat period"),
        ["eff.section.breathingColors"] = ("循环呼吸颜色", "Sequence colors"),
        ["eff.section.pulseColors"] = ("脉冲颜色", "Pulse colors"),
        ["eff.section.heartbeatColors"] = ("心跳颜色", "Heartbeat colors"),
        ["eff.hint.off"] = ("关闭模式不会显示灯效参数。", "Effect parameters are hidden in lights-off mode."),
        ["eff.hint.music"] = ("音乐模式由音乐页配置，灯效参数已禁用。", "Music mode is configured on the music page; effect parameters are disabled."),
        ["eff.summary.none"] = ("还没有选择颜色。", "No colors selected yet."),
        ["eff.summary.breathing"] = ("已选择 {0} 个颜色；每个颜色呼吸 {1} ms，整轮约 {2}。", "{0} colors; each breathes for {1} ms, a full cycle takes about {2}."),
        ["eff.summary.pulse"] = ("已选择 {0} 个颜色；每个颜色脉冲 {1} ms，整轮约 {2}。", "{0} colors; each pulses for {1} ms, a full cycle takes about {2}."),
        ["eff.summary.heartbeat"] = ("已选择 {0} 个颜色；每个颜色完成一组心跳 {1} ms，整轮约 {2}。", "{0} colors; each heartbeat group takes {1} ms, a full cycle takes about {2}."),
        ["eff.summary.hold"] = ("已选择 {0} 个颜色；每个颜色停留约 {1} ms，整轮约 {2}。", "{0} colors; each dwells for about {1} ms, a full cycle takes about {2}."),
        ["eff.seconds"] = ("{0:0.#} 秒", "{0:0.#} s"),
        ["eff.preset.readonly"] = ("软件默认配置不能被修改；请使用“新建/另存为”创建一个自定义预设。", "The software default preset cannot be modified; use “New / save as” to create a custom preset."),
        ["eff.preset.promptName"] = ("新建/另存为", "New / save as"),
        ["eff.preset.nameLabel"] = ("预设名称", "Preset name"),
        ["eff.preset.emptyName"] = ("请输入预设名称。", "Please enter a preset name."),
        ["eff.preset.emptyName2"] = ("请先输入预设名称。", "Enter a preset name first."),
        ["eff.preset.reserved"] = ("自定义预设不能使用“软件默认配置”这个名称。", "Custom presets cannot use the software default preset name."),
        ["eff.preset.overwrite"] = ("预设“{0}”已存在，是否覆盖？", "Preset “{0}” already exists. Overwrite?"),
        ["eff.preset.limit"] = ("每个模式最多保存 {0} 个预设。", "Each mode can store at most {0} presets."),
        ["eff.preset.confirmDelete"] = ("确定删除预设“{0}”？", "Delete preset “{0}”?"),

        // ---- 音乐模式页 ----
        ["mus.source.detecting"] = ("当前音频源：检测中…", "Audio source: detecting…"),
        ["mus.source.switching"] = ("当前音频源：{0}（切换中…）", "Audio source: {0} (switching…)"),
        ["mus.source.hfp"] = ("当前音频源：{0}（蓝牙通话模式，暂不可用）", "Audio source: {0} (Bluetooth HFP, unavailable)"),
        ["mus.source.ok"] = ("当前音频源：{0}", "Audio source: {0}"),
        ["mus.bind"] = ("绑定正在播放的程序", "Bind the playing app"),
        ["mus.unbind"] = ("取消绑定", "Unbind"),
        ["mus.color.preset"] = ("使用音乐预设颜色", "Music preset colors"),
        ["mus.color.dominant"] = ("使用歌曲封面主色", "Album dominant color"),
        ["mus.color.palette"] = ("使用歌曲封面配色", "Album palette"),
        ["mus.coverHint"] = ("封面来源依赖服务端的专辑封面采集；未获取到封面时回退音乐预设颜色。", "Album colors rely on the service collecting artwork; falls back to music preset colors when no artwork is found."),
        ["mus.session.auto"] = ("自动匹配播放器", "Auto-match player"),
        ["mus.preset.saveChanges"] = ("保存预设修改", "Save preset changes"),
        ["mus.preset.saveAs"] = ("新建/另存为", "New / save as"),
        ["mus.preset.delete"] = ("删除预设", "Delete preset"),
        ["mus.response.rhythm"] = ("节奏律动", "Rhythm groove"),
        ["mus.response.beat"] = ("鼓点响应", "Beat response"),
        ["mus.customColors"] = ("自定义颜色", "Custom colors"),
        ["mus.baseBrightness"] = ("基础亮度", "Base brightness"),
        ["mus.peakBrightness"] = ("峰值亮度", "Peak brightness"),
        ["mus.followVolume"] = ("跟随 Windows 系统音量", "Follow Windows system volume"),
        ["mus.advanced"] = ("显示高级参数", "Show advanced parameters"),
        ["mus.noiseGate"] = ("噪声门", "Noise gate"),
        ["mus.beatThreshold"] = ("节拍阈值", "Beat threshold"),
        ["mus.systemMix"] = ("未绑定播放器时，允许系统混音频段分析", "Allow system-mix band analysis when no player is bound"),
        ["mus.eqLow"] = ("低频参考", "Low-band reference"),
        ["mus.eqHigh"] = ("高频参考", "High-band reference"),
        ["mus.sensitivity"] = ("灵敏度", "Sensitivity"),
        ["mus.attack"] = ("响应速度", "Attack"),
        ["mus.release"] = ("衰减速度", "Release"),
        ["mus.card.playback"] = ("播放器与当前配色", "Player & current colors"),
        ["mus.row.colorSource"] = ("键盘颜色来源", "Keyboard color source"),
        ["mus.row.coverSource"] = ("歌曲封面来源", "Cover source"),
        ["mus.card.preset"] = ("音乐预设与响应", "Music preset & response"),
        ["mus.row.preset"] = ("音乐预设", "Music preset"),
        ["mus.row.presetName"] = ("预设名称", "Preset name"),
        ["mus.row.response"] = ("音乐响应", "Music response"),
        ["mus.section.beatColors"] = ("节拍颜色", "Beat colors"),
        ["mus.card.advanced"] = ("高级音乐参数", "Advanced music parameters"),
        ["mus.block.builtin"] = ("当前内置音乐预设已被修改。请先使用“新建/另存为”保存为自定义预设。", "The built-in music preset has been modified. Use “New / save as” to save it as a custom preset first."),
        ["mus.block.dirty"] = ("当前音乐预设有尚未保存的修改。请先点击“保存预设修改”。", "The music preset has unsaved changes. Click “Save preset changes” first."),
        ["mus.binding.off"] = ("未绑定：音乐模式使用系统混音和音乐预设颜色。", "Not bound: music mode uses the system mix and music preset colors."),
        ["mus.session.off"] = ("媒体会话：未启用播放器绑定。", "Media session: player binding is off."),
        ["mus.colors.preset"] = ("当前使用音乐预设配色。", "Using music preset colors."),
        ["mus.session.preset"] = ("媒体会话：颜色来源为音乐预设，封面匹配暂不参与输出。", "Media session: color source is the music preset; cover matching is not in the output path."),
        ["mus.match.auto"] = ("自动匹配成功", "Auto-match succeeded"),
        ["mus.match.manual"] = ("手动匹配成功", "Manual match succeeded"),
        ["mus.cover.got"] = ("已获取封面（{0} 色）", "Artwork acquired ({0} colors)"),
        ["mus.cover.none"] = ("未获取封面，正在使用预设颜色", "No artwork; using preset colors"),
        ["mus.session.matched"] = ("媒体会话：{0} → {1}；{2}；{3}", "Media session: {0} → {1}; {2}; {3}"),
        ["mus.playing"] = ("正在播放", "Playing"),
        ["mus.transition"] = ("切歌/暂停过渡", "Track change / pause transition"),
        ["mus.session.none"] = ("媒体会话：尚未检测到 Windows 播放器会话，请先播放歌曲。", "Media session: no Windows player session detected — play a song first."),
        ["mus.session.multi"] = ("媒体会话：检测到多个可能属于 {0} 的来源（{1}），请在上方手动选择。", "Media session: multiple sources may belong to {0} ({1}); pick one above."),
        ["mus.session.unrecognized"] = ("媒体会话：{0} 未提供可识别的 Windows 媒体会话。", "Media session: {0} does not expose a recognizable Windows media session."),
        ["mus.session.others"] = ("检测到的 {0} 属于其他播放器，不会使用。", "{0} belongs to other players and will not be used."),
        ["mus.session.unavailable"] = ("媒体会话：选择的会话当前不可用；当前可用：{0}。", "Media session: the selected session is unavailable; available: {0}."),
        ["mus.colors.presetList"] = ("当前使用预设颜色：{0}", "Using preset colors: {0}"),
        ["mus.colors.track"] = ("当前歌曲：{0}{1}；实际颜色：{2}", "Now playing: {0}{1}; actual colors: {2}"),
        ["mus.pid"] = ("；当前 PID：{0}", "; PID: {0}"),
        ["mus.media.auto"] = ("；媒体会话：自动匹配", "; media session: auto"),
        ["mus.media.manual"] = ("；媒体会话：{0}", "; media session: {0}"),
        ["mus.bound"] = ("已绑定：{0}{1}{2}", "Bound: {0}{1}{2}"),
        ["mus.preset.readonly"] = ("内置预设不能被修改；请使用“新建/另存为”创建一个自定义预设。", "Built-in presets cannot be modified; use “New / save as” to create a custom preset."),
        ["mus.preset.emptyName"] = ("请输入预设名称。", "Please enter a preset name."),
        ["mus.preset.emptyName2"] = ("请先输入自定义预设名称。", "Enter a custom preset name first."),
        ["mus.preset.reserved"] = ("自定义预设不能使用内置预设名称。", "Custom presets cannot use the built-in preset name."),
        ["mus.preset.overwrite"] = ("预设“{0}”已存在，是否覆盖？", "Preset “{0}” already exists. Overwrite?"),
        ["mus.preset.limit"] = ("最多保存 8 个自定义音乐预设。", "At most 8 custom music presets can be saved."),
        ["mus.preset.noDelete"] = ("内置预设不能删除。", "Built-in presets cannot be deleted."),
        ["mus.hint.modified"] = ("预设内容已修改，请先点击“保存预设修改”。", "Preset changed — click “Save preset changes” first."),
        ["mus.hint.staged"] = ("预设修改已暂存，点击底部“保存并应用”后生效。", "Preset changes staged — click “Save & apply” below to take effect."),
        ["mus.preset.readonly2"] = ("内置预设不能被修改；请使用“新建/另存为”创建一个自定义预设。", "Built-in presets cannot be modified; use “New / save as” to create a custom preset."),
        ["mus.hint.builtinModified"] = ("内置预设不能直接修改，请使用“新建/另存为”。", "Built-in presets cannot be edited directly — use “New / save as”."),
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
