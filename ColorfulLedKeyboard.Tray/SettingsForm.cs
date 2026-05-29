using ColorfulLedKeyboard.Core;
using System.Diagnostics;
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
    private readonly SettingsStore _settingsStore;
    private readonly ComboBox _effectType = new();
    private readonly SliderRow _brightness = new("全局亮度", 0, 100, "%");
    private readonly ComboBox _speed = new();
    private readonly ColorPickerRow _effectColor = new("效果颜色");
    private readonly SliderRow _period = new("呼吸周期", 300, 30000, " ms");
    private readonly SliderRow _minimumBrightness = new("最低亮度", 0, 100, "%");
    private readonly CheckBox _hardBlink = new() { Text = "硬闪烁" };
    private readonly SequenceEditor _sequence = new();
    private readonly ColorPickerRow _musicLowColor = new("低电平颜色");
    private readonly ColorPickerRow _musicHighColor = new("高电平颜色");
    private readonly ComboBox _musicPreset = new();
    private readonly TextBox _musicPresetName = new();
    private readonly Button _musicApplyPreset = new() { Text = "应用预设" };
    private readonly Button _musicSavePreset = new() { Text = "保存自定义" };
    private readonly Button _musicDeletePreset = new() { Text = "删除自定义" };
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
    private readonly SliderRow _musicBaseBrightness = new("基础亮度", 0, 100, "%");
    private readonly SliderRow _musicPeakBrightness = new("峰值亮度", 0, 100, "%");
    private readonly CheckBox _idleEnabled = new() { Text = "启用空闲降亮" };
    private readonly ComboBox _idleAfter = new();
    private readonly SliderRow _idleBrightness = new("空闲亮度", 0, 100, "%");
    private readonly CheckBox _idleTurnOff = new() { Text = "空闲后关闭灯效" };
    private readonly CheckBox _scheduleEnabled = new() { Text = "启用时间计划" };
    private readonly TimeRangePicker _evening = new("傍晚时段");
    private readonly TimeRangePicker _night = new("深夜时段");
    private readonly CheckBox _typingPulseEnabled = new() { Text = "启用敲字闪烁" };
    private readonly SliderRow _typingPulseBaseBrightness = new("基础亮度", 0, 100, "%");
    private readonly SliderRow _typingPulsePeakBrightness = new("触发亮度", 0, 100, "%");
    private readonly SliderRow _typingPulseHold = new("保持时间", 20, 2000, " ms");
    private readonly SliderRow _typingPulseFade = new("回落时间", 50, 5000, " ms");
    private readonly CheckBox _appProfilesEnabled = new() { Text = "启用应用场景配置" };
    private readonly AppProfileEditor _appProfiles = new();
    private readonly ComboBox _updateInterval = new();
    private static readonly double[] MusicSensitivityValues = [0.5, 1.0, 1.5, 2.0];
    private static readonly int[] MusicAttackValues = [10, 35, 100, 300, 1000];
    private static readonly int[] MusicReleaseValues = [80, 180, 500, 1000, 3000];
    private List<MusicPreset> _musicCustomPresets = [];
    private bool _loadingMusicPreset;

    public SettingsForm(SettingsStore settingsStore)
    {
        _settingsStore = settingsStore;
        Text = "ClevoRGBControl 设置";
        StartPosition = FormStartPosition.CenterScreen;
        MinimumSize = new Size(1040, 720);
        ClientSize = new Size(1080, 780);

        BuildUi();
        LoadSettings();
        _effectType.SelectedIndexChanged += (_, _) => UpdateBrightnessAvailability();
        _typingPulseEnabled.CheckedChanged += (_, _) => UpdateBrightnessAvailability();
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

        var pages = new Panel { Dock = DockStyle.Fill };
        var pageDefinitions = new (string Title, Panel Page)[]
        {
            ("常规", BuildGeneralPage()),
            ("效果", BuildEffectPage()),
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
        split.Panel2.Controls.Add(pages);

        var buttons = new FlowLayoutPanel
        {
            FlowDirection = FlowDirection.RightToLeft,
            Dock = DockStyle.Bottom,
            Height = 52,
            Padding = new Padding(0, 8, 14, 10)
        };

        var save = new Button { Text = "保存", Width = 96, Height = ButtonHeight };
        save.Click += (_, _) => SaveSettings();

        var cancel = new Button { Text = "取消", Width = 96, Height = ButtonHeight };
        cancel.Click += (_, _) => Close();

        buttons.Controls.Add(save);
        buttons.Controls.Add(cancel);

        Controls.Add(split);
        Controls.Add(buttons);
    }

    private Panel BuildGeneralPage()
    {
        var page = CreatePage();
        _effectType.DropDownStyle = ComboBoxStyle.DropDownList;
        _effectType.Items.AddRange(["固定颜色", "RGB 循环", "单色呼吸", "色彩序列", "音乐模式", "关闭"]);

        _speed.DropDownStyle = ComboBoxStyle.DropDownList;
        _speed.Items.AddRange(["非常慢", "慢", "正常", "快", "很快"]);

        page.Controls.Add(Row("当前效果", _effectType));
        page.Controls.Add(_brightness);
        page.Controls.Add(Row("速度", _speed));
        return page;
    }

    private Panel BuildEffectPage()
    {
        var page = CreatePage();
        page.Controls.Add(_effectColor);
        page.Controls.Add(_period);
        page.Controls.Add(_minimumBrightness);
        page.Controls.Add(PlainRow(_hardBlink));
        page.Controls.Add(Section("色彩序列"));
        page.Controls.Add(_sequence);
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
        };
        _musicApplyPreset.Click += (_, _) => ApplySelectedMusicPreset();
        _musicSavePreset.Click += (_, _) => SaveCustomMusicPreset();
        _musicDeletePreset.Click += (_, _) => DeleteSelectedCustomMusicPreset();
        _musicAdvanced.CheckedChanged += (_, _) => UpdateMusicAdvancedVisibility();

        page.Controls.Add(Row("音乐预设", _musicPreset));
        page.Controls.Add(ButtonRow(_musicApplyPreset, _musicSavePreset, _musicDeletePreset));
        page.Controls.Add(Row("自定义名称", _musicPresetName));
        page.Controls.Add(Row("音乐响应", _musicResponseMode));
        page.Controls.Add(_musicLowColor);
        page.Controls.Add(_musicHighColor);
        page.Controls.Add(_musicBaseBrightness);
        page.Controls.Add(_musicPeakBrightness);
        page.Controls.Add(PlainRow(_musicAdvanced));
        _musicSensitivityRow = Row("灵敏度", _musicSensitivity);
        _musicAttackRow = Row("响应速度", _musicAttack);
        _musicReleaseRow = Row("衰减速度", _musicRelease);
        page.Controls.Add(_musicSensitivityRow);
        page.Controls.Add(_musicAttackRow);
        page.Controls.Add(_musicReleaseRow);
        page.Controls.Add(_musicNoiseGate);
        page.Controls.Add(_musicBeatThreshold);
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
        page.Controls.Add(_typingPulseBaseBrightness);
        page.Controls.Add(_typingPulsePeakBrightness);
        page.Controls.Add(_typingPulseHold);
        page.Controls.Add(_typingPulseFade);
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
            if (MessageBox.Show("确定恢复默认设置？", "ClevoRGBControl", MessageBoxButtons.YesNo, MessageBoxIcon.Question) != DialogResult.Yes)
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
        var settings = _settingsStore.Load();
        _effectType.SelectedIndex = settings.Effect.Type switch
        {
            EffectType.Static => 0,
            EffectType.Rainbow => 1,
            EffectType.Breathing => 2,
            EffectType.Sequence => 3,
            EffectType.Music => 4,
            EffectType.Off => 5,
            _ => 1
        };

        _brightness.Value = settings.Brightness;
        _speed.SelectedIndex = SpeedToIndex(settings.Effect.Step, settings.Effect.IntervalMs);
        _effectColor.ColorHex = settings.Effect.Color;
        _period.Value = settings.Effect.PeriodMs;
        _minimumBrightness.Value = settings.Effect.MinimumBrightness;
        _hardBlink.Checked = settings.Effect.HardBlink;
        _sequence.Colors = settings.Effect.Sequence.Select(item => item.Color).ToList();
        _musicCustomPresets = settings.Effect.Music.CustomPresets.Select(CloneMusicPreset).ToList();
        RefreshMusicPresetList(settings.Effect.Music.PresetName);
        _musicPresetName.Text = IsBuiltInMusicPreset(settings.Effect.Music.PresetName) ? "" : settings.Effect.Music.PresetName;
        _musicResponseMode.SelectedIndex = settings.Effect.Music.ResponseMode == MusicResponseMode.BrightnessPulse ? 1 : 0;
        _musicLowColor.ColorHex = settings.Effect.Music.LowColor;
        _musicHighColor.ColorHex = settings.Effect.Music.HighColor;
        _musicSensitivity.SelectedIndex = ClosestIndex(MusicSensitivityValues, settings.Effect.Music.Sensitivity);
        _musicAttack.SelectedIndex = ClosestIndex(MusicAttackValues, settings.Effect.Music.AttackMs);
        _musicRelease.SelectedIndex = ClosestIndex(MusicReleaseValues, settings.Effect.Music.ReleaseMs);
        _musicNoiseGate.Value = (int)Math.Round(settings.Effect.Music.NoiseGate * 100);
        _musicBeatThreshold.Value = (int)Math.Round(settings.Effect.Music.BeatThreshold * 100);
        _musicBaseBrightness.Value = settings.Effect.Music.BaseBrightness;
        _musicPeakBrightness.Value = settings.Effect.Music.PeakBrightness;
        _idleEnabled.Checked = settings.IdleDim.Enabled;
        _idleAfter.SelectedIndex = SecondsToIdleIndex(settings.IdleDim.AfterSeconds);
        _idleBrightness.Value = settings.IdleDim.Brightness;
        _idleTurnOff.Checked = settings.IdleDim.TurnOff;
        _scheduleEnabled.Checked = settings.Schedule.Enabled;
        _typingPulseEnabled.Checked = settings.TypingPulse.Enabled;
        _typingPulseBaseBrightness.Value = settings.TypingPulse.BaseBrightness;
        _typingPulsePeakBrightness.Value = settings.TypingPulse.PeakBrightness;
        _typingPulseHold.Value = settings.TypingPulse.HoldMs;
        _typingPulseFade.Value = settings.TypingPulse.FadeMs;
        _appProfilesEnabled.Checked = settings.AppProfiles.Enabled;
        _appProfiles.Rules = settings.AppProfiles.Rules;
        _updateInterval.SelectedIndex = UpdateIntervalToIndex(settings.Update.CheckInterval);

        var evening = settings.Schedule.Rules.FirstOrDefault(rule => rule.Name == "Evening");
        var night = settings.Schedule.Rules.FirstOrDefault(rule => rule.Name == "Night");
        _evening.SetRange(evening?.Start ?? "19:00", evening?.End ?? "23:30");
        _night.SetRange(night?.Start ?? "23:30", night?.End ?? "07:00");
        UpdateBrightnessAvailability();
    }

    private void SaveSettings()
    {
        try
        {
            var settings = _settingsStore.Load();
            settings.Enabled = true;
            settings.Effect.Type = SelectedEffectType(settings.Effect.Type);
            settings.Effect.Color = _effectColor.ColorHex;
            ApplySpeed(settings);
            settings.Effect.PeriodMs = _period.Value;
            settings.Effect.MinimumBrightness = _minimumBrightness.Value;
            settings.Effect.HardBlink = _hardBlink.Checked;
            settings.Effect.Sequence = _sequence.Colors.Select(color => new SequenceColor
            {
                Color = color,
                HoldMs = 300,
                TransitionMs = 1200,
                Breathing = true
            }).ToList();

            settings.Effect.Music.PresetName = SelectedMusicPresetName();
            settings.Effect.Music.ResponseMode = _musicResponseMode.SelectedIndex == 1
                ? MusicResponseMode.BrightnessPulse
                : MusicResponseMode.LevelColor;
            settings.Effect.Music.LevelColorEnabled = settings.Effect.Music.ResponseMode == MusicResponseMode.LevelColor;
            settings.Effect.Music.LowColor = _musicLowColor.ColorHex;
            settings.Effect.Music.HighColor = _musicHighColor.ColorHex;
            settings.Effect.Music.Sensitivity = MusicSensitivityValues[ClampIndex(_musicSensitivity.SelectedIndex, MusicSensitivityValues.Length)];
            settings.Effect.Music.AttackMs = MusicAttackValues[ClampIndex(_musicAttack.SelectedIndex, MusicAttackValues.Length)];
            settings.Effect.Music.ReleaseMs = MusicReleaseValues[ClampIndex(_musicRelease.SelectedIndex, MusicReleaseValues.Length)];
            settings.Effect.Music.NoiseGate = _musicNoiseGate.Value / 100d;
            settings.Effect.Music.BeatThreshold = _musicBeatThreshold.Value / 100d;
            settings.Effect.Music.BaseBrightness = _musicBaseBrightness.Value;
            settings.Effect.Music.PeakBrightness = _musicPeakBrightness.Value;
            settings.Effect.Music.CustomPresets = _musicCustomPresets.Select(CloneMusicPreset).ToList();
            settings.IdleDim.Enabled = _idleEnabled.Checked;
            settings.IdleDim.AfterSeconds = IdleIndexToSeconds(_idleAfter.SelectedIndex);
            settings.IdleDim.Brightness = _idleBrightness.Value;
            settings.IdleDim.TurnOff = _idleTurnOff.Checked;
            settings.Schedule.Enabled = _scheduleEnabled.Checked;
            settings.Schedule.Rules = BuildScheduleRules();
            settings.TypingPulse.Enabled = _typingPulseEnabled.Checked;
            settings.TypingPulse.BaseBrightness = _typingPulseBaseBrightness.Value;
            settings.TypingPulse.PeakBrightness = _typingPulsePeakBrightness.Value;
            settings.TypingPulse.HoldMs = _typingPulseHold.Value;
            settings.TypingPulse.FadeMs = _typingPulseFade.Value;
            settings.AppProfiles.Enabled = _appProfilesEnabled.Checked;
            settings.AppProfiles.Rules = _appProfiles.Rules;
            settings.Update.CheckInterval = IndexToUpdateInterval(_updateInterval.SelectedIndex);
            settings.Brightness = _brightness.Enabled ? _brightness.Value : settings.Brightness;
            _settingsStore.Save(settings);
            Text = "ClevoRGBControl 设置 - 已保存";
        }
        catch (Exception ex) when (ex is FormatException or IOException or UnauthorizedAccessException)
        {
            MessageBox.Show($"无法保存设置：{ex.Message}", "ClevoRGBControl", MessageBoxButtons.OK, MessageBoxIcon.Warning);
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
        4 => EffectType.Music,
        5 => EffectType.Off,
        _ => fallback
    };

    private void UpdateBrightnessAvailability()
    {
        var effect = SelectedEffectType(EffectType.Rainbow);
        _brightness.Enabled = effect != EffectType.Music && !_typingPulseEnabled.Checked;
        _brightness.BackColor = _brightness.Enabled ? SystemColors.Window : SystemColors.Control;
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
    }

    private void ApplySelectedMusicPreset()
    {
        if (FindSelectedMusicPreset() is not { } preset)
        {
            return;
        }

        ApplyMusicPresetToControls(preset);
    }

    private void SaveCustomMusicPreset()
    {
        var name = _musicPresetName.Text.Trim();
        if (string.IsNullOrWhiteSpace(name))
        {
            MessageBox.Show("请先输入自定义预设名称。", "ClevoRGBControl", MessageBoxButtons.OK, MessageBoxIcon.Information);
            return;
        }

        if (IsBuiltInMusicPreset(name))
        {
            MessageBox.Show("自定义预设不能使用内置预设名称。", "ClevoRGBControl", MessageBoxButtons.OK, MessageBoxIcon.Information);
            return;
        }

        var preset = BuildMusicPresetFromControls(name);
        var existing = _musicCustomPresets.FindIndex(item => string.Equals(item.Name, name, StringComparison.OrdinalIgnoreCase));
        if (existing >= 0)
        {
            _musicCustomPresets[existing] = preset;
        }
        else
        {
            if (_musicCustomPresets.Count >= 8)
            {
                MessageBox.Show("最多保存 8 个自定义音乐预设。", "ClevoRGBControl", MessageBoxButtons.OK, MessageBoxIcon.Information);
                return;
            }

            _musicCustomPresets.Add(preset);
        }

        RefreshMusicPresetList(name);
    }

    private void DeleteSelectedCustomMusicPreset()
    {
        var name = SelectedMusicPresetName();
        if (IsBuiltInMusicPreset(name))
        {
            MessageBox.Show("内置预设不能删除。", "ClevoRGBControl", MessageBoxButtons.OK, MessageBoxIcon.Information);
            return;
        }

        _musicCustomPresets.RemoveAll(item => string.Equals(item.Name, name, StringComparison.OrdinalIgnoreCase));
        _musicPresetName.Text = "";
        RefreshMusicPresetList("流行");
    }

    private void ApplyMusicPresetToControls(MusicPreset preset, bool refreshSelection = true)
    {
        _musicPresetName.Text = IsBuiltInMusicPreset(preset.Name) ? "" : preset.Name;
        _musicResponseMode.SelectedIndex = preset.ResponseMode == MusicResponseMode.BrightnessPulse ? 1 : 0;
        _musicLowColor.ColorHex = preset.LowColor;
        _musicHighColor.ColorHex = preset.HighColor;
        _musicSensitivity.SelectedIndex = ClosestIndex(MusicSensitivityValues, preset.Sensitivity);
        _musicAttack.SelectedIndex = ClosestIndex(MusicAttackValues, preset.AttackMs);
        _musicRelease.SelectedIndex = ClosestIndex(MusicReleaseValues, preset.ReleaseMs);
        _musicNoiseGate.Value = (int)Math.Round(preset.NoiseGate * 100);
        _musicBeatThreshold.Value = (int)Math.Round(preset.BeatThreshold * 100);
        _musicBaseBrightness.Value = preset.BaseBrightness;
        _musicPeakBrightness.Value = preset.PeakBrightness;
        if (refreshSelection)
        {
            RefreshMusicPresetList(preset.Name);
        }
    }

    private MusicPreset BuildMusicPresetFromControls(string name)
    {
        return new MusicPreset
        {
            Name = name,
            ResponseMode = _musicResponseMode.SelectedIndex == 1 ? MusicResponseMode.BrightnessPulse : MusicResponseMode.LevelColor,
            LowColor = _musicLowColor.ColorHex,
            HighColor = _musicHighColor.ColorHex,
            Sensitivity = MusicSensitivityValues[ClampIndex(_musicSensitivity.SelectedIndex, MusicSensitivityValues.Length)],
            AttackMs = MusicAttackValues[ClampIndex(_musicAttack.SelectedIndex, MusicAttackValues.Length)],
            ReleaseMs = MusicReleaseValues[ClampIndex(_musicRelease.SelectedIndex, MusicReleaseValues.Length)],
            BaseBrightness = _musicBaseBrightness.Value,
            PeakBrightness = _musicPeakBrightness.Value,
            NoiseGate = _musicNoiseGate.Value / 100d,
            BeatThreshold = _musicBeatThreshold.Value / 100d
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
        return _musicPreset.SelectedItem?.ToString() ?? "流行";
    }

    private static bool IsBuiltInMusicPreset(string? name)
    {
        return MusicSettings.BuiltInPresets.Any(item => string.Equals(item.Name, name, StringComparison.OrdinalIgnoreCase));
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
    }

    private static MusicPreset CloneMusicPreset(MusicPreset preset)
    {
        return new MusicPreset
        {
            Name = preset.Name,
            ResponseMode = preset.ResponseMode,
            LowColor = preset.LowColor,
            HighColor = preset.HighColor,
            Sensitivity = preset.Sensitivity,
            AttackMs = preset.AttackMs,
            ReleaseMs = preset.ReleaseMs,
            BaseBrightness = preset.BaseBrightness,
            PeakBrightness = preset.PeakBrightness,
            IntervalMs = preset.IntervalMs,
            NoiseGate = preset.NoiseGate,
            BeatThreshold = preset.BeatThreshold,
            PeakHoldMs = preset.PeakHoldMs
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
        var serviceDll = Path.Combine(AppContext.BaseDirectory, "InsydeDCHU.dll");
        var installedServiceDll = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles),
            "ClevoRGBControl",
            "Service",
            "InsydeDCHU.dll");
        var controlCenterDll = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86),
            "ControlCenter",
            "InsydeDCHU.dll");

        if (File.Exists(serviceDll))
        {
            return $"已安装：{serviceDll}";
        }

        if (File.Exists(installedServiceDll))
        {
            return $"已安装：{installedServiceDll}";
        }

        if (File.Exists(controlCenterDll))
        {
            return $"ControlCenter 中存在，未复制到服务目录";
        }

        return "未找到";
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
        EffectType.Sequence => "色彩序列",
        EffectType.Music => "音乐模式",
        EffectType.Off => "关闭",
        _ => effect.ToString()
    };

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
}

