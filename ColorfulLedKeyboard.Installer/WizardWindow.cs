using ColorfulLedKeyboard.Core;
using System.Drawing;
using System.Windows.Forms;

namespace ColorfulLedKeyboard.Installer;

/// <summary>引擎操作委托：由 Program 注入，向导只负责编排页面与展示。</summary>
internal sealed record WizardOps(
    Action<Action<string>> Install,
    Action<bool, Action<string>> Uninstall,
    Func<string> InstallDoneText,
    Func<bool, string> UninstallDoneText,
    Func<(string Version, string ServiceStatus)> DescribeInstalledState);

/// <summary>
/// 单窗口安装向导：语言选择 → 操作选择（已安装时）→ 卸载确认 → 进度 → 完成/失败。
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
    private readonly List<(string Key, Label Marker)> _stepMarkers = [];
    private Label _statusLabel = new();
    private Operation? _currentOperation;
    private string _selectedLanguage = "auto";

    public WizardAction Result { get; private set; } = WizardAction.None;
    public bool KeepSettings { get; private set; }

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

    /// <summary>安装向导（新装或已安装）：从语言选择页开始。
    /// previewPage 供 /preview 截图验收直跳指定页："action" / "progress" / "done"。</summary>
    public static WizardWindow ForSetup(bool installed, bool demo, WizardOps ops, string? previewPage = null)
    {
        var wizard = new WizardWindow(ops, installed, demo);
        switch (previewPage)
        {
            case "action":
                wizard._selectedLanguage = InstallerStrings.Current;
                wizard.ShowActionPage();
                break;
            case "progress":
                wizard.StartInstall();
                break;
            case "done":
                wizard.Result = WizardAction.Install;
                wizard.ShowDone("progress.install");
                break;
            default:
                wizard.ShowLanguagePage();
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
        var titleLabel = InstallerTheme.MakeLabel(title, InstallerTheme.TitleFont);
        titleLabel.Location = new Point(28, 26);
        _content.Controls.Clear();
        _content.Controls.Add(titleLabel);
        if (subtitle is not null)
        {
            var subtitleLabel = InstallerTheme.MakeLabel(subtitle, color: InstallerTheme.Muted);
            subtitleLabel.Location = new Point(28, 64);
            _content.Controls.Add(subtitleLabel);
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

        var heading = InstallerTheme.MakeLabel(InstallerStrings.Get("lang.title"), InstallerTheme.SectionFont);
        heading.Location = new Point(28, 116);
        _content.Controls.Add(heading);

        _content.Controls.Add(MakeLanguageOption(164, "中文（简体）", InstallerStrings.ZhCn));
        _content.Controls.Add(MakeLanguageOption(232, "English", InstallerStrings.EnUs));

        var hint = InstallerTheme.MakeLabel(InstallerStrings.Get("lang.hint"), color: InstallerTheme.Faint);
        hint.Location = new Point(28, 306);
        _content.Controls.Add(hint);
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
            // 事件回调内重建控件树需要延迟一拍，避免在样式切换中途析构
            BeginInvoke(ShowLanguagePage);
        };
        return radio;
    }

    private Button MakeContinueFromLanguageButton()
    {
        var button = InstallerTheme.MakeButton(InstallerStrings.Get("wizard.continue"), "primary");
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

    private Button MakeCancelButton()
    {
        var button = InstallerTheme.MakeButton(InstallerStrings.Get("wizard.cancel"), "ghost");
        button.Width = 100;
        button.Click += (_, _) => Close();
        return button;
    }

    // ---- 操作选择页（已安装） ----

    private void ShowActionPage()
    {
        var (version, serviceStatus) = _ops.DescribeInstalledState();
        RenderChrome(
            InstallerStrings.Format("action.title", AppDisplayName),
            InstallerStrings.Format("action.subtitle", version, serviceStatus),
            MakeCancelButton());

        MakeActionCard(104, InstallerStrings.Get("action.repair"),
            InstallerStrings.Get("action.repairDesc"), primary: true, StartInstall);
        MakeActionCard(186, InstallerStrings.Get("action.uninstall"),
            InstallerStrings.Get("action.uninstallDesc"), primary: false, ShowUninstallConfirm);
    }

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
        RunOperation(new Operation("progress.install", InstallStepKeys, report => _ops.Install(report)));
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
