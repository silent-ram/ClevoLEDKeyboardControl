using ColorfulLedKeyboard.Core;
using System.Diagnostics;
using System.Text.Json;
using static ColorfulLedKeyboard.Tray.UiMetrics;

namespace ColorfulLedKeyboard.Tray;

internal static class UiMetrics
{
    public const int ContentWidth = 800;
    public const int LabelWidth = 150;
    public const int ControlLeft = 165;
    public const int RowHeight = 48;
    public const int ButtonHeight = 34;

    public static int ScaleForDpi(int logicalPixels, int dpi) =>
        Math.Max(1, (int)Math.Round(logicalPixels * Math.Max(96, dpi) / 96d));
}

public sealed partial class SettingsForm : ThemedForm
{
    private const int DefaultRainbowHoldMs = EffectPresetSettings.DefaultPeriodMs;
    private const string SoftwareDefaultPresetName = "软件默认配置";
    private readonly SettingsStore _settingsStore;
    private readonly RadioButton _modeLighting = new() { Text = "灯效模式", AutoSize = true };
    private readonly RadioButton _modeMusic = new() { Text = "音乐模式", AutoSize = true };
    private readonly RadioButton _modeOff = new() { Text = "关闭", AutoSize = true };
    private ListBox? _navigation;
    private readonly ComboBox _effectType = new();
    private readonly SliderRow _brightness = new("全局亮度", 0, 100, "%");
    private readonly ComboBox _speed = new();
    private readonly Button _customColors = new() { Text = "自定义颜色", Width = 128, Height = ButtonHeight };
    private readonly ColorPickerRow _effectColor = new("效果颜色");
    private readonly SliderRow _period = new("呼吸周期", 300, 30000, " ms");
    private readonly SliderRow _minimumBrightness = new("最低亮度", 0, 100, "%");
    private readonly CheckBox _hardBlink = new() { Text = "硬闪烁" };
    private readonly SequenceEditor _sequence = new();
    private Panel? _speedRow;
    private Panel? _customColorsRow;
    private Panel? _hardBlinkRow;
    private Panel? _effectTypeRow;
    private Label? _sequenceSection;
    private Label? _sequenceSummary;
    private Label? _modeHint;
    private Label? _effectPresetSection;
    private Panel? _effectPresetRow;
    private Panel? _effectPresetNameRow;
    private Panel? _effectPresetButtonsRow;
    private readonly ComboBox _effectPreset = new();
    private readonly TextBox _effectPresetName = new();
    private readonly Button _effectSavePreset = new() { Text = "保存修改" };
    private readonly Button _effectCreatePreset = new() { Text = "新建/另存为" };
    private readonly Button _effectDeletePreset = new() { Text = "删除预设" };
    private readonly Button _musicCustomColors = new() { Text = "自定义颜色", Width = 128, Height = ButtonHeight };
    private readonly SequenceEditor _musicSequence = new();
    private readonly CheckBox _musicFollowSystemVolume = new() { Text = "跟随 Windows 系统音量" };
    private readonly ComboBox _musicPreset = new();
    private readonly Label _audioSourceLabel = new()
    {
        AutoSize = true,
        ForeColor = SystemColors.GrayText,
        Text = "当前音频源：检测中…",
        Margin = new Padding(0, 4, 0, 8),
    };
    private readonly TextBox _musicPresetName = new();
    private readonly Button _musicSavePreset = new() { Text = "保存修改" };
    private readonly Button _musicCreatePreset = new() { Text = "新建/另存为" };
    private readonly Button _musicDeletePreset = new() { Text = "删除预设" };
    private readonly CheckBox _musicAdvanced = new() { Text = "显示高级参数" };
    private readonly ComboBox _musicSensitivity = new();
    private readonly ComboBox _musicAttack = new();
    private readonly ComboBox _musicRelease = new();
    private Panel? _musicSensitivityRow;
    private Panel? _musicAttackRow;
    private Panel? _musicReleaseRow;
    private readonly ComboBox _musicResponseMode = new();
    private readonly SliderRow _musicNoiseGate = new("噪声门", 0, 50, "%");
    private readonly SliderRow _musicBeatThreshold = new("节拍阈值", 0, 100, "%");
    private readonly CheckBox _musicEqEnabled = new() { Text = "自适应鼓点检测" };
    private readonly CheckBox _musicSystemMixFallback = new() { Text = "进程独立捕获不可用时，允许系统混音频段分析" };
    private readonly SliderRow _musicEqLow = new("低频参考", 20, 1000, " Hz");
    private readonly SliderRow _musicEqHigh = new("高频参考", 40, 16000, " Hz");
    private readonly SliderRow _musicBaseBrightness = new("基础亮度", 0, 100, "%");
    private readonly SliderRow _musicPeakBrightness = new("峰值亮度", 0, 100, "%");
    private readonly CheckBox _notificationFlashEnabled = new() { Text = "收到 Windows 通知时闪烁键盘" };
    private readonly ColorPickerRow _notificationFlashColor = new("通知闪烁颜色");
    private readonly SliderRow _notificationFlashPulses = new("闪烁次数", 1, 5, " 次");
    private readonly SliderRow _notificationFlashCooldown = new("冷却时间", 1, 60, " 秒");
    private readonly CheckBox _idleEnabled = new() { Text = "启用空闲降亮" };
    private readonly ComboBox _idleAfter = new();
    private readonly SliderRow _idleBrightness = new("空闲亮度", 0, 100, "%");
    private readonly CheckBox _idleTurnOff = new() { Text = "空闲后关闭灯效" };
    private readonly CheckBox _scheduleEnabled = new() { Text = "启用时间计划" };
    private readonly TimeRangePicker _evening = new("傍晚时段");
    private readonly TimeRangePicker _night = new("深夜时段");
    private readonly CheckBox _typingPulseEnabled = new() { Text = "启用敲字闪烁" };
    private readonly SliderRow _typingPulsePeakBrightness = new("触发亮度", 0, 100, "%");
    private readonly SliderRow _typingPulseHold = new("保持时间", 20, 2000, " ms");
    private readonly SliderRow _typingPulseFade = new("回落时间", 50, 5000, " ms");
    private readonly CheckBox _appProfilesEnabled = new() { Text = "启用应用场景配置" };
    private readonly AppProfileEditor _appProfiles = new();
    private readonly CheckBox _automationEnabled = new() { Text = "启用场景自动化" };
    private readonly SceneAutomationEditorV2 _sceneAutomation = new();
    private readonly Label _automationStatus = new()
    {
        AutoSize = true,
        ForeColor = SystemColors.GrayText,
        MaximumSize = new Size(ContentWidth, 0)
    };
    private readonly Label _musicBindingStatus = new() { AutoSize = true, ForeColor = SystemColors.GrayText };
    private readonly Button _musicBindPlayer = new() { Text = "绑定正在播放的程序" };
    private readonly Button _musicClearPlayer = new() { Text = "取消绑定" };
    private readonly ComboBox _musicBindingColorSource = new();
    private readonly ComboBox _musicMediaSession = new();
    private readonly Label _musicCurrentColorStatus = new() { AutoSize = true, ForeColor = SystemColors.GrayText };
    private readonly Label _musicMediaMatchStatus = new() { AutoSize = true, ForeColor = SystemColors.GrayText };
    private readonly PalettePreviewControl _musicPalettePreview = new();
    private readonly System.Windows.Forms.Timer _automationStatusTimer = new() { Interval = 1000 };
    private readonly ComboBox _updateInterval = new();
    private readonly Label _serviceSummary = new();
    private readonly Label _componentSummary = new();
    private readonly Label _controlSummary = new();
    private static readonly double[] MusicSensitivityValues = [0.5, 1.0, 1.5, 2.0, 2.2, 2.5, 2.8, 3.1, 3.4, 3.6, 3.8, 4.0];
    private static readonly int[] MusicAttackValues = [10, 15, 20, 25, 30, 40];
    private static readonly int[] MusicReleaseValues = [70, 80, 90, 100, 110, 120, 130, 140, 150, 160, 170, 180, 190, 200];
    private EffectPresetSettings _effectPresets = new();
    private List<MusicPreset> _musicCustomPresets = [];
    private MusicPlayerBinding _musicPlayerBinding = new();
    private bool _loadingSettings;
    private bool _effectChangedByUser;
    private bool _loadingEffectPreset;
    private bool _loadingMusicPreset;
    private bool _customSequenceColorsEnabled;

    public SettingsForm(SettingsStore settingsStore)
    {
        _settingsStore = settingsStore;
        _uiStateStore = UiStateStore.Shared;
        _initialUiState = _uiStateStore.Load().Clone();
        Text = "ClevoLEDKeyboardControl 设置";
        MinimumSize = new Size(1040, 720);
        RestoreWindowState(_initialUiState);

        BuildUi();
        WireDirtyTracking(this);
        _typingPulseEnabled.CheckedChanged += (_, _) => UpdateEventFeedbackVisibility();
        _notificationFlashEnabled.CheckedChanged += (_, _) => UpdateEventFeedbackVisibility();
        _musicAdvanced.CheckedChanged += (_, _) =>
        {
            if (!_loadingSettings) _uiStateStore.Update(state => state.MusicAdvancedExpanded = _musicAdvanced.Checked);
        };
        _sceneAutomation.Changed += (_, _) => MarkDirty();
        LoadSettings();
        _loadingSettings = true;
        _musicAdvanced.Checked = _initialUiState.MusicAdvancedExpanded;
        _loadingSettings = false;
        UpdateMusicAdvancedVisibility();
        _automationStatusTimer.Tick += (_, _) => UpdateAutomationStatus();
        _automationStatusTimer.Start();
        ThemeManager.ThemeChanged += OnThemeChanged;
        FormClosing += OnSettingsFormClosing;
        FormClosed += (_, _) =>
        {
            PersistWindowState();
            ThemeManager.ThemeChanged -= OnThemeChanged;
            _automationStatusTimer.Dispose();
        };
        TextChanged += (_, _) =>
        {
            if (!_loadingSettings && Text.Contains("未应用", StringComparison.Ordinal)) _settingsChanged = true;
            UpdateSaveBar();
        };
        _effectType.SelectedIndexChanged += (_, _) =>
        {
            if (!_loadingSettings)
            {
                _effectChangedByUser = true;
                MarkDirty();
            }

            UpdateBrightnessAvailability();
            UpdateCustomColorsButton();
            UpdateEffectConfigurationVisibility();
            RefreshEffectPresetList();
        };
        UpdateAudioSourceLabel(AudioSourceStatusFile.Read());
        UpdateEventFeedbackVisibility();
        ThemeManager.Apply(this);
        UpdateStatusHeader();
        UpdateSaveBar();
    }

    public event EventHandler? SettingsSaved;

    public void ReloadFromStore()
    {
        LoadSettings();
        UpdateStatusHeader();
    }

    private void BuildUi()
    {
        var split = new SplitContainer
        {
            Dock = DockStyle.Fill,
            FixedPanel = FixedPanel.Panel1,
            SplitterDistance = 330,
            BackColor = ThemeManager.Current.Border,
            IsSplitterFixed = true
        };
        ThemeManager.SetSurface(split, ThemeSurfaceRole.Window);
        ThemeManager.SetSurface(split.Panel1, ThemeSurfaceRole.Sidebar);
        ThemeManager.SetSurface(split.Panel2, ThemeSurfaceRole.Window);
        split.Panel1MinSize = 190;
        Shown += (_, _) => split.SplitterDistance = UiMetrics.ScaleForDpi(205, DeviceDpi);

        var navigation = new NavigationListBox
        {
            Dock = DockStyle.Fill,
            AccessibleName = "设置页面导航"
        };
        _navigation = navigation;

        var pages = ThemeManager.SetSurface(new Panel { Dock = DockStyle.Fill }, ThemeSurfaceRole.Window);
        var content = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            ColumnCount = 1,
            RowCount = 2
        };
        content.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        content.RowStyles.Add(new RowStyle(SizeType.Absolute, 72));
        content.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        var statusHeader = BuildStatusHeader();
        var pageDefinitions = new (string Title, Panel Page)[]
        {
            ("当前状态", BuildOverviewPage()),
            ("灯效设置", BuildGeneralPage()),
            ("音乐模式", BuildMusicPage()),
            ("场景自动化", BuildAutomationPage()),
            ("事件反馈", BuildEventFeedbackPage()),
            ("诊断与恢复", BuildDiagnosticsPage()),
            ("软件设置", BuildAdvancedPage()),
            ("关于", BuildAboutPage())
        };
        ThemeManager.SetSurface(content, ThemeSurfaceRole.Window);

        foreach (var (title, page) in pageDefinitions)
        {
            navigation.Items.Add(title);
            page.Dock = DockStyle.Fill;
            page.Visible = false;
            pages.Controls.Add(page);
        }

        navigation.SelectedIndexChanged += (_, _) =>
        {
            for (var i = 0; i < pageDefinitions.Length; i++)
            {
                pageDefinitions[i].Page.Visible = i == navigation.SelectedIndex;
            }
            if (navigation.SelectedIndex >= 0)
            {
                _pageTitle.Text = pageDefinitions[navigation.SelectedIndex].Title;
                _uiStateStore.Update(state => state.LastPage = navigation.SelectedIndex);
            }
        };
        navigation.SelectedIndex = Math.Clamp(_initialUiState.LastPage, 0, pageDefinitions.Length - 1);

        split.Panel1.BackColor = ThemeManager.Current.Sidebar;
        split.Panel1.Padding = new Padding(10, 14, 8, 12);
        split.Panel1.Controls.Add(navigation);
        content.Controls.Add(statusHeader, 0, 0);
        content.Controls.Add(pages, 0, 1);
        split.Panel2.Controls.Add(content);

