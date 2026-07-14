using ColorfulLedKeyboard.Core;
using System.Diagnostics;
using System.Reflection;

namespace ColorfulLedKeyboard.Tray;

public sealed partial class SettingsForm
{
    private readonly Label _pageTitle = new();
    private readonly Label _headerStatus = new();
    private readonly Button _themeQuickButton = new();
    private readonly Label _dirtyLabel = new();
    private readonly Button _revertButton = new();
    private readonly Button _applyButton = new();
    private readonly Label _overviewMode = OverviewValueLabel();
    private readonly Label _overviewRule = OverviewValueLabel();
    private readonly Label _overviewBrightness = OverviewValueLabel();
    private readonly Label _overviewBrightnessHint = OverviewHintLabel();
    private readonly Label _overviewPlayer = OverviewValueLabel();
    private readonly Label _overviewEvents = OverviewValueLabel();
    private readonly Label _overviewService = OverviewValueLabel();
    private UiStateStore _uiStateStore = null!;
    private UiState _initialUiState = new();
    private KeyboardSettings _loadedSettingsSnapshot = new();
    private bool _themeChanged;
    private bool _settingsChanged;
    private bool _allowClose;
    private bool _lastServiceReady;
    private bool _lastComponentReady;

    private Panel BuildOverviewPage()
    {
        var page = CreatePage();
        var firstRow = new FlowLayoutPanel
        {
            Width = UiMetrics.ContentWidth + 32,
            Height = 132,
            FlowDirection = FlowDirection.LeftToRight,
            WrapContents = false,
            Margin = new Padding(0, 0, 0, 14)
        };
        firstRow.Controls.AddRange([
            OverviewCard("当前模式", _overviewMode, "应用正在使用的基础灯效"),
            OverviewCard("最终亮度", _overviewBrightness, _overviewBrightnessHint),
            OverviewCard("运行状态", _overviewService, "服务与厂商灯控组件")
        ]);

        var runtime = new UiCard("当前场景",
            OverviewRow("命中规则", _overviewRule),
            OverviewRow("播放器", _overviewPlayer),
            OverviewRow("事件反馈", _overviewEvents));

        var quickActions = new FlowLayoutPanel { Width = UiMetrics.ContentWidth, Height = 48, FlowDirection = FlowDirection.LeftToRight };
        var lighting = new Button { Text = "打开灯效设置", Width = 130, Height = UiMetrics.ButtonHeight };
        lighting.Click += (_, _) => SelectPage(1);
        var music = new Button { Text = "打开音乐模式", Width = 130, Height = UiMetrics.ButtonHeight };
        music.Click += (_, _) => SelectPage(2);
        var automation = new Button { Text = "管理场景自动化", Width = 145, Height = UiMetrics.ButtonHeight };
        automation.Click += (_, _) => SelectPage(3);
        quickActions.Controls.AddRange([lighting, music, automation]);
        var quick = new UiCard("快捷操作", quickActions);

        page.Controls.Add(firstRow);
        page.Controls.Add(runtime);
        page.Controls.Add(quick);
        return page;
    }

    private Panel BuildAboutPage()
    {
        var page = CreatePage();
        var version = Assembly.GetExecutingAssembly().GetName().Version?.ToString(3) ?? "0.0.0";
        var title = new Label
        {
            Text = "ClevoLEDKeyboardControl",
            Width = UiMetrics.ContentWidth,
            Height = 36,
            Font = new Font("Segoe UI Semibold", 15F)
        };
        var description = new Label
        {
            Text = $"版本 {version}\r\n面向 Clevo 兼容机型的键盘背光灯效控制程序。",
            Width = UiMetrics.ContentWidth,
            Height = 56
        };
        var actions = new FlowLayoutPanel { Width = UiMetrics.ContentWidth, Height = 46 };
        var repository = new Button { Text = "打开 GitHub 仓库", Width = 145, Height = UiMetrics.ButtonHeight };
        repository.Click += (_, _) => Process.Start(new ProcessStartInfo("https://github.com/silent-ram/ClevoLEDKeyboardControl") { UseShellExecute = true });
        var issues = new Button { Text = "反馈问题", Width = 112, Height = UiMetrics.ButtonHeight };
        issues.Click += (_, _) => Process.Start(new ProcessStartInfo("https://github.com/silent-ram/ClevoLEDKeyboardControl/issues") { UseShellExecute = true });
        actions.Controls.AddRange([repository, issues]);
        page.Controls.Add(new UiCard("关于", title, description, actions));
        return page;
    }

