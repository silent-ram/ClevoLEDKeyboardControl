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
}

public sealed class SettingsForm : Form
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
    private readonly SliderRow _musicBeatThreshold = new("节拍阈值", 2, 80, "%");
    private readonly CheckBox _musicEqEnabled = new() { Text = "自适应鼓点检测" };
    private readonly SliderRow _musicEqLow = new("低频参考", 20, 1000, " Hz");
    private readonly SliderRow _musicEqHigh = new("高频参考", 40, 8000, " Hz");
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
    private readonly ComboBox _updateInterval = new();
    private readonly Label _serviceSummary = new();
    private readonly Label _componentSummary = new();
    private readonly Label _controlSummary = new();
    private static readonly double[] MusicSensitivityValues = [0.5, 1.0, 1.5, 2.0];
    private static readonly int[] MusicAttackValues = [10, 15, 20, 25, 30, 40];
    private static readonly int[] MusicReleaseValues = [70, 80, 90, 100, 110, 120, 130, 140, 150, 160, 170, 180, 190, 200];
    private EffectPresetSettings _effectPresets = new();
    private List<MusicPreset> _musicCustomPresets = [];
    private bool _loadingSettings;
    private bool _effectChangedByUser;
    private bool _loadingEffectPreset;
    private bool _loadingMusicPreset;
    private bool _customSequenceColorsEnabled;

    public SettingsForm(SettingsStore settingsStore)
    {
        _settingsStore = settingsStore;
        Text = "ClevoLEDKeyboardControl 设置";
        StartPosition = FormStartPosition.CenterScreen;
        MinimumSize = new Size(1040, 720);
        ClientSize = new Size(1080, 780);

        BuildUi();
        LoadSettings();
        _effectType.SelectedIndexChanged += (_, _) =>
        {
            if (!_loadingSettings)
            {
                _effectChangedByUser = true;
            }

            UpdateBrightnessAvailability();
            UpdateCustomColorsButton();
            UpdateEffectConfigurationVisibility();
            RefreshEffectPresetList();
        };
        UpdateAudioSourceLabel(AudioSourceStatusFile.Read());
    }

    public event EventHandler? SettingsSaved;

    public void ReloadFromStore()
    {
        LoadSettings();
    }

    private void BuildUi()
    {
        var split = new SplitContainer
        {
            Dock = DockStyle.Fill,
            FixedPanel = FixedPanel.Panel1,
            SplitterDistance = 150,
            IsSplitterFixed = true
        };

        var navigation = new ListBox
        {
            Dock = DockStyle.Fill,
            IntegralHeight = false,
            BorderStyle = BorderStyle.None,
            Font = new Font(SystemFonts.MessageBoxFont ?? Control.DefaultFont, FontStyle.Regular)
        };
        _navigation = navigation;

        var pages = new Panel { Dock = DockStyle.Fill };
        var content = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            ColumnCount = 1,
            RowCount = 2
        };
        content.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        content.RowStyles.Add(new RowStyle(SizeType.Absolute, 92));
        content.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        var statusHeader = BuildStatusHeader();
        var pageDefinitions = new (string Title, Panel Page)[]
        {
            ("常规", BuildGeneralPage()),
            ("音乐", BuildMusicPage()),
            ("自动化", BuildAutomationPage()),
            ("应用场景", BuildAppProfilesPage()),
            ("诊断", BuildDiagnosticsPage()),
            ("高级", BuildAdvancedPage())
        };

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
        };
        navigation.SelectedIndex = 0;

        split.Panel1.Padding = new Padding(10, 12, 8, 12);
        split.Panel1.Controls.Add(navigation);
        content.Controls.Add(statusHeader, 0, 0);
        content.Controls.Add(pages, 0, 1);
        split.Panel2.Controls.Add(content);

        var buttons = new FlowLayoutPanel
        {
            FlowDirection = FlowDirection.RightToLeft,
            Dock = DockStyle.Bottom,
            Height = 52,
            Padding = new Padding(0, 8, 14, 10)
        };

        var save = new Button { Text = "应用", Width = 96, Height = ButtonHeight };
        save.Click += (_, _) => SaveSettings();

        var cancel = new Button { Text = "取消", Width = 96, Height = ButtonHeight };
        cancel.Click += (_, _) => Close();

        buttons.Controls.Add(save);
        buttons.Controls.Add(cancel);

        Controls.Add(split);
        Controls.Add(buttons);
        UpdateStatusHeader();
    }

    private Panel BuildStatusHeader()
    {
        var panel = new Panel
        {
            Dock = DockStyle.Fill,
            Padding = new Padding(18, 12, 18, 10),
            BackColor = SystemColors.ControlLightLight
        };

        var title = new Label
        {
            Text = "当前状态",
            Font = new Font(SystemFonts.MessageBoxFont ?? Control.DefaultFont, FontStyle.Bold),
            Location = new Point(18, 10),
            Size = new Size(150, 24)
        };

        ConfigureStatusLabel(_serviceSummary, 18, 42);
        ConfigureStatusLabel(_componentSummary, 260, 42);
        ConfigureStatusLabel(_controlSummary, 560, 42);

        panel.Controls.Add(title);
        panel.Controls.Add(_serviceSummary);
        panel.Controls.Add(_componentSummary);
        panel.Controls.Add(_controlSummary);
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
        page.Controls.Add(modeRow);

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
        page.Controls.Add(_effectTypeRow);
        page.Controls.Add(_brightness);
        page.Controls.Add(_effectColor);
        page.Controls.Add(_speedRow);
        page.Controls.Add(_period);
        page.Controls.Add(_minimumBrightness);
        page.Controls.Add(_hardBlinkRow);
        page.Controls.Add(_customColorsRow);
        page.Controls.Add(_sequenceSection);
        page.Controls.Add(_sequenceSummary);
        page.Controls.Add(_sequence);
        page.Controls.Add(_effectPresetSection);
        page.Controls.Add(_effectPresetRow);
        page.Controls.Add(_effectPresetNameRow);
        page.Controls.Add(_effectPresetButtonsRow);
        page.Controls.Add(_modeHint);
        return page;
    }

    private Panel BuildMusicPage()
    {
        var page = CreatePage();
        _musicPreset.DropDownStyle = ComboBoxStyle.DropDownList;
        _musicResponseMode.DropDownStyle = ComboBoxStyle.DropDownList;
        _musicResponseMode.Items.AddRange(["按电平变色", "仅亮度脉冲"]);
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

        page.Controls.Add(_audioSourceLabel);
        page.Controls.Add(Row("音乐预设", _musicPreset));
        page.Controls.Add(ButtonRow(_musicSavePreset, _musicCreatePreset, _musicDeletePreset));
        page.Controls.Add(Row("当前预设", _musicPresetName));
        page.Controls.Add(Row("音乐响应", _musicResponseMode));
        page.Controls.Add(PlainRow(_musicCustomColors));
        page.Controls.Add(Section("节拍颜色"));
        page.Controls.Add(_musicSequence);
        page.Controls.Add(_musicBaseBrightness);
        page.Controls.Add(_musicPeakBrightness);
        page.Controls.Add(PlainRow(_musicFollowSystemVolume));
        page.Controls.Add(PlainRow(_musicAdvanced));
        _musicSensitivityRow = Row("灵敏度", _musicSensitivity);
        _musicAttackRow = Row("响应速度", _musicAttack);
        _musicReleaseRow = Row("衰减速度", _musicRelease);
        page.Controls.Add(_musicSensitivityRow);
        page.Controls.Add(_musicAttackRow);
        page.Controls.Add(_musicReleaseRow);
        page.Controls.Add(_musicNoiseGate);
        page.Controls.Add(_musicBeatThreshold);
        page.Controls.Add(PlainRow(_musicEqEnabled));
        page.Controls.Add(_musicEqLow);
        page.Controls.Add(_musicEqHigh);
        UpdateMusicAdvancedVisibility();
        return page;
    }

    private Panel BuildAutomationPage()
    {
        var page = CreatePage();
        _idleAfter.DropDownStyle = ComboBoxStyle.DropDownList;
        _idleAfter.Items.AddRange(["1 分钟", "3 分钟", "5 分钟", "10 分钟", "30 分钟"]);

        page.Controls.Add(Section("空闲降亮"));
        page.Controls.Add(PlainRow(_idleEnabled));
        page.Controls.Add(Row("空闲时间", _idleAfter));
        page.Controls.Add(_idleBrightness);
        page.Controls.Add(PlainRow(_idleTurnOff));
        page.Controls.Add(Section("时间计划"));
        page.Controls.Add(PlainRow(_scheduleEnabled));
        page.Controls.Add(_evening);
        page.Controls.Add(_night);
        page.Controls.Add(Section("敲字闪烁"));
        page.Controls.Add(PlainRow(_typingPulseEnabled));
        page.Controls.Add(_typingPulsePeakBrightness);
        page.Controls.Add(_typingPulseHold);
        page.Controls.Add(_typingPulseFade);
        page.Controls.Add(Section("通知闪烁"));
        page.Controls.Add(PlainRow(_notificationFlashEnabled));
        page.Controls.Add(_notificationFlashColor);
        page.Controls.Add(_notificationFlashPulses);
        page.Controls.Add(_notificationFlashCooldown);
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
        var configDirectory = DiagnosticTextBox();
        var refresh = new Button { Text = "刷新诊断信息", Width = 130 };

        void Refresh()
        {
            var diagnostics = CollectDiagnostics();
            serviceStatus.Text = diagnostics.ServiceStatus;
            driverStatus.Text = diagnostics.DriverStatus;
            foregroundApp.Text = diagnostics.ForegroundApp;
            matchedProfile.Text = diagnostics.MatchedProfile;
            updateStatus.Text = diagnostics.UpdateStatus;
            configDirectory.Text = diagnostics.ConfigDirectory;
            UpdateStatusHeader();
        }

        refresh.Click += (_, _) => Refresh();

        page.Controls.Add(Row("服务状态", serviceStatus));
        page.Controls.Add(Row("驱动 DLL", driverStatus));
        page.Controls.Add(Row("当前前台应用", foregroundApp));
        page.Controls.Add(Row("命中应用场景", matchedProfile));
        page.Controls.Add(Row("更新检查", updateStatus));
        page.Controls.Add(Row("配置目录", configDirectory));
        page.Controls.Add(PlainRow(refresh));
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

        var openFolder = new Button { Text = "打开配置目录", Width = 120 };
        openFolder.Click += (_, _) =>
        {
            Directory.CreateDirectory(AppPaths.ProgramDataDirectory);
            Process.Start(new ProcessStartInfo("explorer.exe", AppPaths.ProgramDataDirectory) { UseShellExecute = true });
        };

        var reset = new Button { Text = "恢复默认设置", Width = 120 };
        reset.Click += (_, _) =>
        {
            if (MessageBox.Show("确定恢复默认设置？", "ClevoLEDKeyboardControl", MessageBoxButtons.YesNo, MessageBoxIcon.Question) != DialogResult.Yes)
            {
                return;
            }

            _settingsStore.Save(new KeyboardSettings());
            LoadSettings();
        };

        page.Controls.Add(Section("更新"));
        page.Controls.Add(Row("自动检查更新", _updateInterval));
        page.Controls.Add(Section("配置"));
        page.Controls.Add(Row("配置文件", configPath));
        page.Controls.Add(PlainRow(openFolder));
        page.Controls.Add(PlainRow(reset));
        return page;
    }

    private void LoadSettings()
    {
        _loadingSettings = true;
        var settings = _settingsStore.Load();
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
            _musicEqLow.Value = settings.Effect.Music.EqLowHz;
            _musicEqHigh.Value = settings.Effect.Music.EqHighHz;
            _musicBaseBrightness.Value = settings.Effect.Music.BaseBrightness;
            _musicPeakBrightness.Value = settings.Effect.Music.PeakBrightness;
            _musicFollowSystemVolume.Checked = settings.Effect.Music.FollowSystemVolume;
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
            settings.Effect.Music.EqLowHz = _musicEqLow.Value;
            settings.Effect.Music.EqHighHz = _musicEqHigh.Value;
            settings.Effect.Music.Spotify.AlbumColorEnabled = false;
            settings.Effect.Music.BaseBrightness = _musicBaseBrightness.Value;
            settings.Effect.Music.PeakBrightness = _musicPeakBrightness.Value;
            settings.Effect.Music.FollowSystemVolume = _musicFollowSystemVolume.Checked;
            settings.Effect.Music.CustomPresets = _musicCustomPresets.Select(CloneMusicPreset).ToList();
            settings.EffectPresets = KeyboardSettings.CloneEffectPresets(_effectPresets);
            settings.IdleDim.Enabled = _idleEnabled.Checked;
            settings.IdleDim.AfterSeconds = IdleIndexToSeconds(_idleAfter.SelectedIndex);
            settings.IdleDim.Brightness = _idleBrightness.Value;
            settings.IdleDim.TurnOff = _idleTurnOff.Checked;
            settings.Schedule.Enabled = _scheduleEnabled.Checked;
            settings.Schedule.Rules = BuildScheduleRules();
            settings.TypingPulse.Enabled = _typingPulseEnabled.Checked;
            settings.TypingPulse.PeakBrightness = _typingPulsePeakBrightness.Value;
            settings.TypingPulse.HoldMs = _typingPulseHold.Value;
            settings.TypingPulse.FadeMs = _typingPulseFade.Value;
            settings.NotificationFlash.Enabled = _notificationFlashEnabled.Checked;
            settings.NotificationFlash.Color = _notificationFlashColor.ColorHex;
            settings.NotificationFlash.Pulses = _notificationFlashPulses.Value;
            settings.NotificationFlash.CooldownSeconds = _notificationFlashCooldown.Value;
            settings.AppProfiles.Enabled = _appProfilesEnabled.Checked;
            settings.AppProfiles.Rules = _appProfiles.Rules;
            settings.Update.CheckInterval = IndexToUpdateInterval(_updateInterval.SelectedIndex);
            settings.Brightness = _brightness.Enabled ? _brightness.Value : settings.Brightness;
            _settingsStore.Save(settings);
            _effectChangedByUser = false;
            UpdateStatusHeader();
            Text = "ClevoLEDKeyboardControl 设置 - 已应用";
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
        _brightness.BackColor = _brightness.Enabled ? SystemColors.Window : SystemColors.Control;
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

        var preset = BuildMusicPresetFromControls(name);
        var existing = _musicCustomPresets.FindIndex(item => string.Equals(item.Name, name, StringComparison.OrdinalIgnoreCase));
        var originalIndex = string.IsNullOrWhiteSpace(originalName)
            ? -1
            : _musicCustomPresets.FindIndex(item => string.Equals(item.Name, originalName, StringComparison.OrdinalIgnoreCase));

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

    private DiagnosticsSnapshot CollectDiagnostics()
    {
        var settings = _settingsStore.Load();
        var foreground = ForegroundAppState.Load();
        var foregroundText = foreground is null
            ? "无状态"
            : $"{foreground.ProcessName}，{FormatAge(DateTimeOffset.UtcNow - foreground.UpdatedUtc)}前更新";

        var matched = "未命中";
        if (foreground is not null && DateTimeOffset.UtcNow - foreground.UpdatedUtc <= TimeSpan.FromSeconds(10))
        {
            var rule = settings.AppProfiles.Rules.FirstOrDefault(item => item.Matches(foreground.ProcessName));
            if (rule is not null)
            {
                matched = $"{rule.ProcessName}，{(rule.AutoColorEnabled ? "图标色" : "手动色")}，{EffectTypeLabel(rule.TargetEffect)}";
            }
        }

        return new DiagnosticsSnapshot(
            ServiceStatus: GetServiceStatusText(),
            DriverStatus: GetDriverStatusText(),
            ForegroundApp: foregroundText,
            MatchedProfile: settings.AppProfiles.Enabled ? matched : "应用场景未启用",
            UpdateStatus: GetUpdateStatusText(settings.Update.CheckInterval),
            ConfigDirectory: AppPaths.ProgramDataDirectory);
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
            var button = new Button { BackColor = ColorTranslator.FromHtml(color), Location = new Point(x, 9), Size = new Size(24, 24), FlatStyle = FlatStyle.Flat };
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
            _hex.BackColor = SystemColors.Window;
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
internal sealed class RunningAppsForm : Form
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
