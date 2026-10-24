using ColorfulLedKeyboard.Core;
using System.Drawing;
using System.Windows.Forms;

namespace ColorfulLedKeyboard.Installer;

/// <summary>引擎操作委托：由 Program 注入，向导只负责编排页面与展示。
/// Install 携带安装选项（桌面快捷方式/开机自启动），由标准流程的选项页或静默路径提供。</summary>
internal sealed record WizardOps(
    Action<Action<string>, bool, bool> Install,
    Action<bool, Action<string>> Uninstall,
    Func<string> InstallDoneText,
    Func<bool, string> UninstallDoneText,
    Func<(string Version, string ServiceStatus)> DescribeInstalledState);

/// <summary>
/// 单窗口安装向导。全新安装走标准流程：欢迎 → 许可协议 → 安装选项 → 进度 → 完成（“立即运行”勾选）；
/// 已安装时一键“修复或更新”直达进度（正常程序的覆盖安装行为）。卸载：确认 → 进度 → 完成。
/// 所有页面在同一窗体内切换，替代旧版的连环对话框与 MessageBox。
/// demo 模式（/preview）只演示页面流转，不执行任何引擎操作。
/// </summary>
internal sealed class WizardWindow : Form
{
    public enum WizardAction { None, Install, Uninstall }

    private sealed record Operation(string TitleKey, string[] StepKeys, Action<Action<string>> Engine);

    private static readonly string[] InstallStepKeys =
        ["prereq", "backup", "stop", "files", "driver", "service", "finalize"];
    private static readonly string[] UninstallStepKeys = ["stopOnly", "removeStartup", "removeFiles", "purge"];

    private const string AppDisplayName = "ClevoLEDKeyboardControl";

    private readonly WizardOps _ops;
    private readonly bool _installed;
    private readonly bool _demo;
    private readonly bool _progressOnlyUninstall;

    private Panel _content = null!;
    private Panel _buttonBar = null!;
    private Label _titleLabel;
    private Label _subtitleLabel;
    private readonly List<(string Key, Label Marker)> _stepMarkers = [];
    private Label _statusLabel = new();
    private Operation? _currentOperation;
    private string _selectedLanguage = "auto";

    public WizardAction Result { get; private set; } = WizardAction.None;
    public bool KeepSettings { get; private set; }

    /// <summary>完成页“立即运行”勾选（仅安装结果有意义，默认勾选）。</summary>
    public bool LaunchAfterInstall { get; private set; } = true;

    /// <summary>安装选项页的勾选结果；修复/更新与静默路径维持全默认。</summary>
    private bool _desktopShortcut = true;
    private bool _autostart = true;

    public string SelectedLanguage => _selectedLanguage;

    private WizardWindow(WizardOps ops, bool installed, bool demo,
        bool progressOnlyUninstall = false)
    {
        _ops = ops;
        _installed = installed;
        _demo = demo;
        _progressOnlyUninstall = progressOnlyUninstall;

        Text = InstallerStrings.Get("wizard.title");
        FormBorderStyle = FormBorderStyle.FixedDialog;
        StartPosition = FormStartPosition.CenterScreen;
        MaximizeBox = false;
        MinimizeBox = false;
        ClientSize = new Size(560, 470);
        BackColor = InstallerTheme.Window;
        Font = InstallerTheme.BaseFont;
        ShowInTaskbar = false;

        var spectrum = new InstallerTheme.SpectrumBar { Dock = DockStyle.Top };
        _buttonBar = new Panel { Dock = DockStyle.Bottom, Height = 74, BackColor = InstallerTheme.Window };
        _content = new Panel { Dock = DockStyle.Fill, BackColor = InstallerTheme.Window };
        Controls.Add(_content);
        Controls.Add(_buttonBar);
        Controls.Add(spectrum);

        Shown += (_, _) => InstallerTheme.ApplyDarkTitleBar(this);
    }