    private UiCard BuildThemeSelector()
    {
        var hint = new Label
        {
            Text = "选择后立即预览，并保存到当前 Windows 用户。",
            Width = UiMetrics.ContentWidth,
            Height = 28,
            ForeColor = ThemeManager.Current.MutedText
        };
        var row = new FlowLayoutPanel
        {
            Width = UiMetrics.ContentWidth,
            Height = 92,
            FlowDirection = FlowDirection.LeftToRight,
            WrapContents = false
        };
        foreach (var kind in Enum.GetValues<UiThemeKind>())
        {
            var theme = UiTheme.For(kind);
            var button = new ThemePreviewButton(kind)
            {
                Width = 245,
                Height = 78,
                Margin = new Padding(0, 0, 12, 0),
                AccessibleName = $"选择{theme.DisplayName}"
            };
            button.Click += (_, _) => SelectTheme(kind);
            row.Controls.Add(button);
        }
        return new UiCard("界面主题", hint, row);
    }

    private void ShowThemeMenu(Control anchor)
    {
        var menu = new ContextMenuStrip();
        foreach (var kind in Enum.GetValues<UiThemeKind>())
        {
            var item = new ToolStripMenuItem(UiTheme.For(kind).DisplayName)
            {
                Checked = ThemeManager.CurrentKind == kind
            };
            item.Click += (_, _) => SelectTheme(kind);
            menu.Items.Add(item);
        }
        ThemeManager.Apply(menu);
        menu.Show(anchor, new Point(0, anchor.Height + 2));
    }

    private void SelectTheme(UiThemeKind kind)
    {
        if (ThemeManager.CurrentKind == kind) return;
        _uiStateStore.Update(state => state.Theme = kind);
        ThemeManager.SetTheme(kind);
        _themeChanged = kind != _initialUiState.Theme;
        UpdateSaveBar();
    }

    private static string ThemeQuickName(UiThemeKind kind) => kind switch
    {
        UiThemeKind.Technology => "白色科技风",
        UiThemeKind.Warm => "柔和暖色风",
        _ => "Windows 11"
    };

    private void OnThemeChanged(object? sender, EventArgs e)
    {
        if (IsDisposed) return;
        ThemeManager.Apply(this);
        _themeQuickButton.Text = $"主题：{ThemeQuickName(ThemeManager.CurrentKind)}  ▾";
        UpdateModeAvailability();
        UpdateStatusHeader();
        UpdateSaveBar();
        Invalidate(true);
    }

    private void RestoreWindowState(UiState state)
    {
        if (state.WindowX == int.MinValue || state.WindowY == int.MinValue)
        {
            StartPosition = FormStartPosition.CenterScreen;
            ClientSize = new Size(state.WindowWidth, state.WindowHeight);
            return;
        }
        var workAreas = Screen.AllScreens.Select(screen => screen.WorkingArea).ToArray();
        var bounds = UiStateStore.EnsureVisible(
            new Rectangle(state.WindowX, state.WindowY, state.WindowWidth, state.WindowHeight), workAreas);
        StartPosition = FormStartPosition.Manual;
        Bounds = bounds;
    }

    private void PersistWindowState()
    {
        var bounds = WindowState == FormWindowState.Normal ? Bounds : RestoreBounds;
        _uiStateStore.Update(state =>
        {
            state.Theme = ThemeManager.CurrentKind;
            state.WindowX = bounds.X;
            state.WindowY = bounds.Y;
            state.WindowWidth = bounds.Width;
            state.WindowHeight = bounds.Height;
            state.LastPage = _navigation?.SelectedIndex ?? 0;
            state.MusicAdvancedExpanded = _musicAdvanced.Checked;
        });
    }

    private void RevertChanges()
    {
        _loadingSettings = true;
        try
        {
            ThemeManager.SetTheme(_initialUiState.Theme);
            _uiStateStore.Save(_initialUiState.Clone());
            _themeChanged = false;
            _settingsChanged = false;
        }
        finally
        {
            _loadingSettings = false;
        }
        LoadSettings();
        _loadingSettings = true;
        _musicAdvanced.Checked = _initialUiState.MusicAdvancedExpanded;
        _loadingSettings = false;
        Text = "ClevoLEDKeyboardControl 设置";
        UpdateSaveBar();
    }