internal sealed class SliderRow : UserControl
{
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

        Controls.Add(new Label { Text = label, Width = LabelWidth, Height = 30, Location = new Point(0, 18) });
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

    public ColorPickerRow(string label)
    {
        Width = ContentWidth;
        Height = 54;
        Controls.Add(new Label { Text = label, Width = LabelWidth, Height = 30, Location = new Point(0, 13) });

        _swatch.Location = new Point(ControlLeft, 12);
        _swatch.Size = new Size(28, 24);
        _swatch.BorderStyle = BorderStyle.FixedSingle;
        _swatch.Click += (_, _) => PickColor();
        Controls.Add(_swatch);

        _hex.Location = new Point(ControlLeft + 40, 12);
        _hex.Width = 90;
        _hex.TextChanged += (_, _) => UpdateSwatch();
        Controls.Add(_hex);

        var pick = new Button { Text = "选择", Location = new Point(ControlLeft + 142, 9), Width = 86, Height = ButtonHeight };
        pick.Click += (_, _) => PickColor();
        Controls.Add(pick);

        var palette = new[] { "#FF0000", "#FF8000", "#FFFF00", "#00FF00", "#00FFFF", "#0060FF", "#8000FF", "#FFFFFF", "#FFD2A1", "#CFE8FF" };
        var x = ControlLeft + 240;
        foreach (var color in palette)
        {
            var button = new Button { BackColor = ColorTranslator.FromHtml(color), Location = new Point(x, 9), Size = new Size(24, 24), FlatStyle = FlatStyle.Flat };
            button.Click += (_, _) => ColorHex = color;
            Controls.Add(button);
            x += 28;
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
        using var dialog = new ColorDialog { FullOpen = true, Color = _swatch.BackColor };
        if (dialog.ShowDialog() == DialogResult.OK)
        {
            ColorHex = $"#{dialog.Color.R:X2}{dialog.Color.G:X2}{dialog.Color.B:X2}";
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
    private readonly ListBox _list = new();
    private readonly List<string> _colors = [];

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

        AddButton("添加", 465, 6, AddColor);
        AddButton("删除", 465, 46, RemoveSelected);
        AddButton("上移", 465, 86, () => MoveSelected(-1));
        AddButton("下移", 465, 126, () => MoveSelected(1));
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

    private void AddButton(string text, int x, int y, Action action)
    {
        var button = new Button { Text = text, Location = new Point(x, y), Width = 86, Height = ButtonHeight };
        button.Click += (_, _) => action();
        Controls.Add(button);
    }

    private void AddColor()
    {
        using var dialog = new ColorDialog { FullOpen = true };
        if (dialog.ShowDialog() == DialogResult.OK)
        {
            _colors.Add($"#{dialog.Color.R:X2}{dialog.Color.G:X2}{dialog.Color.B:X2}");
            RefreshList();
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
    private readonly SliderRow _brightness = new("亮度", 0, 100, "%");
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

        _brightness.Location = new Point(0, 350);
        _brightness.ValueChanged += (_, _) => SaveSelected();
        Controls.Add(_brightness);

        _color.Location = new Point(0, 420);
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
            AutoColorEnabled = false,
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
            _brightness.Enabled = enabled;
            _effectType.Enabled = enabled;
            _color.Enabled = enabled;
            _enabled.Enabled = enabled;
            _useAppIconColor.Enabled = enabled;

            if (rule is null)
            {
                _processName.Text = "";
                _brightness.Value = 70;
                _effectType.SelectedIndex = 0;
                _enabled.Checked = false;
                _useAppIconColor.Checked = false;
                _color.ColorHex = "#FFFFFF";
                UpdateColorVisibility();
                return;
            }

            _processName.Text = rule.ProcessName;
            _brightness.Value = rule.Brightness;
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
        rule.Brightness = _brightness.Value;
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
        _useAppIconColor.Checked = false;
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