    /// <summary>安装向导：新装走 欢迎→协议→选项 标准流程；已装直接“修复或更新”。
    /// previewPage 供 /preview 截图验收直跳指定页："action" / "welcome" / "license" / "options" / "progress" / "done" / "lang"。</summary>
    public static WizardWindow ForSetup(bool installed, bool demo, WizardOps ops, string? previewPage = null)
    {
        var wizard = new WizardWindow(ops, installed, demo);
        switch (previewPage)
        {
            case "action":
                wizard._selectedLanguage = InstallerStrings.Current;
                wizard.ShowActionPage();
                break;
            case "welcome":
                wizard._selectedLanguage = InstallerStrings.Current;
                wizard.ShowWelcomePage();
                break;
            case "license":
                wizard._selectedLanguage = InstallerStrings.Current;
                wizard.ShowLicensePage();
                break;
            case "options":
                wizard._selectedLanguage = InstallerStrings.Current;
                wizard.ShowOptionsPage();
                break;
            case "progress":
                wizard.StartInstall();
                break;
            case "done":
                wizard.Result = WizardAction.Install;
                wizard.ShowDone("progress.install");
                break;
            case "lang":
                wizard.ShowLanguagePage();
                break;
            default:
                if (wizard._installed) wizard.ShowActionPage();
                else wizard.ShowWelcomePage();
                break;
        }
        return wizard;
    }

    /// <summary>卸载器子进程：跳过语言页，直接进入卸载进度（语言取 UiState 已存值）。</summary>
    public static WizardWindow ForUninstallProgress(bool keepSettings, bool demo, WizardOps ops)
    {
        var wizard = new WizardWindow(ops, installed: true, demo, progressOnlyUninstall: true)
        {
            KeepSettings = keepSettings,
            Result = WizardAction.Uninstall
        };
        wizard.ShowUninstallProgress();
        return wizard;
    }

    /// <summary>卸载器确认页（在安装目录内运行的卸载器本体，语言取 UiState 已存值）。</summary>
    public static WizardWindow ForUninstallerConfirm(WizardOps ops)
    {
        var wizard = new WizardWindow(ops, installed: true, demo: false);
        wizard.ShowUninstallConfirm();
        return wizard;
    }

    // ---- 页面骨架 ----

    private void RenderChrome(string title, string? subtitle, params Button[] buttons)
    {
        _titleLabel = InstallerTheme.MakeLabel(title, InstallerTheme.TitleFont);
        _titleLabel.Location = new Point(28, 26);
        _content.Controls.Clear();
        _content.Controls.Add(_titleLabel);
        if (subtitle is not null)
        {
            _subtitleLabel = InstallerTheme.MakeLabel(subtitle, color: InstallerTheme.Muted);
            _subtitleLabel.Location = new Point(28, 64);
            _content.Controls.Add(_subtitleLabel);
        }

        _buttonBar.Controls.Clear();
        var right = ClientSize.Width - 24;
        // 传入顺序即从右往左排列
        foreach (var button in buttons)
        {
            button.Location = new Point(right - button.Width, (74 - button.Height) / 2);
            _buttonBar.Controls.Add(button);
            right -= button.Width + 10;
        }
    }

    private static Button MakeCloseButton()
    {
        var button = InstallerTheme.MakeButton(InstallerStrings.Get("wizard.close"), "ghost");
        button.Width = 100;
        button.Click += (_, _) => System.Windows.Forms.Application.Exit();
        return button;
    }

    // ---- 语言选择页 ----

    private void ShowLanguagePage()
    {
        var initial = ResolveInitialLanguage();
        InstallerStrings.Set(initial);
        _selectedLanguage = initial;

        RenderChrome(InstallerStrings.Get("wizard.title"), null,
            MakeCancelButton(), MakeContinueFromLanguageButton());

        _langHeading = InstallerTheme.MakeLabel(InstallerStrings.Get("lang.title"), InstallerTheme.SectionFont);
        _langHeading.Location = new Point(28, 116);
        _content.Controls.Add(_langHeading);

        _langZh = MakeLanguageOption(164, "中文（简体）", InstallerStrings.ZhCn);
        _langEn = MakeLanguageOption(232, "English", InstallerStrings.EnUs);
        _content.Controls.Add(_langZh);
        _content.Controls.Add(_langEn);

        _langHint = InstallerTheme.MakeLabel(InstallerStrings.Get("lang.hint"), color: InstallerTheme.Faint);
        _langHint.Location = new Point(28, 306);
        _content.Controls.Add(_langHint);
    }

    private Label _langHeading;
    private Label _langHint;
    private RadioButton _langZh;
    private RadioButton _langEn;
    private Button _langContinue;

