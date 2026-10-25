namespace ColorfulLedKeyboard.Installer;

/// <summary>
/// 安装器双语文案。语言页选择后立即切换（Set），卸载器子进程从 UiState 读取已存语言。
/// key 缺失回退中文；AllKeys 暴露给单元测试校验中英 key 对齐。
/// </summary>
internal static class InstallerStrings
{
    public const string ZhCn = "zh-CN";
    public const string EnUs = "en-US";

    private static readonly Dictionary<string, (string Zh, string En)> Table = new()
    {
        ["wizard.title"] = ("ClevoLEDKeyboardControl 安装程序", "ClevoLEDKeyboardControl Setup"),
        ["wizard.cancel"] = ("取消", "Cancel"),
        ["wizard.close"] = ("关闭", "Close"),
        ["wizard.retry"] = ("重试", "Retry"),
        ["wizard.continue"] = ("继续", "Continue"),
        ["wizard.finish"] = ("完成", "Finish"),
        ["wizard.next"] = ("下一步", "Next"),
        ["wizard.back"] = ("上一步", "Back"),

        ["welcome.title"] = ("欢迎使用安装向导", "Welcome to the setup wizard"),
        ["welcome.subtitle"] = ("版本 {0}", "Version {0}"),
        ["welcome.version"] = ("版本 {0}", "Version {0}"),
        ["welcome.body"] = ("将安装到系统程序目录并注册后台灯控服务，点击“下一步”继续。", "Installs to the system programs folder and registers the background lighting service. Click “Next” to continue."),

        ["lic.title"] = ("许可协议", "License agreement"),
        ["lic.accept"] = ("我已阅读并接受本协议（GPL-3.0）", "I have read and accept the agreement (GPL-3.0)"),

        ["opts.title"] = ("安装选项", "Installation options"),
        ["opts.shortcut"] = ("创建桌面快捷方式", "Create a desktop shortcut"),
        ["opts.autostart"] = ("开机自动启动（托盘与灯控服务）", "Launch at startup (tray and lighting service)"),
        ["opts.install"] = ("安装", "Install"),
        ["opts.note"] = ("两个选项都可随时在软件“设置”中更改。", "Both options can be changed later in the app settings."),

        ["done.launch"] = ("立即运行 {0}", "Run {0} now"),

        ["lang.title"] = ("选择安装语言", "Choose installation language"),
        ["lang.hint"] = ("稍后可在软件设置中更改。", "You can change this later in the app settings."),

        ["action.title"] = ("已安装", "Already installed"),
        ["action.subtitle"] = ("当前版本：{0}　·　服务：{1}", "Current version: {0} · Service: {1}"),
        ["action.repair"] = ("修复或更新", "Repair or update"),
        ["action.repairDesc"] = ("安装新版本 v{0} 并保留全部设置。", "Install version v{0} and keep all settings."),
        ["action.uninstall"] = ("卸载", "Uninstall"),
        ["action.uninstallDesc"] = ("从电脑中移除本软件，可选择保留配置。", "Remove this app from the computer; you can keep your settings."),
        ["act.uninstall.missing"] = ("未检测到已安装的卸载器，请使用“设置 → 应用”卸载。", "The installed uninstaller was not found. Use Windows Settings → Apps to uninstall."),

        ["confirm.title"] = ("卸载 {0}", "Uninstall {0}"),
        ["confirm.text"] = ("将卸载程序与后台服务，并默认删除本机配置。", "This removes the app and the background service, and deletes local configuration by default."),
        ["confirm.keep"] = ("保留我的设置和配置文件", "Keep my settings and configuration files"),
        ["confirm.uninstall"] = ("卸载", "Uninstall"),

        ["progress.install"] = ("正在安装", "Installing"),
        ["progress.uninstall"] = ("正在卸载", "Uninstalling"),
        ["step.prereq"] = ("前置检查", "Pre-flight checks"),
        ["step.backup"] = ("备份现有配置", "Backing up existing configuration"),
        ["step.stop"] = ("停止旧服务与托盘", "Stopping the old service and tray"),
        ["step.files"] = ("写入程序文件", "Writing program files"),
        ["step.driver"] = ("安装厂商驱动组件", "Installing the vendor driver component"),
        ["step.service"] = ("注册并启动服务", "Registering and starting the service"),
        ["step.finalize"] = ("完成收尾（自启动 / 快捷方式）", "Finalizing (autorun / shortcuts)"),
        ["step.stopOnly"] = ("停止服务与托盘", "Stopping the service and tray"),
        ["step.removeStartup"] = ("移除自启动与快捷方式", "Removing autorun and shortcuts"),
        ["step.removeFiles"] = ("删除程序文件", "Deleting program files"),
        ["step.purge"] = ("清理配置文件", "Deleting configuration files"),

        ["done.install"] = ("安装完成", "Installation complete"),
        ["done.uninstall"] = ("卸载完成", "Uninstall complete"),
        ["fail.title"] = ("操作未完成", "Operation not completed"),
        ["fail.log"] = ("安装日志：", "Installer log:"),

        ["state.running"] = ("运行中", "Running"),
        ["state.starting"] = ("正在启动", "Starting"),
        ["state.stopped"] = ("已停止", "Stopped"),
        ["state.installed"] = ("已安装", "Installed"),
        ["state.notInstalled"] = ("未安装", "Not installed"),
        ["driver.installed"] = ("已安装", "Installed"),
        ["driver.missing"] = ("未找到", "Not found"),
        ["driver.component"] = ("厂商灯控组件", "Vendor driver component"),
        ["autostart"] = ("开机自启动", "Launch at startup"),
        ["autostart.enabled"] = ("已启用", "Enabled"),

        ["done.settingsOpen"] = ("设置窗口将自动打开。", "The settings window will open automatically."),
        ["done.driverMissing"] = ("安装包内没有包含厂商灯控组件，且本机未检测到 OEM Control Center。程序已安装，但暂时无法控制键盘灯。请安装 OEM Control Center 后重新运行安装器进行修复。", "The installer payload did not include the vendor driver component and no OEM Control Center was found on this machine. The app is installed but cannot control the keyboard yet. Install the OEM Control Center, then re-run the installer and choose repair."),
        ["done.keptSettings"] = ("已保留设置与配置文件。", "Settings and configuration files were kept."),
        ["done.removedSettings"] = ("已删除本机配置。", "Local configuration was deleted."),
        ["admin.needed"] = ("需要管理员权限才能安装 Windows 服务。请右键安装包，选择“以管理员身份运行”。", "Administrator privileges are required to install the Windows service. Right-click the installer and choose \"Run as administrator\"."),
    };

    /// <summary>当前语言（"zh-CN" / "en-US"）。</summary>
    public static string Current { get; private set; } = ZhCn;

    public static bool IsEnglish => Current == EnUs;

    public static IReadOnlyCollection<string> AllKeys => Table.Keys;

    /// <summary>切换语言；未知值回退中文。</summary>
    public static void Set(string language) => Current = language == EnUs ? EnUs : ZhCn;

    public static string Get(string key)
    {
        return Table.TryGetValue(key, out var entry) ? (IsEnglish ? entry.En : entry.Zh) : key;
    }

    public static string Format(string key, params object[] args) => string.Format(Get(key), args);
}
