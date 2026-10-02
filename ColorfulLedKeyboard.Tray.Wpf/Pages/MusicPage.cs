using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using ColorfulLedKeyboard.Core;
using ColorfulLedKeyboard.Tray.Wpf.Controls;
using ColorfulLedKeyboard.Tray.Wpf.Dialogs;

namespace ColorfulLedKeyboard.Tray.Wpf.Pages;

/// <summary>
/// 音乐模式页：WinForms BuildMusicPage 及全部交互的移植件。
/// 播放器绑定、媒体会话匹配、音乐预设两段式保存、高级参数；保存由宿主窗口统一处理。
/// </summary>
public sealed class MusicPage : UserControl
{
    private static readonly double[] MusicSensitivityValues = [0.5, 1.0, 1.5, 2.0, 2.2, 2.5, 2.8, 3.1, 3.4, 3.6, 3.8, 4.0];
    private static readonly int[] MusicAttackValues = [10, 15, 20, 25, 30, 40];
    private static readonly int[] MusicReleaseValues = [70, 80, 90, 100, 110, 120, 130, 140, 150, 160, 170, 180, 190, 200];

    private readonly TextBlock _audioSourceLabel = MakeMutedLabel("当前音频源：检测中…");
    private readonly TextBlock _musicBindingStatus = MakeMutedLabel("");
    private readonly Button _bindPlayer = MakeButton("绑定正在播放的程序", 170);
    private readonly Button _clearPlayer = MakeButton("取消绑定", 110);
    private readonly System.Windows.Controls.ComboBox _bindingColorSource = MakeCombo(["使用音乐预设颜色", "使用歌曲封面主色", "使用歌曲封面配色"]);
    private readonly TextBlock _coverColorHint = new()
    {
        Text = "封面来源依赖服务端的专辑封面采集；未获取到封面时回退音乐预设颜色。",
        TextWrapping = TextWrapping.Wrap,
        MaxWidth = 430,
        Foreground = FindBrush("Brush.Warning"),
        FontSize = 11.5,
        VerticalAlignment = VerticalAlignment.Center
    };
    private readonly System.Windows.Controls.ComboBox _mediaSession = MakeCombo(["自动匹配播放器"]);
    private readonly TextBlock _mediaMatchStatus = MakeMutedLabel("");
    private readonly PalettePreviewControl _palettePreview = new();
    private readonly TextBlock _currentColorStatus = MakeMutedLabel("");

    private readonly System.Windows.Controls.ComboBox _preset = MakeCombo([]);
    private readonly Button _savePreset = MakeButton("保存预设修改", 140);
    private readonly Button _createPreset = MakeButton("新建/另存为", 112);
    private readonly Button _deletePreset = MakeButton("删除预设", 112);
    private readonly TextBlock _presetSaveHint = new() { TextWrapping = TextWrapping.Wrap, Margin = new Thickness(ControlLeft, 0, 0, 0) };
    private readonly System.Windows.Controls.TextBox _presetName = MakeTextBox(220);
    private readonly System.Windows.Controls.ComboBox _responseMode = MakeCombo(["节奏律动", "鼓点响应"]);
    private readonly Button _customColors = MakeButton("自定义颜色", 128);
    private readonly UiSequenceEditor _sequence = new(showAddButton: false);
    private readonly UiSliderRow _baseBrightness = new("基础亮度", 0, 100, "%");
    private readonly UiSliderRow _peakBrightness = new("峰值亮度", 0, 100, "%");
    private readonly System.Windows.Controls.CheckBox _followSystemVolume = MakeCheckBox("跟随 Windows 系统音量");

    private readonly System.Windows.Controls.CheckBox _advanced = MakeCheckBox("显示高级参数");
    private readonly System.Windows.Controls.ComboBox _sensitivity = MakeCombo([]);
    private readonly System.Windows.Controls.ComboBox _attack = MakeCombo([]);
    private readonly System.Windows.Controls.ComboBox _release = MakeCombo([]);
    private readonly UiSliderRow _noiseGate = new("噪声门", 0, 50, "%");
    private readonly UiSliderRow _beatThreshold = new("节拍阈值", 0, 100, "%");
    private readonly System.Windows.Controls.CheckBox _systemMixFallback = MakeCheckBox("未绑定播放器时，允许系统混音频段分析");
    private readonly UiSliderRow _eqLow = new("低频参考", 20, 1000, " Hz");
    private readonly UiSliderRow _eqHigh = new("高频参考", 40, 16000, " Hz");

    private readonly UIElement _coverHintRow;
    private readonly UIElement _presetSaveHintHost;
    private readonly UIElement _sensitivityRow;
    private readonly UIElement _attackRow;
    private readonly UIElement _releaseRow;
    private readonly UIElement _noiseGateHost;
    private readonly UIElement _beatThresholdHost;
    private readonly UIElement _eqLowHost;
    private readonly UIElement _eqHighHost;