        var buttons = new TableLayoutPanel
        {
            Dock = DockStyle.Bottom,
            Height = 58,
            ColumnCount = 2,
            Padding = new Padding(18, 9, 18, 9),
            BackColor = ThemeManager.Current.Surface
        };
        ThemeManager.SetSurface(buttons, ThemeSurfaceRole.Surface);
        buttons.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        buttons.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));

        _dirtyLabel.AutoSize = true;
        _dirtyLabel.Margin = new Padding(0, 9, 0, 0);
        var actions = new FlowLayoutPanel { AutoSize = true, FlowDirection = FlowDirection.LeftToRight, WrapContents = false };
        _revertButton.Text = "恢复修改";
        _revertButton.Width = 104;
        _revertButton.Height = ButtonHeight;
        _revertButton.Click += (_, _) => RevertChanges();
        _applyButton.Text = "保存并应用";
        _applyButton.Width = 120;
        _applyButton.Height = ButtonHeight;
        _applyButton.AccessibleDescription = "PrimaryAction";
        _applyButton.Click += (_, _) => SaveSettings();
        actions.Controls.Add(_revertButton);
        actions.Controls.Add(_applyButton);
        buttons.Controls.Add(_dirtyLabel, 0, 0);
        buttons.Controls.Add(actions, 1, 0);

        Controls.Add(split);
        Controls.Add(buttons);
        UpdateStatusHeader();
    }

    private Panel BuildStatusHeader()
    {
        var panel = new Panel
        {
            Dock = DockStyle.Fill,
            Padding = new Padding(18, 10, 18, 8),
            BackColor = ThemeManager.Current.Surface
        };
        ThemeManager.SetSurface(panel, ThemeSurfaceRole.Surface);

        _pageTitle.Font = new Font("Segoe UI Semibold", 14F);
        _pageTitle.Location = new Point(18, 17);
        _pageTitle.Size = new Size(300, 32);
        _headerStatus.AutoSize = true;
        _headerStatus.Location = new Point(350, 24);
        _themeQuickButton.Text = $"主题：{ThemeQuickName(ThemeManager.CurrentKind)}  ▾";
        _themeQuickButton.Width = 215;
        _themeQuickButton.Height = ButtonHeight;
        _themeQuickButton.Anchor = AnchorStyles.Top | AnchorStyles.Right;
        _themeQuickButton.Location = new Point(Math.Max(600, panel.Width - 210), 14);
        panel.Resize += (_, _) => _themeQuickButton.Left = Math.Max(520, panel.ClientSize.Width - _themeQuickButton.Width - 18);
        _themeQuickButton.Click += (_, _) => ShowThemeMenu(_themeQuickButton);
        panel.Controls.Add(_pageTitle);
        panel.Controls.Add(_headerStatus);
        panel.Controls.Add(_themeQuickButton);
        return panel;
    }

    private static void ConfigureStatusLabel(Label label, int x, int y)
    {
        label.Location = new Point(x, y);
        label.Size = new Size(280, 28);
        label.AutoEllipsis = true;
    }

    private void UpdateStatusHeader()
    {
        var diagnostics = CollectDiagnostics();
        var serviceReady = diagnostics.ServiceStatus == "运行中";
        var componentReady = diagnostics.DriverStatus.StartsWith("已安装", StringComparison.OrdinalIgnoreCase);

        _serviceSummary.Text = $"服务：{diagnostics.ServiceStatus}";
        _componentSummary.Text = $"厂商灯控组件：{(componentReady ? "正常" : "需要检查")}";
        _componentSummary.Tag = diagnostics.DriverStatus;
        _controlSummary.Text = serviceReady && componentReady
            ? "灯效控制：可用"
            : "灯效控制：需要检查";
        _headerStatus.Text = serviceReady && componentReady ? "● 服务与灯控正常" : "⚠ 需要检查运行状态";
        _headerStatus.ForeColor = serviceReady && componentReady ? ThemeManager.Current.Success : ThemeManager.Current.Warning;
        UpdateOverviewStatus(diagnostics, serviceReady, componentReady);
    }

    private Panel BuildGeneralPage()
    {
        var page = CreatePage();

        var modeRow = new FlowLayoutPanel
        {
            FlowDirection = FlowDirection.LeftToRight,
            AutoSize = true,
            Margin = new Padding(0, 0, 0, 8),
            Width = ContentWidth
        };
        modeRow.Controls.Add(new Label { Text = "模式：", AutoSize = true, Margin = new Padding(0, 6, 8, 0) });
        modeRow.Controls.Add(_modeLighting);
        modeRow.Controls.Add(_modeMusic);
        modeRow.Controls.Add(_modeOff);
        _modeLighting.CheckedChanged += (_, _) => OnModeChanged();
        _modeMusic.CheckedChanged += (_, _) => OnModeChanged();
        _modeOff.CheckedChanged += (_, _) => OnModeChanged();
        _effectType.DropDownStyle = ComboBoxStyle.DropDownList;
        _effectType.Items.AddRange(["固定颜色", "RGB 循环", "单色呼吸", "循环呼吸", "脉冲", "心跳"]);

        _speed.DropDownStyle = ComboBoxStyle.DropDownList;
        _speed.Items.AddRange(["非常慢", "慢", "正常", "快", "很快"]);
        _effectPreset.DropDownStyle = ComboBoxStyle.DropDownList;
        _effectPreset.SelectedIndexChanged += (_, _) =>
        {
            if (_loadingEffectPreset)
            {
                return;
            }

            _effectPresetName.Text = IsSoftwareDefaultEffectPresetSelected() ? "" : SelectedEffectPresetName();
            UpdateEffectPresetButtons();
            ApplySelectedEffectPreset();
        };
        _effectPresetName.Width = 240;
        _effectSavePreset.Click += (_, _) => SaveSelectedEffectPreset();
        _effectCreatePreset.Click += (_, _) => CreateEffectPreset();
        _effectDeletePreset.Click += (_, _) => DeleteSelectedEffectPreset();
        _effectColor.UseCompactEditor = true;
        _sequence.ShowAddButton = false;
        _customColors.Click += (_, _) => EditCustomColors();
        _period.ValueChanged += (_, _) => UpdateEffectConfigurationVisibility();
        _sequence.ColorsChanged += (_, _) =>
        {
            if (SelectedEffectType(EffectType.Rainbow) == EffectType.Rainbow)
            {
                _customSequenceColorsEnabled = true;
            }

            if (!_loadingSettings)
            {
                _effectChangedByUser = true;
                MarkDirty();
            }

            UpdateEffectConfigurationVisibility();
        };

        _speedRow = Row("速度", _speed);
        _customColorsRow = PlainRow(_customColors);
        _hardBlinkRow = PlainRow(_hardBlink);
        _sequenceSection = Section("循环颜色");
        _sequenceSummary = Section("");
        _modeHint = Section("");
        _effectPresetSection = Section("配置预设");
        _effectPresetRow = Row("当前预设", _effectPreset);
        _effectPresetNameRow = Row("当前预设", _effectPresetName);
        _effectPresetButtonsRow = ButtonRow(_effectSavePreset, _effectCreatePreset, _effectDeletePreset);

        _effectTypeRow = Row("当前效果", _effectType);
        page.Controls.Add(new UiCard("模式", modeRow, _modeHint));
        page.Controls.Add(new UiCard("灯效参数", _effectTypeRow, _brightness, _effectColor, _speedRow,
            _period, _minimumBrightness, _hardBlinkRow, _customColorsRow, _sequenceSection, _sequenceSummary, _sequence));
        page.Controls.Add(new UiCard("配置预设", _effectPresetRow, _effectPresetNameRow, _effectPresetButtonsRow));
        return page;
    }

    private Panel BuildMusicPage()
    {
        var page = CreatePage();
        _musicPreset.DropDownStyle = ComboBoxStyle.DropDownList;
        _musicResponseMode.DropDownStyle = ComboBoxStyle.DropDownList;
        _musicResponseMode.Items.AddRange(["按电平变色", "仅亮度脉冲"]);
        _musicBindingColorSource.DropDownStyle = ComboBoxStyle.DropDownList;
        _musicBindingColorSource.Items.AddRange(["使用音乐预设颜色", "使用歌曲封面主色", "使用歌曲封面配色"]);
        _musicBindingColorSource.SelectedIndex = 0;
        _musicBindingColorSource.SelectedIndexChanged += (_, _) => RefreshMusicBindingStatus();
        _musicMediaSession.DropDownStyle = ComboBoxStyle.DropDownList;
        _musicMediaSession.Items.Add("自动匹配播放器");
        _musicMediaSession.SelectedIndex = 0;
        _musicMediaSession.SelectedIndexChanged += (_, _) =>
        {
            if (_loadingSettings || _musicMediaSession.SelectedIndex < 0) return;
            _musicPlayerBinding.MediaSessionId = _musicMediaSession.SelectedIndex == 0
                ? ""
                : _musicMediaSession.SelectedItem?.ToString() ?? "";
            RefreshMusicBindingStatus();
        };
        _musicBindPlayer.Click += (_, _) => BindMusicPlayer();
        _musicClearPlayer.Click += (_, _) => ClearMusicPlayerBinding();
        SetupCombo(_musicSensitivity, MusicSensitivityValues.Select(value => $"{value:0.0}x"));
        SetupCombo(_musicAttack, MusicAttackValues.Select(value => $"{value} ms"));
        SetupCombo(_musicRelease, MusicReleaseValues.Select(value => $"{value} ms"));
        _musicPresetName.Width = 220;
        _musicPreset.SelectedIndexChanged += (_, _) =>
        {
            if (_loadingMusicPreset)
            {
                return;
            }

            if (FindSelectedMusicPreset() is { } preset)
            {
                ApplyMusicPresetToControls(preset, refreshSelection: false);
            }

            UpdateMusicPresetButtons();
        };
        _musicSavePreset.Click += (_, _) => SaveSelectedMusicPreset();
        _musicCreatePreset.Click += (_, _) => CreateMusicPreset();
        _musicDeletePreset.Click += (_, _) => DeleteSelectedCustomMusicPreset();
        _musicCustomColors.Click += (_, _) => EditMusicColors();
        _musicSequence.ShowAddButton = false;
        _musicSequence.ColorsChanged += (_, _) => Text = "ClevoLEDKeyboardControl 设置 - 有未应用的更改";
        _musicAdvanced.CheckedChanged += (_, _) => UpdateMusicAdvancedVisibility();

        _musicSensitivityRow = Row("灵敏度", _musicSensitivity);
        _musicAttackRow = Row("响应速度", _musicAttack);
        _musicReleaseRow = Row("衰减速度", _musicRelease);
        page.Controls.Add(new UiCard("播放器与当前配色", _audioSourceLabel, _musicBindingStatus,
            ButtonRow(_musicBindPlayer, _musicClearPlayer), Row("键盘颜色来源", _musicBindingColorSource),
            Row("歌曲封面来源", _musicMediaSession), _musicMediaMatchStatus,
            Row("当前实际颜色", _musicPalettePreview), _musicCurrentColorStatus));
        page.Controls.Add(new UiCard("音乐预设与响应", Row("音乐预设", _musicPreset),
            ButtonRow(_musicSavePreset, _musicCreatePreset, _musicDeletePreset), Row("当前预设", _musicPresetName),
            Row("音乐响应", _musicResponseMode), PlainRow(_musicCustomColors), Section("节拍颜色"), _musicSequence,
            _musicBaseBrightness, _musicPeakBrightness, PlainRow(_musicFollowSystemVolume)));
        page.Controls.Add(new UiCard("高级音乐参数", PlainRow(_musicAdvanced), _musicSensitivityRow,
            _musicAttackRow, _musicReleaseRow, _musicNoiseGate, _musicBeatThreshold, PlainRow(_musicEqEnabled),
            PlainRow(_musicSystemMixFallback), _musicEqLow, _musicEqHigh));
        UpdateMusicAdvancedVisibility();
        return page;
    }

    private void BindMusicPlayer()
    {
        using var picker = new AudioApplicationPickerForm(includeVisibleProcesses: true);
        if (picker.ShowDialog(this) != DialogResult.OK || picker.Selected is null) return;
        var selected = picker.Selected;
        _musicPlayerBinding.Enabled = true;
        _musicPlayerBinding.ProcessName = selected.ProcessName;
        _musicPlayerBinding.ExecutablePath = selected.ExecutablePath;
        _musicPlayerBinding.IncludeChildProcesses = true;
        _musicPlayerBinding.MediaSessionId = MediaPlaybackState.Load()?.Sessions.FirstOrDefault(session =>
            session.SourceId.Contains(selected.ProcessName, StringComparison.OrdinalIgnoreCase))?.SourceId ?? "";
        RefreshMusicMediaSessions(_musicPlayerBinding.MediaSessionId);
        RefreshMusicBindingStatus();
        Text = "ClevoLEDKeyboardControl 设置 - 有未应用的更改";
    }

    private void ClearMusicPlayerBinding()
    {
        _musicPlayerBinding = new MusicPlayerBinding();
        RefreshMusicMediaSessions("");
        RefreshMusicBindingStatus();
        Text = "ClevoLEDKeyboardControl 设置 - 有未应用的更改";
    }

    private void RefreshMusicBindingStatus(AutomationStatus? status = null)
    {
        if (!_musicPlayerBinding.Enabled)
        {
            _musicBindingStatus.Text = "未绑定：音乐模式使用系统混音和音乐预设颜色。";
            _musicMediaMatchStatus.Text = "媒体会话：未启用播放器绑定。";
            _musicMediaMatchStatus.ForeColor = ThemeManager.Current.MutedText;
            _musicPalettePreview.Colors = _musicSequence.Colors;
            _musicCurrentColorStatus.Text = "当前使用音乐预设配色。";
            _musicClearPlayer.Enabled = false;
            return;
        }

        _musicClearPlayer.Enabled = true;
        var runtime = status?.ActiveMusicApplication == _musicPlayerBinding.ProcessName
            ? $"；当前 PID：{string.Join(",", status.ActiveProcessIds)}{(string.IsNullOrWhiteSpace(status.TrackTitle) ? "" : $"；{status.TrackTitle}")}"
            : "";
        var media = string.IsNullOrWhiteSpace(_musicPlayerBinding.MediaSessionId)
            ? "；媒体会话：自动匹配"
            : $"；媒体会话：{_musicPlayerBinding.MediaSessionId}";
        _musicBindingStatus.Text = $"已绑定：{_musicPlayerBinding.ProcessName}{runtime}{media}";
        var mediaState = MediaPlaybackState.Load();
        var playback = mediaState?.Find(_musicPlayerBinding);
        var source = (MusicColorSource)Math.Max(0, _musicBindingColorSource.SelectedIndex);
        var colors = source switch
        {
            MusicColorSource.AlbumDominant when playback is not null && !string.IsNullOrWhiteSpace(playback.DominantColor) => new List<string> { playback.DominantColor },
            MusicColorSource.AlbumPalette when playback is not null && playback.Palette.Count > 0 => playback.Palette,
            _ => _musicSequence.Colors
        };
        _musicPalettePreview.Colors = colors;
        if (source == MusicColorSource.Preset)
        {
            _musicMediaMatchStatus.Text = "媒体会话：颜色来源为音乐预设，封面匹配暂不参与输出。";
            _musicMediaMatchStatus.ForeColor = ThemeManager.Current.MutedText;
        }
        else if (playback is not null)
        {
            var mode = string.IsNullOrWhiteSpace(_musicPlayerBinding.MediaSessionId) ? "自动匹配成功" : "手动匹配成功";
            var cover = playback.Palette.Count > 0 ? $"已获取封面（{playback.Palette.Count} 色）" : "未获取封面，正在使用预设颜色";
            _musicMediaMatchStatus.Text = $"媒体会话：{mode} → {playback.SourceId}；{(playback.IsPlaying ? "正在播放" : "切歌/暂停过渡")}；{cover}";
            _musicMediaMatchStatus.ForeColor = playback.Palette.Count > 0 ? ThemeManager.Current.Success : ThemeManager.Current.Warning;
        }
        else
        {
            var candidates = mediaState?.Sessions.Select(item => item.SourceId)
                .Where(id => !string.IsNullOrWhiteSpace(id)).Distinct(StringComparer.OrdinalIgnoreCase).ToList() ?? [];
            var automatic = string.IsNullOrWhiteSpace(_musicPlayerBinding.MediaSessionId);
            _musicMediaMatchStatus.Text = candidates.Count == 0
                ? "媒体会话：尚未检测到 Windows 播放器会话，请先播放歌曲。"
                : automatic
                    ? $"媒体会话：自动匹配失败；当前可用：{string.Join("、", candidates)}。可在上方改为手动选择。"
                    : $"媒体会话：选择的会话当前不可用；当前可用：{string.Join("、", candidates)}。";
            _musicMediaMatchStatus.ForeColor = ThemeManager.Current.Error;
        }
        _musicCurrentColorStatus.Text = playback is null
            ? $"当前使用预设颜色：{string.Join("  ", colors)}"
            : $"当前歌曲：{playback.Title}{(string.IsNullOrWhiteSpace(playback.Artist) ? "" : " - " + playback.Artist)}；实际颜色：{string.Join("  ", colors)}";
    }

    private void RefreshMusicMediaSessions(string? selectedSource = null)
    {
        selectedSource ??= _musicMediaSession.SelectedIndex <= 0 ? _musicPlayerBinding.MediaSessionId : _musicMediaSession.SelectedItem?.ToString();
        var sources = MediaPlaybackState.Load()?.Sessions
            .Select(session => session.SourceId)
            .Where(source => !string.IsNullOrWhiteSpace(source))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .OrderBy(source => source, StringComparer.OrdinalIgnoreCase)
            .ToList() ?? [];
        _musicMediaSession.Items.Clear();
        _musicMediaSession.Items.Add("自动匹配播放器");
        foreach (var source in sources) _musicMediaSession.Items.Add(source);
        var index = string.IsNullOrWhiteSpace(selectedSource)
            ? 0
            : Enumerable.Range(1, _musicMediaSession.Items.Count - 1)
                .FirstOrDefault(i => string.Equals(_musicMediaSession.Items[i]?.ToString(), selectedSource, StringComparison.OrdinalIgnoreCase));
        _musicMediaSession.SelectedIndex = index;
    }

    private Panel BuildAutomationPage()
    {
        var page = CreatePage();
        _idleAfter.DropDownStyle = ComboBoxStyle.DropDownList;
        _idleAfter.Items.AddRange(["1 分钟", "3 分钟", "5 分钟", "10 分钟", "30 分钟"]);

        var simulator = new Button { Text = "场景模拟器...", Width = 150 };
        simulator.Click += (_, _) => ShowAutomationSimulator();
        var priority = new Label
        {
            Text = "有声音乐程序  →  前台灯效程序  →  时间计划  →  手动模式",
            Width = ContentWidth,
            Height = 34,
            ForeColor = ThemeManager.Current.Primary,
            Font = new Font("Segoe UI Semibold", 9.5F)
        };
        page.Controls.Add(new UiCard("运行状态", _automationStatus, priority, PlainRow(simulator)));
        page.Controls.Add(new UiCard("场景规则", PlainRow(_automationEnabled), _sceneAutomation));
        page.Controls.Add(new UiCard("空闲最终覆盖", PlainRow(_idleEnabled), Row("空闲时间", _idleAfter),
            _idleBrightness, PlainRow(_idleTurnOff)));
        return page;
    }

    private void ShowAutomationSimulator()
    {
        var settings = _settingsStore.Load();
        using var dialog = new Form { Text = "场景模拟器（不会改变灯光）", Width = 620, Height = 470, StartPosition = FormStartPosition.CenterParent };
        var layout = new FlowLayoutPanel { Dock = DockStyle.Fill, FlowDirection = FlowDirection.TopDown, WrapContents = false, AutoScroll = true, Padding = new Padding(12) };
        var time = new DateTimePicker { Width = 260, Format = DateTimePickerFormat.Custom, CustomFormat = "yyyy-MM-dd HH:mm:ss", Value = DateTime.Now };
        var foreground = new TextBox { Width = 260, PlaceholderText = "例如 winword.exe" };
        var audio = new ComboBox { Width = 260, DropDownStyle = ComboBoxStyle.DropDown };
        audio.Items.AddRange(settings.Automation.MusicApplications.Select(rule => rule.ProcessName).Where(name => name.Length > 0).Distinct().Cast<object>().ToArray());
        var playing = new CheckBox { Text = "模拟该程序正在播放", AutoSize = true };
        var level = new NumericUpDown { Width = 100, Minimum = 0, Maximum = 100, Value = 30 };
        var idle = new CheckBox { Text = "模拟已进入空闲状态", AutoSize = true };
        var output = new TextBox { Width = 560, Height = 180, Multiline = true, ReadOnly = true, ScrollBars = ScrollBars.Vertical };
        var run = new Button { Text = "模拟", Width = 100 };
        run.Click += (_, _) =>
        {
            var process = AppProfileRule.NormalizeProcessName(audio.Text);
            var states = playing.Checked && process.Length > 0
                ? new AudioApplicationState[] { new(process, "", [1234], (float)level.Value / 100f, true,
                    string.Equals(process, AppProfileRule.NormalizeProcessName(foreground.Text), StringComparison.OrdinalIgnoreCase)) }
                : [];
            var result = AutomationSimulator.Simulate(settings,
                new AutomationSimulationInput(time.Value, foreground.Text, states, idle.Checked));
            var issues = result.Health.Count == 0 ? "无" : string.Join(Environment.NewLine,
                result.Health.Select(issue => $"[{issue.Severity}] {issue.RuleName}：{issue.Reason}"));
            output.Text = $"优先级结果：{result.PriorityTrace}{Environment.NewLine}" +
                $"最终亮度上限：{result.FinalBrightnessLimit}%{Environment.NewLine}" +
                $"跳过提示：{result.Selection.InvalidReason ?? "无"}{Environment.NewLine}{Environment.NewLine}规则健康检查：{Environment.NewLine}{issues}";
        };
        layout.Controls.AddRange([new Label { Text = "本地时间", AutoSize = true }, time,
            new Label { Text = "前台程序", AutoSize = true }, foreground,
            new Label { Text = "播放程序", AutoSize = true }, audio, playing,
            new Label { Text = "模拟音量（%）", AutoSize = true }, level, idle, run, output]);
        dialog.Controls.Add(layout);
        ThemeManager.Apply(dialog);
        dialog.ShowDialog(this);
    }

    private Panel BuildEventFeedbackPage()
    {
        var page = CreatePage();
        page.Controls.Add(new UiCard("敲字反馈", PlainRow(_typingPulseEnabled), _typingPulsePeakBrightness,
            _typingPulseHold, _typingPulseFade));
        page.Controls.Add(new UiCard("通知反馈", PlainRow(_notificationFlashEnabled), _notificationFlashColor,
            _notificationFlashPulses, _notificationFlashCooldown));
        var explanation = new Label
        {
            Text = "事件策略按“全局 → 音乐规则 → 前台灯效规则”覆盖；空闲关灯会抑制包括通知在内的全部输出。",
            Width = ContentWidth,
            Height = 42,
            ForeColor = ThemeManager.Current.MutedText
        };
        page.Controls.Add(new UiCard("当前覆盖关系", explanation));
        return page;
    }

    private Panel BuildAppProfilesPage()
    {
        var page = CreatePage();
        page.Controls.Add(PlainRow(_appProfilesEnabled));
        page.Controls.Add(_appProfiles);
        return page;
    }

    private Panel BuildDiagnosticsPage()
    {
        var page = CreatePage();
        var serviceStatus = DiagnosticTextBox();
        var driverStatus = DiagnosticTextBox();
        var foregroundApp = DiagnosticTextBox();
        var matchedProfile = DiagnosticTextBox();
        var updateStatus = DiagnosticTextBox();
        var monitorStatus = DiagnosticTextBox();
        var recoveryStatus = DiagnosticTextBox();
        var configDirectory = DiagnosticTextBox();
        var refresh = new Button { Text = "刷新诊断信息", Width = 150 };

        void Refresh()
        {
            var diagnostics = CollectDiagnostics();
            serviceStatus.Text = diagnostics.ServiceStatus;
            driverStatus.Text = diagnostics.DriverStatus;
            foregroundApp.Text = diagnostics.ForegroundApp;
            matchedProfile.Text = diagnostics.MatchedProfile;
            updateStatus.Text = diagnostics.UpdateStatus;
            monitorStatus.Text = diagnostics.MonitorStatus;
            recoveryStatus.Text = diagnostics.RecoveryStatus;
            configDirectory.Text = diagnostics.ConfigDirectory;
            UpdateStatusHeader();
        }

        refresh.Click += (_, _) => Refresh();

        page.Controls.Add(new UiCard("服务与硬件", Row("服务状态", serviceStatus), Row("驱动 DLL", driverStatus)));
        page.Controls.Add(new UiCard("自动化与播放器", Row("当前前台应用", foregroundApp),
            Row("命中应用场景", matchedProfile), Row("播放器监视", monitorStatus)));
        page.Controls.Add(new UiCard("更新与配置恢复", Row("更新检查", updateStatus),
            Row("配置恢复", recoveryStatus), Row("配置目录", configDirectory), PlainRow(refresh)));
        Refresh();
        return page;
    }

    private Panel BuildAdvancedPage()
    {
        var page = CreatePage();
        _updateInterval.DropDownStyle = ComboBoxStyle.DropDownList;
        _updateInterval.Items.AddRange(["从不", "每天", "每周", "每月"]);

        var configPath = new TextBox
        {
            Text = AppPaths.SettingsPath,
            ReadOnly = true,
            Width = 430
        };

        var openFolder = new Button { Text = "打开配置目录", Width = 150 };
        openFolder.Click += (_, _) =>
        {
            Directory.CreateDirectory(AppPaths.ProgramDataDirectory);
            Process.Start(new ProcessStartInfo("explorer.exe", AppPaths.ProgramDataDirectory) { UseShellExecute = true });
        };

        var reset = new Button { Text = "恢复默认设置", Width = 150 };
        reset.Click += (_, _) =>
        {
            if (MessageBox.Show("确定恢复默认设置？", "ClevoLEDKeyboardControl", MessageBoxButtons.YesNo, MessageBoxIcon.Question) != DialogResult.Yes)
            {
                return;
            }

            _settingsStore.Save(new KeyboardSettings());
            LoadSettings();
        };

        var export = new Button { Text = "导出配置...", Width = 140 };
        export.Click += (_, _) => ExportConfiguration();
        var import = new Button { Text = "导入配置...", Width = 140 };
        import.Click += (_, _) => ImportConfiguration();
        var restore = new Button { Text = "恢复最近备份", Width = 150 };
        restore.Click += (_, _) => RestoreLastGoodConfiguration();

        var configActions = new FlowLayoutPanel { Width = ContentWidth, Height = 46, FlowDirection = FlowDirection.LeftToRight };
        configActions.Controls.AddRange([export, import, restore]);
        var folderActions = new FlowLayoutPanel { Width = ContentWidth, Height = 46, FlowDirection = FlowDirection.LeftToRight };
        folderActions.Controls.AddRange([openFolder, reset]);
        page.Controls.Add(BuildThemeSelector());
        page.Controls.Add(new UiCard("自动更新", Row("自动检查更新", _updateInterval)));
        page.Controls.Add(new UiCard("配置管理", configActions, Row("配置文件", configPath), folderActions));
        return page;
    }

    private void LoadSettings()
    {
        _loadingSettings = true;
        var settings = _settingsStore.Load();
        _loadedSettingsSnapshot = settings;
        try
        {
            _modeLighting.Checked = settings.Enabled && settings.OperatingMode == OperatingMode.Lighting;
            _modeMusic.Checked = settings.Enabled && settings.OperatingMode == OperatingMode.Music;
            _modeOff.Checked = !settings.Enabled;
            _effectType.SelectedIndex = settings.Effect.Type switch
            {
                EffectType.Static => 0,
                EffectType.Rainbow => 1,
                EffectType.Breathing => 2,
                EffectType.Sequence => 3,
                EffectType.Pulse => 4,
                EffectType.Heartbeat => 5,
                _ => 1
            };

            _brightness.Value = settings.Brightness;
            _speed.SelectedIndex = SpeedToIndex(settings.Effect.Step, settings.Effect.IntervalMs);
            _effectColor.ColorHex = settings.Effect.Color;
            _period.Value = EffectivePeriodValue(settings.Effect);
            _minimumBrightness.Value = settings.Effect.MinimumBrightness;
            _hardBlink.Checked = settings.Effect.HardBlink;
            _sequence.Colors = settings.Effect.Sequence.Select(item => item.Color).ToList();
            _customSequenceColorsEnabled = settings.Effect.CustomSequenceColorsEnabled;
            _effectPresets = KeyboardSettings.CloneEffectPresets(settings.EffectPresets);
            RefreshEffectPresetList();
            _musicCustomPresets = settings.Effect.Music.CustomPresets.Select(CloneMusicPreset).ToList();
            RefreshMusicPresetList(settings.Effect.Music.PresetName);
            _musicPresetName.Text = IsBuiltInMusicPreset(settings.Effect.Music.PresetName) ? "" : settings.Effect.Music.PresetName;
            _musicResponseMode.SelectedIndex = settings.Effect.Music.ResponseMode == MusicResponseMode.BrightnessPulse ? 1 : 0;
            _musicSequence.Colors = settings.Effect.Music.Colors;
            _musicSensitivity.SelectedIndex = ClosestIndex(MusicSensitivityValues, settings.Effect.Music.Sensitivity);
            _musicAttack.SelectedIndex = ClosestIndex(MusicAttackValues, settings.Effect.Music.AttackMs);
            _musicRelease.SelectedIndex = ClosestIndex(MusicReleaseValues, settings.Effect.Music.ReleaseMs);
            _musicNoiseGate.Value = (int)Math.Round(settings.Effect.Music.NoiseGate * 100);
            _musicBeatThreshold.Value = (int)Math.Round(settings.Effect.Music.BeatThreshold * 100);
            _musicEqEnabled.Checked = settings.Effect.Music.EqEnabled;
            _musicSystemMixFallback.Checked = settings.Effect.Music.AllowSystemMixFallback;
            _musicEqLow.Value = settings.Effect.Music.EqLowHz;
            _musicEqHigh.Value = settings.Effect.Music.EqHighHz;
            _musicBaseBrightness.Value = settings.Effect.Music.BaseBrightness;
            _musicPeakBrightness.Value = settings.Effect.Music.PeakBrightness;
            _musicFollowSystemVolume.Checked = settings.Effect.Music.FollowSystemVolume;
            _musicPlayerBinding = new MusicPlayerBinding
            {
                Enabled = settings.Effect.Music.PlayerBinding.Enabled,
                ProcessName = settings.Effect.Music.PlayerBinding.ProcessName,
                ExecutablePath = settings.Effect.Music.PlayerBinding.ExecutablePath,
                IncludeChildProcesses = settings.Effect.Music.PlayerBinding.IncludeChildProcesses,
                MediaSessionId = settings.Effect.Music.PlayerBinding.MediaSessionId,
                ColorSource = settings.Effect.Music.PlayerBinding.ColorSource
            };
            _musicBindingColorSource.SelectedIndex = (int)_musicPlayerBinding.ColorSource;
            RefreshMusicMediaSessions(_musicPlayerBinding.MediaSessionId);
            RefreshMusicBindingStatus();
            _idleEnabled.Checked = settings.IdleDim.Enabled;
            _idleAfter.SelectedIndex = SecondsToIdleIndex(settings.IdleDim.AfterSeconds);
            _idleBrightness.Value = settings.IdleDim.Brightness;
            _idleTurnOff.Checked = settings.IdleDim.TurnOff;
            _scheduleEnabled.Checked = settings.Schedule.Enabled;
            _typingPulseEnabled.Checked = settings.TypingPulse.Enabled;
            _typingPulsePeakBrightness.Value = settings.TypingPulse.PeakBrightness;
            _typingPulseHold.Value = settings.TypingPulse.HoldMs;
            _typingPulseFade.Value = settings.TypingPulse.FadeMs;
            _notificationFlashEnabled.Checked = settings.NotificationFlash.Enabled;
            _notificationFlashColor.ColorHex = settings.NotificationFlash.Color;
            _notificationFlashPulses.Value = settings.NotificationFlash.Pulses;
            _notificationFlashCooldown.Value = settings.NotificationFlash.CooldownSeconds;
            _appProfilesEnabled.Checked = settings.AppProfiles.Enabled;
            _appProfiles.Rules = settings.AppProfiles.Rules;
            _automationEnabled.Checked = settings.Automation.Enabled;
            _sceneAutomation.SetPresets(settings.EffectPresets, settings.Effect.Music.CustomPresets);
            _sceneAutomation.Automation = settings.Automation;
            UpdateAutomationStatus();
            _updateInterval.SelectedIndex = UpdateIntervalToIndex(settings.Update.CheckInterval);

            var evening = settings.Schedule.Rules.FirstOrDefault(rule => rule.Name == "Evening");
            var night = settings.Schedule.Rules.FirstOrDefault(rule => rule.Name == "Night");
            _evening.SetRange(evening?.Start ?? "19:00", evening?.End ?? "23:30");
            _night.SetRange(night?.Start ?? "23:30", night?.End ?? "07:00");
            _effectChangedByUser = false;
            UpdateBrightnessAvailability();
            UpdateCustomColorsButton();
            UpdateEffectConfigurationVisibility();
            RefreshEffectPresetList();
            ApplySelectedEffectPreset(markDirty: false);
            if (FindSelectedMusicPreset() is { } preset)
            {
                ApplyMusicPresetToControls(preset, refreshSelection: false, markDirty: false);
            }
        }
        finally
        {
            _loadingSettings = false;
        }

        UpdateModeAvailability();
        UpdateEventFeedbackVisibility();
    }

    private void SaveSettings()
    {
        try
        {
            var settings = _settingsStore.Load();
            settings.Enabled = !_modeOff.Checked;
            if (!_modeOff.Checked)
            {
                settings.OperatingMode = _modeMusic.Checked ? OperatingMode.Music : OperatingMode.Lighting;
            }
            if (_effectChangedByUser)
            {
                settings.Effect.Type = SelectedEffectType(settings.Effect.Type);
            }

            settings.Effect.Color = _effectColor.ColorHex;
            var selectedEffect = SelectedEffectType(settings.Effect.Type);
            settings.Effect.PeriodMs = _period.Value;
            settings.Effect.MinimumBrightness = _minimumBrightness.Value;
            settings.Effect.HardBlink = _hardBlink.Checked;
            settings.Effect.CustomSequenceColorsEnabled = selectedEffect == EffectType.Rainbow;
            settings.Effect.Sequence = BuildSequenceColors(selectedEffect);
            RememberEffect(settings, settings.Effect);
            if (settings.Effect.Type != EffectType.Off)
            {
                settings.SavedEffects ??= new EffectMemorySettings();
                settings.SavedEffects.LastUsedLightingEffect = settings.Effect.Type;
            }

            settings.Effect.Music.PresetName = SelectedMusicPresetName();
            settings.Effect.Music.ResponseMode = _musicResponseMode.SelectedIndex == 1
                ? MusicResponseMode.BrightnessPulse
                : MusicResponseMode.LevelColor;
            settings.Effect.Music.LevelColorEnabled = settings.Effect.Music.ResponseMode == MusicResponseMode.LevelColor;
            settings.Effect.Music.Colors = NormalizedMusicColors();
            settings.Effect.Music.LowColor = settings.Effect.Music.Colors[0];
            settings.Effect.Music.HighColor = settings.Effect.Music.Colors[^1];
            settings.Effect.Music.Sensitivity = MusicSensitivityValues[ClampIndex(_musicSensitivity.SelectedIndex, MusicSensitivityValues.Length)];
            settings.Effect.Music.AttackMs = MusicAttackValues[ClampIndex(_musicAttack.SelectedIndex, MusicAttackValues.Length)];
            settings.Effect.Music.ReleaseMs = MusicReleaseValues[ClampIndex(_musicRelease.SelectedIndex, MusicReleaseValues.Length)];
            settings.Effect.Music.NoiseGate = _musicNoiseGate.Value / 100d;
            settings.Effect.Music.BeatThreshold = _musicBeatThreshold.Value / 100d;
            settings.Effect.Music.EqEnabled = _musicEqEnabled.Checked;
            settings.Effect.Music.AllowSystemMixFallback = _musicSystemMixFallback.Checked;
            settings.Effect.Music.EqLowHz = _musicEqLow.Value;
            settings.Effect.Music.EqHighHz = _musicEqHigh.Value;
            settings.Effect.Music.Spotify.AlbumColorEnabled = false;
            settings.Effect.Music.BaseBrightness = _musicBaseBrightness.Value;
            settings.Effect.Music.PeakBrightness = _musicPeakBrightness.Value;
            settings.Effect.Music.FollowSystemVolume = _musicFollowSystemVolume.Checked;
            _musicPlayerBinding.ColorSource = (MusicColorSource)Math.Max(0, _musicBindingColorSource.SelectedIndex);
            _musicPlayerBinding.MediaSessionId = _musicMediaSession.SelectedIndex <= 0
                ? ""
                : _musicMediaSession.SelectedItem?.ToString() ?? "";
            settings.Effect.Music.PlayerBinding = new MusicPlayerBinding
            {
                Enabled = _musicPlayerBinding.Enabled,
                ProcessName = _musicPlayerBinding.ProcessName,
                ExecutablePath = _musicPlayerBinding.ExecutablePath,
                IncludeChildProcesses = _musicPlayerBinding.IncludeChildProcesses,
                MediaSessionId = _musicPlayerBinding.MediaSessionId,
                ColorSource = _musicPlayerBinding.ColorSource
            };
            settings.Effect.Music.CustomPresets = _musicCustomPresets.Select(CloneMusicPreset).ToList();
            settings.EffectPresets = KeyboardSettings.CloneEffectPresets(_effectPresets);
            settings.IdleDim.Enabled = _idleEnabled.Checked;
            settings.IdleDim.AfterSeconds = IdleIndexToSeconds(_idleAfter.SelectedIndex);
            settings.IdleDim.Brightness = _idleBrightness.Value;
            settings.IdleDim.TurnOff = _idleTurnOff.Checked;
            settings.TypingPulse.Enabled = _typingPulseEnabled.Checked;
            settings.TypingPulse.PeakBrightness = _typingPulsePeakBrightness.Value;
            settings.TypingPulse.HoldMs = _typingPulseHold.Value;
            settings.TypingPulse.FadeMs = _typingPulseFade.Value;
            settings.NotificationFlash.Enabled = _notificationFlashEnabled.Checked;
            settings.NotificationFlash.Color = _notificationFlashColor.ColorHex;
            settings.NotificationFlash.Pulses = _notificationFlashPulses.Value;
            settings.NotificationFlash.CooldownSeconds = _notificationFlashCooldown.Value;
            settings.Automation.Enabled = _automationEnabled.Checked;
            _sceneAutomation.SetPresets(_effectPresets, _musicCustomPresets);
            var automation = _sceneAutomation.Automation;
            settings.Automation.MusicApplications = automation.MusicApplications;
            settings.Automation.LightingApplications = automation.LightingApplications;
            settings.Automation.ScheduleRules = automation.ScheduleRules;
            settings.Automation.Rules.Clear();
            settings.Update.CheckInterval = IndexToUpdateInterval(_updateInterval.SelectedIndex);
            settings.Brightness = _brightness.Enabled ? _brightness.Value : settings.Brightness;
            _settingsStore.Save(settings);
            _loadedSettingsSnapshot = settings;
            _effectChangedByUser = false;
            UpdateStatusHeader();
            UpdateAutomationStatus();
            Text = "ClevoLEDKeyboardControl 设置 - 已应用";
            _settingsChanged = false;
            _themeChanged = false;
            _initialUiState = _uiStateStore.Load().Clone();
            _initialUiState.Theme = ThemeManager.CurrentKind;
            _initialUiState.MusicAdvancedExpanded = _musicAdvanced.Checked;
            _uiStateStore.Save(_initialUiState);
            UpdateSaveBar();
            SettingsSaved?.Invoke(this, EventArgs.Empty);
        }
        catch (Exception ex) when (ex is FormatException or IOException or UnauthorizedAccessException)
        {
            MessageBox.Show($"无法保存设置：{ex.Message}", "ClevoLEDKeyboardControl", MessageBoxButtons.OK, MessageBoxIcon.Warning);
        }
    }

    private List<ScheduleRule> BuildScheduleRules()
    {
        return
        [
            new ScheduleRule
            {
                Name = "Evening",
                Start = _evening.StartTime,
                End = _evening.EndTime,
                Enabled = true,
                Brightness = 35,
                Effect = new LightingEffectSettings { Type = EffectType.Static, Color = "#FFD2A1" }
            },
            new ScheduleRule
            {
                Name = "Night",
                Start = _night.StartTime,
                End = _night.EndTime,
                Enabled = true,
                Brightness = 0,
                Effect = new LightingEffectSettings { Type = EffectType.Off }
            }
        ];
    }

    private static FlowLayoutPanel CreatePage()
    {
        var page = new FlowLayoutPanel
        {
            Dock = DockStyle.Fill,
            FlowDirection = FlowDirection.TopDown,
            WrapContents = false,
            AutoScroll = true,
            Padding = new Padding(18, 18, 18, 28)
        };
        ThemeManager.SetSurface(page, ThemeSurfaceRole.Window);
        return page;
    }

    private static Panel Row(string label, Control control)
    {
        var panel = new Panel { Width = ContentWidth, Height = RowHeight };
        panel.Controls.Add(new Label { Text = label, Width = LabelWidth, Height = 30, Location = new Point(0, 10), AutoEllipsis = false });
        control.Location = new Point(ControlLeft, 7);
        control.Width = Math.Max(control.Width, 240);
        control.Height = Math.Max(control.Height, 30);
        panel.Controls.Add(control);
        return panel;
    }

    private static void SetupCombo(ComboBox combo, IEnumerable<string> values)
    {
        combo.DropDownStyle = ComboBoxStyle.DropDownList;
        if (combo.Items.Count == 0)
        {
            combo.Items.AddRange(values.Cast<object>().ToArray());
        }
    }

    private static Panel PlainRow(Control control)
    {
        var panel = new Panel { Width = ContentWidth, Height = RowHeight };
        control.Location = new Point(ControlLeft, 9);
        if (control is CheckBox checkBox)
        {
            checkBox.AutoSize = true;
            checkBox.MaximumSize = new Size(ContentWidth - ControlLeft - 20, 0);
        }
        else
        {
            control.Height = Math.Max(control.Height, 30);
        }
        panel.Controls.Add(control);
        return panel;
    }

    private static Panel ButtonRow(params Button[] buttons)
    {
        var panel = new Panel { Width = ContentWidth, Height = RowHeight };
        var x = ControlLeft;
        foreach (var button in buttons)
        {
            button.Location = new Point(x, 7);
            button.Width = Math.Max(button.Width, 112);
            button.Height = ButtonHeight;
            panel.Controls.Add(button);
            x += button.Width + 10;
        }

        return panel;
    }

    private static Label Section(string text)
    {
        return new Label
        {
            Text = text,
            Font = new Font(SystemFonts.MessageBoxFont ?? Control.DefaultFont, FontStyle.Bold),
            Width = ContentWidth,
            Height = 38,
            Padding = new Padding(0, 10, 0, 0)
        };
    }

    private EffectType SelectedEffectType(EffectType fallback) => _effectType.SelectedIndex switch
    {
        0 => EffectType.Static,
        1 => EffectType.Rainbow,
        2 => EffectType.Breathing,
        3 => EffectType.Sequence,
        4 => EffectType.Pulse,
        5 => EffectType.Heartbeat,
        _ => fallback
    };

    private static int EffectTypeToIndex(EffectType effect) => effect switch
    {
        EffectType.Static => 0,
        EffectType.Rainbow => 1,
        EffectType.Breathing => 2,
        EffectType.Sequence => 3,
        EffectType.Pulse => 4,
        EffectType.Heartbeat => 5,
        _ => 1
    };

    private void UpdateBrightnessAvailability()
    {
        var effect = SelectedEffectType(EffectType.Rainbow);
        var brightnessEnabled = effect != EffectType.Off;
        _brightness.Enabled = brightnessEnabled && !_modeMusic.Checked && !_modeOff.Checked;
        _brightness.Visible = brightnessEnabled && !_modeMusic.Checked && !_modeOff.Checked;
        _brightness.BackColor = _brightness.Enabled ? ThemeManager.Current.Surface : ThemeManager.Current.Window;
    }

    private void UpdateCustomColorsButton()
    {
        var effect = SelectedEffectType(EffectType.Rainbow);
        var visible = effect is EffectType.Static or EffectType.Rainbow or EffectType.Breathing or EffectType.Sequence or EffectType.Pulse or EffectType.Heartbeat;
        _customColors.Text = "自定义颜色";
        _customColors.Visible = visible;
        if (_customColorsRow is not null)
        {
            _customColorsRow.Visible = visible;
        }
    }

    private void UpdateEffectConfigurationVisibility()
    {
        var off = _modeOff.Checked;
        var music = _modeMusic.Checked;
        var hideEffectParams = off || music;
        var effect = SelectedEffectType(EffectType.Rainbow);
        var singleColor = !hideEffectParams && (effect is EffectType.Static or EffectType.Breathing);
        var sequenceVisible = !hideEffectParams && (effect is EffectType.Rainbow or EffectType.Sequence or EffectType.Pulse or EffectType.Heartbeat);

        if (_effectTypeRow is not null)
        {
            _effectTypeRow.Visible = !hideEffectParams;
        }

        _brightness.Visible = !hideEffectParams && effect != EffectType.Off;
        _effectColor.Visible = singleColor;
        if (_speedRow is not null)
        {
            _speedRow.Visible = false;
        }

        _period.LabelText = effect switch
        {
            EffectType.Rainbow => "停留时长",
            EffectType.Pulse => "脉冲周期",
            EffectType.Heartbeat => "心跳周期",
            _ => "呼吸周期"
        };
        _period.Visible = !hideEffectParams && (effect is EffectType.Rainbow or EffectType.Breathing or EffectType.Sequence or EffectType.Pulse or EffectType.Heartbeat);
        _minimumBrightness.Visible = !hideEffectParams && effect == EffectType.Breathing;
        if (_hardBlinkRow is not null)
        {
            _hardBlinkRow.Visible = !hideEffectParams && effect == EffectType.Breathing;
        }

        if (_customColorsRow is not null)
        {
            _customColorsRow.Visible = !hideEffectParams && _customColorsRow.Controls.OfType<Control>().Any(c => c.Visible);
        }

        if (_sequenceSection is not null)
        {
            _sequenceSection.Text = effect switch
            {
                EffectType.Sequence => "循环呼吸颜色",
                EffectType.Pulse => "脉冲颜色",
                EffectType.Heartbeat => "心跳颜色",
                _ => "自定义循环颜色"
            };
            _sequenceSection.Visible = sequenceVisible;
        }

        if (_sequenceSummary is not null)
        {
            _sequenceSummary.Text = BuildSequenceSummary(effect);
            _sequenceSummary.Visible = sequenceVisible;
        }

        _sequence.Visible = sequenceVisible;
        var presetVisible = !hideEffectParams && (effect is EffectType.Static or EffectType.Rainbow or EffectType.Breathing or EffectType.Sequence or EffectType.Pulse or EffectType.Heartbeat);
        if (_effectPresetSection is not null)
        {
            _effectPresetSection.Visible = presetVisible;
        }

        if (_effectPresetRow is not null)
        {
            _effectPresetRow.Visible = presetVisible;
        }

        if (_effectPresetNameRow is not null)
        {
            _effectPresetNameRow.Visible = presetVisible;
        }

        if (_effectPresetButtonsRow is not null)
        {
            _effectPresetButtonsRow.Visible = presetVisible;
        }

        if (_modeHint is not null)
        {
            if (off)
            {
                _modeHint.Visible = true;
                _modeHint.Text = "关闭模式不会显示灯效参数。";
            }
            else if (music)
            {
                _modeHint.Visible = true;
                _modeHint.Text = "音乐模式由音乐页配置，灯效参数已禁用。";
            }
            else
            {
                _modeHint.Visible = false;
                _modeHint.Text = "";
            }
        }
    }

    private void EditCustomColors()
    {
        var effect = SelectedEffectType(EffectType.Rainbow);
        var singleSelection = effect is EffectType.Static or EffectType.Breathing;
        var selectedColors = effect is EffectType.Static or EffectType.Breathing
            ? new List<string> { _effectColor.ColorHex }
            : _sequence.Colors;

        using var dialog = new ColorSelectionDialog(selectedColors, singleSelection);
        if (dialog.ShowDialog(this) != DialogResult.OK)
        {
            return;
        }

        var colors = dialog.SelectedColors;
        if (colors.Count == 0)
        {
            return;
        }

        if (singleSelection)
        {
            _effectColor.ColorHex = colors[0];
        }
        else
        {
            _sequence.Colors = colors;
            _customSequenceColorsEnabled = effect == EffectType.Rainbow;
        }

        if (!_loadingSettings)
        {
            _effectChangedByUser = true;
        }

        UpdateCustomColorsButton();
        UpdateEffectConfigurationVisibility();
    }

    private void RefreshEffectPresetList(string? selectedName = null)
    {
        var effect = SelectedEffectType(EffectType.Rainbow);
        var presets = _effectPresets.ForType(effect);
        var selected = selectedName ?? FindMatchingEffectPresetName(effect);

        _loadingEffectPreset = true;
        try
        {
            _effectPreset.Items.Clear();
            _effectPreset.Items.Add(SoftwareDefaultPresetName);
            foreach (var preset in presets)
            {
                _effectPreset.Items.Add(preset.Name);
            }

            var index = -1;
            for (var i = 0; i < _effectPreset.Items.Count; i++)
            {
                if (string.Equals(_effectPreset.Items[i]?.ToString(), selected, StringComparison.OrdinalIgnoreCase))
                {
                    index = i;
                    break;
                }
            }

            _effectPreset.SelectedIndex = index;
            _effectPresetName.Text = index > 0 ? _effectPreset.Items[index]?.ToString() ?? "" : "";
        }
        finally
        {
            _loadingEffectPreset = false;
        }

        UpdateEffectPresetButtons();
    }

    private void UpdateEffectPresetButtons()
    {
        var presetVisible = SelectedEffectType(EffectType.Rainbow) is EffectType.Static or EffectType.Rainbow or EffectType.Breathing or EffectType.Sequence or EffectType.Pulse or EffectType.Heartbeat;
        var customSelected = presetVisible && _effectPreset.SelectedIndex > 0;
        _effectSavePreset.Enabled = customSelected;
        _effectCreatePreset.Enabled = presetVisible;
        _effectDeletePreset.Enabled = customSelected;
    }

    private void ApplySelectedEffectPreset(bool markDirty = true)
    {
        var effectType = SelectedEffectType(EffectType.Rainbow);
        LightingEffectSettings? effect = null;
        var selectedName = SelectedEffectPresetName();

        if (IsSoftwareDefaultEffectPresetSelected())
        {
            effect = EffectPresetSettings.CreateSoftwareDefault(effectType);
            selectedName = SoftwareDefaultPresetName;
        }
        else
        {
            var preset = _effectPresets.ForType(effectType)
                .FirstOrDefault(item => string.Equals(item.Name, selectedName, StringComparison.OrdinalIgnoreCase));
            if (preset is not null)
            {
                effect = KeyboardSettings.CloneEffect(preset.Effect);
            }
        }

        if (effect is null)
        {
            return;
        }

        ApplyEffectToGeneralControls(effect, selectedName, markDirty);
    }

    private void SaveSelectedEffectPreset()
    {
        if (IsSoftwareDefaultEffectPresetSelected())
        {
            MessageBox.Show("软件默认配置不能被修改；请使用“新建/另存为”创建一个自定义预设。", "ClevoLEDKeyboardControl", MessageBoxButtons.OK, MessageBoxIcon.Information);
            return;
        }

        var originalName = SelectedEffectPresetName();
        var newName = _effectPresetName.Text.Trim();
        if (string.IsNullOrWhiteSpace(newName))
        {
            newName = originalName;
        }

        if (UpsertEffectPreset(newName, originalName))
        {
            RefreshEffectPresetList(newName);
        }
    }

    private void CreateEffectPreset()
    {
        var name = PromptForEffectPresetName();
        if (name is null)
        {
            return;
        }

        if (UpsertEffectPreset(name, originalName: null))
        {
            RefreshEffectPresetList(name);
        }
    }

    private string? PromptForEffectPresetName()
    {
        using var dialog = new Form
        {
            Text = "新建/另存为",
            StartPosition = FormStartPosition.CenterParent,
            FormBorderStyle = FormBorderStyle.FixedDialog,
            ClientSize = new Size(360, 140),
            MaximizeBox = false,
            MinimizeBox = false,
            ShowInTaskbar = false
        };

        var label = new Label
        {
            Text = "预设名称",
            Location = new Point(18, 18),
            Size = new Size(320, 24)
        };
        var input = new TextBox
        {
            Location = new Point(18, 46),
            Width = 320
        };
        var ok = new Button
        {
            Text = "确定",
            Location = new Point(170, 92),
            Width = 78,
            DialogResult = DialogResult.OK
        };
        var cancel = new Button
        {
            Text = "取消",
            Location = new Point(260, 92),
            Width = 78,
            DialogResult = DialogResult.Cancel
        };

        dialog.Controls.Add(label);
        dialog.Controls.Add(input);
        dialog.Controls.Add(ok);
        dialog.Controls.Add(cancel);
        dialog.AcceptButton = ok;
        dialog.CancelButton = cancel;
        ThemeManager.Apply(dialog);

        while (dialog.ShowDialog(this) == DialogResult.OK)
        {
            var name = input.Text.Trim();
            if (!string.IsNullOrWhiteSpace(name))
            {
                return name;
            }

            MessageBox.Show(dialog, "请输入预设名称。", "ClevoLEDKeyboardControl", MessageBoxButtons.OK, MessageBoxIcon.Information);
            input.Focus();
        }

        return null;
    }

    private bool UpsertEffectPreset(string name, string? originalName)
    {
        name = name.Trim();
        if (string.IsNullOrWhiteSpace(name))
        {
            MessageBox.Show("请先输入预设名称。", "ClevoLEDKeyboardControl", MessageBoxButtons.OK, MessageBoxIcon.Information);
            return false;
        }

        if (string.Equals(name, SoftwareDefaultPresetName, StringComparison.OrdinalIgnoreCase))
        {
            MessageBox.Show("自定义预设不能使用“软件默认配置”这个名称。", "ClevoLEDKeyboardControl", MessageBoxButtons.OK, MessageBoxIcon.Information);
            return false;
        }

        var effectType = SelectedEffectType(EffectType.Rainbow);
        var presets = _effectPresets.ForType(effectType);
        var existingIndex = presets.FindIndex(item => string.Equals(item.Name, name, StringComparison.OrdinalIgnoreCase));
        var originalIndex = string.IsNullOrWhiteSpace(originalName)
            ? -1
            : presets.FindIndex(item => string.Equals(item.Name, originalName, StringComparison.OrdinalIgnoreCase));

        if (existingIndex >= 0 && existingIndex != originalIndex)
        {
            var choice = MessageBox.Show($"预设“{name}”已存在，是否覆盖？", "ClevoLEDKeyboardControl", MessageBoxButtons.YesNo, MessageBoxIcon.Question);
            if (choice != DialogResult.Yes)
            {
                return false;
            }
        }

        if (existingIndex < 0 && originalIndex < 0 && presets.Count >= EffectPresetSettings.MaxPresetsPerMode)
        {
            MessageBox.Show($"每个模式最多保存 {EffectPresetSettings.MaxPresetsPerMode} 个预设。", "ClevoLEDKeyboardControl", MessageBoxButtons.OK, MessageBoxIcon.Information);
            return false;
        }

        var preset = new EffectPreset
        {
            Id = originalIndex >= 0
                ? presets[originalIndex].Id
                : existingIndex >= 0 ? presets[existingIndex].Id : Guid.NewGuid().ToString("N"),
            Name = name,
            Effect = BuildCurrentEffectFromGeneralControls(effectType)
        }.Normalize(effectType);

        if (existingIndex >= 0)
        {
            presets[existingIndex] = preset;
            if (originalIndex >= 0 && originalIndex != existingIndex)
            {
                presets.RemoveAt(originalIndex);
            }
        }
        else if (originalIndex >= 0)
        {
            presets[originalIndex] = preset;
        }
        else
        {
            presets.Add(preset);
        }

        _effectPresets.Normalize();
        _effectChangedByUser = true;
        Text = "ClevoLEDKeyboardControl 设置 - 有未应用的更改";
        return true;
    }

    private void DeleteSelectedEffectPreset()
    {
        if (IsSoftwareDefaultEffectPresetSelected())
        {
            return;
        }

        var name = SelectedEffectPresetName();
        if (MessageBox.Show($"确定删除预设“{name}”？", "ClevoLEDKeyboardControl", MessageBoxButtons.YesNo, MessageBoxIcon.Question) != DialogResult.Yes)
        {
            return;
        }

        var presets = _effectPresets.ForType(SelectedEffectType(EffectType.Rainbow));
        presets.RemoveAll(item => string.Equals(item.Name, name, StringComparison.OrdinalIgnoreCase));
        _effectPresets.Normalize();
        _effectChangedByUser = true;
        _effectPresetName.Text = "";
        RefreshEffectPresetList();
        Text = "ClevoLEDKeyboardControl 设置 - 有未应用的更改";
    }

    private string? FindMatchingEffectPresetName(EffectType effectType)
    {
        LightingEffectSettings current;
        try
        {
            current = BuildCurrentEffectFromGeneralControls(effectType);
        }
        catch (FormatException)
        {
            return null;
        }

        if (AreEquivalentEffects(current, EffectPresetSettings.CreateSoftwareDefault(effectType)))
        {
            return SoftwareDefaultPresetName;
        }

        return _effectPresets.ForType(effectType)
            .FirstOrDefault(preset => AreEquivalentEffects(current, preset.Effect))
            ?.Name;
    }

    private static bool AreEquivalentEffects(LightingEffectSettings left, LightingEffectSettings right)
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

    private LightingEffectSettings BuildCurrentEffectFromGeneralControls(EffectType effectType)
    {
        var effect = new LightingEffectSettings
        {
            Type = effectType,
            Color = _effectColor.ColorHex,
            PeriodMs = _period.Value,
            MinimumBrightness = _minimumBrightness.Value,
            HardBlink = _hardBlink.Checked,
            CustomSequenceColorsEnabled = effectType == EffectType.Rainbow,
            Sequence = BuildSequenceColors(effectType)
        };

        if (effectType == EffectType.Rainbow)
        {
            effect.CustomSequenceColorsEnabled = true;
        }

        return effect.Normalize();
    }

    private void ApplyEffectToGeneralControls(LightingEffectSettings effect, string? selectedPresetName, bool markDirty = true)
    {
        var wasLoading = _loadingSettings;
        _loadingSettings = true;
        try
        {
            _effectType.SelectedIndex = EffectTypeToIndex(effect.Type);
            _effectColor.ColorHex = effect.Color;
            _period.Value = EffectivePeriodValue(effect);
            _minimumBrightness.Value = effect.MinimumBrightness;
            _hardBlink.Checked = effect.HardBlink;
            _sequence.Colors = effect.Sequence.Select(item => item.Color).ToList();
            _customSequenceColorsEnabled = effect.CustomSequenceColorsEnabled;
        }
        finally
        {
            _loadingSettings = wasLoading;
        }

        if (markDirty)
        {
            _effectChangedByUser = true;
        }

        RefreshEffectPresetList(selectedPresetName);
        UpdateBrightnessAvailability();
        UpdateCustomColorsButton();
        UpdateEffectConfigurationVisibility();
        if (markDirty)
        {
            Text = "ClevoLEDKeyboardControl 设置 - 有未应用的更改";
        }
    }

    private string SelectedEffectPresetName() => _effectPreset.SelectedItem?.ToString() ?? "";

    private bool IsSoftwareDefaultEffectPresetSelected() =>
        string.Equals(SelectedEffectPresetName(), SoftwareDefaultPresetName, StringComparison.OrdinalIgnoreCase);

    private List<SequenceColor> BuildSequenceColors(EffectType effect)
    {
        var breathing = effect == EffectType.Sequence;
        return _sequence.Colors.Select(color => new SequenceColor
        {
            Color = color,
            HoldMs = _period.Value,
            TransitionMs = 0,
            Breathing = breathing
        }).ToList();
    }

    private static void RememberEffect(KeyboardSettings settings, LightingEffectSettings effect)
    {
        settings.SavedEffects ??= new EffectMemorySettings();
        var copy = KeyboardSettings.CloneEffect(effect);
        copy.Normalize();

        switch (copy.Type)
        {
            case EffectType.Static:
                settings.SavedEffects.Static = copy;
                break;
            case EffectType.Rainbow:
                copy.CustomSequenceColorsEnabled = true;
                settings.SavedEffects.Rainbow = copy;
                break;
            case EffectType.Breathing:
                settings.SavedEffects.Breathing = copy;
                break;
            case EffectType.Sequence:
                settings.SavedEffects.Sequence = copy;
                break;
            case EffectType.Pulse:
                settings.SavedEffects.Pulse = copy;
                break;
            case EffectType.Heartbeat:
                settings.SavedEffects.Heartbeat = copy;
                break;
        }

        settings.SavedEffects.Normalize();
    }

    private static int EffectivePeriodValue(LightingEffectSettings effect)
    {
        if (effect.Type == EffectType.Rainbow)
        {
            var first = effect.Sequence.FirstOrDefault();
            if (first is null || first.TransitionMs > 0)
            {
                return DefaultRainbowHoldMs;
            }

            return Math.Clamp(first.HoldMs, 300, 30000);
        }

        if (effect.Type == EffectType.Sequence)
        {
            var holdMs = effect.Sequence.FirstOrDefault()?.HoldMs ?? effect.PeriodMs;
            return Math.Clamp(holdMs, 300, 30000);
        }

        return effect.PeriodMs;
    }

    private string BuildSequenceSummary(EffectType effect)
    {
        var count = _sequence.Colors.Count;
        if (count == 0)
        {
            return "还没有选择颜色。";
        }

        if (effect == EffectType.Sequence)
        {
            return $"已选择 {count} 个颜色；每个颜色呼吸 {_period.Value} ms，整轮约 {FormatDuration(count * _period.Value)}。";
        }

        if (effect == EffectType.Pulse)
        {
            return $"已选择 {count} 个颜色；每个颜色脉冲 {_period.Value} ms，整轮约 {FormatDuration(count * _period.Value)}。";
        }

        if (effect == EffectType.Heartbeat)
        {
            return $"已选择 {count} 个颜色；每个颜色完成一组心跳 {_period.Value} ms，整轮约 {FormatDuration(count * _period.Value)}。";
        }

        var holdMs = _period.Value;
        return $"已选择 {count} 个颜色；每个颜色停留约 {holdMs} ms，整轮约 {FormatDuration(count * holdMs)}。";
    }

    private static string FormatDuration(int milliseconds)
    {
        if (milliseconds < 10000)
        {
            return $"{milliseconds / 1000d:0.0} 秒";
        }

        return $"{milliseconds / 1000d:0} 秒";
    }

    private void ApplySpeed(KeyboardSettings settings)
    {
        (settings.Effect.Step, settings.Effect.IntervalMs) = _speed.SelectedIndex switch
        {
            0 => (1, 160),
            1 => (1, 80),
            3 => (6, 30),
            4 => (10, 20),
            _ => (3, 40)
        };
    }

    private static int SpeedToIndex(int step, int intervalMs)
    {
        if (step <= 1 && intervalMs >= 120) return 0;
        if (step <= 1 || intervalMs >= 80) return 1;
        if (step >= 10 || intervalMs <= 20) return 4;
        if (step >= 6 || intervalMs <= 30) return 3;
        return 2;
    }

    private static int ClosestIndex(double[] values, double target)
    {
        var best = 0;
        var bestDistance = double.MaxValue;
        for (var i = 0; i < values.Length; i++)
        {
            var distance = Math.Abs(values[i] - target);
            if (distance < bestDistance)
            {
                best = i;
                bestDistance = distance;
            }
        }

        return best;
    }

    private static int ClosestIndex(int[] values, int target)
    {
        var best = 0;
        var bestDistance = int.MaxValue;
        for (var i = 0; i < values.Length; i++)
        {
            var distance = Math.Abs(values[i] - target);
            if (distance < bestDistance)
            {
                best = i;
                bestDistance = distance;
            }
        }

        return best;
    }

    private static int SecondsToIdleIndex(int seconds) => seconds switch
    {
        <= 60 => 0,
        <= 180 => 1,
        <= 300 => 2,
        <= 600 => 3,
        _ => 4
    };

    private static int IdleIndexToSeconds(int index) => index switch
    {
        0 => 60,
        1 => 180,
        2 => 300,
        3 => 600,
        _ => 1800
    };

    private static int UpdateIntervalToIndex(UpdateCheckInterval interval) => interval switch
    {
        UpdateCheckInterval.Never => 0,
        UpdateCheckInterval.Weekly => 2,
        UpdateCheckInterval.Monthly => 3,
        _ => 1
    };

    private static UpdateCheckInterval IndexToUpdateInterval(int index) => index switch
    {
        0 => UpdateCheckInterval.Never,
        2 => UpdateCheckInterval.Weekly,
        3 => UpdateCheckInterval.Monthly,
        _ => UpdateCheckInterval.Daily
    };

    private void RefreshMusicPresetList(string? selectedName = null)
    {
        var selected = selectedName ?? SelectedMusicPresetName();
        _loadingMusicPreset = true;
        try
        {
            _musicPreset.Items.Clear();

            foreach (var preset in MusicSettings.BuiltInPresets)
            {
                _musicPreset.Items.Add(preset.Name);
            }

            foreach (var preset in _musicCustomPresets)
            {
                _musicPreset.Items.Add(preset.Name);
            }

            var index = 0;
            for (var i = 0; i < _musicPreset.Items.Count; i++)
            {
                if (string.Equals(_musicPreset.Items[i]?.ToString(), selected, StringComparison.OrdinalIgnoreCase))
                {
                    index = i;
                    break;
                }
            }

            _musicPreset.SelectedIndex = _musicPreset.Items.Count == 0 ? -1 : index;
        }
        finally
        {
            _loadingMusicPreset = false;
        }

        UpdateMusicPresetButtons();
    }

    private void EditMusicColors()
    {
        using var dialog = new ColorSelectionDialog(NormalizedMusicColors(), singleSelection: false);
        if (dialog.ShowDialog(this) != DialogResult.OK)
        {
            return;
        }

        _musicSequence.Colors = dialog.SelectedColors.Count == 0
            ? MusicSettings.DefaultColors()
            : dialog.SelectedColors;
        Text = "ClevoLEDKeyboardControl 设置 - 有未应用的更改";
    }

    private List<string> NormalizedMusicColors()
    {
        var colors = _musicSequence.Colors
            .Select(color => RgbColor.FromHex(color).Hex)
            .ToList();
        return colors.Count == 0 ? MusicSettings.DefaultColors() : colors;
    }

    private void SaveSelectedMusicPreset()
    {
        if (IsBuiltInMusicPreset(SelectedMusicPresetName()))
        {
            MessageBox.Show("内置预设不能被修改；请使用“新建/另存为”创建一个自定义预设。", "ClevoLEDKeyboardControl", MessageBoxButtons.OK, MessageBoxIcon.Information);
            return;
        }

        var originalName = SelectedMusicPresetName();
        var newName = _musicPresetName.Text.Trim();
        if (string.IsNullOrWhiteSpace(newName))
        {
            newName = originalName;
        }

        if (UpsertMusicPreset(newName, originalName))
        {
            RefreshMusicPresetList(newName);
            Text = "ClevoLEDKeyboardControl 设置 - 有未应用的更改";
        }
    }

    private void CreateMusicPreset()
    {
        var name = PromptForEffectPresetName();
        if (name is null)
        {
            return;
        }

        if (UpsertMusicPreset(name, originalName: null))
        {
            RefreshMusicPresetList(name);
            Text = "ClevoLEDKeyboardControl 设置 - 有未应用的更改";
        }
    }

    private bool UpsertMusicPreset(string name, string? originalName)
    {
        name = name.Trim();
        if (string.IsNullOrWhiteSpace(name))
        {
            MessageBox.Show("请先输入自定义预设名称。", "ClevoLEDKeyboardControl", MessageBoxButtons.OK, MessageBoxIcon.Information);
            return false;
        }

        if (IsBuiltInMusicPreset(name))
        {
            MessageBox.Show("自定义预设不能使用内置预设名称。", "ClevoLEDKeyboardControl", MessageBoxButtons.OK, MessageBoxIcon.Information);
            return false;
        }

        var existing = _musicCustomPresets.FindIndex(item => string.Equals(item.Name, name, StringComparison.OrdinalIgnoreCase));
        var originalIndex = string.IsNullOrWhiteSpace(originalName)
            ? -1
            : _musicCustomPresets.FindIndex(item => string.Equals(item.Name, originalName, StringComparison.OrdinalIgnoreCase));
        var preset = BuildMusicPresetFromControls(name);
        preset.Id = originalIndex >= 0
            ? _musicCustomPresets[originalIndex].Id
            : existing >= 0 ? _musicCustomPresets[existing].Id : Guid.NewGuid().ToString("N");

        if (existing >= 0 && existing != originalIndex)
        {
            var choice = MessageBox.Show($"预设“{name}”已存在，是否覆盖？", "ClevoLEDKeyboardControl", MessageBoxButtons.YesNo, MessageBoxIcon.Question);
            if (choice != DialogResult.Yes)
            {
                return false;
            }
        }

        if (existing < 0 && originalIndex < 0 && _musicCustomPresets.Count >= 8)
        {
            MessageBox.Show("最多保存 8 个自定义音乐预设。", "ClevoLEDKeyboardControl", MessageBoxButtons.OK, MessageBoxIcon.Information);
            return false;
        }

        if (existing >= 0)
        {
            _musicCustomPresets[existing] = preset;
            if (originalIndex >= 0 && originalIndex != existing)
            {
                _musicCustomPresets.RemoveAt(originalIndex);
            }
        }
        else if (originalIndex >= 0)
        {
            _musicCustomPresets[originalIndex] = preset;
        }
        else
        {
            _musicCustomPresets.Add(preset);
        }

        return true;
    }

    private void DeleteSelectedCustomMusicPreset()
    {
        var name = SelectedMusicPresetName();
        if (IsBuiltInMusicPreset(name))
        {
            MessageBox.Show("内置预设不能删除。", "ClevoLEDKeyboardControl", MessageBoxButtons.OK, MessageBoxIcon.Information);
            return;
        }

        _musicCustomPresets.RemoveAll(item => string.Equals(item.Name, name, StringComparison.OrdinalIgnoreCase));
        _musicPresetName.Text = "";
        RefreshMusicPresetList(MusicSettings.DefaultPresetName);
        Text = "ClevoLEDKeyboardControl 设置 - 有未应用的更改";
    }

    private void ApplyMusicPresetToControls(MusicPreset preset, bool refreshSelection = true, bool markDirty = true)
    {
        _musicPresetName.Text = IsBuiltInMusicPreset(preset.Name) ? "" : preset.Name;
        _musicResponseMode.SelectedIndex = preset.ResponseMode == MusicResponseMode.BrightnessPulse ? 1 : 0;
        _musicSequence.Colors = preset.Colors;
        _musicSensitivity.SelectedIndex = ClosestIndex(MusicSensitivityValues, preset.Sensitivity);
        _musicAttack.SelectedIndex = ClosestIndex(MusicAttackValues, preset.AttackMs);
        _musicRelease.SelectedIndex = ClosestIndex(MusicReleaseValues, preset.ReleaseMs);
        _musicNoiseGate.Value = (int)Math.Round(preset.NoiseGate * 100);
        _musicBeatThreshold.Value = (int)Math.Round(preset.BeatThreshold * 100);
        _musicEqEnabled.Checked = preset.EqEnabled;
        _musicEqLow.Value = preset.EqLowHz;
        _musicEqHigh.Value = preset.EqHighHz;
        _musicBaseBrightness.Value = preset.BaseBrightness;
        _musicPeakBrightness.Value = preset.PeakBrightness;
        _musicFollowSystemVolume.Checked = preset.FollowSystemVolume;
        if (refreshSelection)
        {
            RefreshMusicPresetList(preset.Name);
        }

        if (markDirty)
        {
            Text = "ClevoLEDKeyboardControl 设置 - 有未应用的更改";
        }

        UpdateMusicPresetButtons();
    }

    private void UpdateMusicPresetButtons()
    {
        var customSelected = !IsBuiltInMusicPreset(SelectedMusicPresetName());
        _musicSavePreset.Enabled = customSelected;
        _musicDeletePreset.Enabled = customSelected;
        _musicCreatePreset.Enabled = true;
    }

    private MusicPreset BuildMusicPresetFromControls(string name)
    {
        var colors = NormalizedMusicColors();
        return new MusicPreset
        {
            Name = name,
            ResponseMode = _musicResponseMode.SelectedIndex == 1 ? MusicResponseMode.BrightnessPulse : MusicResponseMode.LevelColor,
            LowColor = colors[0],
            HighColor = colors[^1],
            Colors = colors,
            Sensitivity = MusicSensitivityValues[ClampIndex(_musicSensitivity.SelectedIndex, MusicSensitivityValues.Length)],
            AttackMs = MusicAttackValues[ClampIndex(_musicAttack.SelectedIndex, MusicAttackValues.Length)],
            ReleaseMs = MusicReleaseValues[ClampIndex(_musicRelease.SelectedIndex, MusicReleaseValues.Length)],
            BaseBrightness = _musicBaseBrightness.Value,
            PeakBrightness = _musicPeakBrightness.Value,
            NoiseGate = _musicNoiseGate.Value / 100d,
            BeatThreshold = _musicBeatThreshold.Value / 100d,
            FollowSystemVolume = _musicFollowSystemVolume.Checked,
            EqEnabled = _musicEqEnabled.Checked,
            EqLowHz = _musicEqLow.Value,
            EqHighHz = _musicEqHigh.Value
        }.Normalize();
    }

    private MusicPreset? FindSelectedMusicPreset()
    {
        var name = SelectedMusicPresetName();
        return MusicSettings.BuiltInPresets.FirstOrDefault(item => string.Equals(item.Name, name, StringComparison.OrdinalIgnoreCase)) ??
            _musicCustomPresets.FirstOrDefault(item => string.Equals(item.Name, name, StringComparison.OrdinalIgnoreCase));
    }

    private string SelectedMusicPresetName()
    {
        return _musicPreset.SelectedItem?.ToString() ?? MusicSettings.DefaultPresetName;
    }

    private static bool IsBuiltInMusicPreset(string? name)
    {
        return MusicSettings.IsBuiltInPresetName(name);
    }

    private static int ClampIndex(int index, int length)
    {
        return Math.Clamp(index, 0, Math.Max(0, length - 1));
    }

    private void UpdateMusicAdvancedVisibility()
    {
        var visible = _musicAdvanced.Checked;
        if (_musicSensitivityRow is not null) _musicSensitivityRow.Visible = visible;
        if (_musicAttackRow is not null) _musicAttackRow.Visible = visible;
        if (_musicReleaseRow is not null) _musicReleaseRow.Visible = visible;
        _musicNoiseGate.Visible = visible;
        _musicBeatThreshold.Visible = visible;
        _musicEqEnabled.Visible = visible;
        _musicEqLow.Visible = visible;
        _musicEqHigh.Visible = visible;
    }

    private static MusicPreset CloneMusicPreset(MusicPreset preset)
    {
        return new MusicPreset
        {
            Id = preset.Id,
            Name = preset.Name,
            ResponseMode = preset.ResponseMode,
            LowColor = preset.LowColor,
            HighColor = preset.HighColor,
            Colors = [.. preset.Colors],
            Sensitivity = preset.Sensitivity,
            AttackMs = preset.AttackMs,
            ReleaseMs = preset.ReleaseMs,
            BaseBrightness = preset.BaseBrightness,
            PeakBrightness = preset.PeakBrightness,
            IntervalMs = preset.IntervalMs,
            NoiseGate = preset.NoiseGate,
            BeatThreshold = preset.BeatThreshold,
            PeakHoldMs = preset.PeakHoldMs,
            FollowSystemVolume = preset.FollowSystemVolume,
            EqEnabled = preset.EqEnabled,
            EqLowHz = preset.EqLowHz,
            EqHighHz = preset.EqHighHz
        }.Normalize();
    }

    private void UpdateAutomationStatus()
    {
        var status = AutomationStatus.Load();
        if (status is null || DateTimeOffset.UtcNow - status.UpdatedUtc > TimeSpan.FromSeconds(10))
        {
            _automationStatus.Text = "服务状态尚未更新。应用条件需要托盘程序保持运行。";
            RefreshMusicBindingStatus();
            if (_navigation?.SelectedIndex == 3) _sceneAutomation.RefreshRuntimeState();
            UpdateOverviewRuntime(status);
            return;
        }

        RefreshMusicBindingStatus(status);

        var foreground = status.ForegroundAvailable
            ? $"前台：{status.ForegroundProcessName}"
            : "应用检测不可用";
        var active = string.IsNullOrWhiteSpace(status.ActiveRuleName)
            ? "当前：基础设置"
            : $"当前：{status.ActiveRuleName} → {status.TargetDescription}";
        var idle = status.IdleOverrideActive ? "；空闲覆盖生效" : "";
        var invalid = string.IsNullOrWhiteSpace(status.InvalidReason) ? "" : $"；提示：{status.InvalidReason}";
        var audio = string.IsNullOrWhiteSpace(status.ActiveMusicApplication)
            ? ""
            : $"；音频：{status.ActiveMusicApplication} PID {string.Join(",", status.ActiveProcessIds)}，{status.AudioCaptureMode}";
        var track = string.IsNullOrWhiteSpace(status.TrackTitle)
            ? ""
            : $"；歌曲：{status.TrackTitle}{(string.IsNullOrWhiteSpace(status.TrackArtist) ? "" : " - " + status.TrackArtist)}";
        var color = string.IsNullOrWhiteSpace(status.AlbumColor) ? "" : $"；封面色：{status.AlbumColor}";
        _automationStatus.Text = $"{active}；{foreground}{audio}{track}{color}{idle}{invalid}";
        if (_navigation?.SelectedIndex == 3) _sceneAutomation.RefreshRuntimeState();
        UpdateOverviewRuntime(status);
    }

    private DiagnosticsSnapshot CollectDiagnostics()
    {
        var settings = _settingsStore.Load();
        var foreground = ForegroundAppState.Load();
        var foregroundText = foreground is null
            ? "无状态"
            : $"{foreground.ProcessName}，{FormatAge(DateTimeOffset.UtcNow - foreground.UpdatedUtc)}前更新";

        var automation = AutomationStatus.Load();
        var audioState = AudioApplicationsState.Load();
        var mediaState = MediaPlaybackState.Load();
        var recovery = SettingsRecoveryState.Load();
        var matched = automation is null
            ? "无自动化状态"
            : string.IsNullOrWhiteSpace(automation.ActiveRuleName)
                ? $"基础设置{(string.IsNullOrWhiteSpace(automation.InvalidReason) ? "" : $"；{automation.InvalidReason}")}"
                : $"{automation.ActiveRuleName} → {automation.TargetDescription}{(automation.IdleOverrideActive ? "；空闲覆盖" : "")}";

        return new DiagnosticsSnapshot(
            ServiceStatus: GetServiceStatusText(),
            DriverStatus: GetDriverStatusText(),
            ForegroundApp: foregroundText,
            MatchedProfile: settings.Automation.Enabled ? matched : "场景自动化未启用",
            UpdateStatus: GetUpdateStatusText(settings.Update.CheckInterval),
            MonitorStatus: BuildMonitorStatus(audioState, mediaState),
            RecoveryStatus: recovery is null ? "未发生配置恢复" :
                $"{recovery.Result}，{FormatAge(DateTimeOffset.UtcNow - recovery.UpdatedUtc)}前",
            ConfigDirectory: AppPaths.ProgramDataDirectory);
    }

    private static string BuildMonitorStatus(AudioApplicationsState? audio, MediaPlaybackState? media)
    {
        var errors = new[] { audio?.LastError, media?.LastError }
            .Where(value => !string.IsNullOrWhiteSpace(value)).ToList();
        if (errors.Count > 0) return string.Join("；", errors);
        if (audio is null || DateTimeOffset.UtcNow - audio.UpdatedUtc > TimeSpan.FromSeconds(10))
            return "托盘未运行或音频检测状态过期";
        var ipc = ServiceIpc.IsAvailable() ? "安全管道已连接" : "安全管道不可用，设置只读";
        return $"{ipc}；音频程序 {audio.Applications.Count} 个；媒体会话 {media?.Sessions.Count ?? 0} 个";
    }

    private static string GetServiceStatusText()
    {
        try
        {
            using var controller = new System.ServiceProcess.ServiceController(AppPaths.ServiceName);
            return controller.Status switch
            {
                System.ServiceProcess.ServiceControllerStatus.Running => "运行中",
                System.ServiceProcess.ServiceControllerStatus.Stopped => "已停止",
                System.ServiceProcess.ServiceControllerStatus.Paused => "已暂停",
                System.ServiceProcess.ServiceControllerStatus.StartPending => "正在启动",
                System.ServiceProcess.ServiceControllerStatus.StopPending => "正在停止",
                _ => controller.Status.ToString()
            };
        }
        catch (InvalidOperationException)
        {
            return "未安装";
        }
        catch (Exception ex) when (ex is System.ComponentModel.Win32Exception or UnauthorizedAccessException)
        {
            return $"无法读取：{ex.Message}";
        }
    }

    private static string GetDriverStatusText()
    {
        var state = LoadDriverComponentState();
        if (state is not null)
        {
            if (string.Equals(state.Status, "Installed", StringComparison.OrdinalIgnoreCase) &&
                !string.IsNullOrWhiteSpace(state.InstalledPath) &&
                File.Exists(state.InstalledPath))
            {
                return $"已安装（来源：{state.Source ?? "未知"}）：{state.InstalledPath}";
            }

            if (string.Equals(state.Status, "Missing", StringComparison.OrdinalIgnoreCase))
            {
                return "未找到（安装器最近检查未命中）";
            }
        }

        var serviceDll = Path.Combine(AppContext.BaseDirectory, "InsydeDCHU.dll");
        var installedServiceDll = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles),
            "ClevoLEDKeyboardControl",
            "Service",
            "InsydeDCHU.dll");
        var controlCenterDll = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86),
            "ControlCenter",
            "InsydeDCHU.dll");

        if (File.Exists(serviceDll))
        {
            return $"已安装（来源：托盘目录）：{serviceDll}";
        }

        if (File.Exists(installedServiceDll))
        {
            return $"已安装（来源：服务目录）：{installedServiceDll}";
        }

        if (File.Exists(controlCenterDll))
        {
            return $"OEM Control Center 中存在，未复制到服务目录";
        }

        return "未找到";
    }

    private static DriverComponentState? LoadDriverComponentState()
    {
        try
        {
            if (!File.Exists(AppPaths.DriverComponentStatePath))
            {
                return null;
            }

            var json = File.ReadAllText(AppPaths.DriverComponentStatePath);
            return JsonSerializer.Deserialize<DriverComponentState>(json);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or JsonException)
        {
            return null;
        }
    }

    private static string GetUpdateStatusText(UpdateCheckInterval interval)
    {
        var lastChecked = UpdateChecker.LoadLastCheckedUtc();
        var intervalText = interval switch
        {
            UpdateCheckInterval.Never => "从不",
            UpdateCheckInterval.Weekly => "每周",
            UpdateCheckInterval.Monthly => "每月",
            _ => "每天"
        };

        return lastChecked is null
            ? $"检查频率：{intervalText}，尚未检查"
            : $"检查频率：{intervalText}，上次：{lastChecked.Value.ToLocalTime():yyyy-MM-dd HH:mm}";
    }

    private static string FormatAge(TimeSpan age)
    {
        if (age.TotalSeconds < 1)
        {
            return "刚刚";
        }

        if (age.TotalMinutes < 1)
        {
            return $"{Math.Max(1, (int)age.TotalSeconds)} 秒";
        }

        if (age.TotalHours < 1)
        {
            return $"{(int)age.TotalMinutes} 分钟";
        }

        return $"{(int)age.TotalHours} 小时";
    }

    private static string EffectTypeLabel(EffectType effect) => effect switch
    {
        EffectType.Static => "固定颜色",
        EffectType.Breathing => "单色呼吸",
        EffectType.Rainbow => "RGB 循环",
        EffectType.Sequence => "循环呼吸",
        EffectType.Pulse => "脉冲",
        EffectType.Heartbeat => "心跳",
        EffectType.Off => "关闭",
        _ => effect.ToString()
    };

    private void OnModeChanged()
    {
        if (_loadingSettings) return;
        UpdateModeAvailability();
        UpdateEffectConfigurationVisibility();
        if (_modeMusic.Checked)
        {
            // 切到音乐模式：自动跳到"音乐"导航页
            if (_navigation is not null)
            {
                for (var i = 0; i < _navigation.Items.Count; i++)
                {
                    var label = _navigation.Items[i]?.ToString();
                    if (string.Equals(label, "音乐", StringComparison.Ordinal))
                    {
                        _navigation.SelectedIndex = i;
                        break;
                    }
                }
            }
        }
        else if (_modeLighting.Checked)
        {
            // 切回灯效模式：从 LastUsedLightingEffect 恢复
            var settings = _settingsStore.Load();
            var lastEffect = settings.SavedEffects?.LastUsedLightingEffect ?? EffectType.Static;
            if (lastEffect == EffectType.Off) lastEffect = EffectType.Static;
            _effectType.SelectedIndex = EffectTypeToIndex(lastEffect);
        }
        // 关闭模式：什么都不做，仅 UpdateModeAvailability 会禁用其他控件
    }

    private void UpdateModeAvailability()
    {
        var music = _modeMusic.Checked;
        var off = _modeOff.Checked;
        var lightingEditable = !music && !off;
        _effectType.Enabled = lightingEditable;
        UpdateBrightnessAvailability();
        _effectColor.Enabled = lightingEditable;
        _period.Enabled = lightingEditable;
        _minimumBrightness.Enabled = lightingEditable;
        _hardBlink.Enabled = lightingEditable;
        _sequence.Enabled = lightingEditable;
        _customColors.Enabled = lightingEditable;
        _effectPreset.Enabled = lightingEditable;
        _effectPresetName.Enabled = lightingEditable;
        _effectSavePreset.Enabled = lightingEditable;
        _effectCreatePreset.Enabled = lightingEditable;
        _effectDeletePreset.Enabled = lightingEditable;
    }

    private static TextBox DiagnosticTextBox()
    {
        return new TextBox
        {
            ReadOnly = true,
            Width = 520
        };
    }

    private sealed record DiagnosticsSnapshot(
        string ServiceStatus,
        string DriverStatus,
        string ForegroundApp,
        string MatchedProfile,
        string UpdateStatus,
        string MonitorStatus,
        string RecoveryStatus,
        string ConfigDirectory);

    private sealed record DriverComponentState(
        string Status,
        string? Source,
        string? SourcePath,
        string InstalledPath,
        DateTimeOffset UpdatedUtc);

    public void UpdateAudioSourceLabel(AudioSourceStatusInfo? info)
    {
        if (IsDisposed) return;
        if (InvokeRequired)
        {
            try { BeginInvoke(new Action(() => UpdateAudioSourceLabel(info))); }
            catch (ObjectDisposedException) { }
            catch (InvalidOperationException) { }
            return;
        }

        var deviceName = info?.DeviceFriendlyName ?? "";
        _audioSourceLabel.Text = string.IsNullOrEmpty(deviceName)
            ? "当前音频源：检测中…"
            : $"当前音频源：{deviceName}";
    }

    private void ExportConfiguration()
    {
        using var dialog = new SaveFileDialog { Filter = "JSON 配置|*.json", FileName = $"ClevoLEDKeyboardControl-{DateTime.Now:yyyyMMdd-HHmmss}.json" };
        if (dialog.ShowDialog(this) != DialogResult.OK) return;
        var options = new JsonSerializerOptions { WriteIndented = true };
        options.Converters.Add(new System.Text.Json.Serialization.JsonStringEnumConverter());
        File.WriteAllText(dialog.FileName, JsonSerializer.Serialize(_settingsStore.Load(), options));
    }

    private void ImportConfiguration()
    {
        using var dialog = new OpenFileDialog { Filter = "JSON 配置|*.json", CheckFileExists = true };
        if (dialog.ShowDialog(this) != DialogResult.OK) return;
        var json = File.ReadAllText(dialog.FileName);
        if (!SettingsStore.TryParse(json, out var settings, out var error))
        {
            MessageBox.Show($"配置验证失败，当前设置未改变：{error}", "导入配置", MessageBoxButtons.OK, MessageBoxIcon.Warning);
            return;
        }
        _settingsStore.Save(settings);
        LoadSettings();
        MessageBox.Show("配置已验证并导入。", "导入配置", MessageBoxButtons.OK, MessageBoxIcon.Information);
    }

    private void RestoreLastGoodConfiguration()
    {
        var path = AppPaths.SettingsPath + SettingsStore.LastGoodSuffix;
        if (!File.Exists(path))
        {
            MessageBox.Show("没有可用的最近备份。", "恢复配置", MessageBoxButtons.OK, MessageBoxIcon.Information);
            return;
        }
        if (!SettingsStore.TryParse(File.ReadAllText(path), out var settings, out var error))
        {
            MessageBox.Show($"最近备份无效：{error}", "恢复配置", MessageBoxButtons.OK, MessageBoxIcon.Warning);
            return;
        }
        _settingsStore.Save(settings);
        LoadSettings();
        MessageBox.Show("已恢复最近一次有效配置。", "恢复配置", MessageBoxButtons.OK, MessageBoxIcon.Information);
    }
}