    private void OnSettingsFormClosing(object? sender, FormClosingEventArgs e)
    {
        if (_allowClose || !HasUnsavedChanges) return;
        var result = MessageBox.Show("存在尚未保存的修改，是否放弃这些修改？", "ClevoLEDKeyboardControl",
            MessageBoxButtons.YesNo, MessageBoxIcon.Question);
        if (result == DialogResult.No)
        {
            e.Cancel = true;
            return;
        }
        ThemeManager.SetTheme(_initialUiState.Theme);
        _uiStateStore.Save(_initialUiState.Clone());
        _themeChanged = false;
        _allowClose = true;
    }

    private bool HasUnsavedChanges => _themeChanged || _settingsChanged;

    private void MarkDirty()
    {
        if (_loadingSettings) return;
        _settingsChanged = true;
        Text = "ClevoLEDKeyboardControl 设置 - 有未应用的更改";
    }

    private void UpdateSaveBar()
    {
        var dirty = HasUnsavedChanges;
        _dirtyLabel.Text = dirty ? "● 有尚未保存的修改" : "✓ 设置已保存";
        _dirtyLabel.ForeColor = dirty ? ThemeManager.Current.Warning : ThemeManager.Current.Success;
        _applyButton.Enabled = dirty;
        _revertButton.Enabled = dirty;
        ThemeManager.StyleButton(_applyButton, ThemeManager.Current);
        ThemeManager.StyleButton(_revertButton, ThemeManager.Current);
    }

    private void WireDirtyTracking(Control root)
    {
        foreach (Control control in root.Controls)
        {
            switch (control)
            {
                case NavigationListBox:
                    break;
                case CheckBox checkBox:
                    checkBox.CheckedChanged += (_, _) => MarkDirty();
                    break;
                case RadioButton radio:
                    radio.CheckedChanged += (_, _) => MarkDirty();
                    break;
                case ComboBox combo:
                    combo.SelectedIndexChanged += (_, _) => MarkDirty();
                    break;
                case TrackBar track:
                    track.ValueChanged += (_, _) => MarkDirty();
                    break;
                case NumericUpDown numeric:
                    numeric.ValueChanged += (_, _) => MarkDirty();
                    break;
                case TextBox textBox when !textBox.ReadOnly:
                    textBox.TextChanged += (_, _) => MarkDirty();
                    break;
            }
            WireDirtyTracking(control);
        }
    }

    private void UpdateEventFeedbackVisibility()
    {
        _typingPulsePeakBrightness.Visible = _typingPulseEnabled.Checked;
        _typingPulseHold.Visible = _typingPulseEnabled.Checked;
        _typingPulseFade.Visible = _typingPulseEnabled.Checked;
        _notificationFlashColor.Visible = _notificationFlashEnabled.Checked;
        _notificationFlashPulses.Visible = _notificationFlashEnabled.Checked;
        _notificationFlashCooldown.Visible = _notificationFlashEnabled.Checked;
    }

    private void SelectPage(int index)
    {
        if (_navigation is not null && index >= 0 && index < _navigation.Items.Count)
            _navigation.SelectedIndex = index;
    }

    private void UpdateOverviewStatus(DiagnosticsSnapshot diagnostics, bool serviceReady, bool componentReady)
    {
        _lastServiceReady = serviceReady;
        _lastComponentReady = componentReady;
        var status = AutomationStatus.Load();
        UpdateOverviewRuntime(status);
    }