    private List<MusicPreset> _musicCustomPresets = [];
    private MusicPlayerBinding _musicPlayerBinding = new();
    private bool _loadingSettings;
    private bool _loadingMusicPreset;
    private bool _applyingMusicPresetControls;
    private bool _refreshingMediaSessions;
    private bool _musicPresetChanged;
    private bool _musicPresetChangesStaged;
    private bool _generalChanged;

    public event EventHandler<int>? PageRequested;
    public event EventHandler? Changed;
    public event EventHandler? MusicPresetStateChanged;

    public MusicPage()
    {
        _bindingColorSource.SelectedIndex = 0;
        _bindingColorSource.SelectionChanged += (_, _) =>
        {
            MarkGeneralChanged();
            RefreshBindingStatus();
        };
        _mediaSession.SelectionChanged += (_, _) =>
        {
            if (_loadingSettings || _refreshingMediaSessions || _mediaSession.SelectedIndex < 0) return;
            _musicPlayerBinding.MediaSessionId = _mediaSession.SelectedIndex == 0
                ? ""
                : _mediaSession.SelectedItem?.ToString() ?? "";
            MarkGeneralChanged();
            RefreshBindingStatus();
        };
        _bindPlayer.Click += (_, _) => BindMusicPlayer();
        _clearPlayer.Click += (_, _) => ClearMusicPlayerBinding();

        SetupCombo(_sensitivity, MusicSensitivityValues.Select(value => $"{value:0.0}x"));
        SetupCombo(_attack, MusicAttackValues.Select(value => $"{value} ms"));
        SetupCombo(_release, MusicReleaseValues.Select(value => $"{value} ms"));
        _preset.SelectionChanged += (_, _) =>
        {
            if (_loadingMusicPreset) return;
            if (FindSelectedMusicPreset() is { } preset)
            {
                ApplyMusicPresetToControls(preset, refreshSelection: false);
            }
            _musicPresetChanged = false;
            _musicPresetChangesStaged = false;
            UpdateMusicPresetEditState();
            UpdateMusicPresetButtons();
            // WinForms 的通用 WireDirtyTracking 对所有 ComboBox 追加 MarkDirty：
            // 切换预设属于用户改动，需要保存（写入 PresetName），保存栏要点亮。
            MarkGeneralChanged();
        };
        _savePreset.Click += (_, _) => SaveSelectedMusicPreset();
        _createPreset.Click += (_, _) => CreateMusicPreset();
        _deletePreset.Click += (_, _) => DeleteSelectedCustomMusicPreset();
        _customColors.Click += (_, _) => EditMusicColors();
        _sequence.ColorsChanged += (_, _) => OnMusicPresetControlChanged();
        _advanced.Checked += (_, _) => UpdateMusicAdvancedVisibility();
        _advanced.Unchecked += (_, _) => UpdateMusicAdvancedVisibility();
        _baseBrightness.ValueChanged += (_, _) => OnMusicPresetControlChanged();
        _peakBrightness.ValueChanged += (_, _) => OnMusicPresetControlChanged();
        _followSystemVolume.Checked += (_, _) => OnMusicPresetControlChanged();
        _followSystemVolume.Unchecked += (_, _) => OnMusicPresetControlChanged();
        // 以下均为音乐预设内容字段（WinForms WireMusicPresetTracking 的 12 项对齐）
        _responseMode.SelectionChanged += (_, _) => OnMusicPresetControlChanged();
        _sensitivity.SelectionChanged += (_, _) => OnMusicPresetControlChanged();
        _attack.SelectionChanged += (_, _) => OnMusicPresetControlChanged();
        _release.SelectionChanged += (_, _) => OnMusicPresetControlChanged();
        _noiseGate.ValueChanged += (_, _) => OnMusicPresetControlChanged();
        _beatThreshold.ValueChanged += (_, _) => OnMusicPresetControlChanged();
        _eqLow.ValueChanged += (_, _) => OnMusicPresetControlChanged();
        _eqHigh.ValueChanged += (_, _) => OnMusicPresetControlChanged();
        _systemMixFallback.Checked += (_, _) => OnMusicPresetControlChanged();
        _systemMixFallback.Unchecked += (_, _) => OnMusicPresetControlChanged();
        _presetName.TextChanged += (_, _) => OnMusicPresetControlChanged();

        _coverHintRow = _coverColorHint;
        _coverHintRow.Visibility = Visibility.Collapsed;
        _presetSaveHintHost = _presetSaveHint;
        _presetSaveHintHost.Visibility = Visibility.Collapsed;
        _sensitivityRow = Row("灵敏度", _sensitivity);
        _attackRow = Row("响应速度", _attack);
        _releaseRow = Row("衰减速度", _release);
        _noiseGateHost = RowHost(_noiseGate);
        _beatThresholdHost = RowHost(_beatThreshold);
        _eqLowHost = RowHost(_eqLow);
        _eqHighHost = RowHost(_eqHigh);

        var stack = new StackPanel { Margin = new Thickness(18, 18, 18, 28), MaxWidth = 832, HorizontalAlignment = HorizontalAlignment.Left };
        stack.Children.Add(MakeCard("播放器与当前配色", _audioSourceLabel, _musicBindingStatus,
            ButtonRow(_bindPlayer, _clearPlayer),
            RowWithHint("键盘颜色来源", _bindingColorSource, _coverColorHint),
            Row("歌曲封面来源", _mediaSession), _mediaMatchStatus,
            RowHost(_palettePreview), _currentColorStatus));
        stack.Children.Add(MakeCard("音乐预设与响应", Row("音乐预设", _preset),
            ButtonRow(_savePreset, _createPreset, _deletePreset), _presetSaveHintHost, Row("当前预设", _presetName),
            Row("音乐响应", _responseMode), PlainRow(_customColors), Section("节拍颜色"), _sequence,
            _baseBrightness, _peakBrightness, PlainRow(_followSystemVolume)));
        stack.Children.Add(MakeCard("高级音乐参数", PlainRow(_advanced), _sensitivityRow,
            _attackRow, _releaseRow, _noiseGateHost, _beatThresholdHost,
            PlainRow(_systemMixFallback), _eqLowHost, _eqHighHost));

        var scroll = new ScrollViewer
        {
            Content = stack,
            VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
            HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled
        };
        Content = scroll;

        SetupComboDefaults();
        UpdateMusicAdvancedVisibility();
    }

