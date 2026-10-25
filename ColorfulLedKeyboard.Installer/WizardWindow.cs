using ColorfulLedKeyboard.Core;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Windows.Forms;

namespace ColorfulLedKeyboard.Installer;

/// <summary>引擎操作委托：由 Program 注入，向导只负责编排页面与展示。
/// Install 携带安装选项（桌面快捷方式/开机自启动），由标准流程的选项页或静默路径提供。</summary>
/// <summary>LaunchUninstaller：由安装器发起卸载时调起已安装的卸载器；返回错误文案，null = 已启动。</summary>
internal sealed record WizardOps(
    Action<Action<string>, bool, bool> Install,
    Action<bool, Action<string>> Uninstall,
    Func<string> InstallDoneText,
    Func<bool, string> UninstallDoneText,
    Func<(string Version, string ServiceStatus)> DescribeInstalledState,
    Func<string?> LaunchUninstaller);

/// <summary>
/// 经典 NSIS/MUI2 风格安装向导：白底内容区 + 左侧蓝色品牌栏 + 底部「上一步/下一步/取消」。
/// 全新安装走标准流程：欢迎 → 许可协议 → 安装选项 → 进度（绿色进度条）→ 完成（“立即运行”勾选）；
/// 已安装时一键“修复或更新”直达进度；卸载：确认 → 进度 → 完成。
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

    // 布局统一显式 DPI 缩放：设计基准 96 DPI，PerMonitorV2 下动态添加的控件与静态部分同一比例
    private int Dpi(int v) => Math.Max(1, v * DeviceDpi / 96);
    private int Pad => Dpi(28);
    private int BodyWidth => ClientSize.Width - Pad * 2;

    private readonly WizardOps _ops;
    private readonly bool _installed;
    private readonly bool _demo;
    private readonly bool _progressOnlyUninstall;
    private readonly string _sidebarVersion;

    private Panel _content = null!;
    private Panel _buttonBar = null!;
    private Label _titleLabel = null!;
    private Label _subtitleLabel = null!;
    private readonly List<(string Key, Label Marker)> _stepMarkers = [];
    private Label _statusLabel = new();
    private ModernProgressBar _progress = null!;
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
        _sidebarVersion = ops.DescribeInstalledState().Version;

        Text = InstallerStrings.Get("wizard.title");
        FormBorderStyle = FormBorderStyle.FixedDialog;
        StartPosition = FormStartPosition.CenterScreen;
        MaximizeBox = false;
        MinimizeBox = false;
        ClientSize = new Size(Dpi(560), Dpi(416));
        BackColor = InstallerTheme.Window;
        Font = InstallerTheme.BaseFont;
        ShowInTaskbar = true;

        _buttonBar = new Panel { Dock = DockStyle.Bottom, Height = Dpi(52), BackColor = InstallerTheme.Window };
        _content = new Panel { Dock = DockStyle.Fill, BackColor = InstallerTheme.Window };
        Controls.Add(_content);
        Controls.Add(_buttonBar);
    }

    // ---- 工厂入口 ----

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

    /// <summary>卸载器子进程：跳过确认页，直接进入卸载进度（语言取 UiState 已存值）。</summary>
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

    // ---- 页面骨架：两行页头 + 底部导航按钮 ----

    private void RenderChrome(string title, string? subtitle, params Button[] buttons)
    {
        _titleLabel = InstallerTheme.MakeLabel(title, InstallerTheme.TitleFont);
        _titleLabel.Location = new Point(Pad, Dpi(20));
        _content.Controls.Clear();
        _content.Controls.Add(_titleLabel);
        if (subtitle is not null)
        {
            _subtitleLabel = InstallerTheme.MakeLabel(subtitle, color: InstallerTheme.Muted);
            _subtitleLabel.Location = new Point(Pad, Dpi(54));
            _content.Controls.Add(_subtitleLabel);
        }

        _buttonBar.Controls.Clear();
        var right = _buttonBar.ClientSize.Width - 12;
        // 传入顺序即从右往左排列
        foreach (var button in buttons)
        {
            button.Location = new Point(right - button.Width, (_buttonBar.Height - button.Height) / 2);
            _buttonBar.Controls.Add(button);
            right -= button.Width + 8;
        }
    }

    // ---- 底部导航按钮 ----

    private Button MakeCancelButton()
    {
        var button = InstallerTheme.MakeButton(InstallerStrings.Get("wizard.cancel"));
        _langCancel = button;
        button.Width = Dpi(84);
        button.Click += (_, _) => Close();
        CancelButton = button;
        return button;
    }

    private Button MakeCloseButton()
    {
        var button = InstallerTheme.MakeButton(InstallerStrings.Get("wizard.close"));
        button.Width = Dpi(80);
        button.Click += (_, _) => System.Windows.Forms.Application.Exit();
        return button;
    }

    private Button MakeNextButton(Action onNext, string? key = null)
    {
        var button = InstallerTheme.MakeButton(InstallerStrings.Get(key ?? "wizard.next"));
        button.Width = Dpi(88);
        button.Click += (_, _) => onNext();
        AcceptButton = button;
        return button;
    }

    private Button MakeBackButton(Action onBack)
    {
        var button = InstallerTheme.MakeButton(InstallerStrings.Get("wizard.back"));
        button.Width = Dpi(88);
        button.Click += (_, _) => onBack();
        return button;
    }

    // ---- 语言选择页（仅 /preview-page:lang 与历史路由可达） ----

    private void ShowLanguagePage()
    {
        var initial = ResolveInitialLanguage();
        InstallerStrings.Set(initial);
        _selectedLanguage = initial;

        RenderChrome(InstallerStrings.Get("wizard.title"), null,
            MakeCancelButton(), MakeContinueFromLanguageButton());

        _langHeading = InstallerTheme.MakeLabel(InstallerStrings.Get("lang.title"), InstallerTheme.SectionFont);
        _langHeading.Location = new Point(Pad, Dpi(84));
        _content.Controls.Add(_langHeading);

        _langZh = MakeLanguageOption(Dpi(130), "中文（简体）", InstallerStrings.ZhCn);
        _langEn = MakeLanguageOption(Dpi(186), "English", InstallerStrings.EnUs);
        _content.Controls.Add(_langZh);
        _content.Controls.Add(_langEn);

        _langHint = InstallerTheme.MakeLabel(InstallerStrings.Get("lang.hint"), color: InstallerTheme.Faint);
        _langHint.Location = new Point(Pad, Dpi(250));
        _content.Controls.Add(_langHint);
    }

    private Label _langHeading;
    private Label _langHint;
    private RadioButton _langZh;
    private RadioButton _langEn;
    private Button _langContinue;
    private Button _langCancel;

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
            Font = new Font(InstallerTheme.BaseFont.FontFamily, 10.5f, FontStyle.Bold),
            ForeColor = InstallerTheme.Text,
            BackColor = InstallerTheme.Window,
            FlatStyle = FlatStyle.Flat,
            Location = new Point(Pad, y),
            Size = new Size(BodyWidth, Dpi(44)),
            Padding = new Padding(10, 0, 0, 0),
            Cursor = Cursors.Hand,
            Checked = InstallerStrings.Current == language
        };
        radio.FlatAppearance.BorderColor = InstallerTheme.CardBorder;
        radio.FlatAppearance.MouseOverBackColor = Color.FromArgb(232, 240, 252);
        radio.FlatAppearance.CheckedBackColor = Color.FromArgb(213, 225, 242);
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
        var button = InstallerTheme.MakeButton(InstallerStrings.Get("wizard.continue"));
        _langContinue = button;
        button.Width = Dpi(88);
        AcceptButton = button;
        button.Click += (_, _) =>
        {
            if (_installed)
            {
                ShowActionPage();
            }
            else
            {
                ShowWelcomePage();
            }
        };
        return button;
    }

    // ---- 操作选择页（已安装：一键修复或更新） ----

    private void ShowActionPage()
    {
        var (version, serviceStatus) = _ops.DescribeInstalledState();
        RenderChrome(
            InstallerStrings.Format("action.title", AppDisplayName),
            InstallerStrings.Format("action.subtitle", version, serviceStatus),
            MakeCancelButton());

        var repair = MakeActionCard(Dpi(100), InstallerStrings.Get("action.repair"),
            InstallerStrings.Get("action.repairDesc"), primary: true, StartInstall);

        // 卸载入口：调起已安装的卸载器（其自带确认页与“保留设置”勾选）
        var uninstallTop = Dpi(100) + repair.Height + Dpi(14);
        var uninstall = MakeActionCard(uninstallTop, InstallerStrings.Get("action.uninstall"),
            InstallerStrings.Get("action.uninstallDesc"), primary: false, LaunchUninstaller);
        _ = uninstall;

        AddLanguageDots(uninstallTop + uninstall.Height + Dpi(20), ShowActionPage);
    }

    private void LaunchUninstaller()
    {
        var error = _ops.LaunchUninstaller();
        if (error is not null)
        {
            System.Windows.Forms.MessageBox.Show(this, error, InstallerStrings.Get("wizard.title"),
                MessageBoxButtons.OK, MessageBoxIcon.Warning);
        }
    }

    private ActionCard MakeActionCard(int y, string title, string description, bool primary, Action onClick)
    {
        // 单一自绘表面（ActionCard，见文件尾）：无子控件，整卡同一命中区，点击不再丢失。
        // 卡高按描述换行后的实际高度动态计算——固定值在不同 DPI/字数下会裁掉第二行。
        var u = Math.Max(1, DeviceDpi / 96);
        var descHeight = TextRenderer.MeasureText(description, InstallerTheme.BaseFont,
            new Size(BodyWidth - 32 * u, int.MaxValue), TextFormatFlags.WordBreak).Height;
        var card = new ActionCard(title, description, primary)
        {
            Location = new Point(Pad, y),
            Size = new Size(BodyWidth, Math.Max(Dpi(68), 36 * u + descHeight + 10 * u))
        };
        card.Activated += onClick;
        _content.Controls.Add(card);
        return card;
    }

    private string LanguageSwitchText()
    {
        // 以目标语言书写的一句可执行短语（安装器语言场景的标准做法）
        return InstallerStrings.IsEnglish ? "切换到中文" : "Switch to English";
    }

    // ---- 全新安装：欢迎 → 许可协议 → 安装选项（标准安装流程） ----

    private void ShowWelcomePage()
    {
        var (version, _) = _ops.DescribeInstalledState();
        RenderChrome(InstallerStrings.Get("welcome.title"), null,
            MakeCancelButton(), MakeNextButton(ShowLicensePage));
        _titleLabel.Font = new Font(InstallerTheme.TitleFont.FontFamily, 12.5f, FontStyle.Bold);
        _titleLabel.MaximumSize = new Size(BodyWidth, 0);

        // 品牌行：光谱流光产品名（视觉签名）+ 灰字版本
        var brand = new SpectrumTitle
        {
            Text = AppDisplayName,
            Font = new Font(InstallerTheme.BaseFont.FontFamily, 12F, FontStyle.Bold),
            Location = new Point(Pad, Dpi(58)),
            Size = new Size(BodyWidth, Dpi(30)),
            BackColor = Color.Transparent
        };
        _content.Controls.Add(brand);
        var versionLabel = InstallerTheme.MakeLabel(InstallerStrings.Format("welcome.version", version), color: InstallerTheme.Muted);
        versionLabel.Location = new Point(Pad, Dpi(92));
        _content.Controls.Add(versionLabel);

        var body = InstallerTheme.MakeLabel(InstallerStrings.Format("welcome.body", AppDisplayName), color: InstallerTheme.Muted);
        body.Location = new Point(Pad, Dpi(130));
        body.MaximumSize = new Size(BodyWidth, 0);
        _content.Controls.Add(body);

        AddLanguageDots(Dpi(252), ShowWelcomePage);
    }

    /// <summary>圆点语言选择：实心圆点=当前语言，点击切换并重渲染本页。</summary>
    private void AddLanguageDots(int y, Action reRender)
    {
        var dots = new LanguageDots(InstallerStrings.IsEnglish ? 1 : 0)
        {
            Location = new Point(Pad, y)
        };
        dots.LanguagePicked += _ =>
        {
            _selectedLanguage = InstallerStrings.Current;
            reRender();
        };
        _content.Controls.Add(dots);
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
            Font = new Font("Consolas", 8.5F),
            BackColor = Color.FromArgb(250, 250, 250),
            ForeColor = InstallerTheme.Text,
            BorderStyle = BorderStyle.FixedSingle,
            Location = new Point(Pad, Dpi(80)),
            Size = new Size(BodyWidth, Dpi(216)),
            TabStop = false,
            HideSelection = true
        };
        // ReadOnly 文本框获得首焦点时会出现整篇选中态（蓝色高亮），不取焦点即可避免；
        // 兜底：万一被聚焦（点击）也立即清空选区。
        licenseBox.GotFocus += (_, _) => licenseBox.BeginInvoke(() => licenseBox.Select(0, 0));
        _content.Controls.Add(licenseBox);

        var accept = new CheckBox
        {
            Text = InstallerStrings.Get("lic.accept"),
            ForeColor = InstallerTheme.Text,
            Location = new Point(Pad, Dpi(302)),
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
            Location = new Point(Pad, Dpi(88)),
            AutoSize = true
        };

        var autostart = new CheckBox
        {
            Text = InstallerStrings.Get("opts.autostart"),
            Checked = _autostart,
            ForeColor = InstallerTheme.Text,
            Location = new Point(Pad, Dpi(118)),
            AutoSize = true
        };

        RenderChrome(InstallerStrings.Get("opts.title"), null,
            MakeBackButton(ShowLicensePage), MakeNextButton(StartFromOptions, "opts.install"));

        _content.Controls.Add(shortcut);
        _content.Controls.Add(autostart);

        var note = InstallerTheme.MakeLabel(InstallerStrings.Get("opts.note"), color: InstallerTheme.Faint);
        note.Location = new Point(Pad, Dpi(156));
        note.MaximumSize = new Size(BodyWidth, 0);
        _content.Controls.Add(note);

        void StartFromOptions()
        {
            _desktopShortcut = shortcut.Checked;
            _autostart = autostart.Checked;
            StartInstall();
        }
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

    // ---- 卸载确认 / 卸载进度 ----

    private void ShowUninstallConfirm()
    {
        // 底栏只放"取消"；执行用的警示色"卸载"按钮在内容区，避免出现两个同名按钮
        RenderChrome(
            InstallerStrings.Format("confirm.title", AppDisplayName),
            null,
            MakeCancelButton());

        var text = InstallerTheme.MakeLabel(InstallerStrings.Get("confirm.text"));
        text.Location = new Point(Pad, Dpi(100));
        text.MaximumSize = new Size(BodyWidth - Dpi(6), 0);
        _content.Controls.Add(text);

        var keep = new CheckBox
        {
            Text = InstallerStrings.Get("confirm.keep"),
            ForeColor = InstallerTheme.Text,
            Location = new Point(Pad, text.Bottom + Dpi(20)),
            AutoSize = true
        };
        _content.Controls.Add(keep);

        var uninstallButton = InstallerTheme.MakeButton(InstallerStrings.Get("action.uninstall"), "danger");
        uninstallButton.Width = 110;
        uninstallButton.Location = new Point(Pad, keep.Bottom + Dpi(22));
        uninstallButton.Click += (_, _) =>
        {
            KeepSettings = keep.Checked;
            Result = WizardAction.Uninstall;
            StartUninstall();
        };
        _content.Controls.Add(uninstallButton);
    }

    // ---- 安装 / 卸载进度 ----

    private void StartInstall()
    {
        Result = WizardAction.Install;
        RunOperation(new Operation("progress.install", InstallStepKeys,
            report => _ops.Install(report, _desktopShortcut, _autostart)));
    }

    private void StartUninstall()
    {
        RunOperation(new Operation("progress.uninstall", UninstallStepKeys,
            report => _ops.Uninstall(KeepSettings, report)));
    }

    private void ShowUninstallProgress()
    {
        RunOperation(new Operation("progress.uninstall", UninstallStepKeys,
            report => _ops.Uninstall(KeepSettings, report)));
    }

    private void RunOperation(Operation operation)
    {
        _currentOperation = operation;
        RenderChrome(InstallerStrings.Get(operation.TitleKey),
            InstallerStrings.Format("welcome.subtitle", _sidebarVersion));

        // 连体圆角进度条（浅灰轨道 + 扁平绿）+ 状态行 + 分步清单
        _progress = new ModernProgressBar
        {
            Location = new Point(Pad, Dpi(96)),
            Size = new Size(BodyWidth, Dpi(8)),
            Percent = 0
        };
        _content.Controls.Add(_progress);

        _statusLabel = InstallerTheme.MakeLabel(_demo ? "demo…" : "", color: InstallerTheme.Faint);
        _statusLabel.Location = new Point(Pad, Dpi(118));
        _content.Controls.Add(_statusLabel);

        _stepMarkers.Clear();
        var y = Dpi(156);
        foreach (var key in operation.StepKeys)
        {
            var marker = InstallerTheme.MakeLabel("○", color: InstallerTheme.Faint);
            marker.Font = new Font(InstallerTheme.BaseFont.FontFamily, 9.5f, FontStyle.Bold);
            marker.Location = new Point(Pad, y);
            var text = InstallerTheme.MakeLabel(InstallerStrings.Get("step." + key));
            text.Location = new Point(Pad + Dpi(26), y + 1);
            _content.Controls.Add(marker);
            _content.Controls.Add(text);
            _stepMarkers.Add((key, marker));
            y += Dpi(28);
        }

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
            if (_progress is not null && _stepMarkers.Count > 0)
            {
                _progress.Percent = Math.Min(100, _progress.Percent + 100 / _stepMarkers.Count);
                _progress.Invalidate();
            }
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

        var body = InstallerTheme.MakeLabel(message, color: InstallerTheme.Muted);
        body.Location = new Point(Pad, Dpi(96));
        body.MaximumSize = new Size(BodyWidth, 0);
        _content.Controls.Add(body);

        // 分割线与操作区跟随正文实际高度（多行信息块高度不可硬编码）
        var dividerY = body.Bottom + Dpi(18);
        var divider = new Panel
        {
            Location = new Point(Pad, dividerY),
            Size = new Size(BodyWidth, Dpi(1)),
            BackColor = InstallerTheme.CardBorder
        };
        _content.Controls.Add(divider);

        if (Result == WizardAction.Install)
        {
            // 标准完成页：勾选“立即运行”（仅安装；卸载完成页不需要）
            var launch = new CheckBox
            {
                Text = InstallerStrings.Format("done.launch", AppDisplayName),
                Checked = true,
                ForeColor = InstallerTheme.Text,
                Location = new Point(Pad, dividerY + Dpi(22)),
                AutoSize = true
            };
            launch.CheckedChanged += (_, _) => LaunchAfterInstall = launch.Checked;
            _content.Controls.Add(launch);
        }
    }

    private Button MakeFinishButton()
    {
        var button = InstallerTheme.MakeButton(InstallerStrings.Get("wizard.finish"));
        button.Width = Dpi(96);
        button.Click += (_, _) => Close();
        AcceptButton = button;
        return button;
    }

    // ---- 失败页 ----

    private void ShowFailed(Operation operation, Exception ex)
    {
        RenderChrome("✕ " + InstallerStrings.Get("fail.title"), null,
            MakeCancelButton(), MakeRetryButton(operation));

        var message = InstallerTheme.MakeLabel(ex.Message, color: InstallerTheme.Danger);
        message.Location = new Point(Pad, Dpi(100));
        message.MaximumSize = new Size(BodyWidth - Dpi(6), 0);
        _content.Controls.Add(message);

        var log = InstallerTheme.MakeLabel(
            InstallerStrings.Get("fail.log") + "\n" + Program.InstallerLogPath,
            color: InstallerTheme.Faint);
        log.Location = new Point(Pad, Dpi(210));
        log.MaximumSize = new Size(BodyWidth - Dpi(6), 0);
        _content.Controls.Add(log);
    }

    private Button MakeRetryButton(Operation operation)
    {
        var button = InstallerTheme.MakeButton(InstallerStrings.Get("wizard.retry"));
        button.Width = Dpi(88);
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

/// <summary>光谱流光品牌字：七色渐变填充文字，渐变随时间平移形成流光（产品视觉签名，与托盘关于页同源）。</summary>
internal sealed class SpectrumTitle : Control
{
    private readonly System.Windows.Forms.Timer _timer = new() { Interval = 60 };
    private float _offset;

    public SpectrumTitle()
    {
        SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer |
                 ControlStyles.UserPaint | ControlStyles.SupportsTransparentBackColor | ControlStyles.ResizeRedraw, true);
        TabStop = false;
        _timer.Tick += (_, _) =>
        {
            // 单向平移；TileFlipXY 镜像平铺使颜色永远连续，无需回绕
            _offset += 3f;
            Invalidate();
        };
        _timer.Start();
    }

    protected override void OnPaint(PaintEventArgs e)
    {
        if (string.IsNullOrEmpty(Text) || Width <= 0 || Height <= 0) return;
        var g = e.Graphics;
        g.SmoothingMode = SmoothingMode.AntiAlias;
        g.TextRenderingHint = System.Drawing.Text.TextRenderingHint.ClearTypeGridFit;
        using var path = new GraphicsPath();
        var emSize = Font.Size * g.DpiY / 72f;
        path.AddString(Text, Font.FontFamily, (int)Font.Style, emSize,
            new PointF(0, 0), StringFormat.GenericDefault);
        var bounds = new Rectangle(0, 0, Math.Max(1, Width), Math.Max(1, Height));
        using var brush = new LinearGradientBrush(bounds, Color.Empty, Color.Empty, 0f)
        {
            WrapMode = WrapMode.TileFlipXY
        };
        var spectrum = InstallerTheme.Spectrum;
        var blend = new ColorBlend(spectrum.Length)
        {
            Colors = spectrum,
            Positions = Enumerable.Range(0, spectrum.Length)
                .Select(i => (float)i / (spectrum.Length - 1)).ToArray()
        };
        brush.InterpolationColors = blend;
        brush.TranslateTransform(_offset, 0);
        g.FillPath(brush, path);
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing) _timer.Dispose();
        base.Dispose(disposing);
    }
}