    /// <summary>就地刷新语言页全部文案（含按钮），不重建控件树。</summary>
    private void RefreshLanguagePageTexts()
    {
        _langHeading.Text = InstallerStrings.Get("lang.title");
        _langHint.Text = InstallerStrings.Get("lang.hint");
        _langZh.Text = "中文（简体）";
        _langEn.Text = "English";
        Text = InstallerStrings.Get("wizard.title");
        if (_langContinue is not null) _langContinue.Text = InstallerStrings.Get("wizard.continue");
        if (_langCancel is not null) _langCancel.Text = InstallerStrings.Get("wizard.cancel");
        // 兜底：按钮栏里任何显示为取消翻译值的按钮都同步（防字段时序遗漏）
        foreach (Control control in _buttonBar.Controls)
        {
            if (control is Button button && (button.Text == "取消" || button.Text == "Cancel" || button.Text == "common.cancel"))
                button.Text = InstallerStrings.Get("wizard.cancel");
        }
    }

    private RadioButton MakeLanguageOption(int y, string text, string language)
    {
        var radio = new RadioButton
        {
            Text = text,
            Font = new Font(InstallerTheme.BaseFont.FontFamily, 12f, FontStyle.Bold),
            ForeColor = InstallerTheme.Text,
            BackColor = InstallerTheme.Card,
            FlatStyle = FlatStyle.Flat,
            Location = new Point(28, y),
            Size = new Size(504, 52),
            Padding = new Padding(14, 0, 0, 0),
            Cursor = Cursors.Hand,
            Checked = InstallerStrings.Current == language
        };
        radio.FlatAppearance.BorderColor = InstallerTheme.Border;
        radio.FlatAppearance.MouseOverBackColor = Color.FromArgb(28, 35, 54);
        radio.FlatAppearance.CheckedBackColor = InstallerTheme.Accent;
        radio.CheckedChanged += (_, _) =>
        {
            if (!radio.Checked) return;
            InstallerStrings.Set(language);
            _selectedLanguage = language;
            // 就地刷新本页文案；不重建控件树（重建会让 WinForms 单选组重置勾选，
            // 表现为"选不中 English"）。页面控件极少，逐个更新文本即可。
            RefreshLanguagePageTexts();
        };
        return radio;
    }

    private Button MakeContinueFromLanguageButton()
    {
        var button = InstallerTheme.MakeButton(InstallerStrings.Get("wizard.continue"), "primary");
        _langContinue = button;
        button.Width = 120;
        button.Click += (_, _) =>
        {
            if (_installed)
            {
                ShowActionPage();
            }
            else
            {
                StartInstall();
            }
        };
        return button;
    }

    private Button _langCancel;

    private Button MakeCancelButton()
    {
        var button = InstallerTheme.MakeButton(InstallerStrings.Get("wizard.cancel"), "ghost");
        _langCancel = button;
        button.Width = 100;
        button.Click += (_, _) => Close();
        return button;
    }

    // ---- 操作选择页（已安装） ----

    private Label _actionLangSwitch;

    private void ShowActionPage()
    {
        var (version, serviceStatus) = _ops.DescribeInstalledState();
        RenderChrome(
            InstallerStrings.Format("action.title", AppDisplayName),
            InstallerStrings.Format("action.subtitle", version, serviceStatus),
            MakeCancelButton());

        MakeActionCard(104, InstallerStrings.Get("action.repair"),
            InstallerStrings.Get("action.repairDesc"), primary: true, StartInstall);

        AddLanguageSwitch(196, ShowActionPage);
    }

    // ---- 全新安装：欢迎 → 许可协议 → 安装选项（标准安装流程） ----

    private void ShowWelcomePage()
    {
        var (version, _) = _ops.DescribeInstalledState();
        RenderChrome(
            InstallerStrings.Get("welcome.title"),
            InstallerStrings.Format("welcome.subtitle", version),
            MakeCancelButton(), MakeNextButton(ShowLicensePage));

        var body = InstallerTheme.MakeLabel(InstallerStrings.Format("welcome.body", AppDisplayName), color: InstallerTheme.Muted);
        body.Location = new Point(28, 116);
        body.MaximumSize = new Size(504, 0);
        _content.Controls.Add(body);

        AddLanguageSwitch(196, ShowWelcomePage);
    }

