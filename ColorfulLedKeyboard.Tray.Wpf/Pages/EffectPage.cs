using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using ColorfulLedKeyboard.Core;
using ColorfulLedKeyboard.Tray.Wpf.Controls;
using ColorfulLedKeyboard.Tray.Wpf.Dialogs;

namespace ColorfulLedKeyboard.Tray.Wpf.Pages;

/// <summary>
/// 灯效设置页：WinForms BuildGeneralPage 及其全部交互逻辑的移植件。
/// 模式（灯效/音乐/关闭）、效果参数、效果记忆与配置预设；保存由宿主窗口统一处理。
/// </summary>
public sealed class EffectPage : UserControl
{
    public const string SoftwareDefaultPresetName = "软件默认配置";
    public const string UnsavedEffectPresetName = "当前设置（未保存为预设）";

    private static readonly string[] EffectLabels = ["固定颜色", "RGB 循环", "单色呼吸", "循环呼吸", "脉冲", "心跳"];

    private readonly System.Windows.Controls.RadioButton _modeLighting = MakeRadio("灯效模式");
    private readonly System.Windows.Controls.RadioButton _modeMusic = MakeRadio("音乐模式");
    private readonly System.Windows.Controls.RadioButton _modeOff = MakeRadio("关闭");
    private readonly System.Windows.Controls.ComboBox _effectType = MakeCombo(EffectLabels);
    private readonly UiSliderRow _brightness = new("亮度", 0, 100, "%");
    private readonly UiColorPickerRow _effectColor = new(compact: true);
    private readonly UiSliderRow _period = new("呼吸周期", 300, 30000, " ms");
    private readonly UiSliderRow _minimumBrightness = new("最低亮度", 0, 100, "%");
    private readonly System.Windows.Controls.CheckBox _hardBlink = MakeCheckBox("硬闪烁");
    private readonly UiSequenceEditor _sequence = new(showAddButton: false);
    private readonly Button _customColors = MakeButton("自定义颜色", 128);
    private readonly System.Windows.Controls.ComboBox _effectPreset = MakeCombo([]);
    private readonly System.Windows.Controls.TextBox _effectPresetName = MakeTextBox(240);
    private readonly Button _effectSavePreset = MakeButton("保存灯效预设修改", 160);
    private readonly Button _effectCreatePreset = MakeButton("新建/另存为");
    private readonly Button _effectDeletePreset = MakeButton("删除预设");

    private readonly TextBlock _modeHint = MakeHint();
    private readonly TextBlock _sequenceSummary = MakeHint();
    private readonly UIElement _modeHintHost;
    private readonly UIElement _effectTypeRow;
    private readonly UIElement _brightnessHost;
    private readonly UIElement _effectColorHost;
    private readonly UIElement _periodHost;
    private readonly UIElement _minimumBrightnessHost;
    private readonly UIElement _hardBlinkRow;
    private readonly UIElement _customColorsRow;
    private readonly UIElement _sequenceHost;
    private readonly UIElement _sequenceSectionHost;
    private readonly UIElement _sequenceSummaryHost;
    private readonly TextBlock _sequenceSection = Section("自定义循环颜色");
    private readonly UIElement _presetSectionHost;
    private readonly UIElement _presetComboRow;
    private readonly UIElement _presetNameRow;
    private readonly UIElement _presetButtonsRow;

    private EffectPresetSettings _effectPresets = new();
    private EffectMemorySettings _workingEffectMemory = new();
    private EffectType _lastSelectedEffectType = EffectType.Rainbow;
    private bool _customSequenceColorsEnabled;
    private bool _loadingSettings;
    private bool _effectChangedByUser;

    /// <summary>页面内有未应用修改（含预设增删改）。</summary>
    public bool IsDirty => _effectChangedByUser;

    /// <summary>模式切到音乐时请求跳转音乐页（WinForms 版按文字查找有 bug，这里直接给索引）。</summary>
    public event EventHandler<int>? PageRequested;

    /// <summary>任一控件变化，宿主据此点亮保存栏。</summary>
    public event EventHandler? Changed;