internal sealed class SliderRow : UserControl
{
    private readonly Label _label = new();
    private readonly TrackBar _track = new();
    private readonly Label _value = new();
    private readonly string _suffix;
    private readonly Func<int, string>? _formatter;

    public SliderRow(string label, int min, int max, string suffix, Func<int, string>? formatter = null)
    {
        _suffix = suffix;
        _formatter = formatter;
        Width = ContentWidth;
        Height = 64;

        _label.Text = label;
        _label.Width = LabelWidth;
        _label.Height = 30;
        _label.Location = new Point(0, 18);
        Controls.Add(_label);
        _track.Location = new Point(ControlLeft, 9);
        _track.Width = 470;
        _track.Minimum = min;
        _track.Maximum = max;
        _track.TickFrequency = Math.Max(1, (max - min) / 10);
        _track.ValueChanged += (_, _) => UpdateLabel();
        Controls.Add(_track);

        _value.Location = new Point(650, 18);
        _value.Width = 120;
        _value.Height = 30;
        Controls.Add(_value);
    }

    public int Value
    {
        get => _track.Value;
        set
        {
            _track.Value = Math.Clamp(value, _track.Minimum, _track.Maximum);
            UpdateLabel();
        }
    }

    public string LabelText
    {
        get => _label.Text;
        set => _label.Text = value;
    }