    private void ShowLicensePage()
    {
        var next = MakeNextButton(ShowOptionsPage);
        RenderChrome(InstallerStrings.Get("lic.title"), null,
            MakeBackButton(ShowWelcomePage), next);

        var licenseBox = new TextBox
        {
            Multiline = true,
            ReadOnly = true,
            ScrollBars = ScrollBars.Vertical,
            Text = LoadLicenseText(),
            Font = new Font("Consolas", 9F),
            BackColor = InstallerTheme.Card,
            ForeColor = InstallerTheme.Text,
            BorderStyle = BorderStyle.FixedSingle,
            Location = new Point(28, 96),
            Size = new Size(504, 240)
        };
        _content.Controls.Add(licenseBox);

        var accept = new CheckBox
        {
            Text = InstallerStrings.Get("lic.accept"),
            ForeColor = InstallerTheme.Text,
            BackColor = InstallerTheme.Window,
            Location = new Point(28, 350),
            AutoSize = true
        };
        accept.CheckedChanged += (_, _) => next.Enabled = accept.Checked;
        _content.Controls.Add(accept);
        next.Enabled = false;
    }

    private void ShowOptionsPage()
    {
        // 勾选框先声明（局部函数捕获需要确定赋值），再挂到 RenderChrome 之后的页面内容里
        var shortcut = new CheckBox
        {
            Text = InstallerStrings.Get("opts.shortcut"),
            Checked = _desktopShortcut,
            ForeColor = InstallerTheme.Text,
            BackColor = InstallerTheme.Window,
            Location = new Point(28, 108),
            AutoSize = true
        };

        var autostart = new CheckBox
        {
            Text = InstallerStrings.Get("opts.autostart"),
            Checked = _autostart,
            ForeColor = InstallerTheme.Text,
            BackColor = InstallerTheme.Window,
            Location = new Point(28, 146),
            AutoSize = true
        };

        RenderChrome(InstallerStrings.Get("opts.title"), null,
            MakeBackButton(ShowLicensePage), MakeNextButton(StartFromOptions, "opts.install"));

        _content.Controls.Add(shortcut);
        _content.Controls.Add(autostart);

        var note = InstallerTheme.MakeLabel(InstallerStrings.Get("opts.note"), color: InstallerTheme.Faint);
        note.Location = new Point(28, 192);
        note.MaximumSize = new Size(504, 0);
        _content.Controls.Add(note);

        void StartFromOptions()
        {
            _desktopShortcut = shortcut.Checked;
            _autostart = autostart.Checked;
            StartInstall();
        }
    }

    private Button MakeNextButton(Action onNext, string? key = null)
    {
        var button = InstallerTheme.MakeButton(InstallerStrings.Get(key ?? "wizard.next"), "primary");
        button.Width = 110;
        button.Click += (_, _) => onNext();
        return button;
    }

    private Button MakeBackButton(Action onBack)
    {
        var button = InstallerTheme.MakeButton(InstallerStrings.Get("wizard.back"), "ghost");
        button.Width = 110;
        button.Click += (_, _) => onBack();
        return button;
    }

    /// <summary>语言切换小字（欢迎页与操作页共用；切换后整页重渲染）。</summary>
    private void AddLanguageSwitch(int y, Action reRender)
    {
        var label = InstallerTheme.MakeLabel(LanguageSwitchText(), color: InstallerTheme.Faint);
        label.Location = new Point(28, y);
        label.Cursor = Cursors.Hand;
        label.Click += (_, _) =>
        {
            InstallerStrings.Set(InstallerStrings.IsEnglish ? InstallerStrings.ZhCn : InstallerStrings.EnUs);
            _selectedLanguage = InstallerStrings.Current;
            reRender();
        };
        _content.Controls.Add(label);
    }

    private static string LoadLicenseText()
    {
        try
        {
            using var stream = System.Reflection.Assembly.GetExecutingAssembly()
                .GetManifestResourceStream("ClevoLEDKeyboardControl.Installer.GplLicense");
            if (stream is null) return "GPL-3.0";
            using var reader = new StreamReader(stream);
            return reader.ReadToEnd();
        }
        catch
        {
            return "GPL-3.0";
        }
    }

    private string LanguageSwitchText()
    {
        // 双语并列展示，点击切换当前语言
        var current = InstallerStrings.IsEnglish ? "English" : "中文";
        var other = InstallerStrings.IsEnglish ? "中文" : "English";
        return $"{current}  ·  点击切换为 {other} / switch to {other}";
    }

    private Label _actionCardTitle;