    private void SetupComboDefaults()
    {
        _sensitivity.SelectedIndex = 1;
        _attack.SelectedIndex = 2;
        _release.SelectedIndex = 3;
        _responseMode.SelectedIndex = 0;
    }

    public bool IsAdvancedExpanded => _advanced.IsChecked == true;

    public void SetAdvancedExpanded(bool expanded)
    {
        _advanced.IsChecked = expanded;
        UpdateMusicAdvancedVisibility();
    }

    /// <summary>保存栏总脏状态：绑定变更、预设变更或已暂存的预设修改（暂存 ≠ 落盘）。</summary>
    public bool IsDirty => _generalChanged || _musicPresetChanged || _musicPresetChangesStaged;

    /// <summary>SaveSettings 的硬拦截条件（WinForms 语义一致）。</summary>
    public bool IsMusicPresetChanged => _musicPresetChanged;

    public string MusicPresetBlockedMessage =>
        IsBuiltInMusicPreset(SelectedMusicPresetName())
            ? "当前内置音乐预设已被修改。请先使用“新建/另存为”保存为自定义预设。"
            : "当前音乐预设有尚未保存的修改。请先点击“保存预设修改”。";

    // ---- 载入 / 保存（移植自 WinForms LoadSettings/SaveSettings 的音乐页字段）----

    public void LoadFromStore(KeyboardSettings settings)
    {
        _loadingSettings = true;
        try
        {
            _musicCustomPresets = settings.Effect.Music.CustomPresets.Select(CloneMusicPreset).ToList();
            RefreshMusicPresetList(settings.Effect.Music.PresetName);
            _presetName.Text = IsBuiltInMusicPreset(settings.Effect.Music.PresetName) ? "" : settings.Effect.Music.PresetName;
            _responseMode.SelectedIndex = MusicResponseMapping.Index(settings.Effect.Music.EqEnabled);
            _sequence.Colors = settings.Effect.Music.Colors;
            _sensitivity.SelectedIndex = ClosestIndex(MusicSensitivityValues, settings.Effect.Music.Sensitivity);
            _attack.SelectedIndex = ClosestIndex(MusicAttackValues, settings.Effect.Music.AttackMs);
            _release.SelectedIndex = ClosestIndex(MusicReleaseValues, settings.Effect.Music.ReleaseMs);
            _noiseGate.Value = (int)Math.Round(settings.Effect.Music.NoiseGate * 100);
            _beatThreshold.Value = (int)Math.Round(settings.Effect.Music.BeatThreshold * 100);
            _systemMixFallback.IsChecked = settings.Effect.Music.AllowSystemMixFallback;
            _eqLow.Value = settings.Effect.Music.EqLowHz;
            _eqHigh.Value = settings.Effect.Music.EqHighHz;
            _baseBrightness.Value = settings.Effect.Music.BaseBrightness;
            _peakBrightness.Value = settings.Effect.Music.PeakBrightness;
            _followSystemVolume.IsChecked = settings.Effect.Music.FollowSystemVolume;
            _musicPlayerBinding = new MusicPlayerBinding
            {
                Enabled = settings.Effect.Music.PlayerBinding.Enabled,
                ProcessName = settings.Effect.Music.PlayerBinding.ProcessName,
                ExecutablePath = settings.Effect.Music.PlayerBinding.ExecutablePath,
                IncludeChildProcesses = settings.Effect.Music.PlayerBinding.IncludeChildProcesses,
                MediaSessionId = settings.Effect.Music.PlayerBinding.MediaSessionId,
                ColorSource = settings.Effect.Music.PlayerBinding.ColorSource
            };
            _bindingColorSource.SelectedIndex = (int)_musicPlayerBinding.ColorSource;
            RefreshMediaSessions(_musicPlayerBinding.MediaSessionId);
            RefreshBindingStatus();
            // 注意：不用预设模板重放控件值——settings.Effect.Music 可能被托盘合法改过
            // （亮度/音乐响应），模板重放会显示错误值并在保存时静默回滚（WinForms 无此步）。
            _musicPresetChanged = false;
            _musicPresetChangesStaged = false;
            _generalChanged = false;
            UpdateMusicPresetEditState();
        }
        finally
        {
            _loadingSettings = false;
        }
    }