    public EffectPage()
    {
        var stack = new StackPanel { Margin = new Thickness(18, 18, 18, 28), MaxWidth = 832, HorizontalAlignment = HorizontalAlignment.Left };

        _modeLighting.Checked += (_, _) => OnModeChanged();
        _modeMusic.Checked += (_, _) => OnModeChanged();
        _modeOff.Checked += (_, _) => OnModeChanged();
        _effectType.SelectionChanged += (_, _) => OnEffectTypeChanged();
        _brightness.ValueChanged += (_, _) => MarkDirty();
        _effectColor.ColorChanged += (_, _) => MarkDirty();
        _period.ValueChanged += (_, _) =>
        {
            UpdateEffectConfigurationVisibility();
            MarkDirty();
        };
        _minimumBrightness.ValueChanged += (_, _) => MarkDirty();
        _hardBlink.Checked += (_, _) => MarkDirty();
        _hardBlink.Unchecked += (_, _) => MarkDirty();
        _effectPresetName.TextChanged += (_, _) => MarkDirty();
        _sequence.ColorsChanged += (_, _) =>
        {
            if (SelectedEffectType(EffectType.Rainbow) == EffectType.Rainbow)
            {
                _customSequenceColorsEnabled = true;
            }
            if (!_loadingSettings)
            {
                _effectChangedByUser = true;
                Changed?.Invoke(this, EventArgs.Empty);
            }
            UpdateEffectConfigurationVisibility();
        };
        _customColors.Click += (_, _) => EditCustomColors();
        _effectPreset.SelectionChanged += (_, _) =>
        {
            if (_loadingEffectPreset) return;
            _effectPresetName.Text = IsSoftwareDefaultEffectPresetSelected() ? "" : SelectedEffectPresetName();
            UpdateEffectPresetButtons();
            ApplySelectedEffectPreset();
        };
        _effectSavePreset.Click += (_, _) => SaveSelectedEffectPreset();
        _effectCreatePreset.Click += (_, _) => CreateEffectPreset();
        _effectDeletePreset.Click += (_, _) => DeleteSelectedEffectPreset();

        var modeRow = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(ControlLeft, 8, 0, 0) };
        modeRow.Children.Add(_modeLighting);
        _modeMusic.Margin = new Thickness(20, 0, 0, 0);
        modeRow.Children.Add(_modeMusic);
        _modeOff.Margin = new Thickness(20, 0, 0, 0);
        modeRow.Children.Add(_modeOff);

        _modeHintHost = _modeHint;
        _modeHintHost.Visibility = Visibility.Collapsed;
        stack.Children.Add(MakeCard("模式", modeRow, _modeHintHost));

        _effectTypeRow = Row("当前效果", _effectType);
        _brightnessHost = RowHost(_brightness);
        _effectColorHost = RowHost(_effectColor);
        _periodHost = RowHost(_period);
        _minimumBrightnessHost = RowHost(_minimumBrightness);
        _hardBlinkRow = PlainRow(_hardBlink);
        _customColorsRow = PlainRow(_customColors);
        _sequenceSectionHost = _sequenceSection;
        _sequenceSummaryHost = _sequenceSummary;
        _sequenceHost = Indent(_sequence);
        stack.Children.Add(MakeCard("灯效参数", _effectTypeRow, _brightnessHost, _effectColorHost, _periodHost,
            _minimumBrightnessHost, _hardBlinkRow, _customColorsRow, _sequenceSectionHost, _sequenceSummaryHost, _sequenceHost));

        _presetSectionHost = Section("配置预设");
        _presetComboRow = Row("当前预设", _effectPreset);
        _presetNameRow = Row("当前预设", _effectPresetName);
        _presetButtonsRow = ButtonRow(_effectSavePreset, _effectCreatePreset, _effectDeletePreset);
        stack.Children.Add(MakeCard("配置预设", _presetComboRow, _presetNameRow, _presetButtonsRow));

        var scroll = new ScrollViewer
        {
            Content = stack,
            VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
            HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled
        };
        Content = scroll;