    public event EventHandler? ValueChanged
    {
        add => _track.ValueChanged += value;
        remove => _track.ValueChanged -= value;
    }

    private void UpdateLabel()
    {
        _value.Text = _formatter is null ? $"{_track.Value}{_suffix}" : $"{_formatter(_track.Value)}{_suffix}";
    }
}

internal sealed class ColorPickerRow : UserControl
{
    private readonly Panel _swatch = new();
    private readonly TextBox _hex = new();
    private readonly Button _pick;
    private readonly List<Button> _paletteButtons = [];

    public ColorPickerRow(string label)
    {
        Width = ContentWidth;
        Height = 54;
        Controls.Add(new Label { Text = label, Width = LabelWidth, Height = 30, Location = new Point(0, 13) });

        _swatch.Location = new Point(ControlLeft, 12);
        _swatch.Size = new Size(28, 24);
        _swatch.BorderStyle = BorderStyle.FixedSingle;
        _swatch.Click += (_, _) =>
        {
            if (!UseCompactEditor)
            {
                PickColor();
            }
        };
        Controls.Add(_swatch);

        _hex.Location = new Point(ControlLeft + 40, 12);
        _hex.Width = 90;
        _hex.TextChanged += (_, _) => UpdateSwatch();
        Controls.Add(_hex);

        _pick = new Button { Text = "选择", Location = new Point(ControlLeft + 142, 9), Width = 86, Height = ButtonHeight };
        _pick.Click += (_, _) => PickColor();
        Controls.Add(_pick);

        var palette = new[] { "#FF0000", "#FF8000", "#FFFF00", "#00FF00", "#00FFFF", "#0060FF", "#8000FF", "#FFFFFF", "#FFD2A1", "#CFE8FF" };
        var x = ControlLeft + 240;
        foreach (var color in palette)
        {
            var button = new Button { BackColor = ColorTranslator.FromHtml(color), Location = new Point(x, 9), Size = new Size(24, 24), FlatStyle = FlatStyle.Flat, AccessibleDescription = "ColorSwatch" };
            button.Click += (_, _) => ColorHex = color;
            Controls.Add(button);
            _paletteButtons.Add(button);
            x += 28;
        }
    }