    public void ApplyTo(KeyboardSettings settings)
    {
        settings.Effect.Music.PresetName = SelectedMusicPresetName();
        settings.Effect.Music.ResponseMode = MusicResponseMode.LevelColor;
        settings.Effect.Music.LevelColorEnabled = true;
        settings.Effect.Music.Colors = NormalizedMusicColors();
        settings.Effect.Music.LowColor = settings.Effect.Music.Colors[0];
        settings.Effect.Music.HighColor = settings.Effect.Music.Colors[^1];
        settings.Effect.Music.Sensitivity = MusicSensitivityValues[ClampIndex(_sensitivity.SelectedIndex, MusicSensitivityValues.Length)];
        settings.Effect.Music.AttackMs = MusicAttackValues[ClampIndex(_attack.SelectedIndex, MusicAttackValues.Length)];
        settings.Effect.Music.ReleaseMs = MusicReleaseValues[ClampIndex(_release.SelectedIndex, MusicReleaseValues.Length)];
        settings.Effect.Music.NoiseGate = _noiseGate.Value / 100d;
        settings.Effect.Music.BeatThreshold = _beatThreshold.Value / 100d;
        settings.Effect.Music.EqEnabled = MusicResponseMapping.UsesBeatDetection(_responseMode.SelectedIndex);
        settings.Effect.Music.AllowSystemMixFallback = _systemMixFallback.IsChecked == true;
        settings.Effect.Music.EqLowHz = _eqLow.Value;
        settings.Effect.Music.EqHighHz = _eqHigh.Value;
        settings.Effect.Music.Spotify.AlbumColorEnabled = false;
        settings.Effect.Music.BaseBrightness = _baseBrightness.Value;
        settings.Effect.Music.PeakBrightness = _peakBrightness.Value;
        settings.Effect.Music.FollowSystemVolume = _followSystemVolume.IsChecked == true;
        _musicPlayerBinding.ColorSource = (MusicColorSource)Math.Max(0, _bindingColorSource.SelectedIndex);
        _musicPlayerBinding.MediaSessionId = _mediaSession.SelectedIndex <= 0
            ? ""
            : _mediaSession.SelectedItem?.ToString() ?? "";
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
    }

    public void OnSaved(KeyboardSettings settings)
    {
        _musicPresetChangesStaged = false;
        _musicPresetChanged = false;
        _generalChanged = false;
        UpdateMusicPresetEditState();
    }

    // ---- 绑定与运行状态（移植自 WinForms BindMusicPlayer/RefreshMusicBindingStatus/...）----

    private void BindMusicPlayer()
    {
        var picker = new AudioApplicationPickerDialog(includeVisibleProcesses: true) { Owner = Window.GetWindow(this) };
        if (picker.ShowDialog() != true || picker.Selected is null) return;
        var selected = picker.Selected;
        _musicPlayerBinding.Enabled = true;
        _musicPlayerBinding.ProcessName = selected.ProcessName;
        _musicPlayerBinding.ExecutablePath = selected.ExecutablePath;
        _musicPlayerBinding.IncludeChildProcesses = true;
        _musicPlayerBinding.MediaSessionId = "";
        RefreshMediaSessions(_musicPlayerBinding.MediaSessionId);
        RefreshBindingStatus();
        MarkGeneralChanged();
    }

    private void ClearMusicPlayerBinding()
    {
        _musicPlayerBinding = new MusicPlayerBinding();
        RefreshMediaSessions("");
        RefreshBindingStatus();
        MarkGeneralChanged();
    }

    /// <summary>状态定时器驱动：媒体会话与绑定状态每秒刷新。</summary>
    public void RefreshRuntimeStatus(AutomationStatus? status = null)
    {
        RefreshMediaSessions();
        RefreshBindingStatus(status);
    }