        UpdateEffectConfigurationVisibility();
        UpdateModeAvailability();
    }

    private const int ControlLeft = 165;
    private const int LabelWidth = 130;

    // ---- 状态载入 / 保存（移植自 WinForms LoadSettings/SaveSettings 的效果页字段）----

    /// <summary>截图验收专用：临时强制灯效模式以展示完整参数布局，不产生脏状态。</summary>
    public void ForceLightingModeForCapture()
    {
        _loadingSettings = true;
        try
        {
            _modeMusic.IsChecked = false;
            _modeOff.IsChecked = false;
            _modeLighting.IsChecked = true;
        }
        finally
        {
            _loadingSettings = false;
        }
        UpdateModeAvailability();
        UpdateBrightnessAvailability();
        UpdateCustomColorsButton();
        UpdateEffectConfigurationVisibility();
    }

    public void LoadFromStore(KeyboardSettings settings)
    {
        _loadingSettings = true;
        try
        {
            _modeLighting.IsChecked = settings.Enabled && settings.OperatingMode != OperatingMode.Music;
            _modeMusic.IsChecked = settings.Enabled && settings.OperatingMode == OperatingMode.Music;
            _modeOff.IsChecked = !settings.Enabled;
            _effectType.SelectedIndex = EffectTypeToIndex(settings.Effect.Type);
            _brightness.Value = settings.Brightness;
            _effectColor.ColorHex = settings.Effect.Color;
            _period.Value = EffectivePeriodValue(settings.Effect);
            _minimumBrightness.Value = settings.Effect.MinimumBrightness;
            _hardBlink.IsChecked = settings.Effect.HardBlink;
            _sequence.Colors = settings.Effect.Sequence.Select(item => item.Color).ToList();
            _customSequenceColorsEnabled = settings.Effect.CustomSequenceColorsEnabled;
            _effectPresets = KeyboardSettings.CloneEffectPresets(settings.EffectPresets);
            _workingEffectMemory = CloneEffectMemory(settings.SavedEffects);
            _lastSelectedEffectType = settings.Effect.Type;
            RefreshEffectPresetList();
            _effectChangedByUser = false;
            UpdateBrightnessAvailability();
            UpdateCustomColorsButton();
            UpdateEffectConfigurationVisibility();
            RefreshEffectPresetList();
            ApplySelectedEffectPreset(markDirty: false);
        }
        finally
        {
            _loadingSettings = false;
        }

        UpdateModeAvailability();
    }

    public void ApplyTo(KeyboardSettings settings)
    {
        settings.Enabled = _modeOff.IsChecked != true;
        if (_modeOff.IsChecked != true)
        {
            settings.OperatingMode = _modeMusic.IsChecked == true ? OperatingMode.Music : OperatingMode.Lighting;
        }
        if (_effectChangedByUser)
        {
            settings.Effect.Type = SelectedEffectType(settings.Effect.Type);
        }

        settings.Effect.Color = _effectColor.ColorHex;
        var selectedEffect = SelectedEffectType(settings.Effect.Type);
        settings.Effect.PeriodMs = _period.Value;
        settings.Effect.MinimumBrightness = _minimumBrightness.Value;
        settings.Effect.HardBlink = _hardBlink.IsChecked == true;
        settings.Effect.CustomSequenceColorsEnabled = selectedEffect == EffectType.Rainbow;
        settings.Effect.Sequence = BuildSequenceColors(selectedEffect);
        settings.SavedEffects = CloneEffectMemory(_workingEffectMemory);
        RememberEffect(settings, settings.Effect);
        if (settings.Effect.Type != EffectType.Off)
        {
            settings.SavedEffects ??= new EffectMemorySettings();
            settings.SavedEffects.LastUsedLightingEffect = settings.Effect.Type;
        }

        settings.EffectPresets = KeyboardSettings.CloneEffectPresets(_effectPresets);
        settings.Brightness = _brightness.IsEnabled ? _brightness.Value : settings.Brightness;
    }

    public void OnSaved(KeyboardSettings settings)
    {
        _effectChangedByUser = false;
        _workingEffectMemory = CloneEffectMemory(settings.SavedEffects);
        _lastSelectedEffectType = settings.Effect.Type;
    }

    private void MarkDirty()
    {
        if (_loadingSettings) return;
        _effectChangedByUser = true;
        Changed?.Invoke(this, EventArgs.Empty);
    }

    // ---- 模式与可见性 ----

    private void OnModeChanged()
    {
        if (_loadingSettings) return;
        UpdateModeAvailability();
        UpdateEffectConfigurationVisibility();
        if (_modeMusic.IsChecked == true)
        {
            PageRequested?.Invoke(this, 2);
        }
        else if (_modeLighting.IsChecked == true)
        {
            // 切回灯效模式：从 LastUsedLightingEffect 恢复
            var settings = new SettingsStore().Load();
            var lastEffect = settings.SavedEffects?.LastUsedLightingEffect ?? EffectType.Static;
            if (lastEffect == EffectType.Off) lastEffect = EffectType.Static;
            _effectType.SelectedIndex = EffectTypeToIndex(lastEffect);
        }
        // 关闭模式：什么都不做，UpdateModeAvailability 会禁用其他控件
    }

    private void UpdateModeAvailability()
    {
        var music = _modeMusic.IsChecked == true;
        var off = _modeOff.IsChecked == true;
        var lightingEditable = !music && !off;
        _effectType.IsEnabled = lightingEditable;
        UpdateBrightnessAvailability();
        _effectColor.IsEnabled = lightingEditable;
        _period.IsEnabled = lightingEditable;
        _minimumBrightness.IsEnabled = lightingEditable;
        _hardBlink.IsEnabled = lightingEditable;
        _sequence.IsEnabled = lightingEditable;
        _customColors.IsEnabled = lightingEditable;
        _effectPreset.IsEnabled = lightingEditable;
        _effectPresetName.IsEnabled = lightingEditable;
        UpdateEffectPresetButtons();
    }

    private void UpdateBrightnessAvailability()
    {
        var effect = SelectedEffectType(EffectType.Rainbow);
        var brightnessEnabled = effect != EffectType.Off;
        var visible = brightnessEnabled && _modeMusic.IsChecked != true && _modeOff.IsChecked != true;
        _brightness.IsEnabled = visible;
        _brightnessHost.Visibility = visible ? Visibility.Visible : Visibility.Collapsed;
    }

    private void UpdateCustomColorsButton()
    {
        var effect = SelectedEffectType(EffectType.Rainbow);
        var visible = effect is EffectType.Static or EffectType.Rainbow or EffectType.Breathing
            or EffectType.Sequence or EffectType.Pulse or EffectType.Heartbeat;
        _customColorsRow.Visibility = visible ? Visibility.Visible : Visibility.Collapsed;
    }

    private void UpdateEffectConfigurationVisibility()
    {
        var off = _modeOff.IsChecked == true;
        var music = _modeMusic.IsChecked == true;
        var hideEffectParams = off || music;
        var effect = SelectedEffectType(EffectType.Rainbow);
        var singleColor = !hideEffectParams && effect is EffectType.Static or EffectType.Breathing;
        var sequenceVisible = !hideEffectParams && effect is EffectType.Rainbow or EffectType.Sequence or EffectType.Pulse or EffectType.Heartbeat;

        _effectTypeRow.Visibility = hideEffectParams ? Visibility.Collapsed : Visibility.Visible;
        _brightness.Visibility = !hideEffectParams && effect != EffectType.Off ? Visibility.Visible : Visibility.Collapsed;
        _effectColorHost.Visibility = singleColor ? Visibility.Visible : Visibility.Collapsed;
        _period.SetLabelText(effect switch
        {
            EffectType.Rainbow => "停留时长",
            EffectType.Pulse => "脉冲周期",
            EffectType.Heartbeat => "心跳周期",
            _ => "呼吸周期"
        });
        _periodHost.Visibility = !hideEffectParams && effect is EffectType.Rainbow or EffectType.Breathing or EffectType.Sequence or EffectType.Pulse or EffectType.Heartbeat
            ? Visibility.Visible : Visibility.Collapsed;
        _minimumBrightnessHost.Visibility = !hideEffectParams && effect == EffectType.Breathing ? Visibility.Visible : Visibility.Collapsed;
        _hardBlinkRow.Visibility = !hideEffectParams && effect == EffectType.Breathing ? Visibility.Visible : Visibility.Collapsed;
        _customColorsRow.Visibility = !hideEffectParams && effect is EffectType.Static or EffectType.Rainbow or EffectType.Breathing or EffectType.Sequence or EffectType.Pulse or EffectType.Heartbeat
            ? Visibility.Visible : Visibility.Collapsed;
        _sequenceSection.Text = effect switch
        {
            EffectType.Sequence => "循环呼吸颜色",
            EffectType.Pulse => "脉冲颜色",
            EffectType.Heartbeat => "心跳颜色",
            _ => "自定义循环颜色"
        };
        _sequenceSectionHost.Visibility = sequenceVisible ? Visibility.Visible : Visibility.Collapsed;
        _sequenceSummary.Text = BuildSequenceSummary(effect);
        _sequenceSummaryHost.Visibility = sequenceVisible ? Visibility.Visible : Visibility.Collapsed;
        _sequenceHost.Visibility = sequenceVisible ? Visibility.Visible : Visibility.Collapsed;
        var presetVisible = !hideEffectParams && effect is EffectType.Static or EffectType.Rainbow or EffectType.Breathing or EffectType.Sequence or EffectType.Pulse or EffectType.Heartbeat;
        _presetSectionHost.Visibility = presetVisible ? Visibility.Visible : Visibility.Collapsed;
        _presetComboRow.Visibility = presetVisible ? Visibility.Visible : Visibility.Collapsed;
        _presetNameRow.Visibility = presetVisible ? Visibility.Visible : Visibility.Collapsed;
        _presetButtonsRow.Visibility = presetVisible ? Visibility.Visible : Visibility.Collapsed;
        if (off)
        {
            _modeHint.Text = "关闭模式不会显示灯效参数。";
            _modeHintHost.Visibility = Visibility.Visible;
        }
        else if (music)
        {
            _modeHint.Text = "音乐模式由音乐页配置，灯效参数已禁用。";
            _modeHintHost.Visibility = Visibility.Visible;
        }
        else
        {
            _modeHintHost.Visibility = Visibility.Collapsed;
        }
    }

    // ---- 类型映射与效果构建 ----

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

    private void OnEffectTypeChanged()
    {
        if (_loadingSettings) return;
        var selectedEffect = SelectedEffectType(_lastSelectedEffectType);
        if (selectedEffect != _lastSelectedEffectType)
        {
            RememberEffect(_workingEffectMemory, BuildCurrentEffectFromGeneralControls(_lastSelectedEffectType));
            _lastSelectedEffectType = selectedEffect;
            ApplyEffectToGeneralControls(EffectFromMemory(_workingEffectMemory, selectedEffect), selectedPresetName: null, markDirty: false);
        }
        _effectChangedByUser = true;
        MarkDirty();
        UpdateBrightnessAvailability();
        UpdateCustomColorsButton();
        UpdateEffectConfigurationVisibility();
        RefreshEffectPresetList();
    }

    private LightingEffectSettings BuildCurrentEffectFromGeneralControls(EffectType effectType)
    {
        var effect = new LightingEffectSettings
        {
            Type = effectType,
            Color = _effectColor.ColorHex,
            PeriodMs = _period.Value,
            MinimumBrightness = _minimumBrightness.Value,
            HardBlink = _hardBlink.IsChecked == true,
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
            _hardBlink.IsChecked = effect.HardBlink;
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
            Changed?.Invoke(this, EventArgs.Empty);
        }
    }

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
        RememberEffect(settings.SavedEffects, effect);
    }

    private static void RememberEffect(EffectMemorySettings memory, LightingEffectSettings effect)
    {
        var copy = KeyboardSettings.CloneEffect(effect);
        copy.Normalize();

        switch (copy.Type)
        {
            case EffectType.Static:
                memory.Static = copy;
                break;
            case EffectType.Rainbow:
                copy.CustomSequenceColorsEnabled = true;
                memory.Rainbow = copy;
                break;
            case EffectType.Breathing:
                memory.Breathing = copy;
                break;
            case EffectType.Sequence:
                memory.Sequence = copy;
                break;
            case EffectType.Pulse:
                memory.Pulse = copy;
                break;
            case EffectType.Heartbeat:
                memory.Heartbeat = copy;
                break;
        }

        memory.Normalize();
    }

    private static LightingEffectSettings EffectFromMemory(EffectMemorySettings memory, EffectType type) =>
        KeyboardSettings.CloneEffect(type switch
        {
            EffectType.Static => memory.Static,
            EffectType.Rainbow => memory.Rainbow,
            EffectType.Breathing => memory.Breathing,
            EffectType.Sequence => memory.Sequence,
            EffectType.Pulse => memory.Pulse,
            EffectType.Heartbeat => memory.Heartbeat,
            _ => EffectPresetSettings.CreateSoftwareDefault(type)
        });

    private static EffectMemorySettings CloneEffectMemory(EffectMemorySettings memory) => new()
    {
        Static = KeyboardSettings.CloneEffect(memory.Static),
        Rainbow = KeyboardSettings.CloneEffect(memory.Rainbow),
        Breathing = KeyboardSettings.CloneEffect(memory.Breathing),
        Sequence = KeyboardSettings.CloneEffect(memory.Sequence),
        Pulse = KeyboardSettings.CloneEffect(memory.Pulse),
        Heartbeat = KeyboardSettings.CloneEffect(memory.Heartbeat),
        LastUsedLightingEffect = memory.LastUsedLightingEffect
    };

    private static int EffectivePeriodValue(LightingEffectSettings effect)
    {
        if (effect.Type == EffectType.Rainbow)
        {
            var first = effect.Sequence.FirstOrDefault();
            if (first is null || first.TransitionMs > 0)
            {
                return EffectPresetSettings.DefaultPeriodMs;
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

    // ---- 自定义颜色 ----

    private void EditCustomColors()
    {
        var effect = SelectedEffectType(EffectType.Rainbow);
        var singleSelection = effect is EffectType.Static or EffectType.Breathing;
        var selectedColors = effect is EffectType.Static or EffectType.Breathing
            ? new List<string> { _effectColor.ColorHex }
            : _sequence.Colors;

        var dialog = new ColorSelectionDialog(selectedColors, singleSelection) { Owner = Window.GetWindow(this) };
        if (dialog.ShowDialog() != true)
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
            _sequence.SetColorsNotify(colors);
            _customSequenceColorsEnabled = effect == EffectType.Rainbow;
        }

        if (!_loadingSettings)
        {
            _effectChangedByUser = true;
        }

        UpdateCustomColorsButton();
        UpdateEffectConfigurationVisibility();
    }

    // ---- 配置预设 ----

    private void RefreshEffectPresetList(string? selectedName = null)
    {
        var effect = SelectedEffectType(EffectType.Rainbow);
        var presets = _effectPresets.ForType(effect);
        var selected = selectedName ?? FindMatchingEffectPresetName(effect);
        var showUnsaved = string.IsNullOrWhiteSpace(selected) ||
            !string.Equals(selected, SoftwareDefaultPresetName, StringComparison.OrdinalIgnoreCase) &&
            !presets.Any(preset => string.Equals(preset.Name, selected, StringComparison.OrdinalIgnoreCase));
        if (showUnsaved)
        {
            selected = UnsavedEffectPresetName;
        }

        _loadingEffectPreset = true;
        try
        {
            _effectPreset.Items.Clear();
            _effectPreset.Items.Add(SoftwareDefaultPresetName);
            foreach (var preset in presets)
            {
                _effectPreset.Items.Add(preset.Name);
            }
            if (showUnsaved)
            {
                _effectPreset.Items.Add(UnsavedEffectPresetName);
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
            _effectPresetName.Text = index > 0 && !string.Equals(selected, UnsavedEffectPresetName, StringComparison.OrdinalIgnoreCase)
                ? _effectPreset.Items[index]?.ToString() ?? ""
                : "";
        }
        finally
        {
            _loadingEffectPreset = false;
        }

        UpdateEffectPresetButtons();
    }

    private void UpdateEffectPresetButtons()
    {
        var presetVisible = SelectedEffectType(EffectType.Rainbow) is EffectType.Static or EffectType.Rainbow or EffectType.Breathing
            or EffectType.Sequence or EffectType.Pulse or EffectType.Heartbeat;
        var selected = SelectedEffectPresetName();
        var customSelected = presetVisible && _effectPresets.ForType(SelectedEffectType(EffectType.Rainbow))
            .Any(preset => string.Equals(preset.Name, selected, StringComparison.OrdinalIgnoreCase));
        _effectSavePreset.IsEnabled = customSelected;
        _effectCreatePreset.IsEnabled = presetVisible;
        _effectDeletePreset.IsEnabled = customSelected;
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
            System.Windows.MessageBox.Show("软件默认配置不能被修改；请使用“新建/另存为”创建一个自定义预设。",
                "ClevoLEDKeyboardControl", MessageBoxButton.OK, MessageBoxImage.Information);
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
        var dialog = new Window
        {
            Title = "新建/另存为",
            WindowStartupLocation = WindowStartupLocation.CenterOwner,
            ResizeMode = ResizeMode.NoResize,
            ShowInTaskbar = false,
            Width = 380,
            Height = 170,
            Background = (Brush)Application.Current.Resources["Brush.Window"],
            FontFamily = (FontFamily)Application.Current.Resources["Font.Body"],
            Foreground = (Brush)Application.Current.Resources["Brush.Text"],
            Owner = Window.GetWindow(this)
        };

        var input = new System.Windows.Controls.TextBox { Style = (Style)Application.Current.Resources["UiTextBox"], Width = 300 };
        var ok = new Button { Content = "确定", Style = (Style)Application.Current.Resources["UiButtonPrimary"], MinWidth = 78 };
        var cancel = new Button { Content = "取消", Style = (Style)Application.Current.Resources["UiButton"], MinWidth = 78, Margin = new Thickness(12, 0, 0, 0) };
        var panel = new StackPanel { Margin = new Thickness(18) };
        panel.Children.Add(new TextBlock { Text = "预设名称" });
        panel.Children.Add(input);
        input.Margin = new Thickness(0, 10, 0, 0);
        var buttons = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right, Margin = new Thickness(0, 14, 0, 0) };
        buttons.Children.Add(ok);
        buttons.Children.Add(cancel);
        panel.Children.Add(buttons);
        dialog.Content = panel;

        string? result = null;
        ok.Click += (_, _) =>
        {
            var name = input.Text.Trim();
            if (!string.IsNullOrWhiteSpace(name))
            {
                result = name;
                dialog.Close();
                return;
            }
            System.Windows.MessageBox.Show(dialog, "请输入预设名称。", "ClevoLEDKeyboardControl",
                MessageBoxButton.OK, MessageBoxImage.Information);
            input.Focus();
        };
        cancel.Click += (_, _) => dialog.Close();

        while (dialog.ShowDialog() == true)
        {
            if (result is not null) return result;
            input.Focus();
        }

        return null;
    }

    private bool UpsertEffectPreset(string name, string? originalName)
    {
        name = name.Trim();
        if (string.IsNullOrWhiteSpace(name))
        {
            System.Windows.MessageBox.Show("请先输入预设名称。", "ClevoLEDKeyboardControl", MessageBoxButton.OK, MessageBoxImage.Information);
            return false;
        }

        if (string.Equals(name, SoftwareDefaultPresetName, StringComparison.OrdinalIgnoreCase))
        {
            System.Windows.MessageBox.Show("自定义预设不能使用“软件默认配置”这个名称。", "ClevoLEDKeyboardControl", MessageBoxButton.OK, MessageBoxImage.Information);
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
            var choice = System.Windows.MessageBox.Show($"预设“{name}”已存在，是否覆盖？", "ClevoLEDKeyboardControl",
                MessageBoxButton.YesNo, MessageBoxImage.Question);
            if (choice != MessageBoxResult.Yes)
            {
                return false;
            }
        }

        if (existingIndex < 0 && originalIndex < 0 && presets.Count >= EffectPresetSettings.MaxPresetsPerMode)
        {
            System.Windows.MessageBox.Show($"每个模式最多保存 {EffectPresetSettings.MaxPresetsPerMode} 个预设。",
                "ClevoLEDKeyboardControl", MessageBoxButton.OK, MessageBoxImage.Information);
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
        Changed?.Invoke(this, EventArgs.Empty);
        return true;
    }

    private void DeleteSelectedEffectPreset()
    {
        if (IsSoftwareDefaultEffectPresetSelected())
        {
            return;
        }

        var name = SelectedEffectPresetName();
        if (System.Windows.MessageBox.Show($"确定删除预设“{name}”？", "ClevoLEDKeyboardControl",
                MessageBoxButton.YesNo, MessageBoxImage.Question) != MessageBoxResult.Yes)
        {
            return;
        }

        var presets = _effectPresets.ForType(SelectedEffectType(EffectType.Rainbow));
        presets.RemoveAll(item => string.Equals(item.Name, name, StringComparison.OrdinalIgnoreCase));
        _effectPresets.Normalize();
        _effectChangedByUser = true;
        _effectPresetName.Text = "";
        RefreshEffectPresetList();
        Changed?.Invoke(this, EventArgs.Empty);
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

        if (EffectComparison.AreEquivalentEffects(current, EffectPresetSettings.CreateSoftwareDefault(effectType)))
        {
            return SoftwareDefaultPresetName;
        }

        return _effectPresets.ForType(effectType)
            .FirstOrDefault(preset => EffectComparison.AreEquivalentEffects(current, preset.Effect))
            ?.Name;
    }

    private string SelectedEffectPresetName() => _effectPreset.SelectedItem?.ToString() ?? "";

    private bool IsSoftwareDefaultEffectPresetSelected() =>
        string.Equals(SelectedEffectPresetName(), SoftwareDefaultPresetName, StringComparison.OrdinalIgnoreCase);

    private bool _loadingEffectPreset;

    // ---- UI 构建辅助（对应 WinForms Row/PlainRow/Section/ButtonRow）----

    private static Border MakeCard(string title, params UIElement[] children)
    {
        var stack = new StackPanel();
        stack.Children.Add(new TextBlock
        {
            Text = title,
            FontSize = 13,
            FontWeight = FontWeights.Bold,
            Foreground = FindBrushStatic("Brush.Text"),
            Margin = new Thickness(0, 0, 0, 8)
        });
        foreach (var child in children) stack.Children.Add(child);
        return new Border
        {
            Style = (Style)Application.Current.Resources["UiCard"],
            Child = stack
        };
    }

    private static UIElement Row(string label, FrameworkElement control)
    {
        control.MinWidth = Math.Max(control.MinWidth, 240);
        var grid = new Grid { MinHeight = 40, Width = UiMetrics.ContentWidth };
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(LabelWidth) });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        grid.Children.Add(new TextBlock
        {
            Text = label,
            VerticalAlignment = VerticalAlignment.Center,
            Foreground = FindBrushStatic("Brush.Text")
        });
        control.VerticalAlignment = VerticalAlignment.Center;
        Grid.SetColumn(control, 1);
        grid.Children.Add(control);
        return grid;
    }

    // SliderRow/ColorPickerRow 自带标签列，宿主行不再重复加标签
    private static UIElement RowHost(FrameworkElement control)
    {
        var grid = new Grid { MinHeight = 40, Width = UiMetrics.ContentWidth };
        control.VerticalAlignment = VerticalAlignment.Center;
        grid.Children.Add(control);
        return grid;
    }

    private static UIElement PlainRow(FrameworkElement control)
    {
        var grid = new Grid { MinHeight = 40, Width = UiMetrics.ContentWidth };
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(LabelWidth) });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        control.VerticalAlignment = VerticalAlignment.Center;
        control.HorizontalAlignment = HorizontalAlignment.Left;
        Grid.SetColumn(control, 1);
        grid.Children.Add(control);
        return grid;
    }

    private static UIElement ButtonRow(params Button[] buttons)
    {
        var panel = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            Margin = new Thickness(ControlLeft, 0, 0, 0),
            MinHeight = 40
        };
        foreach (var button in buttons)
        {
            button.MinWidth = Math.Max(button.MinWidth, 112);
            button.Margin = new Thickness(0, 0, 10, 0);
            panel.Children.Add(button);
        }
        return panel;
    }

    private static UIElement Indent(FrameworkElement control)
    {
        control.Margin = new Thickness(ControlLeft, 0, 0, 0);
        control.HorizontalAlignment = HorizontalAlignment.Left;
        control.Width = 590;
        return control;
    }

    private static TextBlock Section(string text) => new()
    {
        Text = text,
        FontWeight = FontWeights.Bold,
        Margin = new Thickness(0, 6, 0, 2),
        Foreground = FindBrushStatic("Brush.Text")
    };

    private static TextBlock MakeHint() => new()
    {
        TextWrapping = TextWrapping.Wrap,
        Foreground = FindBrushStatic("Brush.MutedText"),
        Margin = new Thickness(0, 4, 0, 0)
    };

    private static System.Windows.Controls.RadioButton MakeRadio(string text) => new()
    {
        Content = text,
        Style = (Style)Application.Current.Resources["UiRadioButton"],
        GroupName = "OperatingMode"
    };

    private static System.Windows.Controls.CheckBox MakeCheckBox(string text) => new()
    {
        Content = text,
        Style = (Style)Application.Current.Resources["UiCheckBox"]
    };

    private static System.Windows.Controls.ComboBox MakeCombo(IEnumerable<string> items)
    {
        var combo = new System.Windows.Controls.ComboBox
        {
            Style = (Style)Application.Current.Resources["UiComboBox"],
            MinWidth = 240
        };
        foreach (var item in items) combo.Items.Add(item);
        return combo;
    }

    private static System.Windows.Controls.TextBox MakeTextBox(double width) => new()
    {
        Style = (Style)Application.Current.Resources["UiTextBox"],
        Width = width
    };

    private static Button MakeButton(string text, double minWidth = 112) => new()
    {
        Content = text,
        Style = (Style)Application.Current.Resources["UiButton"],
        MinWidth = minWidth
    };

    private static Brush FindBrushStatic(string key) => (Brush)Application.Current.Resources[key];
}