/// <summary>圆点语言选择：实心圆点=当前语言，空心=可切换；点击即切换并通知宿主重渲染。</summary>
internal sealed class LanguageDots : Control
{
    private static readonly string[] Labels = ["中文", "English"];
    private static readonly string[] Codes = [InstallerStrings.ZhCn, InstallerStrings.EnUs];
    private int _current;

    public event Action<int>? LanguagePicked;

    public LanguageDots(int current)
    {
        _current = Math.Clamp(current, 0, 1);
        SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer |
                 ControlStyles.UserPaint | ControlStyles.SupportsTransparentBackColor | ControlStyles.ResizeRedraw, true);
        TabStop = false;
        BackColor = Color.Transparent;
        Cursor = Cursors.Hand;
        var u = Math.Max(1, DeviceDpi / 96);
        Width = 220 * u;
        Height = 26 * u;
    }

    protected override void OnPaint(PaintEventArgs e)
    {
        var g = e.Graphics;
        g.SmoothingMode = SmoothingMode.AntiAlias;
        g.TextRenderingHint = System.Drawing.Text.TextRenderingHint.ClearTypeGridFit;
        int x = 0;
        for (var i = 0; i < Labels.Length; i++)
        {
            var active = i == _current;
            var u = Math.Max(1, DeviceDpi / 96);
            var dotRect = new Rectangle(x, Height / 2 - 4 * u, 8 * u, 8 * u);
            if (active)
            {
                using var fill = new SolidBrush(InstallerTheme.Text);
                g.FillEllipse(fill, dotRect);
            }
            else
            {
                using var pen = new Pen(InstallerTheme.Faint, 1.4f);
                g.DrawEllipse(pen, dotRect);
            }
            using var font = new Font(Font.FontFamily, 9.5F, active ? FontStyle.Bold : FontStyle.Regular);
            TextRenderer.DrawText(g, Labels[i], font,
                new Point(x + 14 * u, Height / 2 - TextRenderer.MeasureText(Labels[i], font).Height / 2),
                active ? InstallerTheme.Text : InstallerTheme.Muted);
            x += 14 * u + TextRenderer.MeasureText(Labels[i], font).Width + 26 * u;
        }
    }

    protected override void OnMouseDown(MouseEventArgs e)
    {
        base.OnMouseDown(e);
            var u = Math.Max(1, DeviceDpi / 96);
            var x = 0;
        for (var i = 0; i < Labels.Length; i++)
        {
            using var font = new Font(Font.FontFamily, 9.5F, i == _current ? FontStyle.Bold : FontStyle.Regular);
            var groupWidth = 14 * u + TextRenderer.MeasureText(Labels[i], font).Width + 26 * u;
            if (e.X >= x && e.X < x + groupWidth)
            {
                if (i != _current)
                {
                    _current = i;
                    InstallerStrings.Set(Codes[i]);
                    Invalidate();
                    LanguagePicked?.Invoke(i);
                }
                return;
            }
            x += groupWidth;
        }
    }
}