    private void RefreshBindingStatus(AutomationStatus? status = null)
    {
        _coverHintRow.Visibility = _bindingColorSource.SelectedIndex is 1 or 2 ? Visibility.Visible : Visibility.Collapsed;
        if (!_musicPlayerBinding.Enabled)
        {
            _musicBindingStatus.Text = "未绑定：音乐模式使用系统混音和音乐预设颜色。";
            _mediaMatchStatus.Text = "媒体会话：未启用播放器绑定。";
            _mediaMatchStatus.Foreground = FindBrush("Brush.MutedText");
            _palettePreview.Colors = _sequence.Colors;
            _currentColorStatus.Text = "当前使用音乐预设配色。";
            _clearPlayer.IsEnabled = false;
            return;
        }

        _clearPlayer.IsEnabled = true;
        var runtime = status?.ActiveMusicApplication == _musicPlayerBinding.ProcessName
            ? $"；当前 PID：{string.Join(",", status.ActiveProcessIds)}{(string.IsNullOrWhiteSpace(status.TrackTitle) ? "" : $"；{status.TrackTitle}")}"
            : "";
        var media = string.IsNullOrWhiteSpace(_musicPlayerBinding.MediaSessionId)
            ? "；媒体会话：自动匹配"
            : $"；媒体会话：{_musicPlayerBinding.MediaSessionId}";
        _musicBindingStatus.Text = $"已绑定：{_musicPlayerBinding.ProcessName}{runtime}{media}";
        var mediaState = MediaPlaybackState.Load();
        var playback = mediaState?.Find(_musicPlayerBinding);
        var source = (MusicColorSource)Math.Max(0, _bindingColorSource.SelectedIndex);
        var colors = source switch
        {
            MusicColorSource.AlbumDominant when playback is not null && !string.IsNullOrWhiteSpace(playback.DominantColor) =>
                new List<string> { playback.DominantColor },
            MusicColorSource.AlbumPalette when playback is not null && playback.Palette.Count > 0 => playback.Palette,
            _ => _sequence.Colors
        };
        _palettePreview.Colors = colors;
        if (source == MusicColorSource.Preset)
        {
            _mediaMatchStatus.Text = "媒体会话：颜色来源为音乐预设，封面匹配暂不参与输出。";
            _mediaMatchStatus.Foreground = FindBrush("Brush.MutedText");
        }
        else if (playback is not null)
        {
            var mode = string.IsNullOrWhiteSpace(_musicPlayerBinding.MediaSessionId) ? "自动匹配成功" : "手动匹配成功";
            var cover = playback.Palette.Count > 0 ? $"已获取封面（{playback.Palette.Count} 色）" : "未获取封面，正在使用预设颜色";
            _mediaMatchStatus.Text = $"媒体会话：{mode} → {playback.SourceId}；{(playback.IsPlaying ? "正在播放" : "切歌/暂停过渡")}；{cover}";
            _mediaMatchStatus.Foreground = FindBrush(playback.Palette.Count > 0 ? "Brush.Success" : "Brush.Warning");
        }
        else
        {
            var candidates = mediaState?.Sessions.Select(item => item.SourceId)
                .Where(id => !string.IsNullOrWhiteSpace(id)).Distinct(StringComparer.OrdinalIgnoreCase).ToList() ?? [];
            var automatic = string.IsNullOrWhiteSpace(_musicPlayerBinding.MediaSessionId);
            var matchingCandidates = mediaState?.Sessions.Where(session =>
                    MediaPlaybackState.SessionMatchesProcess(session, _musicPlayerBinding.ProcessName))
                .Select(session => session.SourceId).Distinct(StringComparer.OrdinalIgnoreCase).ToList() ?? [];
            var otherPlayers = candidates.Where(id => !matchingCandidates.Contains(id, StringComparer.OrdinalIgnoreCase)).ToList();
            _mediaMatchStatus.Text = candidates.Count == 0
                ? "媒体会话：尚未检测到 Windows 播放器会话，请先播放歌曲。"
                : automatic
                    ? matchingCandidates.Count > 0
                        ? $"媒体会话：检测到多个可能属于 {_musicPlayerBinding.ProcessName} 的来源（{string.Join("、", matchingCandidates)}），请在上方手动选择。"
                        : $"媒体会话：{_musicPlayerBinding.ProcessName} 未提供可识别的 Windows 媒体会话。" +
                          (otherPlayers.Count == 0 ? "" : $"检测到的 {string.Join("、", otherPlayers)} 属于其他播放器，不会使用。")
                    : $"媒体会话：选择的会话当前不可用；当前可用：{string.Join("、", candidates)}。";
            _mediaMatchStatus.Foreground = FindBrush("Brush.Error");
        }
        _currentColorStatus.Text = playback is null
            ? $"当前使用预设颜色：{string.Join("  ", colors)}"
            : $"当前歌曲：{playback.Title}{(string.IsNullOrWhiteSpace(playback.Artist) ? "" : " - " + playback.Artist)}；实际颜色：{string.Join("  ", colors)}";
    }

    private void RefreshMediaSessions(string? selectedSource = null)
    {
        selectedSource ??= _mediaSession.SelectedIndex <= 0 ? _musicPlayerBinding.MediaSessionId : _mediaSession.SelectedItem?.ToString();
        var sources = MediaPlaybackState.Load()?.Sessions
            .Select(session => session.SourceId)
            .Where(source => !string.IsNullOrWhiteSpace(source))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .OrderBy(source => source, StringComparer.OrdinalIgnoreCase)
            .ToList() ?? [];
        if (!string.IsNullOrWhiteSpace(selectedSource) && !sources.Contains(selectedSource, StringComparer.OrdinalIgnoreCase))
            sources.Add(selectedSource);
        var desiredItems = new[] { "自动匹配播放器" }.Concat(sources).ToList();
        var currentItems = _mediaSession.Items.Cast<object>().Select(item => item?.ToString() ?? "").ToList();
        var desiredIndex = string.IsNullOrWhiteSpace(selectedSource)
            ? 0
            : desiredItems.FindIndex(item => string.Equals(item, selectedSource, StringComparison.OrdinalIgnoreCase));
        if (currentItems.SequenceEqual(desiredItems, StringComparer.OrdinalIgnoreCase) &&
            _mediaSession.SelectedIndex == Math.Max(0, desiredIndex)) return;
        _refreshingMediaSessions = true;
        try
        {
            _mediaSession.Items.Clear();
            foreach (var item in desiredItems) _mediaSession.Items.Add(item);
            _mediaSession.SelectedIndex = Math.Max(0, desiredIndex);
        }
        finally
        {
            _refreshingMediaSessions = false;
        }
    }