    public bool UseCompactEditor
    {
        get => !_pick.Visible;
        set
        {
            _pick.Visible = !value;
            foreach (var button in _paletteButtons)
            {
                button.Visible = !value;
            }
        }
    }

    public string ColorHex
    {
        get => RgbColor.FromHex(_hex.Text).Hex;
        set
        {
            var normalized = RgbColor.FromHex(value).Hex;
            if (string.Equals(_hex.Text, normalized, StringComparison.OrdinalIgnoreCase))
            {
                UpdateSwatch();
                return;
            }

            _hex.Text = normalized;
            UpdateSwatch();
        }
    }

    public event EventHandler? ColorChanged;

    private void PickColor()
    {
        using var dialog = new ColorSelectionDialog([ColorHex], singleSelection: true);
        if (dialog.ShowDialog() == DialogResult.OK)
        {
            ColorHex = dialog.SelectedColors[0];
        }
    }

    private void UpdateSwatch()
    {
        try
        {
            var previous = _swatch.BackColor;
            _swatch.BackColor = ColorTranslator.FromHtml(RgbColor.FromHex(_hex.Text).Hex);
            _hex.BackColor = ThemeManager.Current.Field;
            if (previous.ToArgb() != _swatch.BackColor.ToArgb())
            {
                ColorChanged?.Invoke(this, EventArgs.Empty);
            }
        }
        catch (FormatException)
        {
            _hex.BackColor = Color.MistyRose;
        }
    }
}

internal sealed class TimeRangePicker : UserControl
{
    private readonly ComboBox _startHour = new();
    private readonly ComboBox _startMinute = new();
    private readonly ComboBox _endHour = new();
    private readonly ComboBox _endMinute = new();

    public TimeRangePicker(string label)
    {
        Width = ContentWidth;
        Height = RowHeight;
        Controls.Add(new Label { Text = label, Width = LabelWidth, Height = 30, Location = new Point(0, 10) });
        Setup(_startHour, ControlLeft, Enumerable.Range(0, 24).Select(value => value.ToString("00")));
        Setup(_startMinute, ControlLeft + 74, ["00", "15", "30", "45"]);
        Controls.Add(new Label { Text = "到", Location = new Point(ControlLeft + 146, 12), AutoSize = true });
        Setup(_endHour, ControlLeft + 184, Enumerable.Range(0, 24).Select(value => value.ToString("00")));
        Setup(_endMinute, ControlLeft + 258, ["00", "15", "30", "45"]);
    }

    public string StartTime => $"{_startHour.Text}:{_startMinute.Text}";

    public string EndTime => $"{_endHour.Text}:{_endMinute.Text}";

    public void SetRange(string start, string end)
    {
        SetTime(start, _startHour, _startMinute);
        SetTime(end, _endHour, _endMinute);
    }

    private void Setup(ComboBox combo, int x, IEnumerable<string> values)
    {
        combo.DropDownStyle = ComboBoxStyle.DropDownList;
        combo.Items.AddRange(values.Cast<object>().ToArray());
        combo.Location = new Point(x, 9);
        combo.Width = 64;
        combo.Height = 30;
        Controls.Add(combo);
    }

    private static void SetTime(string value, ComboBox hour, ComboBox minute)
    {
        if (!TimeOnly.TryParse(value, out var time))
        {
            time = new TimeOnly(0, 0);
        }

        hour.Text = time.Hour.ToString("00");
        minute.Text = ((time.Minute / 15) * 15).ToString("00");
    }
}

internal sealed class SequenceEditor : UserControl
{
    private const int ActionButtonX = 465;
    private const int SecondaryActionButtonX = 570;
    private const int ActionButtonWidth = 96;
    private const int WideActionButtonWidth = 116;
    private readonly ListBox _list = new();
    private readonly List<string> _colors = [];
    private readonly Button _add;
    private readonly Button _shuffle;

