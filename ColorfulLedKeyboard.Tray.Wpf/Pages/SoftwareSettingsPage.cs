using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using ColorfulLedKeyboard.Core;

namespace ColorfulLedKeyboard.Tray.Wpf.Pages;

/// <summary>软件设置页：外观（主题/强调色）、自动更新、用户改进计划、开机自启动、配置管理。</summary>
public sealed class SoftwareSettingsPage : UserControl
{
    private const int AccentDefault = UiState.AccentDefault;
    private const int AccentFollowKeyboard = UiState.AccentFollowKeyboard;

    private readonly System.Windows.Controls.RadioButton _darkThemeRadio = MakeRadio("set.theme.dark");
    private readonly System.Windows.Controls.RadioButton _lightThemeRadio = MakeRadio("set.theme.light");
    private readonly List<(Border Chip, int AccentArgb, string Name)> _accentSwatches = [];
    private List<int> _hiddenPresets = [];
    private Button? _customSwatch;

    /// <summary>内置强调色预设（默认色板之外）。</summary>
    private static readonly (string Key, int Argb)[] PresetAccents =
    [
        ("set.accent.red",   ArgbConst(229, 72, 77)),
        ("set.accent.orange", ArgbConst(247, 107, 21)),
        ("set.accent.cyan",  ArgbConst(56, 200, 240)),
        ("set.accent.blue",  ArgbConst(76, 125, 255)),
        ("set.accent.green", ArgbConst(74, 222, 128)),
        ("set.accent.purple", ArgbConst(154, 92, 232)),
        ("set.accent.rose",  ArgbConst(224, 92, 140)),
    ];

    private static string AccentName(string key) => AppStrings.Get(key);

    private static int ArgbConst(byte r, byte g, byte b) =>
        unchecked((int)(0xFF000000u | ((uint)r << 16) | ((uint)g << 8) | b));

    /// <summary>强调色 ARGB → 界面画刷；默认槽位随主题取对应调色板色。</summary>
    private SolidColorBrush ColorFor(int argb)
    {
        var color = argb == AccentDefault
            ? WpfThemeManager.DefaultAccent
            : Color.FromArgb(0xFF, (byte)((argb >> 16) & 0xFF), (byte)((argb >> 8) & 0xFF), (byte)(argb & 0xFF));
        return new SolidColorBrush(color);
    }
    private readonly ContentControl _accentRowHost = new();
    private List<int> _customAccents = [];
    private Border? _currentSwatch;
    private readonly TextBlock _accentSummary = MakeMutedLabel("");
    private int _accentMode = AccentDefault;
    private readonly UiStateStore _uiStateStore = UiStateStore.Shared;
    private bool _updatingAppearance;
    private readonly List<Border> _closeBadges = [];

    /// <summary>截图验收专用：强制显示所有色块 ✕ 徽标（模拟悬停态）。</summary>
    public void ShowSwatchCloseBadgesForCapture()
    {
        foreach (var badge in _closeBadges) badge.Visibility = Visibility.Visible;
    }

    private static readonly string[] UpdateIntervalKeys = ["set.update.never", "set.update.daily", "set.update.weekly", "set.update.monthly"];
    private readonly System.Windows.Controls.ComboBox _updateInterval = MakeCombo(UpdateIntervalKeys);
    private readonly TextBlock _updateAvailable = new()
    {
        Cursor = System.Windows.Input.Cursors.Hand,
        Margin = new Thickness(0, 0, 0, 4),
        Visibility = Visibility.Collapsed
    };
    private string? _updateReleaseUrl;
    private int _checkingUpdates;

    private readonly System.Windows.Controls.CheckBox _userImprovementPlanEnabled = MakeCheck("set.telemetry.enable");
    // 注意必须是实例字段：静态 UI 元素在 RebuildPagesForTheme 重建页面时会被
    // 第二次加入逻辑树，WPF 抛"元素已是另一个元素的逻辑子元素"直接闪退。
    private readonly TextBlock _userPlanDescription = MakeMutedLabel(
        AppStrings.Get("set.telemetry.desc"));