    public void UpdateAudioSourceLabel(AudioSourceStatusInfo? info)
    {
        var deviceName = info?.DeviceFriendlyName ?? "";
        _audioSourceLabel.Text = string.IsNullOrEmpty(deviceName)
            ? "当前音频源：检测中…"
            : $"当前音频源：{deviceName}";
    }

    // ---- 预设流（移植自 WinForms 两段式保存）----

    private void RefreshMusicPresetList(string? selectedName = null)
    {
        var selected = selectedName ?? SelectedMusicPresetName();
        _loadingMusicPreset = true;
        try
        {
            _preset.Items.Clear();
            foreach (var preset in MusicSettings.BuiltInPresets)
            {
                _preset.Items.Add(preset.Name);
            }
            foreach (var preset in _musicCustomPresets)
            {
                _preset.Items.Add(preset.Name);
            }

            var index = 0;
            for (var i = 0; i < _preset.Items.Count; i++)
            {
                if (string.Equals(_preset.Items[i]?.ToString(), selected, StringComparison.OrdinalIgnoreCase))
                {
                    index = i;
                    break;
                }
            }

            _preset.SelectedIndex = _preset.Items.Count == 0 ? -1 : index;
        }
        finally
        {
            _loadingMusicPreset = false;
        }

        UpdateMusicPresetButtons();
    }

    private void EditMusicColors()
    {
        var dialog = new ColorSelectionDialog(NormalizedMusicColors(), singleSelection: false) { Owner = Window.GetWindow(this) };
        if (dialog.ShowDialog() != true)
        {
            return;
        }

        _sequence.SetColorsNotify(dialog.SelectedColors.Count == 0 ? MusicSettings.DefaultColors() : dialog.SelectedColors);
    }

    private List<string> NormalizedMusicColors()
    {
        var colors = _sequence.Colors
            .Select(color => RgbColor.FromHex(color).Hex)
            .ToList();
        return colors.Count == 0 ? MusicSettings.DefaultColors() : colors;
    }