    private void MakeActionCard(int y, string title, string description, bool primary, Action onClick)
    {
        var card = new Panel
        {
            Location = new Point(28, y),
            Size = new Size(504, 66),
            BackColor = InstallerTheme.Card,
            Cursor = Cursors.Hand
        };
        var accent = new Panel { Location = new Point(0, 0), Size = new Size(4, 66), BackColor = primary ? InstallerTheme.Accent : InstallerTheme.Border };
        var titleLabel = InstallerTheme.MakeLabel(title, InstallerTheme.SectionFont);
        if (primary) _actionCardTitle = titleLabel;
        titleLabel.Location = new Point(18, 12);
        var descLabel = InstallerTheme.MakeLabel(description, color: InstallerTheme.Muted);
        descLabel.Location = new Point(18, 38);
        card.Controls.Add(accent);
        card.Controls.Add(titleLabel);
        card.Controls.Add(descLabel);

        void Raise(object? sender, EventArgs e) => onClick();
        card.Click += Raise;
        accent.Click += Raise;
        titleLabel.Click += Raise;
        descLabel.Click += Raise;
        foreach (Control control in card.Controls)
        {
            control.MouseEnter += (_, _) => card.BackColor = Color.FromArgb(28, 35, 54);
            control.MouseLeave += (_, _) => card.BackColor = InstallerTheme.Card;
        }
        card.MouseEnter += (_, _) => card.BackColor = Color.FromArgb(28, 35, 54);
        card.MouseLeave += (_, _) => card.BackColor = InstallerTheme.Card;
        _content.Controls.Add(card);
    }

    // ---- 卸载确认页 ----

    private void ShowUninstallConfirm()
    {
        // 底栏只放"取消"；执行用的警示色"卸载"按钮在内容区，避免出现两个同名按钮
        RenderChrome(
            InstallerStrings.Format("confirm.title", AppDisplayName),
            null,
            MakeCancelButton());

        var text = InstallerTheme.MakeLabel(InstallerStrings.Get("confirm.text"));
        text.Location = new Point(28, 112);
        text.MaximumSize = new Size(500, 0);
        _content.Controls.Add(text);

        var keep = new CheckBox
        {
            Text = InstallerStrings.Get("confirm.keep"),
            ForeColor = InstallerTheme.Text,
            BackColor = InstallerTheme.Window,
            Location = new Point(28, 176),
            AutoSize = true
        };
        _content.Controls.Add(keep);

        var uninstallButton = InstallerTheme.MakeButton(InstallerStrings.Get("confirm.uninstall"), "danger");
        uninstallButton.Width = 140;
        uninstallButton.Location = new Point(28, 232);
        uninstallButton.Click += (_, _) =>
        {
            KeepSettings = keep.Checked;
            Result = WizardAction.Uninstall;
            StartUninstall();
        };
        _content.Controls.Add(uninstallButton);
    }

    private Button MakeUninstallButton()
    {
        var button = InstallerTheme.MakeButton(InstallerStrings.Get("confirm.uninstall"), "danger");
        button.Width = 140;
        button.Click += (_, _) => ShowUninstallConfirm();
        return button;
    }

    // ---- 进度页 ----

    private void StartInstall()
    {
        Result = WizardAction.Install;
        RunOperation(new Operation("progress.install", InstallStepKeys,
            report => _ops.Install(report, _desktopShortcut, _autostart)));
    }

    private void StartUninstall()
    {
        Result = WizardAction.Uninstall;
        RunOperation(new Operation("progress.uninstall", UninstallStepKeys,
            report => _ops.Uninstall(KeepSettings, report)));
    }

    private void ShowUninstallProgress()
    {
        // 卸载器子进程路径：确认已在宿主完成，直接进入进度页
        Result = WizardAction.Uninstall;
        RunOperation(new Operation("progress.uninstall", UninstallStepKeys,
            report => _ops.Uninstall(KeepSettings, report)));
    }

    private void RunOperation(Operation operation)
    {
        _currentOperation = operation;
        RenderChrome(InstallerStrings.Get(operation.TitleKey), null);

        _stepMarkers.Clear();
        var y = 116;
        foreach (var key in operation.StepKeys)
        {
            var marker = InstallerTheme.MakeLabel("○", color: InstallerTheme.Faint);
            marker.Font = new Font(InstallerTheme.BaseFont.FontFamily, 11f, FontStyle.Bold);
            marker.Location = new Point(30, y);
            var text = InstallerTheme.MakeLabel(InstallerStrings.Get("step." + key));
            text.Location = new Point(58, y + 1);
            _content.Controls.Add(marker);
            _content.Controls.Add(text);
            _stepMarkers.Add((key, marker));
            y += 36;
        }
        _statusLabel = InstallerTheme.MakeLabel(_demo ? "demo…" : "", color: InstallerTheme.Faint);
        _statusLabel.Location = new Point(28, y + 14);
        _content.Controls.Add(_statusLabel);

        _ = RunOperationAsync(operation);
    }