    private readonly System.Windows.Controls.CheckBox _startupEnabled = MakeCheck("set.startup.enable");
    private readonly TextBlock _startupState = MakeMutedLabel("");
    private bool _applyingStartup;

    private readonly TextBlock _configPath = MakeMutedLabel(AppPaths.SettingsPath);
    private readonly Button _export = MakeButton("set.config.export", 140);
    private readonly Button _import = MakeButton("set.config.import", 140);
    private readonly Button _restoreBackup = MakeButton("set.config.restore", 150);
    private readonly Button _openFolder = MakeButton("set.config.openFolder", 150);
    private readonly Button _reset = MakeButton("set.config.reset", 150);

    /// <summary>导入/恢复备份/恢复默认等外部配置变更，宿主应整体重载。</summary>
    public event EventHandler? SettingsChangedExternally;
    /// <summary>用户编辑了"保存并应用"语义的字段，宿主应刷新保存栏。</summary>
    public event EventHandler? Changed;
    public event EventHandler? NavigationBadgeChanged;

    public bool IsDirty { get; private set; }

    /// <summary>保存成功后由宿主调用，复位脏状态。</summary>
    public void ResetDirty() => IsDirty = false;

    /// <summary>窗口关闭时由宿主调用，退订静态事件。</summary>
    public void UnsubscribeEvents() => StartupManager.StartupChanged -= OnStartupChanged;

    public SoftwareSettingsPage()
    {
        _darkThemeRadio.Checked += (_, _) => SelectThemeKind(UiThemeKind.Windows11);
        _lightThemeRadio.Checked += (_, _) => SelectThemeKind(UiThemeKind.Technology);

        _updateInterval.SelectionChanged += (_, _) => MarkDirty();
        _userImprovementPlanEnabled.Checked += (_, _) => MarkDirty();
        _userImprovementPlanEnabled.Unchecked += (_, _) => MarkDirty();
        _updateAvailable.MouseLeftButtonDown += (_, _) =>
        {
            if (!string.IsNullOrWhiteSpace(_updateReleaseUrl)) UpdateChecker.OpenUrl(_updateReleaseUrl);
        };

        // 开机自启动是即时生效的系统开关，不参与"保存并应用"（WinForms 语义一致）。
        _startupEnabled.Checked += (_, _) => ApplyStartup();
        _startupEnabled.Unchecked += (_, _) => ApplyStartup();
        StartupManager.StartupChanged += OnStartupChanged;

        _export.Click += (_, _) => ExportConfiguration();
        _import.Click += (_, _) => ImportConfiguration();
        _restoreBackup.Click += (_, _) => RestoreLastGood();
        _openFolder.Click += (_, _) =>
        {
            Directory.CreateDirectory(AppPaths.ProgramDataDirectory);
            System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo("explorer.exe", AppPaths.ProgramDataDirectory) { UseShellExecute = true });
        };
        _reset.Click += (_, _) =>
        {
            if (System.Windows.MessageBox.Show(AppStrings.Get("set.config.resetConfirm"), "ClevoLEDKeyboardControl",
                    MessageBoxButton.YesNo, MessageBoxImage.Question) != MessageBoxResult.Yes) return;
            new SettingsStore().Save(new KeyboardSettings());
            SettingsChangedExternally?.Invoke(this, EventArgs.Empty);
        };

        var stack = new StackPanel { Margin = new Thickness(18, 18, 18, 28), MaxWidth = 832 };
        stack.Children.Add(BuildAppearanceCard());
        stack.Children.Add(MakeCard("set.card.update", Row("set.row.autoCheck", _updateInterval), _updateAvailable));
        stack.Children.Add(MakeCard("set.card.telemetry", PlainRow(_userImprovementPlanEnabled), PlainRow(_userPlanDescription)));
        stack.Children.Add(MakeCard("set.card.startup", PlainRow(_startupEnabled), PlainRow(_startupState)));
        stack.Children.Add(MakeCard("set.card.config",
            ButtonRow(_export, _import, _restoreBackup),
            Row("set.row.configFile", _configPath),
            ButtonRow(_openFolder, _reset)));