    private void SaveSelectedMusicPreset()
    {
        if (IsBuiltInMusicPreset(SelectedMusicPresetName()))
        {
            System.Windows.MessageBox.Show("内置预设不能被修改；请使用“新建/另存为”创建一个自定义预设。",
                "ClevoLEDKeyboardControl", MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }

        var originalName = SelectedMusicPresetName();
        var newName = _presetName.Text.Trim();
        if (string.IsNullOrWhiteSpace(newName))
        {
            newName = originalName;
        }

        if (UpsertMusicPreset(newName, originalName))
        {
            RefreshMusicPresetList(newName);
            _musicPresetChanged = false;
            _musicPresetChangesStaged = true;
            UpdateMusicPresetEditState();
            Changed?.Invoke(this, EventArgs.Empty);
        }
    }

    private void CreateMusicPreset()
    {
        var name = PromptForPresetName();
        if (name is null)
        {
            return;
        }

        if (UpsertMusicPreset(name, originalName: null))
        {
            RefreshMusicPresetList(name);
            _musicPresetChanged = false;
            _musicPresetChangesStaged = true;
            UpdateMusicPresetEditState();
            Changed?.Invoke(this, EventArgs.Empty);
        }
    }

    private string? PromptForPresetName()
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

    private bool UpsertMusicPreset(string name, string? originalName)
    {
        name = name.Trim();
        if (string.IsNullOrWhiteSpace(name))
        {
            System.Windows.MessageBox.Show("请先输入自定义预设名称。", "ClevoLEDKeyboardControl", MessageBoxButton.OK, MessageBoxImage.Information);
            return false;
        }

        if (IsBuiltInMusicPreset(name))
        {
            System.Windows.MessageBox.Show("自定义预设不能使用内置预设名称。", "ClevoLEDKeyboardControl", MessageBoxButton.OK, MessageBoxImage.Information);
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
            var choice = System.Windows.MessageBox.Show($"预设“{name}”已存在，是否覆盖？", "ClevoLEDKeyboardControl",
                MessageBoxButton.YesNo, MessageBoxImage.Question);
            if (choice != MessageBoxResult.Yes)
            {
                return false;
            }
        }

        if (existing < 0 && originalIndex < 0 && _musicCustomPresets.Count >= 8)
        {
            System.Windows.MessageBox.Show("最多保存 8 个自定义音乐预设。", "ClevoLEDKeyboardControl", MessageBoxButton.OK, MessageBoxImage.Information);
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
            System.Windows.MessageBox.Show("内置预设不能删除。", "ClevoLEDKeyboardControl", MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }

        _musicCustomPresets.RemoveAll(item => string.Equals(item.Name, name, StringComparison.OrdinalIgnoreCase));
        _presetName.Text = "";
        RefreshMusicPresetList(MusicSettings.DefaultPresetName);
        _musicPresetChanged = false;
        _musicPresetChangesStaged = true;
        UpdateMusicPresetEditState();
        Changed?.Invoke(this, EventArgs.Empty);
    }

    private void ApplyMusicPresetToControls(MusicPreset preset, bool refreshSelection = true, bool markDirty = true)
    {
        _applyingMusicPresetControls = true;
        try
        {
            _presetName.Text = IsBuiltInMusicPreset(preset.Name) ? "" : preset.Name;
            _responseMode.SelectedIndex = MusicResponseMapping.Index(preset.EqEnabled);
            _sequence.Colors = preset.Colors;
            _sensitivity.SelectedIndex = ClosestIndex(MusicSensitivityValues, preset.Sensitivity);
            _attack.SelectedIndex = ClosestIndex(MusicAttackValues, preset.AttackMs);
            _release.SelectedIndex = ClosestIndex(MusicReleaseValues, preset.ReleaseMs);
            _noiseGate.Value = (int)Math.Round(preset.NoiseGate * 100);
            _beatThreshold.Value = (int)Math.Round(preset.BeatThreshold * 100);
            _eqLow.Value = preset.EqLowHz;
            _eqHigh.Value = preset.EqHighHz;
            _baseBrightness.Value = preset.BaseBrightness;
            _peakBrightness.Value = preset.PeakBrightness;
            _followSystemVolume.IsChecked = preset.FollowSystemVolume;
        }
        finally
        {
            _applyingMusicPresetControls = false;
        }
        if (refreshSelection)
        {
            RefreshMusicPresetList(preset.Name);
        }

        UpdateMusicPresetButtons();
        UpdateMusicPresetEditState();
    }

    private void UpdateMusicPresetButtons()
    {
        var customSelected = !IsBuiltInMusicPreset(SelectedMusicPresetName());
        _savePreset.IsEnabled = customSelected && _musicPresetChanged;
        _deletePreset.IsEnabled = customSelected;
        _createPreset.IsEnabled = true;
    }

    private void OnMusicPresetControlChanged()
    {
        if (_loadingSettings || _loadingMusicPreset || _applyingMusicPresetControls) return;
        _musicPresetChanged = true;
        _musicPresetChangesStaged = false;
        UpdateMusicPresetEditState();
        MusicPresetStateChanged?.Invoke(this, EventArgs.Empty);
    }

    private void UpdateMusicPresetEditState()
    {
        var builtIn = IsBuiltInMusicPreset(SelectedMusicPresetName());
        _presetSaveHint.Text = _musicPresetChanged
            ? builtIn
                ? "内置预设不能直接修改，请使用“新建/另存为”。"
                : "预设内容已修改，请先点击“保存预设修改”。"
            : _musicPresetChangesStaged
                ? "预设修改已暂存，点击底部“保存并应用”后生效。"
                : "";
        _presetSaveHint.Foreground = FindBrush(_musicPresetChanged ? "Brush.Warning" : "Brush.Success");
        _presetSaveHintHost.Visibility = string.IsNullOrWhiteSpace(_presetSaveHint.Text)
            ? Visibility.Collapsed : Visibility.Visible;
        UpdateMusicPresetButtons();
    }

    private MusicPreset BuildMusicPresetFromControls(string name)
    {
        var colors = NormalizedMusicColors();
        return new MusicPreset
        {
            Name = name,
            ResponseMode = MusicResponseMode.LevelColor,
            LowColor = colors[0],
            HighColor = colors[^1],
            Colors = colors,
            Sensitivity = MusicSensitivityValues[ClampIndex(_sensitivity.SelectedIndex, MusicSensitivityValues.Length)],
            AttackMs = MusicAttackValues[ClampIndex(_attack.SelectedIndex, MusicAttackValues.Length)],
            ReleaseMs = MusicReleaseValues[ClampIndex(_release.SelectedIndex, MusicReleaseValues.Length)],
            BaseBrightness = _baseBrightness.Value,
            PeakBrightness = _peakBrightness.Value,
            NoiseGate = _noiseGate.Value / 100d,
            BeatThreshold = _beatThreshold.Value / 100d,
            FollowSystemVolume = _followSystemVolume.IsChecked == true,
            EqEnabled = MusicResponseMapping.UsesBeatDetection(_responseMode.SelectedIndex),
            EqLowHz = _eqLow.Value,
            EqHighHz = _eqHigh.Value
        }.Normalize();
    }

    private MusicPreset? FindSelectedMusicPreset()
    {
        var name = SelectedMusicPresetName();
        return MusicSettings.BuiltInPresets.FirstOrDefault(item => string.Equals(item.Name, name, StringComparison.OrdinalIgnoreCase)) ??
            _musicCustomPresets.FirstOrDefault(item => string.Equals(item.Name, name, StringComparison.OrdinalIgnoreCase));
    }

    private string SelectedMusicPresetName() => _preset.SelectedItem?.ToString() ?? MusicSettings.DefaultPresetName;

    private static bool IsBuiltInMusicPreset(string? name) => MusicSettings.IsBuiltInPresetName(name);

    private static int ClampIndex(int index, int length) => Math.Clamp(index, 0, Math.Max(0, length - 1));

    private void UpdateMusicAdvancedVisibility()
    {
        var visible = _advanced.IsChecked == true;
        _sensitivityRow.Visibility = visible ? Visibility.Visible : Visibility.Collapsed;
        _attackRow.Visibility = visible ? Visibility.Visible : Visibility.Collapsed;
        _releaseRow.Visibility = visible ? Visibility.Visible : Visibility.Collapsed;
        _noiseGateHost.Visibility = visible ? Visibility.Visible : Visibility.Collapsed;
        _beatThresholdHost.Visibility = visible ? Visibility.Visible : Visibility.Collapsed;
        _eqLowHost.Visibility = visible ? Visibility.Visible : Visibility.Collapsed;
        _eqHighHost.Visibility = visible ? Visibility.Visible : Visibility.Collapsed;
    }

    private void MarkGeneralChanged()
    {
        if (_loadingSettings) return;
        _generalChanged = true;
        Changed?.Invoke(this, EventArgs.Empty);
    }

    // ---- UI 辅助 ----

    private const int ControlLeft = 165;

    private static Border MakeCard(string title, params UIElement[] children)
    {
        var stack = new StackPanel();
        stack.Children.Add(new TextBlock
        {
            Text = title,
            FontSize = 13,
            FontWeight = FontWeights.Bold,
            Foreground = FindBrush("Brush.Text"),
            Margin = new Thickness(0, 0, 0, 8)
        });
        foreach (var child in children) stack.Children.Add(child);
        return new Border { Style = (Style)Application.Current.Resources["UiCard"], Child = stack };
    }

    private static UIElement Row(string label, FrameworkElement control)
    {
        var grid = new Grid { MinHeight = 40, Width = UiMetrics.ContentWidth };
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(130) });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        grid.Children.Add(new TextBlock
        {
            Text = label,
            VerticalAlignment = VerticalAlignment.Center,
            Foreground = FindBrush("Brush.Text")
        });
        control.VerticalAlignment = VerticalAlignment.Center;
        Grid.SetColumn(control, 1);
        grid.Children.Add(control);
        return grid;
    }