    public SequenceEditor()
    {
        Width = ContentWidth;
        Height = 190;

        _list.Location = new Point(ControlLeft, 6);
        _list.Size = new Size(280, 165);
        _list.DrawMode = DrawMode.OwnerDrawFixed;
        _list.ItemHeight = 24;
        _list.DrawItem += DrawItem;
        Controls.Add(_list);

        _add = AddButton("添加", ActionButtonX, 6, ActionButtonWidth, AddColor);
        AddButton("删除", ActionButtonX, 46, ActionButtonWidth, RemoveSelected);
        AddButton("上移", ActionButtonX, 86, ActionButtonWidth, () => MoveSelected(-1));
        AddButton("下移", ActionButtonX, 126, ActionButtonWidth, () => MoveSelected(1));
        _shuffle = AddButton("随机排序", SecondaryActionButtonX, 6, WideActionButtonWidth, ShuffleColors);
    }

    public bool ShowAddButton
    {
        get => _add.Visible;
        set
        {
            _add.Visible = value;
            _shuffle.Location = value
                ? new Point(SecondaryActionButtonX, 6)
                : new Point(ActionButtonX, 6);
        }
    }

    public List<string> Colors
    {
        get => [.. _colors];
        set
        {
            _colors.Clear();
            _colors.AddRange(value.Select(color => RgbColor.FromHex(color).Hex));
            RefreshList();
        }
    }

    public event EventHandler? ColorsChanged;

    private Button AddButton(string text, int x, int y, int width, Action action)
    {
        var button = new Button { Text = text, Location = new Point(x, y), Width = width, Height = ButtonHeight };
        button.Click += (_, _) => action();
        Controls.Add(button);
        return button;
    }

    private void AddColor()
    {
        using var dialog = new ColorSelectionDialog(_colors, singleSelection: false);
        if (dialog.ShowDialog() == DialogResult.OK)
        {
            _colors.Clear();
            _colors.AddRange(dialog.SelectedColors);
            RefreshList();
            ColorsChanged?.Invoke(this, EventArgs.Empty);
        }
    }

    private void RemoveSelected()
    {
        if (_list.SelectedIndex < 0)
        {
            return;
        }

        _colors.RemoveAt(_list.SelectedIndex);
        RefreshList();
        ColorsChanged?.Invoke(this, EventArgs.Empty);
    }

    private void MoveSelected(int direction)
    {
        var index = _list.SelectedIndex;
        var target = index + direction;
        if (index < 0 || target < 0 || target >= _colors.Count)
        {
            return;
        }

        (_colors[index], _colors[target]) = (_colors[target], _colors[index]);
        RefreshList();
        _list.SelectedIndex = target;
        ColorsChanged?.Invoke(this, EventArgs.Empty);
    }

    private void ShuffleColors()
    {
        if (_colors.Count < 2)
        {
            return;
        }

        var previous = _list.SelectedIndex >= 0 && _list.SelectedIndex < _colors.Count
            ? _colors[_list.SelectedIndex]
            : null;

        for (var i = _colors.Count - 1; i > 0; i--)
        {
            var target = Random.Shared.Next(i + 1);
            (_colors[i], _colors[target]) = (_colors[target], _colors[i]);
        }

        RefreshList();
        if (previous is not null)
        {
            _list.SelectedIndex = _colors.FindIndex(color => color == previous);
        }

        ColorsChanged?.Invoke(this, EventArgs.Empty);
    }

    private void RefreshList()
    {
        _list.Items.Clear();
        foreach (var color in _colors)
        {
            _list.Items.Add(color);
        }
    }

    private void DrawItem(object? sender, DrawItemEventArgs e)
    {
        if (e.Index < 0)
        {
            return;
        }

        e.DrawBackground();
        var color = ColorTranslator.FromHtml(_colors[e.Index]);
        using var brush = new SolidBrush(color);
        e.Graphics.FillRectangle(brush, e.Bounds.Left + 4, e.Bounds.Top + 4, 28, 16);
        e.Graphics.DrawRectangle(Pens.Black, e.Bounds.Left + 4, e.Bounds.Top + 4, 28, 16);
        e.Graphics.DrawString(_colors[e.Index], e.Font ?? Font, Brushes.Black, e.Bounds.Left + 40, e.Bounds.Top + 4);
        e.DrawFocusRectangle();
    }
}

internal sealed class SceneAutomationEditorV2 : UserControl
{
    private readonly TabControl _tabs = new() { Dock = DockStyle.Fill };
    private readonly AutomationRuleListBox _music = new() { Dock = DockStyle.Fill };
    private readonly AutomationRuleListBox _lighting = new() { Dock = DockStyle.Fill };
    private readonly AutomationRuleListBox _schedule = new() { Dock = DockStyle.Fill };
    private AutomationSettings _automation = new();
    private EffectPresetSettings _effectPresets = new();
    private List<MusicPreset> _musicPresets = [];

    public SceneAutomationEditorV2()
    {
        Width = UiMetrics.ContentWidth;
        Height = 430;
        Controls.Add(_tabs);
        _tabs.TabPages.Add(BuildTab("音乐程序", _music, AddMusic, EditMusic, RemoveMusic, MoveMusic));
        _tabs.TabPages.Add(BuildTab("灯效程序", _lighting, AddLighting, EditLighting, RemoveLighting, MoveLighting));
        _tabs.TabPages.Add(BuildTab("时间计划", _schedule, AddSchedule, EditSchedule, RemoveSchedule, MoveSchedule));
        _music.DoubleClick += (_, _) => EditMusic();
        _lighting.DoubleClick += (_, _) => EditLighting();
        _schedule.DoubleClick += (_, _) => EditSchedule();
    }

    public event EventHandler? Changed;

    public void RefreshRuntimeState() => RefreshLists(_music.SelectedIndex, _lighting.SelectedIndex, _schedule.SelectedIndex);

    public AutomationSettings Automation
    {
        get => Clone(_automation).Normalize();
        set { _automation = Clone(value ?? new AutomationSettings()).Normalize(); RefreshLists(); }
    }

    public void SetPresets(EffectPresetSettings effects, IEnumerable<MusicPreset> music)
    {
        _effectPresets = KeyboardSettings.CloneEffectPresets(effects);
        _musicPresets = MusicSettings.BuiltInPresets.Concat(music).Select(CloneMusicPreset).ToList();
    }

    private static TabPage BuildTab(string title, ListBox list, Action add, Action edit, Action remove, Action<int> move)
    {
        var page = new TabPage(title);
        var buttons = new FlowLayoutPanel { Dock = DockStyle.Bottom, Height = 44, FlowDirection = FlowDirection.LeftToRight };
        void Add(string text, Action action)
        {
            var button = new Button { Text = text, Width = 112, Height = 34 };
            button.Click += (_, _) => action();
            buttons.Controls.Add(button);
        }
        Add(title == "音乐程序" ? "绑定有声程序" : "添加", add);
        Add("编辑", edit);
        Add("删除", remove);
        Add("上移", () => move(-1));
        Add("下移", () => move(1));
        page.Controls.Add(list);
        page.Controls.Add(buttons);
        return page;
    }

    private void AddMusic()
    {
        using var picker = new AudioApplicationPickerForm();
        if (picker.ShowDialog() != DialogResult.OK || picker.Selected is null) return;
        var rule = new MusicApplicationRule
        {
            Name = picker.Selected.ProcessName,
            ProcessName = picker.Selected.ProcessName,
            ExecutablePath = picker.Selected.ExecutablePath,
            MediaSessionId = MediaPlaybackState.Load()?.Sessions.FirstOrDefault(session =>
                session.SourceId.Contains(picker.Selected.ProcessName, StringComparison.OrdinalIgnoreCase))?.SourceId ?? ""
        }.Normalize();
        using var dialog = AutomationRuleDialog.ForMusic(rule, _musicPresets);
        if (dialog.ShowDialog() != DialogResult.OK) return;
        _automation.MusicApplications.Add(rule.Normalize());
        RefreshLists();
        _music.SelectedIndex = _automation.MusicApplications.Count - 1;
        Changed?.Invoke(this, EventArgs.Empty);
    }

    private void EditMusic()
    {
        if (_music.SelectedIndex < 0) return;
        var rule = Clone(_automation.MusicApplications[_music.SelectedIndex]);
        using var dialog = AutomationRuleDialog.ForMusic(rule, _musicPresets);
        if (dialog.ShowDialog() != DialogResult.OK) return;
        _automation.MusicApplications[_music.SelectedIndex] = rule.Normalize();
        RefreshLists(_music.SelectedIndex, -1, -1);
        Changed?.Invoke(this, EventArgs.Empty);
    }

    private void AddLighting()
    {
        using var picker = new RunningAppsForm();
        if (picker.ShowDialog() != DialogResult.OK || string.IsNullOrWhiteSpace(picker.SelectedProcessName)) return;
        var rule = new LightingApplicationRule
        {
            Name = picker.SelectedProcessName,
            ProcessNames = [picker.SelectedProcessName]
        }.Normalize();
        using var dialog = AutomationRuleDialog.ForLighting(rule, _effectPresets);
        if (dialog.ShowDialog() != DialogResult.OK) return;
        _automation.LightingApplications.Add(rule.Normalize());
        RefreshLists();
        _lighting.SelectedIndex = _automation.LightingApplications.Count - 1;
        Changed?.Invoke(this, EventArgs.Empty);
    }

    private void EditLighting()
    {
        if (_lighting.SelectedIndex < 0) return;
        var rule = Clone(_automation.LightingApplications[_lighting.SelectedIndex]);
        using var dialog = AutomationRuleDialog.ForLighting(rule, _effectPresets);
        if (dialog.ShowDialog() != DialogResult.OK) return;
        _automation.LightingApplications[_lighting.SelectedIndex] = rule.Normalize();
        RefreshLists(-1, _lighting.SelectedIndex, -1);
        Changed?.Invoke(this, EventArgs.Empty);
    }

    private void AddSchedule()
    {
        var rule = new AutomationScheduleRule
        {
            TimeFilter = new AutomationTimeFilter { TimeEnabled = true, Start = "19:00", End = "23:00" }
        }.Normalize();
        using var dialog = AutomationRuleDialog.ForSchedule(rule, _effectPresets, _musicPresets);
        if (dialog.ShowDialog() != DialogResult.OK) return;
        _automation.ScheduleRules.Add(rule.Normalize());
        RefreshLists();
        _schedule.SelectedIndex = _automation.ScheduleRules.Count - 1;
        Changed?.Invoke(this, EventArgs.Empty);
    }

    private void EditSchedule()
    {
        if (_schedule.SelectedIndex < 0) return;
        var rule = Clone(_automation.ScheduleRules[_schedule.SelectedIndex]);
        using var dialog = AutomationRuleDialog.ForSchedule(rule, _effectPresets, _musicPresets);
        if (dialog.ShowDialog() != DialogResult.OK) return;
        _automation.ScheduleRules[_schedule.SelectedIndex] = rule.Normalize();
        RefreshLists(-1, -1, _schedule.SelectedIndex);
        Changed?.Invoke(this, EventArgs.Empty);
    }

    private void RemoveMusic() { if (_music.SelectedIndex >= 0) { _automation.MusicApplications.RemoveAt(_music.SelectedIndex); RefreshLists(); Changed?.Invoke(this, EventArgs.Empty); } }
    private void RemoveLighting() { if (_lighting.SelectedIndex >= 0) { _automation.LightingApplications.RemoveAt(_lighting.SelectedIndex); RefreshLists(); Changed?.Invoke(this, EventArgs.Empty); } }
    private void RemoveSchedule() { if (_schedule.SelectedIndex >= 0) { _automation.ScheduleRules.RemoveAt(_schedule.SelectedIndex); RefreshLists(); Changed?.Invoke(this, EventArgs.Empty); } }
    private void MoveMusic(int offset) => MoveRule(_automation.MusicApplications, _music, offset);
    private void MoveLighting(int offset) => MoveRule(_automation.LightingApplications, _lighting, offset);
    private void MoveSchedule(int offset) => MoveRule(_automation.ScheduleRules, _schedule, offset);

    private void MoveRule<T>(List<T> rules, ListBox list, int offset)
    {
        var from = list.SelectedIndex;
        var to = from + offset;
        if (from < 0 || to < 0 || to >= rules.Count) return;
        (rules[from], rules[to]) = (rules[to], rules[from]);
        RefreshLists();
        list.SelectedIndex = to;
        Changed?.Invoke(this, EventArgs.Empty);
    }

    private void RefreshLists(int music = -1, int lighting = -1, int schedule = -1)
    {
        var activeRuleId = AutomationStatus.Load()?.ActiveRuleId ?? "";
        _music.Items.Clear();
        foreach (var rule in _automation.MusicApplications)
        {
            var missingPreset = !_musicPresets.Any(preset => preset.Id == rule.MusicPresetId);
            var state = !rule.Enabled ? AutomationRuleVisualState.Disabled :
                string.IsNullOrWhiteSpace(rule.ProcessName) || missingPreset ? AutomationRuleVisualState.Error :
                rule.Id == activeRuleId ? AutomationRuleVisualState.Active : AutomationRuleVisualState.Normal;
            var reason = string.IsNullOrWhiteSpace(rule.ProcessName) ? "未配置进程" : missingPreset ? "音乐预设不存在" :
                $"{rule.ProcessName} · {ColorLabel(rule.ColorSource)} · {PresetName(rule.MusicPresetId, _musicPresets)}";
            _music.Items.Add(new AutomationRuleListItem(rule.Name, reason, state));
        }
        _lighting.Items.Clear();
        foreach (var rule in _automation.LightingApplications)
        {
            var invalid = rule.ProcessNames.Count == 0 || !ActionExists(rule.Action);
            var state = !rule.Enabled ? AutomationRuleVisualState.Disabled : invalid ? AutomationRuleVisualState.Error :
                rule.Id == activeRuleId ? AutomationRuleVisualState.Active : AutomationRuleVisualState.Normal;
            var process = rule.ProcessNames.Count == 0 ? "未配置前台进程" : string.Join("、", rule.ProcessNames);
            _lighting.Items.Add(new AutomationRuleListItem(rule.Name, $"{process} · {ActionLabel(rule.Action)}", state));
        }
        _schedule.Items.Clear();
        foreach (var rule in _automation.ScheduleRules)
        {
            var state = !rule.Enabled ? AutomationRuleVisualState.Disabled : !ActionExists(rule.Action) ? AutomationRuleVisualState.Error :
                rule.Id == activeRuleId ? AutomationRuleVisualState.Active : AutomationRuleVisualState.Normal;
            var time = rule.TimeFilter.TimeEnabled ? $"{rule.TimeFilter.Start}–{rule.TimeFilter.End}" : "全天";
            _schedule.Items.Add(new AutomationRuleListItem(rule.Name, $"{time} · {ActionLabel(rule.Action)}", state));
        }
        if (music >= 0 && music < _music.Items.Count) _music.SelectedIndex = music;
        if (lighting >= 0 && lighting < _lighting.Items.Count) _lighting.SelectedIndex = lighting;
        if (schedule >= 0 && schedule < _schedule.Items.Count) _schedule.SelectedIndex = schedule;
    }

    private static string ColorLabel(MusicColorSource source) => source switch
    {
        MusicColorSource.AlbumDominant => "封面主色",
        MusicColorSource.AlbumPalette => "封面配色",
        _ => "预设颜色"
    };

    private bool ActionExists(SceneAction action) => action.Target switch
    {
        SceneTargetKind.Off => true,
        SceneTargetKind.MusicPreset => _musicPresets.Any(preset => preset.Id == action.PresetId),
        SceneTargetKind.LightingPreset => action.PresetId == EffectPresetSettings.BuiltInId(action.LightingEffectType) ||
            _effectPresets.ForType(action.LightingEffectType).Any(preset => preset.Id == action.PresetId),
        _ => false
    };

    private string ActionLabel(SceneAction action) => action.Target switch
    {
        SceneTargetKind.Off => "关闭灯光",
        SceneTargetKind.MusicPreset => $"音乐：{PresetName(action.PresetId, _musicPresets)}",
        SceneTargetKind.LightingPreset => $"灯效：{_effectPresets.ForType(action.LightingEffectType).FirstOrDefault(p => p.Id == action.PresetId)?.Name ?? action.LightingEffectType.ToString()}",
        _ => "动作无效"
    };

    private static string PresetName(string id, IEnumerable<MusicPreset> presets) =>
        presets.FirstOrDefault(preset => preset.Id == id)?.Name ?? "预设不存在";

    private static T Clone<T>(T value) => JsonSerializer.Deserialize<T>(JsonSerializer.Serialize(value))!;
    private static MusicPreset CloneMusicPreset(MusicPreset value) => Clone(value);
}

internal enum AutomationRuleVisualState { Normal, Active, Disabled, Error }
internal sealed record AutomationRuleListItem(string Title, string Detail, AutomationRuleVisualState State)
{
    public override string ToString() => Title;
}

internal sealed class AutomationRuleListBox : ListBox
{
    public AutomationRuleListBox()
    {
        DrawMode = DrawMode.OwnerDrawFixed;
        ItemHeight = 58;
        BorderStyle = BorderStyle.None;
        IntegralHeight = false;
        Font = new Font("Segoe UI", 9F);
    }

    protected override void OnDrawItem(DrawItemEventArgs e)
    {
        if (e.Index < 0 || e.Index >= Items.Count) return;
        var theme = ThemeManager.Current;
        var selected = (e.State & DrawItemState.Selected) != 0;
        var item = Items[e.Index] as AutomationRuleListItem ?? new AutomationRuleListItem(Items[e.Index]?.ToString() ?? "", "", AutomationRuleVisualState.Normal);
        var bounds = Rectangle.Inflate(e.Bounds, -5, -4);
        using var background = new SolidBrush(selected ? theme.PrimarySoft : theme.Surface);
        using var border = new Pen(selected ? theme.Primary : theme.Border);
        e.Graphics.FillRectangle(background, bounds);
        e.Graphics.DrawRectangle(border, bounds);
        var stateColor = item.State switch
        {
            AutomationRuleVisualState.Active => theme.Success,
            AutomationRuleVisualState.Error => theme.Error,
            AutomationRuleVisualState.Disabled => theme.MutedText,
            _ => theme.Primary
        };
        using var stateBrush = new SolidBrush(stateColor);
        e.Graphics.FillEllipse(stateBrush, bounds.X + 10, bounds.Y + 12, 8, 8);
        var stateText = item.State switch
        {
            AutomationRuleVisualState.Active => "生效中",
            AutomationRuleVisualState.Error => "需要处理",
            AutomationRuleVisualState.Disabled => "已停用",
            _ => "已启用"
        };
        using var titleFont = new Font("Segoe UI Semibold", 9F);
        TextRenderer.DrawText(e.Graphics, item.Title, titleFont,
            new Rectangle(bounds.X + 26, bounds.Y + 5, bounds.Width - 120, 23), theme.Text,
            TextFormatFlags.Left | TextFormatFlags.VerticalCenter | TextFormatFlags.EndEllipsis);
        TextRenderer.DrawText(e.Graphics, stateText, Font,
            new Rectangle(bounds.Right - 88, bounds.Y + 5, 78, 23), stateColor,
            TextFormatFlags.Right | TextFormatFlags.VerticalCenter);
        TextRenderer.DrawText(e.Graphics, item.Detail, Font,
            new Rectangle(bounds.X + 26, bounds.Y + 28, bounds.Width - 36, 22), theme.MutedText,
            TextFormatFlags.Left | TextFormatFlags.VerticalCenter | TextFormatFlags.EndEllipsis);
        if ((e.State & DrawItemState.Focus) != 0) e.DrawFocusRectangle();
    }
}

internal sealed class PalettePreviewControl : Control
{
    private List<string> _colors = [];

    public PalettePreviewControl()
    {
        Width = 420;
        Height = 30;
        SetStyle(ControlStyles.OptimizedDoubleBuffer | ControlStyles.AllPaintingInWmPaint | ControlStyles.UserPaint, true);
    }

    public List<string> Colors
    {
        get => [.. _colors];
        set
        {
            _colors = (value ?? []).Where(color => !string.IsNullOrWhiteSpace(color)).ToList();
            Invalidate();
        }
    }

    protected override void OnPaint(PaintEventArgs e)
    {
        base.OnPaint(e);
        e.Graphics.Clear(ThemeManager.Current.Window);
        if (_colors.Count == 0)
        {
            using var emptyBorder = new Pen(ThemeManager.Current.Border);
            e.Graphics.DrawRectangle(emptyBorder, 0, 0, Width - 1, Height - 1);
            return;
        }
        var width = Math.Max(1, Width / _colors.Count);
        for (var index = 0; index < _colors.Count; index++)
        {
            try
            {
                var rgb = RgbColor.FromHex(_colors[index]);
                using var brush = new SolidBrush(Color.FromArgb(rgb.R, rgb.G, rgb.B));
                var left = index * width;
                var right = index == _colors.Count - 1 ? Width : left + width;
                e.Graphics.FillRectangle(brush, left, 0, right - left, Height);
            }
            catch (FormatException) { }
        }
        using var border = new Pen(ThemeManager.Current.Border);
        e.Graphics.DrawRectangle(border, 0, 0, Width - 1, Height - 1);
    }
}

internal sealed class AudioApplicationPickerForm : ThemedForm
{
    private readonly ListView _list = new() { Dock = DockStyle.Fill, View = View.Details, FullRowSelect = true };
    private readonly List<AudioApplicationStatus> _items;
    public AudioApplicationStatus? Selected { get; private set; }

    public AudioApplicationPickerForm(bool includeVisibleProcesses = false)
    {
        Text = "绑定正在运行的音乐程序";
        StartPosition = FormStartPosition.CenterParent;
        Size = new Size(720, 420);
        _list.Columns.Add("程序", 180);
        _list.Columns.Add("PID", 180);
        _list.Columns.Add("电平", 100);
        _list.Columns.Add("状态", 100);
        _items = AudioApplicationsState.Load()?.Applications.ToList()
            ?? AutomationStatus.Load()?.AudioApplications.ToList()
            ?? [];
        if (includeVisibleProcesses)
        {
            foreach (var process in Process.GetProcesses())
            using (process)
            {
                try
                {
                    if (process.MainWindowHandle == IntPtr.Zero) continue;
                    var name = AppProfileRule.NormalizeProcessName(process.ProcessName);
                    var path = process.MainModule?.FileName ?? "";
                    var existing = _items.FirstOrDefault(item =>
                        string.Equals(item.ProcessName, name, StringComparison.OrdinalIgnoreCase) &&
                        (string.IsNullOrWhiteSpace(item.ExecutablePath) ||
                         string.Equals(item.ExecutablePath, path, StringComparison.OrdinalIgnoreCase)));
                    if (existing is not null)
                    {
                        if (!existing.ProcessIds.Contains(process.Id)) existing.ProcessIds.Add(process.Id);
                        continue;
                    }
                    _items.Add(new AudioApplicationStatus
                    {
                        ProcessName = name,
                        ExecutablePath = path,
                        ProcessIds = [process.Id]
                    });
                }
                catch
                {
                }
            }
        }
        _items = _items.OrderByDescending(item => item.IsPlaying)
            .ThenByDescending(item => item.PeakLevel)
            .ThenBy(item => item.ProcessName, StringComparer.OrdinalIgnoreCase)
            .ToList();
        foreach (var app in _items)
        {
            var item = new ListViewItem(app.ProcessName);
            item.SubItems.Add(string.Join(",", app.ProcessIds));
            item.SubItems.Add($"{app.PeakLevel:P1}");
            item.SubItems.Add(app.IsPlaying ? "正在播放" : app.PeakLevel > 0.001f ? "检测声音中" : "已静音");
            _list.Items.Add(item);
        }
        var bind = new Button { Text = "绑定", Dock = DockStyle.Bottom, Height = 40 };
        bind.Click += (_, _) => Accept();
        _list.DoubleClick += (_, _) => Accept();
        Controls.Add(_list);
        Controls.Add(bind);
    }