    private async Task RunOperationAsync(Operation operation)
    {
        _buttonBar.Enabled = false;
        try
        {
            if (_demo)
            {
                foreach (var key in operation.StepKeys)
                {
                    MarkRunning(key);
                    await Task.Delay(450);
                    MarkDone(key);
                }
            }
            else
            {
                var executed = new HashSet<string>();
                await Task.Run(() => operation.Engine(stepKey =>
                {
                    executed.Add(stepKey);
                    BeginInvoke(() => MarkRunning(stepKey));
                }));
                foreach (var (key, _) in _stepMarkers)
                {
                    if (executed.Count == 0 || executed.Contains(key)) BeginInvoke(() => MarkDone(key));
                }
            }

            ShowDone(operation.TitleKey);
        }
        catch (Exception ex)
        {
            ShowFailed(operation, ex);
        }
        finally
        {
            _buttonBar.Enabled = true;
        }
    }

    private void MarkRunning(string key)
    {
        foreach (var (stepKey, marker) in _stepMarkers)
        {
            if (stepKey != key) continue;
            marker.Text = "●";
            marker.ForeColor = InstallerTheme.Accent;
        }
    }

    private void MarkDone(string key)
    {
        foreach (var (stepKey, marker) in _stepMarkers)
        {
            if (stepKey != key) continue;
            marker.Text = "✓";
            marker.ForeColor = InstallerTheme.Success;
        }
    }

    // ---- 完成页 ----

    private void ShowDone(string titleKey)
    {
        var message = Result == WizardAction.Uninstall
            ? _ops.UninstallDoneText(KeepSettings)
            : _ops.InstallDoneText();
        // 进度页标题（progress.*）映射为完成页标题（done.*）
        RenderChrome("✓ " + InstallerStrings.Get(titleKey.Replace("progress.", "done.")), null, MakeFinishButton());

        var body = InstallerTheme.MakeLabel(message);
        body.Location = new Point(28, 112);
        body.MaximumSize = new Size(504, 0);
        _content.Controls.Add(body);

        if (Result == WizardAction.Install)
        {
            // 标准完成页：勾选“立即运行”（仅安装；卸载完成页不需要）
            var launch = new CheckBox
            {
                Text = InstallerStrings.Format("done.launch", AppDisplayName),
                Checked = true,
                ForeColor = InstallerTheme.Text,
                BackColor = InstallerTheme.Window,
                Location = new Point(28, 216),
                AutoSize = true
            };
            launch.CheckedChanged += (_, _) => LaunchAfterInstall = launch.Checked;
            _content.Controls.Add(launch);
        }
    }

    private Button MakeFinishButton()
    {
        var button = InstallerTheme.MakeButton(InstallerStrings.Get("wizard.finish"), "primary");
        button.Width = 120;
        button.Click += (_, _) => Close();
        return button;
    }

    // ---- 失败页 ----

    private void ShowFailed(Operation operation, Exception ex)
    {
        RenderChrome("✕ " + InstallerStrings.Get("fail.title"), null,
            MakeCancelButton(), MakeRetryButton(operation));

        var message = InstallerTheme.MakeLabel(ex.Message, color: InstallerTheme.Danger);
        message.Location = new Point(28, 112);
        message.MaximumSize = new Size(504, 0);
        _content.Controls.Add(message);

        var log = InstallerTheme.MakeLabel(
            InstallerStrings.Get("fail.log") + "\n" + Program.InstallerLogPath,
            color: InstallerTheme.Faint);
        log.Location = new Point(28, 240);
        log.MaximumSize = new Size(504, 0);
        _content.Controls.Add(log);
    }

    private Button MakeRetryButton(Operation operation)
    {
        var button = InstallerTheme.MakeButton(InstallerStrings.Get("wizard.retry"), "primary");
        button.Width = 100;
        button.Click += (_, _) => RunOperation(operation);
        return button;
    }

    // ---- 语言解析 ----

    private string ResolveInitialLanguage()
    {
        var stored = UiStateStore.Shared.Load().Language;
        if (stored == InstallerStrings.ZhCn || stored == InstallerStrings.EnUs) return stored;
        var ui = System.Globalization.CultureInfo.CurrentUICulture.Name;
        return ui.StartsWith("zh", StringComparison.OrdinalIgnoreCase)
            ? InstallerStrings.ZhCn
            : InstallerStrings.EnUs;
    }
}