    private static UIElement RowHost(FrameworkElement control)
    {
        var grid = new Grid { MinHeight = 40, Width = UiMetrics.ContentWidth };
        control.VerticalAlignment = VerticalAlignment.Center;
        grid.Children.Add(control);
        return grid;
    }

    private static UIElement RowWithHint(string label, FrameworkElement control, FrameworkElement hint)
    {
        var grid = Row(label, control);
        hint.VerticalAlignment = VerticalAlignment.Center;
        ((Grid)grid).Children.Add(hint);
        Grid.SetColumn(hint, 1);
        hint.HorizontalAlignment = HorizontalAlignment.Left;
        hint.Margin = new Thickness(control.MinWidth + 12, 0, 0, 0);
        return grid;
    }

    private static UIElement PlainRow(FrameworkElement control)
    {
        var grid = new Grid { MinHeight = 40, Width = UiMetrics.ContentWidth };
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(ControlLeft) });
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

    private static TextBlock Section(string text) => new()
    {
        Text = text,
        FontWeight = FontWeights.Bold,
        Margin = new Thickness(0, 6, 0, 2),
        Foreground = FindBrush("Brush.Text")
    };

    private static TextBlock MakeMutedLabel(string text) => new()
    {
        Text = text,
        TextWrapping = TextWrapping.Wrap,
        Foreground = FindBrush("Brush.MutedText"),
        Margin = new Thickness(0, 2, 0, 2)
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

    private static void SetupCombo(System.Windows.Controls.ComboBox combo, IEnumerable<string> values)
    {
        if (combo.Items.Count == 0)
        {
            foreach (var value in values) combo.Items.Add(value);
        }
    }

    private static int ClosestIndex(int[] values, int target)
    {
        var best = 0;
        var bestDelta = int.MaxValue;
        for (var i = 0; i < values.Length; i++)
        {
            var delta = Math.Abs(values[i] - target);
            if (delta < bestDelta)
            {
                bestDelta = delta;
                best = i;
            }
        }
        return best;
    }

    private static int ClosestIndex(double[] values, double target)
    {
        var best = 0;
        var bestDelta = double.MaxValue;
        for (var i = 0; i < values.Length; i++)
        {
            var delta = Math.Abs(values[i] - target);
            if (delta < bestDelta)
            {
                bestDelta = delta;
                best = i;
            }
        }
        return best;
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

    private static Brush FindBrush(string key) => (Brush)Application.Current.Resources[key];
}