    private void Accept()
    {
        if (_list.SelectedIndices.Count == 0) return;
        Selected = _items[_list.SelectedIndices[0]];
        DialogResult = DialogResult.OK;
        Close();
    }
}

internal sealed class AutomationRuleDialog : ThemedForm
{
    private enum RuleKind { Music, Lighting, Schedule }
    private readonly RuleKind _kind;
    private readonly MusicApplicationRule? _musicRule;
    private readonly LightingApplicationRule? _lightingRule;
    private readonly AutomationScheduleRule? _scheduleRule;
    private readonly TextBox _name = new();
    private readonly TextBox _process = new();
    private readonly CheckBox _enabled = new() { Text = "启用规则" };
    private readonly CheckBox _includeChildren = new() { Text = "包含子进程" };
    private readonly CheckBox _timeEnabled = new() { Text = "限制时间" };
    private readonly DateTimePicker _start = TimePicker();
    private readonly DateTimePicker _end = TimePicker();
    private readonly CheckedListBox _days = new() { Height = 72, CheckOnClick = true };
    private readonly ComboBox _target = DropDown();
    private readonly ComboBox _effectType = DropDown();
    private readonly ComboBox _preset = DropDown();
    private readonly ComboBox _colorSource = DropDown();
    private readonly ComboBox _mediaSession = DropDown();
    private readonly CheckBox _brightnessEnabled = new() { Text = "最大亮度" };
    private readonly NumericUpDown _brightness = new() { Minimum = 0, Maximum = 100 };
    private readonly ComboBox _typing = PolicyCombo();
    private readonly ComboBox _notification = PolicyCombo();
    private readonly EffectPresetSettings _effects;
    private readonly List<MusicPreset> _musicPresets;

    private AutomationRuleDialog(RuleKind kind, object rule, EffectPresetSettings effects, IEnumerable<MusicPreset> music)
    {
        _kind = kind;
        _musicRule = rule as MusicApplicationRule;
        _lightingRule = rule as LightingApplicationRule;
        _scheduleRule = rule as AutomationScheduleRule;
        _effects = effects;
        _musicPresets = music.ToList();
        Text = "编辑自动化规则";
        StartPosition = FormStartPosition.CenterParent;
        ClientSize = new Size(640, 650);
        var panel = new TableLayoutPanel { Dock = DockStyle.Fill, AutoScroll = true, ColumnCount = 2, Padding = new Padding(12) };
        panel.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 150));
        panel.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        Controls.Add(panel);
        Add(panel, "名称", _name);
        Add(panel, "状态", _enabled);
        Add(panel, "进程名", _process);
        Add(panel, "进程范围", _includeChildren);
        Add(panel, "时间条件", _timeEnabled);
        var range = new FlowLayoutPanel { AutoSize = true };
        range.Controls.Add(_start); range.Controls.Add(new Label { Text = "至", AutoSize = true }); range.Controls.Add(_end);
        Add(panel, "时间段", range);
        _days.Items.AddRange(["周一", "周二", "周三", "周四", "周五", "周六", "周日"]);
        Add(panel, "星期", _days);
        _target.Items.AddRange(kind == RuleKind.Lighting
            ? ["灯效预设", "关闭灯光"]
            : ["灯效预设", "音乐预设", "关闭灯光"]);
        _target.SelectedIndexChanged += (_, _) => RefreshPresets();
        Add(panel, "动作", _target);
        _effectType.Items.AddRange(["固定颜色", "RGB 循环", "单色呼吸", "颜色序列", "脉冲", "心跳"]);
        _effectType.SelectedIndexChanged += (_, _) => RefreshPresets();
        Add(panel, "灯效类型", _effectType);
        Add(panel, "目标预设", _preset);
        _colorSource.Items.AddRange(["预设颜色", "封面主色", "封面配色"]);
        Add(panel, "颜色来源", _colorSource);
        _mediaSession.Items.Add("自动匹配");
        foreach (var session in MediaPlaybackState.Load()?.Sessions ?? []) _mediaSession.Items.Add(session.SourceId);
        Add(panel, "媒体会话", _mediaSession);
        var brightness = new FlowLayoutPanel { AutoSize = true }; brightness.Controls.Add(_brightnessEnabled); brightness.Controls.Add(_brightness);
        Add(panel, "亮度", brightness);
        Add(panel, "打字反馈", _typing);
        Add(panel, "通知反馈", _notification);
        var buttons = new FlowLayoutPanel { AutoSize = true, FlowDirection = FlowDirection.RightToLeft };
        var ok = new Button { Text = "确定", DialogResult = DialogResult.OK };
        var cancel = new Button { Text = "取消", DialogResult = DialogResult.Cancel };
        ok.Click += (_, _) => Save();
        buttons.Controls.Add(ok); buttons.Controls.Add(cancel);
        Add(panel, "", buttons);
        AcceptButton = ok;
        CancelButton = cancel;
        LoadRule();
    }

    public static AutomationRuleDialog ForMusic(MusicApplicationRule rule, IEnumerable<MusicPreset> music) =>
        new(RuleKind.Music, rule, new EffectPresetSettings(), music);
    public static AutomationRuleDialog ForLighting(LightingApplicationRule rule, EffectPresetSettings effects) =>
        new(RuleKind.Lighting, rule, effects, []);
    public static AutomationRuleDialog ForSchedule(AutomationScheduleRule rule, EffectPresetSettings effects, IEnumerable<MusicPreset> music) =>
        new(RuleKind.Schedule, rule, effects, music);

    private void LoadRule()
    {
        var filter = _musicRule?.TimeFilter ?? _lightingRule?.TimeFilter ?? _scheduleRule!.TimeFilter;
        _name.Text = _musicRule?.Name ?? _lightingRule?.Name ?? _scheduleRule!.Name;
        _enabled.Checked = _musicRule?.Enabled ?? _lightingRule?.Enabled ?? _scheduleRule!.Enabled;
        _process.Text = _musicRule?.ProcessName ?? string.Join(", ", _lightingRule?.ProcessNames ?? []);
        _includeChildren.Checked = _musicRule?.IncludeChildProcesses ?? false;
        _timeEnabled.Checked = filter.TimeEnabled;
        _start.Value = DateTime.Today + TimeOnly.Parse(filter.Start).ToTimeSpan();
        _end.Value = DateTime.Today + TimeOnly.Parse(filter.End).ToTimeSpan();
        var dayValues = new[] { DayOfWeek.Monday, DayOfWeek.Tuesday, DayOfWeek.Wednesday, DayOfWeek.Thursday, DayOfWeek.Friday, DayOfWeek.Saturday, DayOfWeek.Sunday };
        for (var i = 0; i < dayValues.Length; i++) _days.SetItemChecked(i, filter.Days.Contains(dayValues[i]));
        _brightnessEnabled.Checked = (_musicRule?.BrightnessLimit ?? _lightingRule?.Action.BrightnessLimit ?? _scheduleRule?.Action.BrightnessLimit).HasValue;
        _brightness.Value = _musicRule?.BrightnessLimit ?? _lightingRule?.Action.BrightnessLimit ?? _scheduleRule?.Action.BrightnessLimit ?? 100;
        _typing.SelectedIndex = (int)(_musicRule?.TypingPolicy ?? _lightingRule?.TypingPolicy ?? EventPolicy.Inherit);
        _notification.SelectedIndex = (int)(_musicRule?.NotificationPolicy ?? _lightingRule?.NotificationPolicy ?? EventPolicy.Inherit);
        if (_kind == RuleKind.Music)
        {
            _target.SelectedIndex = (int)SceneTargetKind.MusicPreset;
            _colorSource.SelectedIndex = (int)_musicRule!.ColorSource;
            _mediaSession.SelectedIndex = FindItem(_mediaSession, _musicRule.MediaSessionId);
            RefreshPresets(_musicRule.MusicPresetId);
        }
        else
        {
            var action = _lightingRule?.Action ?? _scheduleRule!.Action;
            _target.SelectedIndex = _kind == RuleKind.Lighting && action.Target == SceneTargetKind.Off
                ? 1
                : (int)action.Target;
            _effectType.SelectedIndex = EffectIndex(action.LightingEffectType);
            RefreshPresets(action.PresetId);
        }
        _process.Enabled = _kind != RuleKind.Schedule;
        _includeChildren.Visible = _kind == RuleKind.Music;
        _colorSource.Visible = _kind == RuleKind.Music;
        _mediaSession.Visible = _kind == RuleKind.Music;
        _typing.Enabled = _kind != RuleKind.Schedule;
        _notification.Enabled = _kind != RuleKind.Schedule;
        _target.Enabled = _kind != RuleKind.Music;
    }

    private void Save()
    {
        var filter = _musicRule?.TimeFilter ?? _lightingRule?.TimeFilter ?? _scheduleRule!.TimeFilter;
        filter.TimeEnabled = _timeEnabled.Checked;
        filter.Start = _start.Value.ToString("HH:mm");
        filter.End = _end.Value.ToString("HH:mm");
        var dayValues = new[] { DayOfWeek.Monday, DayOfWeek.Tuesday, DayOfWeek.Wednesday, DayOfWeek.Thursday, DayOfWeek.Friday, DayOfWeek.Saturday, DayOfWeek.Sunday };
        filter.Days = dayValues.Where((_, index) => _days.GetItemChecked(index)).ToList();
        int? limit = _brightnessEnabled.Checked ? (int)_brightness.Value : null;
        if (_musicRule is not null)
        {
            _musicRule.Name = _name.Text; _musicRule.Enabled = _enabled.Checked;
            _musicRule.ProcessName = AppProfileRule.NormalizeProcessName(_process.Text);
            _musicRule.IncludeChildProcesses = _includeChildren.Checked;
            _musicRule.MusicPresetId = (_preset.SelectedItem as PresetOption)?.Id ?? "";
            _musicRule.ColorSource = (MusicColorSource)Math.Max(0, _colorSource.SelectedIndex);
            _musicRule.MediaSessionId = _mediaSession.SelectedIndex <= 0 ? "" : _mediaSession.SelectedItem?.ToString() ?? "";
            _musicRule.BrightnessLimit = limit;
            _musicRule.TypingPolicy = (EventPolicy)Math.Max(0, _typing.SelectedIndex);
            _musicRule.NotificationPolicy = (EventPolicy)Math.Max(0, _notification.SelectedIndex);
        }
        else
        {
            var action = _lightingRule?.Action ?? _scheduleRule!.Action;
            action.Target = _kind == RuleKind.Lighting && _target.SelectedIndex == 1
                ? SceneTargetKind.Off
                : (SceneTargetKind)Math.Max(0, _target.SelectedIndex);
            action.LightingEffectType = EffectTypes[Math.Max(0, _effectType.SelectedIndex)];
            action.PresetId = (_preset.SelectedItem as PresetOption)?.Id ?? "";
            action.BrightnessLimit = limit;
            if (_lightingRule is not null)
            {
                _lightingRule.Name = _name.Text; _lightingRule.Enabled = _enabled.Checked;
                _lightingRule.ProcessNames = _process.Text.Split([',', '，', ';'], StringSplitOptions.RemoveEmptyEntries).ToList();
                _lightingRule.TypingPolicy = (EventPolicy)Math.Max(0, _typing.SelectedIndex);
                _lightingRule.NotificationPolicy = (EventPolicy)Math.Max(0, _notification.SelectedIndex);
            }
            else { _scheduleRule!.Name = _name.Text; _scheduleRule.Enabled = _enabled.Checked; }
        }
    }

    private void RefreshPresets(string? selectedId = null)
    {
        selectedId ??= (_preset.SelectedItem as PresetOption)?.Id;
        _preset.Items.Clear();
        var target = _kind == RuleKind.Lighting && _target.SelectedIndex == 1
            ? SceneTargetKind.Off
            : (SceneTargetKind)Math.Max(0, _target.SelectedIndex);
        if (_kind == RuleKind.Music || target == SceneTargetKind.MusicPreset)
            foreach (var item in _musicPresets) _preset.Items.Add(new PresetOption(item.Id, item.Name));
        else if (target == SceneTargetKind.LightingPreset)
        {
            var type = EffectTypes[Math.Max(0, _effectType.SelectedIndex)];
            _preset.Items.Add(new PresetOption(EffectPresetSettings.BuiltInId(type), "软件默认配置"));
            foreach (var item in _effects.ForType(type)) _preset.Items.Add(new PresetOption(item.Id, item.Name));
        }
        for (var i = 0; i < _preset.Items.Count; i++) if ((_preset.Items[i] as PresetOption)?.Id == selectedId) _preset.SelectedIndex = i;
        if (_preset.SelectedIndex < 0 && _preset.Items.Count > 0) _preset.SelectedIndex = 0;
    }

    private static readonly EffectType[] EffectTypes = [EffectType.Static, EffectType.Rainbow, EffectType.Breathing, EffectType.Sequence, EffectType.Pulse, EffectType.Heartbeat];
    private static int EffectIndex(EffectType type) => Math.Max(0, Array.IndexOf(EffectTypes, type));
    private static ComboBox DropDown() => new() { DropDownStyle = ComboBoxStyle.DropDownList, Width = 350 };
    private static ComboBox PolicyCombo() { var c = DropDown(); c.Items.AddRange(["继承全局", "强制开启", "强制关闭"]); return c; }
    private static DateTimePicker TimePicker() => new() { Format = DateTimePickerFormat.Custom, CustomFormat = "HH:mm", ShowUpDown = true, Width = 100 };
    private static void Add(TableLayoutPanel panel, string label, Control control)
    {
        panel.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        panel.Controls.Add(new Label { Text = label, AutoSize = true, Margin = new Padding(3, 8, 3, 3) });
        control.Dock = DockStyle.Top;
        panel.Controls.Add(control);
    }
    private static int FindItem(ComboBox combo, string value)
    {
        if (string.IsNullOrWhiteSpace(value)) return 0;
        for (var i = 0; i < combo.Items.Count; i++) if (string.Equals(combo.Items[i]?.ToString(), value, StringComparison.OrdinalIgnoreCase)) return i;
        combo.Items.Add(value); return combo.Items.Count - 1;
    }
    private sealed record PresetOption(string Id, string Name) { public override string ToString() => Name; }
}

internal sealed class SceneAutomationEditor : UserControl
{
    private static readonly EffectType[] LightingTypes =
    [
        EffectType.Static, EffectType.Rainbow, EffectType.Breathing,
        EffectType.Sequence, EffectType.Pulse, EffectType.Heartbeat
    ];
    private static readonly DayOfWeek[] DayValues =
    [
        DayOfWeek.Monday, DayOfWeek.Tuesday, DayOfWeek.Wednesday,
        DayOfWeek.Thursday, DayOfWeek.Friday, DayOfWeek.Saturday, DayOfWeek.Sunday
    ];

    private readonly ListBox _list = new();
    private readonly TextBox _name = new();
    private readonly CheckBox _enabled = new() { Text = "启用此规则", AutoSize = true };
    private readonly CheckBox _timeEnabled = new() { Text = "限制时间段", AutoSize = true };
    private readonly DateTimePicker _start = TimePicker();
    private readonly DateTimePicker _end = TimePicker();
    private readonly CheckBox[] _days =
    [
        new() { Text = "一", AutoSize = true }, new() { Text = "二", AutoSize = true },
        new() { Text = "三", AutoSize = true }, new() { Text = "四", AutoSize = true },
        new() { Text = "五", AutoSize = true }, new() { Text = "六", AutoSize = true },
        new() { Text = "日", AutoSize = true }
    ];
    private readonly CheckBox _applicationsEnabled = new() { Text = "限制前台应用（多个进程任一匹配）", AutoSize = true };
    private readonly TextBox _processNames = new();
    private readonly ComboBox _target = new();
    private readonly ComboBox _effectType = new();
    private readonly ComboBox _preset = new();
    private readonly CheckBox _brightnessEnabled = new() { Text = "限制最大亮度", AutoSize = true };
    private readonly NumericUpDown _brightness = new() { Minimum = 0, Maximum = 100, Width = 90 };
    private readonly Label _validation = new() { AutoSize = true, ForeColor = Color.Firebrick, MaximumSize = new Size(760, 0) };
    private readonly List<SceneRule> _rules = [];
    private EffectPresetSettings _effectPresets = new();
    private List<MusicPreset> _musicPresets = [];
    private bool _loading;

    public SceneAutomationEditor()
    {
        Width = UiMetrics.ContentWidth;
        Height = 610;
        _list.SetBounds(0, 4, 350, 150);
        _list.SelectedIndexChanged += (_, _) => LoadSelected();
        Controls.Add(_list);
        AddButton("添加", 370, 4, 82, AddRule);
        AddButton("复制", 460, 4, 82, CloneRule);
        AddButton("删除", 550, 4, 82, RemoveRule);
        AddButton("上移", 370, 48, 82, () => MoveRule(-1));
        AddButton("下移", 460, 48, 82, () => MoveRule(1));

        AddLabel("名称", 0, 175);
        _name.SetBounds(165, 171, 300, 28);
        Controls.Add(_name);
        _enabled.SetBounds(500, 174, 130, 26);
        Controls.Add(_enabled);

        _timeEnabled.SetBounds(0, 215, 130, 26);
        Controls.Add(_timeEnabled);
        _start.SetBounds(165, 211, 110, 28);
        _end.SetBounds(315, 211, 110, 28);
        Controls.Add(_start);
        Controls.Add(_end);
        Controls.Add(new Label { Text = "至", AutoSize = true, Location = new Point(286, 216) });

        Controls.Add(new Label { Text = "星期（不选表示每天）", AutoSize = true, Location = new Point(0, 255) });
        for (var i = 0; i < _days.Length; i++)
        {
            _days[i].Location = new Point(165 + i * 55, 251);
            Controls.Add(_days[i]);
        }

        _applicationsEnabled.SetBounds(0, 291, 300, 26);
        Controls.Add(_applicationsEnabled);
        _processNames.SetBounds(165, 324, 420, 28);
        Controls.Add(_processNames);
        AddButton("选择运行应用", 600, 321, 140, PickApplication);
        Controls.Add(new Label { Text = "进程名用逗号分隔", AutoSize = true, ForeColor = SystemColors.GrayText, Location = new Point(165, 355) });

        AddLabel("场景动作", 0, 390);
        _target.DropDownStyle = ComboBoxStyle.DropDownList;
        _target.Items.AddRange(["灯效预设", "音乐预设", "关闭灯光"]);
        _target.SetBounds(165, 386, 160, 28);
        Controls.Add(_target);
        _effectType.DropDownStyle = ComboBoxStyle.DropDownList;
        _effectType.Items.AddRange(["固定颜色", "RGB 循环", "单色呼吸", "颜色序列", "脉冲", "心跳"]);
        _effectType.SetBounds(340, 386, 160, 28);
        Controls.Add(_effectType);

        AddLabel("目标预设", 0, 432);
        _preset.DropDownStyle = ComboBoxStyle.DropDownList;
        _preset.SetBounds(165, 428, 335, 28);
        Controls.Add(_preset);
        _brightnessEnabled.SetBounds(0, 474, 150, 26);
        _brightness.SetBounds(165, 470, 90, 28);
        Controls.Add(_brightnessEnabled);
        Controls.Add(_brightness);
        Controls.Add(new Label { Text = "%", AutoSize = true, Location = new Point(260, 475) });
        _validation.Location = new Point(0, 520);
        Controls.Add(_validation);

        _name.TextChanged += (_, _) => SaveSelected();
        _enabled.CheckedChanged += (_, _) => SaveSelected();
        _timeEnabled.CheckedChanged += (_, _) => SaveSelected();
        _start.ValueChanged += (_, _) => SaveSelected();
        _end.ValueChanged += (_, _) => SaveSelected();
        foreach (var day in _days) day.CheckedChanged += (_, _) => SaveSelected();
        _applicationsEnabled.CheckedChanged += (_, _) => SaveSelected();
        _processNames.TextChanged += (_, _) => SaveSelected();
        _target.SelectedIndexChanged += (_, _) => { if (!_loading) { RefreshPresetOptions(); SaveSelected(); } };
        _effectType.SelectedIndexChanged += (_, _) => { if (!_loading) { RefreshPresetOptions(); SaveSelected(); } };
        _preset.SelectedIndexChanged += (_, _) => SaveSelected();
        _brightnessEnabled.CheckedChanged += (_, _) => SaveSelected();
        _brightness.ValueChanged += (_, _) => SaveSelected();
    }

    public List<SceneRule> Rules
    {
        get => _rules.Select(Clone).ToList();
        set
        {
            _rules.Clear();
            _rules.AddRange((value ?? []).Select(Clone));
            RefreshList();
        }
    }

    public void SetPresets(EffectPresetSettings effects, IEnumerable<MusicPreset> music)
    {
        _effectPresets = KeyboardSettings.CloneEffectPresets(effects);
        _musicPresets = MusicSettings.BuiltInPresets.Concat(music).Select(CloneMusic).ToList();
        RefreshPresetOptions();
    }

    private void AddRule()
    {
        _rules.Add(new SceneRule
        {
            Name = "新场景",
            Conditions = new SceneConditions { TimeEnabled = true, Start = "00:00", End = "00:00" }
        }.Normalize());
        RefreshList();
        _list.SelectedIndex = _rules.Count - 1;
    }