/// <summary>可点击的行动卡片（自绘单一表面）：左侧色条 + 标题 + 描述 + 边框 + 悬停高亮。</summary>
internal sealed class ActionCard : Control
{
    private readonly string _title;
    private readonly string _description;
    private readonly bool _primary;
    private bool _hover;

    public event Action? Activated;

    public ActionCard(string title, string description, bool primary)
    {
        _title = title;
        _description = description;
        _primary = primary;
        SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer |
                 ControlStyles.UserPaint | ControlStyles.SupportsTransparentBackColor | ControlStyles.ResizeRedraw, true);
        BackColor = InstallerTheme.Card;
        Cursor = Cursors.Hand;
    }

    protected override void OnMouseEnter(EventArgs e)
    {
        base.OnMouseEnter(e);
        _hover = true;
        Invalidate();
    }

    protected override void OnMouseLeave(EventArgs e)
    {
        base.OnMouseLeave(e);
        _hover = false;
        Invalidate();
    }

    protected override void OnClick(EventArgs e)
    {
        base.OnClick(e);
        Activated?.Invoke();
    }

    protected override void OnPaint(PaintEventArgs e)
    {
        var g = e.Graphics;
        g.SmoothingMode = SmoothingMode.AntiAlias;
        g.Clear(_hover ? Color.FromArgb(238, 244, 252) : InstallerTheme.Card);
        var u = Math.Max(1, DeviceDpi / 96);
        using (var accent = new SolidBrush(_primary ? InstallerTheme.Accent : InstallerTheme.CardBorder))
        {
            g.FillRectangle(accent, 0, 0, 4 * u, Height);
        }
        TextRenderer.DrawText(g, _title, new Font(Font.FontFamily, 10F, FontStyle.Bold),
            new Point(16 * u, 10 * u), InstallerTheme.Text);
        TextRenderer.DrawText(g, _description, Font,
            new Rectangle(16 * u, 36 * u, Width - 32 * u, Height - 36 * u),
            InstallerTheme.Muted, TextFormatFlags.WordBreak | TextFormatFlags.EndEllipsis);
        using var pen = new Pen(InstallerTheme.CardBorder);
        g.DrawRectangle(pen, 0, 0, Width - 1, Height - 1);
    }
}