    private void UpdateOverviewRuntime(AutomationStatus? status)
    {
        var settings = _loadedSettingsSnapshot;
        var fresh = status is not null && DateTimeOffset.UtcNow - status.UpdatedUtc <= TimeSpan.FromSeconds(10);
        _overviewMode.Text = !settings.Enabled || fresh && status!.FinalBrightnessLimit == 0
            ? "灯光已关闭"
            : fresh && (!string.IsNullOrWhiteSpace(status!.ActiveMusicApplication) || status.TargetDescription.StartsWith("音乐：", StringComparison.Ordinal))
                ? "音乐模式"
                : settings.OperatingMode == OperatingMode.Music ? "音乐模式" : "灯效模式";
        _overviewBrightness.Text = fresh && !string.IsNullOrWhiteSpace(status!.BrightnessDisplay)
            ? status.BrightnessDisplay
            : settings.OperatingMode == OperatingMode.Music
                ? $"{settings.Effect.Music.BaseBrightness}–{settings.Effect.Music.PeakBrightness}%"
                : $"{settings.Brightness}%";
        _overviewBrightnessHint.Text = fresh && !string.IsNullOrWhiteSpace(status!.BrightnessDescription)
            ? status.BrightnessDescription
            : "等待服务计算最终输出";
        _overviewRule.Text = fresh && !string.IsNullOrWhiteSpace(status!.ActiveRuleName)
            ? $"{status.ActiveRuleName} → {status.TargetDescription}"
            : settings.Automation.Enabled ? "当前使用基础设置" : "场景自动化未启用";
        _overviewPlayer.Text = fresh && !string.IsNullOrWhiteSpace(status!.ActiveMusicApplication)
            ? $"{status.ActiveMusicApplication} · {(string.IsNullOrWhiteSpace(status.TrackTitle) ? "检测到声音" : status.TrackTitle)}"
            : "当前没有匹配的有声程序";
        var typing = settings.TypingPulse.Enabled ? "打字开启" : "打字关闭";
        var notification = settings.NotificationFlash.Enabled ? "通知开启" : "通知关闭";
        _overviewEvents.Text = $"{typing} · {notification}{(fresh && status!.IdleOverrideActive ? " · 空闲覆盖中" : "")}";
        if (!fresh)
        {
            _overviewService.Text = "状态未更新";
            _overviewService.ForeColor = ThemeManager.Current.Warning;
        }
        else
        {
            var ready = _lastServiceReady && _lastComponentReady;
            _overviewService.Text = ready ? "输出正常" : "需要检查";
            _overviewService.ForeColor = ready ? ThemeManager.Current.Success : ThemeManager.Current.Warning;
        }
    }

    private static Label OverviewValueLabel() => new()
    {
        AutoEllipsis = true,
        AutoSize = false,
        Width = 230,
        Height = 28,
        Font = new Font("Segoe UI Semibold", 10.5F)
    };

    private static UiCard OverviewCard(string title, Label value, string hint)
    {
        return OverviewCard(title, value, new Label { Text = hint, Width = 230, Height = 28, ForeColor = ThemeManager.Current.MutedText });
    }

    private static UiCard OverviewCard(string title, Label value, Label hint)
    {
        var card = new UiCard(title, value, hint) { Width = 264, Height = 122, AutoSize = false, Margin = new Padding(0, 0, 12, 0) };
        return card;
    }

    private static Label OverviewHintLabel() => new()
    {
        Width = 230,
        Height = 28,
        ForeColor = ThemeManager.Current.MutedText
    };

    private static Panel OverviewRow(string label, Control value)
    {
        var panel = new Panel { Width = UiMetrics.ContentWidth, Height = 42 };
        panel.Controls.Add(new Label { Text = label, Width = 130, Height = 28, Location = new Point(0, 7), ForeColor = ThemeManager.Current.MutedText });
        value.Location = new Point(145, 5);
        value.Width = UiMetrics.ContentWidth - 150;
        panel.Controls.Add(value);
        return panel;
    }
}

internal sealed class ThemePreviewButton : Button
{
    public ThemePreviewButton(UiThemeKind kind)
    {
        ThemeKind = kind;
        FlatStyle = FlatStyle.Flat;
        UseVisualStyleBackColor = false;
        Cursor = Cursors.Hand;
    }

    public UiThemeKind ThemeKind { get; }

    protected override void OnPaint(PaintEventArgs e)
    {
        var theme = UiTheme.For(ThemeKind);
        BackColor = theme.Window;
        ForeColor = theme.Text;
        FlatAppearance.BorderColor = ThemeManager.CurrentKind == ThemeKind ? theme.Primary : theme.Border;
        FlatAppearance.BorderSize = ThemeManager.CurrentKind == ThemeKind ? 2 : 1;
        base.OnPaint(e);
        using var accent = new SolidBrush(theme.Primary);
        e.Graphics.FillRectangle(accent, 12, 14, 7, Height - 28);
        var check = ThemeManager.CurrentKind == ThemeKind ? "  ✓" : "";
        using var titleFont = new Font("Segoe UI Semibold", 9.5F);
        TextRenderer.DrawText(e.Graphics, theme.DisplayName + check, titleFont,
            new Rectangle(30, 11, Width - 38, 26), theme.Text, TextFormatFlags.Left | TextFormatFlags.VerticalCenter);
        TextRenderer.DrawText(e.Graphics, ThemeKind switch
        {
            UiThemeKind.Technology => "冷白 · 蓝紫强调 · 利落",
            UiThemeKind.Warm => "米灰 · 蓝绿色 · 柔和",
            _ => "浅灰 · 系统蓝 · 简洁"
        }, Font, new Rectangle(30, 39, Width - 38, 24), theme.MutedText, TextFormatFlags.Left | TextFormatFlags.VerticalCenter);
    }
}