    private void CloneRule()
    {
        if (SelectedRule is not { } selected) return;
        var clone = Clone(selected);
        clone.Id = Guid.NewGuid().ToString("N");
        clone.Name += " 副本";
        _rules.Insert(_list.SelectedIndex + 1, clone);
        RefreshList();
        _list.SelectedIndex++;
    }

    private void RemoveRule()
    {
        if (_list.SelectedIndex < 0) return;
        var index = _list.SelectedIndex;
        _rules.RemoveAt(index);
        RefreshList();
        if (_rules.Count > 0) _list.SelectedIndex = Math.Min(index, _rules.Count - 1);
    }

    private void MoveRule(int offset)
    {
        var index = _list.SelectedIndex;
        var target = index + offset;
        if (index < 0 || target < 0 || target >= _rules.Count) return;
        (_rules[index], _rules[target]) = (_rules[target], _rules[index]);
        RefreshList();
        _list.SelectedIndex = target;
    }

    private void PickApplication()
    {
        using var dialog = new RunningAppsForm();
        if (dialog.ShowDialog() != DialogResult.OK || string.IsNullOrWhiteSpace(dialog.SelectedProcessName)) return;
        var names = ParseProcesses(_processNames.Text);
        if (!names.Contains(dialog.SelectedProcessName, StringComparer.OrdinalIgnoreCase)) names.Add(dialog.SelectedProcessName);
        _processNames.Text = string.Join(", ", names);
        _applicationsEnabled.Checked = true;
    }

    private void LoadSelected()
    {
        _loading = true;
        try
        {
            var rule = SelectedRule;
            if (rule is null)
            {
                UpdateAvailability();
                _validation.Text = "尚无规则，请点击“添加”。";
                return;
            }
            _name.Text = rule.Name;
            _enabled.Checked = rule.Enabled;
            _timeEnabled.Checked = rule.Conditions.TimeEnabled;
            _start.Value = DateTime.Today + TimeOnly.Parse(rule.Conditions.Start).ToTimeSpan();
            _end.Value = DateTime.Today + TimeOnly.Parse(rule.Conditions.End).ToTimeSpan();
            for (var i = 0; i < _days.Length; i++) _days[i].Checked = rule.Conditions.Days.Contains(DayValues[i]);
            _applicationsEnabled.Checked = rule.Conditions.ApplicationsEnabled;
            _processNames.Text = string.Join(", ", rule.Conditions.ProcessNames);
            _target.SelectedIndex = (int)rule.Action.Target;
            _effectType.SelectedIndex = Math.Max(0, Array.IndexOf(LightingTypes, rule.Action.LightingEffectType));
            _brightnessEnabled.Checked = rule.Action.BrightnessLimit.HasValue;
            _brightness.Value = rule.Action.BrightnessLimit ?? 100;
            RefreshPresetOptions(rule.Action.PresetId);
            UpdateAvailability();
            UpdateValidation(rule);
        }
        finally { _loading = false; }
    }

    private void SaveSelected()
    {
        if (_loading || SelectedRule is not { } rule) return;
        rule.Name = _name.Text;
        rule.Enabled = _enabled.Checked;
        rule.Conditions.TimeEnabled = _timeEnabled.Checked;
        rule.Conditions.Start = _start.Value.ToString("HH:mm");
        rule.Conditions.End = _end.Value.ToString("HH:mm");
        rule.Conditions.Days = DayValues.Where((_, index) => _days[index].Checked).ToList();
        rule.Conditions.ApplicationsEnabled = _applicationsEnabled.Checked;
        rule.Conditions.ProcessNames = ParseProcesses(_processNames.Text);
        rule.Action.Target = (SceneTargetKind)Math.Max(0, _target.SelectedIndex);
        rule.Action.LightingEffectType = LightingTypes[Math.Max(0, _effectType.SelectedIndex)];
        rule.Action.PresetId = (_preset.SelectedItem as ScenePresetOption)?.Id ?? "";
        rule.Action.BrightnessLimit = _brightnessEnabled.Checked ? (int)_brightness.Value : null;
        rule.Normalize();
        UpdateAvailability();
        UpdateValidation(rule);
        RefreshList(true);
    }

    private void RefreshPresetOptions(string? selectedId = null)
    {
        if (_target.SelectedIndex < 0) return;
        selectedId ??= (_preset.SelectedItem as ScenePresetOption)?.Id;
        _preset.Items.Clear();
        if (_target.SelectedIndex == (int)SceneTargetKind.LightingPreset)
        {
            var type = LightingTypes[Math.Max(0, _effectType.SelectedIndex)];
            _preset.Items.Add(new ScenePresetOption(EffectPresetSettings.BuiltInId(type), "软件默认配置"));
            foreach (var preset in _effectPresets.ForType(type)) _preset.Items.Add(new ScenePresetOption(preset.Id, preset.Name));
        }
        else if (_target.SelectedIndex == (int)SceneTargetKind.MusicPreset)
        {
            foreach (var preset in _musicPresets) _preset.Items.Add(new ScenePresetOption(preset.Id, preset.Name));
        }
        for (var i = 0; i < _preset.Items.Count; i++)
        {
            if ((_preset.Items[i] as ScenePresetOption)?.Id == selectedId) { _preset.SelectedIndex = i; break; }
        }
        if (_preset.SelectedIndex < 0 && !string.IsNullOrWhiteSpace(selectedId))
        {
            _preset.Items.Add(new ScenePresetOption(selectedId, "[缺失预设]"));
            _preset.SelectedIndex = _preset.Items.Count - 1;
        }
        if (_preset.SelectedIndex < 0 && _preset.Items.Count > 0) _preset.SelectedIndex = 0;
        UpdateAvailability();
    }

    private void UpdateAvailability()
    {
        var hasRule = SelectedRule is not null;
        var lighting = _target.SelectedIndex == (int)SceneTargetKind.LightingPreset;
        var off = _target.SelectedIndex == (int)SceneTargetKind.Off;
        _name.Enabled = hasRule;
        _enabled.Enabled = hasRule;
        _timeEnabled.Enabled = hasRule;
        foreach (var day in _days) day.Enabled = hasRule;
        _applicationsEnabled.Enabled = hasRule;
        _target.Enabled = hasRule;
        _effectType.Enabled = hasRule;
        _brightnessEnabled.Enabled = hasRule;
        _effectType.Visible = lighting;
        _preset.Enabled = hasRule && !off;
        _brightness.Enabled = hasRule && _brightnessEnabled.Checked;
        _start.Enabled = hasRule && _timeEnabled.Checked;
        _end.Enabled = hasRule && _timeEnabled.Checked;
        _processNames.Enabled = hasRule && _applicationsEnabled.Checked;
        _validation.Enabled = true;
    }

    private void UpdateValidation(SceneRule rule)
    {
        string? error = !rule.Conditions.IsValid ? "请至少启用一个有效条件；应用条件必须包含进程名。" : null;
        if (error is null && rule.Action.Target != SceneTargetKind.Off &&
            !PresetExists(rule.Action)) error = "引用的预设不存在；此规则不会执行。";
        _validation.Text = error ?? "规则有效。条件组之间按“并且”匹配，多个进程按“任一”匹配。";
        _validation.ForeColor = error is null ? Color.DarkGreen : Color.Firebrick;
    }

    private bool PresetExists(SceneAction action) => action.Target switch
    {
        SceneTargetKind.Off => true,
        SceneTargetKind.LightingPreset =>
            action.PresetId == EffectPresetSettings.BuiltInId(action.LightingEffectType) ||
            _effectPresets.ForType(action.LightingEffectType).Any(item => item.Id == action.PresetId),
        SceneTargetKind.MusicPreset => _musicPresets.Any(item => item.Id == action.PresetId),
        _ => false
    };

    private void RefreshList(bool preserveSelection = false)
    {
        var selected = preserveSelection ? _list.SelectedIndex : -1;
        _list.Items.Clear();
        foreach (var rule in _rules)
            _list.Items.Add($"{(rule.Enabled ? "" : "[停用] ")}{rule.Name}");
        if (_rules.Count == 0) { _list.SelectedIndex = -1; LoadSelected(); }
        else _list.SelectedIndex = Math.Clamp(selected, 0, _rules.Count - 1);
    }

    private static List<string> ParseProcesses(string text) => text
        .Split([',', ';', '，', '；'], StringSplitOptions.RemoveEmptyEntries)
        .Select(AppProfileRule.NormalizeProcessName)
        .Where(name => !string.IsNullOrWhiteSpace(name))
        .Distinct(StringComparer.OrdinalIgnoreCase)
        .ToList();

    private static SceneRule Clone(SceneRule rule) => new SceneRule
    {
        Id = rule.Id,
        Name = rule.Name,
        Enabled = rule.Enabled,
        Conditions = new SceneConditions
        {
            TimeEnabled = rule.Conditions.TimeEnabled,
            Start = rule.Conditions.Start,
            End = rule.Conditions.End,
            Days = [.. rule.Conditions.Days],
            ApplicationsEnabled = rule.Conditions.ApplicationsEnabled,
            ProcessNames = [.. rule.Conditions.ProcessNames]
        },
        Action = new SceneAction
        {
            Target = rule.Action.Target,
            LightingEffectType = rule.Action.LightingEffectType,
            PresetId = rule.Action.PresetId,
            BrightnessLimit = rule.Action.BrightnessLimit
        }
    }.Normalize();

    private static MusicPreset CloneMusic(MusicPreset preset)
    {
        var clone = new MusicPreset { Id = preset.Id, Name = preset.Name };
        clone.ResponseMode = preset.ResponseMode;
        clone.Colors = [.. preset.Colors];
        clone.LowColor = preset.LowColor;
        clone.HighColor = preset.HighColor;
        clone.Sensitivity = preset.Sensitivity;
        clone.AttackMs = preset.AttackMs;
        clone.ReleaseMs = preset.ReleaseMs;
        clone.BaseBrightness = preset.BaseBrightness;
        clone.PeakBrightness = preset.PeakBrightness;
        clone.IntervalMs = preset.IntervalMs;
        clone.NoiseGate = preset.NoiseGate;
        clone.BeatThreshold = preset.BeatThreshold;
        clone.PeakHoldMs = preset.PeakHoldMs;
        clone.FollowSystemVolume = preset.FollowSystemVolume;
        clone.EqEnabled = preset.EqEnabled;
        clone.EqLowHz = preset.EqLowHz;
        clone.EqHighHz = preset.EqHighHz;
        return clone.Normalize();
    }

    private SceneRule? SelectedRule => _list.SelectedIndex >= 0 && _list.SelectedIndex < _rules.Count
        ? _rules[_list.SelectedIndex] : null;

    private void AddButton(string text, int x, int y, int width, Action action)
    {
        var button = new Button { Text = text, Location = new Point(x, y), Width = width, Height = UiMetrics.ButtonHeight };
        button.Click += (_, _) => action();
        Controls.Add(button);
    }

    private void AddLabel(string text, int x, int y) =>
        Controls.Add(new Label { Text = text, Width = UiMetrics.LabelWidth, Height = 28, Location = new Point(x, y) });

    private static DateTimePicker TimePicker() => new()
    {
        Format = DateTimePickerFormat.Custom,
        CustomFormat = "HH:mm",
        ShowUpDown = true
    };

    private sealed record ScenePresetOption(string Id, string Name)
    {
        public override string ToString() => Name;
    }
}

internal sealed class AppProfileEditor : UserControl
{
    private readonly ListBox _list = new();
    private readonly TextBox _processName = new();
    private readonly ComboBox _effectType = new();
    private readonly ColorPickerRow _color = new("颜色");
    private readonly CheckBox _enabled = new() { Text = "启用此规则" };
    private readonly CheckBox _useAppIconColor = new() { Text = "自动按图标取色" };
    private readonly Button _pickProcess = new() { Text = "选择进程" };
    private readonly List<AppProfileRule> _rules = [];
    private bool _loading;

    public AppProfileEditor()
    {
        Width = ContentWidth;
        Height = 500;

        _list.Location = new Point(0, 4);
        _list.Size = new Size(360, 160);
        _list.SelectedIndexChanged += (_, _) => LoadSelected();
        Controls.Add(_list);

        AddButton("添加运行应用", 385, 4, 140, AddForegroundRule);
        AddButton("删除规则", 385, 48, 140, RemoveSelected);

        Controls.Add(new Label { Text = "进程名", Width = LabelWidth, Height = 30, Location = new Point(0, 185) });
        _processName.Location = new Point(ControlLeft, 181);
        _processName.Width = 300;
        _processName.Height = 30;
        _processName.TextChanged += (_, _) => SaveSelected();
        Controls.Add(_processName);

        _pickProcess.Location = new Point(480, 179);
        _pickProcess.Width = 125;
        _pickProcess.Height = ButtonHeight;
        _pickProcess.Click += (_, _) => PickProcessFromRunningApps();
        Controls.Add(_pickProcess);

        _effectType.DropDownStyle = ComboBoxStyle.DropDownList;
        _effectType.Items.AddRange(["固定颜色", "单色呼吸"]);
        _effectType.Location = new Point(ControlLeft, 222);
        _effectType.Width = 300;
        _effectType.Height = 30;
        _effectType.SelectedIndexChanged += (_, _) => SaveSelected();
        Controls.Add(new Label { Text = "效果", Width = LabelWidth, Height = 30, Location = new Point(0, 226) });
        Controls.Add(_effectType);

        _enabled.Location = new Point(ControlLeft, 263);
        _enabled.AutoSize = true;
        _enabled.CheckedChanged += (_, _) => SaveSelected();
        Controls.Add(_enabled);

        _useAppIconColor.Location = new Point(ControlLeft, 298);
        _useAppIconColor.AutoSize = true;
        _useAppIconColor.CheckedChanged += (_, _) => SaveSelected();
        Controls.Add(_useAppIconColor);

        var iconHint = new Label
        {
            Text = "自动色开启时使用当前应用图标颜色；关闭后可手动选色。",
            Location = new Point(350, 298),
            Size = new Size(430, 44)
        };
        Controls.Add(iconHint);

        _color.Location = new Point(0, 350);
        _color.ColorChanged += (_, _) => SaveSelected();
        Controls.Add(_color);
    }

    public List<AppProfileRule> Rules
    {
        get => _rules.Select(rule => new AppProfileRule
            {
                Name = rule.Name,
                ProcessName = rule.ProcessName,
                Enabled = rule.Enabled,
                AutoColorEnabled = rule.AutoColorEnabled,
                IconColor = rule.IconColor,
                Brightness = rule.Brightness,
                TargetEffect = rule.TargetEffect,
                ManualColor = rule.ManualColor
            }.Normalize()).ToList();
        set
        {
            _rules.Clear();
            _rules.AddRange((value ?? []).Select(rule => new AppProfileRule
            {
                Name = rule.Name,
                ProcessName = rule.ProcessName,
                Enabled = rule.Enabled,
                AutoColorEnabled = rule.AutoColorEnabled,
                IconColor = rule.IconColor,
                Brightness = rule.Brightness,
                TargetEffect = rule.TargetEffect,
                ManualColor = rule.ManualColor
            }.Normalize()));
            RefreshList();
        }
    }

    private void AddButton(string text, int x, int y, int width, Action action)
    {
        var button = new Button { Text = text, Location = new Point(x, y), Width = width, Height = ButtonHeight };
        button.Click += (_, _) => action();
        Controls.Add(button);
    }

    private void AddForegroundRule()
    {
        using var dialog = new RunningAppsForm();
        if (dialog.ShowDialog() != DialogResult.OK || string.IsNullOrWhiteSpace(dialog.SelectedProcessName))
        {
            return;
        }

        var processName = dialog.SelectedProcessName;
        var iconColor = dialog.SelectedIconColor ?? "#FFFFFF";
        _rules.Add(new AppProfileRule
        {
            Name = processName,
            ProcessName = processName,
            AutoColorEnabled = true,
            IconColor = iconColor,
            Brightness = 70,
            TargetEffect = EffectType.Static,
            ManualColor = iconColor
        }.Normalize());
        RefreshList();
        _list.SelectedIndex = _rules.Count - 1;
    }

    private void RemoveSelected()
    {
        if (_list.SelectedIndex < 0)
        {
            return;
        }

        _rules.RemoveAt(_list.SelectedIndex);
        RefreshList();
    }

    private void LoadSelected()
    {
        _loading = true;
        try
        {
            var rule = SelectedRule;
            var enabled = rule is not null;
            _processName.Enabled = enabled;
            _pickProcess.Enabled = enabled;
            _effectType.Enabled = enabled;
            _color.Enabled = enabled;
            _enabled.Enabled = enabled;
            _useAppIconColor.Enabled = enabled;

            if (rule is null)
            {
                _processName.Text = "";
                _effectType.SelectedIndex = 0;
                _enabled.Checked = false;
                _useAppIconColor.Checked = false;
                _color.ColorHex = "#FFFFFF";
                UpdateColorVisibility();
                return;
            }

            _processName.Text = rule.ProcessName;
            _effectType.SelectedIndex = EffectToIndex(rule.TargetEffect);
            _enabled.Checked = rule.Enabled;
            _useAppIconColor.Checked = rule.AutoColorEnabled;
            _color.ColorHex = rule.AutoColorEnabled ? rule.IconColor : rule.ManualColor;
            UpdateColorVisibility();
        }
        finally
        {
            _loading = false;
        }
    }

    private void SaveSelected()
    {
        if (_loading || SelectedRule is not { } rule)
        {
            return;
        }

        rule.ProcessName = AppProfileRule.NormalizeProcessName(_processName.Text);
        rule.Name = string.IsNullOrWhiteSpace(rule.ProcessName) ? "新场景" : rule.ProcessName;
        rule.Enabled = _enabled.Checked;
        rule.AutoColorEnabled = _useAppIconColor.Checked;
        rule.TargetEffect = IndexToEffect(_effectType.SelectedIndex);
        rule.ManualColor = _color.ColorHex;
        rule.Normalize();
        UpdateColorVisibility();
        RefreshList(preserveSelection: true);
    }

    private void UpdateColorVisibility()
    {
        _color.Visible = !_useAppIconColor.Checked;
    }

    private void PickProcessFromRunningApps()
    {
        using var dialog = new RunningAppsForm();
        if (dialog.ShowDialog() != DialogResult.OK || string.IsNullOrWhiteSpace(dialog.SelectedProcessName))
        {
            return;
        }

        _processName.Text = dialog.SelectedProcessName;
        if (dialog.SelectedIconColor is not null && SelectedRule is { } rule)
        {
            rule.IconColor = dialog.SelectedIconColor;
        }
        _useAppIconColor.Checked = true;
        _effectType.SelectedIndex = 0;
        SaveSelected();
    }

    private void RefreshList(bool preserveSelection = false)
    {
        var selected = preserveSelection ? _list.SelectedIndex : -1;
        _list.Items.Clear();
        foreach (var rule in _rules)
        {
            var mode = rule.AutoColorEnabled ? "图标" : "手动";
            _list.Items.Add($"{(rule.Enabled ? "" : "[停用] ")}{rule.ProcessName} ({mode})");
        }

        if (_rules.Count == 0)
        {
            _list.SelectedIndex = -1;
            LoadSelected();
            return;
        }

        _list.SelectedIndex = Math.Clamp(selected, 0, _rules.Count - 1);
    }

    private AppProfileRule? SelectedRule =>
        _list.SelectedIndex >= 0 && _list.SelectedIndex < _rules.Count ? _rules[_list.SelectedIndex] : null;

    private static int EffectToIndex(EffectType effect) => effect switch
    {
        EffectType.Breathing => 1,
        _ => 0
    };

    private static EffectType IndexToEffect(int index) => index switch
    {
        1 => EffectType.Breathing,
        _ => EffectType.Static
    };

}
internal sealed class RunningAppsForm : ThemedForm
{
    private readonly ListView _list = new();

    public string? SelectedProcessName { get; private set; }

    public string? SelectedIconColor { get; private set; }

    public RunningAppsForm()
    {
        Text = "选择应用进程";
        StartPosition = FormStartPosition.CenterParent;
        ClientSize = new Size(640, 460);
        FormBorderStyle = FormBorderStyle.FixedDialog;
        MaximizeBox = false;
        MinimizeBox = false;

        _list.Dock = DockStyle.Top;
        _list.Height = 350;
        _list.View = View.Details;
        _list.FullRowSelect = true;
        _list.Columns.Add("进程名", 180);
        _list.Columns.Add("窗口标题", 300);
        _list.Columns.Add("图标色", 110);
        _list.DoubleClick += (_, _) => ConfirmSelection();
        Controls.Add(_list);

        var refresh = new Button { Text = "刷新", Location = new Point(12, 386), Width = 90 };
        refresh.Click += (_, _) => LoadApps();
        Controls.Add(refresh);

        var ok = new Button { Text = "确定", Location = new Point(442, 386), Width = 85 };
        ok.Click += (_, _) => ConfirmSelection();
        Controls.Add(ok);

        var cancel = new Button { Text = "取消", Location = new Point(537, 386), Width = 85, DialogResult = DialogResult.Cancel };
        Controls.Add(cancel);

        AcceptButton = ok;
        CancelButton = cancel;
        Load += (_, _) => LoadApps();
    }

    private void LoadApps()
    {
        _list.Items.Clear();
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var process in Process.GetProcesses().OrderByDescending(process => process.MainWindowHandle != IntPtr.Zero))
        {
            try
            {
                if (process.MainWindowHandle == IntPtr.Zero || string.IsNullOrWhiteSpace(process.MainWindowTitle))
                {
                    continue;
                }

                if (!seen.Add(process.ProcessName))
                {
                    continue;
                }

                var iconColor = ProcessIconColor.TryGetColor(process.ProcessName, out var color) ? color : "#FFFFFF";
                var item = new ListViewItem(process.ProcessName);
                item.SubItems.Add(process.MainWindowTitle);
                item.SubItems.Add(iconColor);
                item.Tag = (process.ProcessName, iconColor);
                _list.Items.Add(item);
            }
            catch
            {
            }
        }
    }

    private void ConfirmSelection()
    {
        if (_list.SelectedItems.Count == 0)
        {
            return;
        }

        if (_list.SelectedItems[0].Tag is ValueTuple<string, string> data)
        {
            SelectedProcessName = data.Item1;
            SelectedIconColor = data.Item2;
        }

        DialogResult = DialogResult.OK;
        Close();
    }
}