        var scroll = new ScrollViewer
        {
            Content = stack,
            VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
            HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled
        };
        scroll.SetResourceReference(StyleProperty, "DarkScrollViewer");
        Content = scroll;
    }

    private Border BuildAppearanceCard()
    {
        var hint = MakeMutedLabel(AppStrings.Get("set.hint.appearance"));
        _darkThemeRadio.Margin = new Thickness(0, 0, 24, 0);

        var themeRow = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 4, 0, 4) };
        themeRow.Children.Add(_darkThemeRadio);
        themeRow.Children.Add(_lightThemeRadio);

        var accentRow = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 4, 0, 4) };
        var accentLabel = new TextBlock
        {
            Text = AppStrings.Get("set.row.accent"),
            Width = 60,
            VerticalAlignment = VerticalAlignment.Center
        };
        accentLabel.SetResourceReference(TextBlock.ForegroundProperty, "Brush.Text");
        accentRow.Children.Add(accentLabel);
        _accentRowHost.Content = BuildAccentRow();
        var languageRow = BuildLanguageRow();
        return MakeCard("set.card.appearance", hint, themeRow, languageRow, _accentRowHost, _accentSummary);
    }

    // ---- 载入 / 保存 ----

    private bool _loadingSettings;

    public void LoadFromStore(KeyboardSettings settings)
    {
        _loadingSettings = true;
        _updatingAppearance = true;
        try
        {
            var uiState = UiStateStore.Shared.Load();
            _accentMode = uiState.AccentArgb == UiState.AccentFollowKeyboard ? AccentDefault : uiState.AccentArgb;
            _customAccents = uiState.CustomAccents.ToList();
            _hiddenPresets = uiState.HiddenAccentPresets.ToList();
            // 旧版（无自定义列表时代）选过的自定义色自动迁入列表，保证色板上有对应色块
            if (_accentMode != AccentDefault && _accentMode != UiState.AccentFollowKeyboard &&
                PresetAccents.All(p => p.Argb != _accentMode) && !_customAccents.Contains(_accentMode))
            {
                _customAccents.Add(_accentMode);
            }
            _darkThemeRadio.IsChecked = WpfThemeManager.CurrentKind == UiThemeKind.Windows11;
            _lightThemeRadio.IsChecked = WpfThemeManager.CurrentKind != UiThemeKind.Windows11;
            RebuildAccentRow();
            _updateInterval.SelectedIndex = UpdateIntervalToIndex(settings.Update.CheckInterval);
            _userImprovementPlanEnabled.IsChecked = settings.UserImprovementPlan.Enabled;
            IsDirty = false;
        }
        finally
        {
            _updatingAppearance = false;
            _loadingSettings = false;
        }
        RefreshStartupControls();
    }

    public void ApplyTo(KeyboardSettings settings)
    {
        if (_updateInterval.SelectedIndex >= 0)
        {
            settings.Update.CheckInterval = IndexToUpdateInterval(_updateInterval.SelectedIndex);
        }
        settings.UserImprovementPlan.Enabled = _userImprovementPlanEnabled.IsChecked == true;
    }

    private void MarkDirty()
    {
        if (_loadingSettings) return;
        // 更新频率/参与计划走"保存并应用"（由宿主 SaveSettings 持久化），这里只通知宿主刷新保存栏。
        IsDirty = true;
        Changed?.Invoke(this, EventArgs.Empty);
    }

    // ---- 外观（即时生效，独立持久化）----

    private void SelectThemeKind(UiThemeKind kind)
    {
        if (_updatingAppearance || WpfThemeManager.CurrentKind == kind) return;
        WpfThemeManager.SetTheme(kind);
        UiStateStore.Shared.Update(state => state.Theme = kind);
    }

    private void SelectAccent(int accentArgb)
    {
        _accentMode = accentArgb;
        UiStateStore.Shared.Update(state => state.AccentArgb = accentArgb);
        WpfThemeManager.AccentOverride = WpfThemeManager.ResolveAccent(accentArgb);
        UpdateAccentSwatches();
    }

    private void UpdateAccentSwatches()
    {
        _updatingAppearance = true;
        try
        {
            var defaultAccent = WpfThemeManager.DefaultAccent;
            if (_currentSwatch is not null)
            {
                _currentSwatch.Background = new SolidColorBrush(
                    WpfThemeManager.AccentOverride ?? defaultAccent);
            }
            foreach (var (chip, accentArgb, _) in _accentSwatches)
            {
                chip.Background = ColorFor(accentArgb);
                var selected = accentArgb == _accentMode;
                chip.SetResourceReference(Border.BorderBrushProperty, selected ? "Brush.Primary" : "Brush.Border");
                chip.BorderThickness = new Thickness(selected ? 2 : 1);
            }
            var presetKey = PresetAccents.FirstOrDefault(p => p.Argb == _accentMode).Key;
            _accentSummary.Text = $"{AppStrings.Get("set.accent.current")}：{_accentMode switch
            {
                AccentDefault => AppStrings.Get("set.accent.default"),
                _ when presetKey is not null => AccentName(presetKey),
                _ => AppStrings.Format("set.accent.custom", $"{_accentMode & 0xFFFFFF:X6}")
            }}";
        }
        finally
        {
            _updatingAppearance = false;
        }
    }

    private void RebuildAccentRow()
    {
        _accentRowHost.Content = BuildAccentRow();
        UpdateAccentSwatches();
    }

    private StackPanel BuildAccentRow()
    {
        _accentSwatches.Clear();
        var accentRow = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 4, 0, 4) };
        var accentLabel = new TextBlock
        {
            Text = AppStrings.Get("set.row.accent"),
            Width = 60,
            VerticalAlignment = VerticalAlignment.Center
        };
        accentLabel.SetResourceReference(TextBlock.ForegroundProperty, "Brush.Text");
        accentRow.Children.Add(accentLabel);
        // 第一个格子恒显当前生效的强调色（实时预览，不可点击）
        _currentSwatch = new Border
        {
            Width = 34,
            Height = 26,
            CornerRadius = new CornerRadius(5),
            Background = new SolidColorBrush(WpfThemeManager.AccentOverride ?? WpfThemeManager.DefaultAccent),
            BorderThickness = new Thickness(2),
            Margin = new Thickness(0, 0, 10, 0),
            IsHitTestVisible = false,
            ToolTip = AppStrings.Get("set.accent.current")
        };
        _currentSwatch.SetResourceReference(Border.BorderBrushProperty, "Brush.Primary");
        accentRow.Children.Add(_currentSwatch);
        AddAccentSwatch(accentRow, AccentDefault, AppStrings.Get("set.accent.default"));
        foreach (var (name, argb) in PresetAccents)
        {
            if (_hiddenPresets.Contains(argb)) continue;
            AddAccentSwatch(accentRow, argb, name);
        }
        foreach (var custom in _customAccents)
        {
            AddCustomSwatch(accentRow, custom);
        }

        var add = new Button
        {
            Width = 34,
            Height = 26,
            // 样式自带 Padding=12,5 会在 26px 高的按钮里把 ＋ 字形裁掉，归零后靠 ContentPresenter 居中
            Padding = new Thickness(0),
            Margin = new Thickness(0, 0, 10, 0),
            Content = "＋",
            FontSize = 15,
            ToolTip = AppStrings.Get("set.tooltip.custom")
        };
        add.SetResourceReference(StyleProperty, "UiButton");
        add.SetResourceReference(Control.ForegroundProperty, "Brush.Text");
        add.Click += (_, _) => OpenCustomAccentPicker();
        accentRow.Children.Add(add);
        return accentRow;
    }

    /// <summary>自定义色块：与预设芯片同款，✕ 删除走 RemoveCustomAccent。</summary>
    private void AddCustomSwatch(StackPanel row, int accentArgb)
    {
        var (container, _) = BuildColorChip(accentArgb, $"#{accentArgb & 0xFFFFFF:X6}", deletable: true,
            delete: () => RemoveCustomAccent(accentArgb));
        row.Children.Add(container);
    }

    private void AddAccentSwatch(StackPanel row, int accentArgb, string name)
    {
        bool deletable = accentArgb != AccentDefault;
        var (container, chip) = BuildColorChip(accentArgb, name, deletable,
            delete: () => HidePresetAccent(accentArgb));
        row.Children.Add(container);
        _accentSwatches.Add((chip, accentArgb, name));
    }

    /// <summary>色块芯片：纯色 Border（无按钮 chrome，悬停保持原色），
    /// 可删除的色块悬停时右上角浮现 ✕。</summary>
    /// <summary>界面语言行：跟随系统 / 中文 / English，切换即写入 UiState 并触发全局刷新。</summary>
    private UIElement BuildLanguageRow()
    {
        var combo = new System.Windows.Controls.ComboBox
        {
            Width = 280,
            HorizontalAlignment = System.Windows.HorizontalAlignment.Left
        };
        combo.SetResourceReference(StyleProperty, "UiComboBox");
        var items = new[]
        {
            (AppStrings.Auto, "跟随系统", "Follow system"),
            (AppStrings.ZhCn, "中文（简体）", "中文（简体）"),
            (AppStrings.EnUs, "English", "English"),
        };
        foreach (var (_, zh, en) in items)
        {
            combo.Items.Add(AppStrings.IsEnglish ? en : zh);
        }
        var current = _uiStateStore.Load().Language;
        combo.SelectedIndex = current switch
        {
            AppStrings.ZhCn => 1,
            AppStrings.EnUs => 2,
            _ => 0
        };
        var suppress = false;
        combo.SelectionChanged += (_, _) =>
        {
            if (suppress) return;
            var chosen = combo.SelectedIndex switch
            {
                1 => AppStrings.ZhCn,
                2 => AppStrings.EnUs,
                _ => AppStrings.Auto
            };
            if (chosen == _uiStateStore.Load().Language) return;
            suppress = true;
            try { combo.SelectedItem = combo.SelectedItem; } finally { suppress = false; }
            _uiStateStore.Update(state => state.Language = chosen);
            AppStrings.Apply(chosen);
        };
        return Row("set.row.language", combo);
    }

    private (FrameworkElement Container, Border Chip) BuildColorChip(int accentArgb, string name, bool deletable, System.Action delete)
    {
        var container = new Grid { Width = 34, Height = 26, Margin = new Thickness(0, 0, 10, 0) };
        var chip = new Border
        {
            CornerRadius = new CornerRadius(5),
            Background = ColorFor(accentArgb),
            BorderThickness = new Thickness(_accentMode == accentArgb ? 2 : 1),
            ToolTip = name,
            Cursor = System.Windows.Input.Cursors.Hand
        };
        chip.SetResourceReference(Border.BorderBrushProperty, _accentMode == accentArgb ? "Brush.Primary" : "Brush.Border");
        chip.MouseLeftButtonDown += (_, _) => SelectAccent(accentArgb);
        container.Children.Add(chip);

        if (deletable)
        {
            // ✕ 徽标：圆底 + 描边 + 主题前景，任何色块/主题下都有对比度
            //（曾因直接继承默认前景色，深色主题下黑 ✕ 不可见）
            var closeText = new TextBlock
            {
                Text = "✕",
                FontSize = 8,
                FontWeight = FontWeights.Bold,
                HorizontalAlignment = HorizontalAlignment.Center,
                VerticalAlignment = VerticalAlignment.Center
            };
            closeText.SetResourceReference(TextBlock.ForegroundProperty, "Brush.Text");
            var close = new Border
            {
                Width = 14,
                Height = 14,
                CornerRadius = new CornerRadius(7),
                BorderThickness = new Thickness(1),
                HorizontalAlignment = HorizontalAlignment.Right,
                VerticalAlignment = VerticalAlignment.Top,
                Margin = new Thickness(0, -6, -5, 0),
                Cursor = System.Windows.Input.Cursors.Hand,
                Visibility = Visibility.Hidden,
                Child = closeText
            };
            close.SetResourceReference(Border.BackgroundProperty, "Brush.Window");
            close.SetResourceReference(Border.BorderBrushProperty, "Brush.Border");
            close.MouseLeftButtonDown += (_, _) => delete();
            container.Children.Add(close);
            _closeBadges.Add(close);
            container.MouseEnter += (_, _) => close.Visibility = Visibility.Visible;
            container.MouseLeave += (_, _) => close.Visibility = Visibility.Hidden;
            container.ToolTip = AppStrings.Format("set.tooltip.remove", name);
        }
        return (container, chip);
    }

    private void HidePresetAccent(int argb)
    {
        _hiddenPresets.Add(argb);
        _uiStateStore.Update(state => state.HiddenAccentPresets = [.. _hiddenPresets]);
        if (_accentMode == argb) SelectAccent(AccentDefault);
        RebuildAccentRow();
    }

    /// <summary>自定义强调色：复用全局取色器，单选模式。</summary>
    private void OpenCustomAccentPicker()
    {
        string initial;
        if (_accentMode == AccentDefault || PresetAccents.Any(preset => preset.Argb == _accentMode))
        {
            var d = WpfThemeManager.DefaultAccent;
            initial = $"#{d.R:X2}{d.G:X2}{d.B:X2}";
        }
        else
        {
            initial = $"#{_accentMode & 0xFFFFFF:X6}";
        }

        var dialog = new Dialogs.ColorSelectionDialog(new List<string> { initial }, singleSelection: true)
        {
            Owner = Window.GetWindow(this)
        };
        if (dialog.ShowDialog() != true) return;
        var hex = dialog.SelectedColors.FirstOrDefault();
        if (string.IsNullOrWhiteSpace(hex)) return;
        if (Controls.UiColorPickerRow.TryParse(hex, out var rgb))
        {
            AddCustomAccent(ArgbOf(rgb.R, rgb.G, rgb.B));
        }
    }

    /// <summary>添加自定义强调色：入库、持久化、选中并重建色板行。</summary>
    private void AddCustomAccent(int argb)
    {
        if (!_customAccents.Contains(argb)) _customAccents.Add(argb);
        PersistCustomAccents();
        SelectAccent(argb);
        RebuildAccentRow();
    }

    private void RemoveCustomAccent(int argb)
    {
        _customAccents.Remove(argb);
        PersistCustomAccents();
        if (_accentMode == argb) SelectAccent(AccentDefault);
        RebuildAccentRow();
    }

    private void PersistCustomAccents() =>
        _uiStateStore.Update(state => state.CustomAccents = [.. _customAccents]);

    // ---- 更新检查（WinForms ApplyUpdateAvailability/CheckForUpdatesNowAsync 移植件）----

    public async System.Threading.Tasks.Task CheckForUpdatesNowAsync(Func<System.Threading.Tasks.Task<UpdateCheckResult?>>? checkSilently)
    {
        if (checkSilently is null || System.Threading.Interlocked.Exchange(ref _checkingUpdates, 1) != 0) return;
        try
        {
            var result = await checkSilently();
            ApplyUpdateAvailability(result);
        }
        catch
        {
        }
        finally
        {
            System.Threading.Interlocked.Exchange(ref _checkingUpdates, 0);
        }
    }

    private void ApplyUpdateAvailability(UpdateCheckResult? result)
    {
        Dispatcher.BeginInvoke(() =>
        {
            var available = result?.Status == UpdateCheckStatus.Available && result!.LatestVersion is not null;
            _updateReleaseUrl = available ? result!.ReleaseUrl : null;
            _updateAvailable.Text = available
                ? AppStrings.Format("set.update.badge", result!.LatestVersion!.ToString(3))
                : "";
            _updateAvailable.Visibility = available ? Visibility.Visible : Visibility.Collapsed;
            NavigationBadgeChanged?.Invoke(this, EventArgs.Empty);
        });
    }

    /// <summary>导航徽标文本（供宿主设置页签上的"新版本"提示）。</summary>
    public string? UpdateBadge => _updateAvailable.Visibility == Visibility.Visible ? AppStrings.Get("set.update.badgeShort") : null;

    // ---- 开机自启动（即时生效）----

    private async void ApplyStartup()
    {
        if (_applyingStartup) return;
        _applyingStartup = true;
        try
        {
            var enabled = _startupEnabled.IsChecked == true;
            var (ok, error) = await System.Threading.Tasks.Task.Run(() =>
            {
                var success = StartupManager.TrySetEnabled(enabled, out var message);
                return (success, message);
            });
            if (!ok)
            {
                System.Windows.MessageBox.Show(AppStrings.Format("set.startup.fail", error), "ClevoLEDKeyboardControl",
                    MessageBoxButton.OK, MessageBoxImage.Warning);
            }
        }
        finally
        {
            _applyingStartup = false;
        }
        RefreshStartupControls();
    }

    private void OnStartupChanged(object? sender, EventArgs e) =>
        Dispatcher.BeginInvoke(RefreshStartupControls);

    private void RefreshStartupControls()
    {
        var (trayRegistered, serviceAuto) = StartupManager.GetState();
        _applyingStartup = true;
        try
        {
            _startupEnabled.IsChecked = trayRegistered && serviceAuto;
        }
        finally
        {
            _applyingStartup = false;
        }
        var consistent = trayRegistered == serviceAuto;
        _startupState.Text = AppStrings.Format("set.startup.state",
            trayRegistered ? AppStrings.Get("set.startup.registered") : AppStrings.Get("set.startup.unregistered"),
            serviceAuto ? AppStrings.Get("set.startup.auto") : AppStrings.Get("set.startup.manual")) +
            (consistent ? "" : AppStrings.Get("set.startup.mismatch"));
    }

    // ---- 配置管理 ----

    private void ExportConfiguration()
    {
        var dialog = new Microsoft.Win32.SaveFileDialog
        {
            Filter = AppStrings.Get("set.config.filter"),
            FileName = $"ClevoLEDKeyboardControl-{DateTime.Now:yyyyMMdd-HHmmss}.json"
        };
        if (dialog.ShowDialog(Window.GetWindow(this)) != true) return;
        var options = new System.Text.Json.JsonSerializerOptions { WriteIndented = true };
        options.Converters.Add(new System.Text.Json.Serialization.JsonStringEnumConverter());
        File.WriteAllText(dialog.FileName, System.Text.Json.JsonSerializer.Serialize(new SettingsStore().Load(), options));
    }

    private void ImportConfiguration()
    {
        var dialog = new Microsoft.Win32.OpenFileDialog { Filter = AppStrings.Get("set.config.filter"), CheckFileExists = true };
        if (dialog.ShowDialog(Window.GetWindow(this)) != true) return;
        var json = File.ReadAllText(dialog.FileName);
        if (!SettingsStore.TryParse(json, out var settings, out var error))
        {
            System.Windows.MessageBox.Show(AppStrings.Format("set.config.importFail", error), AppStrings.Get("set.config.importTitle"),
                MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }
        new SettingsStore().Save(settings);
        SettingsChangedExternally?.Invoke(this, EventArgs.Empty);
        System.Windows.MessageBox.Show(AppStrings.Get("set.config.importOk"), AppStrings.Get("set.config.importTitle"), MessageBoxButton.OK, MessageBoxImage.Information);
    }

    private void RestoreLastGood()
    {
        var result = new SettingsStore().RestoreLastGood();
        if (result.Success)
        {
            SettingsChangedExternally?.Invoke(this, EventArgs.Empty);
        }
        System.Windows.MessageBox.Show(
            result.Success ? AppStrings.Get("diag.restore.done") : AppStrings.Format("diag.restore.failed", result.Message),
            "ClevoLEDKeyboardControl", MessageBoxButton.OK,
            result.Success ? MessageBoxImage.Information : MessageBoxImage.Warning);
    }

    private static int ArgbOf(byte r, byte g, byte b) => unchecked((int)(0xFF000000u | ((uint)r << 16) | ((uint)g << 8) | b));

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

    // ---- UI 辅助 ----

    private static Border MakeCard(string title, params UIElement[] children)
    {
        var stack = new StackPanel();
        var heading = new TextBlock
        {
            Text = title,
            FontSize = 13,
            FontWeight = FontWeights.Bold,
            Margin = new Thickness(0, 0, 0, 8)
        };
        heading.SetResourceReference(TextBlock.ForegroundProperty, "Brush.Text");
        stack.Children.Add(heading);
        foreach (var child in children) stack.Children.Add(child);
        var card = new Border { Child = stack };
        card.SetResourceReference(StyleProperty, "UiCard");
        return card;
    }

    private static UIElement Row(string label, FrameworkElement control)
    {
        var grid = new Grid { MinHeight = 40, MaxWidth = UiMetrics.ContentWidth };
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(130) });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        var labelBlock = new TextBlock { Text = label, VerticalAlignment = VerticalAlignment.Center };
        labelBlock.SetResourceReference(TextBlock.ForegroundProperty, "Brush.Text");
        grid.Children.Add(labelBlock);
        control.VerticalAlignment = VerticalAlignment.Center;
        Grid.SetColumn(control, 1);
        grid.Children.Add(control);
        return grid;
    }

    private static UIElement PlainRow(FrameworkElement control)
    {
        var grid = new Grid { MinHeight = 40, MaxWidth = UiMetrics.ContentWidth };
        control.VerticalAlignment = VerticalAlignment.Center;
        control.HorizontalAlignment = HorizontalAlignment.Left;
        grid.Children.Add(control);
        return grid;
    }

    private static UIElement ButtonRow(params Button[] buttons)
    {
        var panel = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            MinHeight = 40
        };
        foreach (var button in buttons)
        {
            button.Margin = new Thickness(0, 0, 10, 0);
            panel.Children.Add(button);
        }
        return panel;
    }

    private static System.Windows.Controls.RadioButton MakeRadio(string text)
    {
        var radio = new System.Windows.Controls.RadioButton { Content = text, GroupName = "UiTheme" };
        radio.SetResourceReference(StyleProperty, "UiRadioButton");
        return radio;
    }

    private static System.Windows.Controls.CheckBox MakeCheck(string text)
    {
        var check = new System.Windows.Controls.CheckBox { Content = text };
        check.SetResourceReference(StyleProperty, "UiCheckBox");
        return check;
    }

    private static System.Windows.Controls.ComboBox MakeCombo(params string[] items)
    {
        var combo = new System.Windows.Controls.ComboBox
        {
            Width = 280,
            HorizontalAlignment = HorizontalAlignment.Left
        };
        combo.SetResourceReference(StyleProperty, "UiComboBox");
        foreach (var item in items) combo.Items.Add(item);
        return combo;
    }

    private static Button MakeButton(string text, double minWidth = 112)
    {
        var button = new Button { Content = text, MinWidth = minWidth };
        button.SetResourceReference(StyleProperty, "UiButton");
        return button;
    }

    private static TextBlock MakeMutedLabel(string text)
    {
        var block = new TextBlock
        {
            Text = text,
            TextWrapping = TextWrapping.Wrap
        };
        block.SetResourceReference(TextBlock.ForegroundProperty, "Brush.MutedText");
        return block;
    }
}
