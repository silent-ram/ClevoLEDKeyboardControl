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

    private readonly System.Windows.Controls.RadioButton _darkThemeRadio = MakeRadio("深色仪器风");
    private readonly System.Windows.Controls.RadioButton _lightThemeRadio = MakeRadio("浅色工作台");
    private readonly List<(Button Swatch, int AccentArgb)> _accentSwatches = [];
    private readonly Button _followKeyboard = MakeButton("跟随键盘主色", 136);
    private readonly TextBlock _accentSummary = MakeMutedLabel("");
    private int _accentMode = AccentDefault;
    private bool _updatingAppearance;

    private readonly System.Windows.Controls.ComboBox _updateInterval = MakeCombo(["从不", "每天", "每周", "每月"]);
    private readonly TextBlock _updateAvailable = new()
    {
        Foreground = (Brush)Application.Current.Resources["Brush.Error"],
        Cursor = System.Windows.Input.Cursors.Hand,
        Margin = new Thickness(0, 0, 0, 4),
        Visibility = Visibility.Collapsed
    };
    private string? _updateReleaseUrl;
    private int _checkingUpdates;

    private readonly System.Windows.Controls.CheckBox _userImprovementPlanEnabled = MakeCheck("参与用户改进计划");
    private static readonly TextBlock UserPlanDescription = MakeMutedLabel(
        "用于了解不同版本的实际使用情况，便于安排维护和更新。统计数据匿名且不含个人信息。");

    private readonly System.Windows.Controls.CheckBox _startupEnabled = MakeCheck("开机自启动托盘与灯控服务(切换后立即生效)");
    private readonly TextBlock _startupState = MakeMutedLabel("");
    private bool _applyingStartup;

    private readonly TextBlock _configPath = MakeMutedLabel(AppPaths.SettingsPath);
    private readonly Button _export = MakeButton("导出配置...", 140);
    private readonly Button _import = MakeButton("导入配置...", 140);
    private readonly Button _restoreBackup = MakeButton("恢复最近备份", 150);
    private readonly Button _openFolder = MakeButton("打开配置目录", 150);
    private readonly Button _reset = MakeButton("恢复默认设置", 150);

    public event EventHandler? SettingsChangedExternally;
    public event EventHandler? NavigationBadgeChanged;

    public SoftwareSettingsPage()
    {
        _darkThemeRadio.Checked += (_, _) => SelectThemeKind(UiThemeKind.Windows11);
        _lightThemeRadio.Checked += (_, _) => SelectThemeKind(UiThemeKind.Technology);
        _followKeyboard.Click += (_, _) => SelectAccent(AccentFollowKeyboard);

        _updateInterval.SelectionChanged += (_, _) => MarkDirty();
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
            if (System.Windows.MessageBox.Show("确定恢复默认设置？", "ClevoLEDKeyboardControl",
                    MessageBoxButton.YesNo, MessageBoxImage.Question) != MessageBoxResult.Yes) return;
            new SettingsStore().Save(new KeyboardSettings());
            SettingsChangedExternally?.Invoke(this, EventArgs.Empty);
        };

        var stack = new StackPanel { Margin = new Thickness(18, 18, 18, 28), MaxWidth = 832, HorizontalAlignment = HorizontalAlignment.Left };
        stack.Children.Add(BuildAppearanceCard());
        stack.Children.Add(MakeCard("自动更新", Row("自动检查更新", _updateInterval), _updateAvailable));
        stack.Children.Add(MakeCard("用户改进计划", PlainRow(_userImprovementPlanEnabled), PlainRow(UserPlanDescription)));
        stack.Children.Add(MakeCard("开机自启动", PlainRow(_startupEnabled), PlainRow(_startupState)));
        stack.Children.Add(MakeCard("配置管理",
            ButtonRow(_export, _import, _restoreBackup),
            Row("配置文件", _configPath),
            ButtonRow(_openFolder, _reset)));

        Content = new ScrollViewer
        {
            Content = stack,
            VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
            HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled
        };
    }

    private Border BuildAppearanceCard()
    {
        var hint = MakeMutedLabel("主题立即生效并记住；强调色应用于按钮、导航选中态与链接。");
        _darkThemeRadio.Margin = new Thickness(0, 0, 24, 0);

        var themeRow = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 4, 0, 4) };
        themeRow.Children.Add(_darkThemeRadio);
        themeRow.Children.Add(_lightThemeRadio);

        var accentRow = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 4, 0, 4) };
        accentRow.Children.Add(new TextBlock
        {
            Text = "强调色",
            Width = 60,
            VerticalAlignment = VerticalAlignment.Center,
            Foreground = FindBrush("Brush.Text")
        });
        AddAccentSwatch(accentRow, AccentDefault, "默认");
        AddAccentSwatch(accentRow, ArgbOf(56, 200, 240), "青");
        AddAccentSwatch(accentRow, ArgbOf(224, 92, 140), "玫红");
        AddAccentSwatch(accentRow, ArgbOf(74, 222, 128), "翠绿");
        _followKeyboard.Margin = new Thickness(2, 0, 10, 0);
        accentRow.Children.Add(_followKeyboard);

        return MakeCard("外观", hint, themeRow, accentRow, _accentSummary);
    }

    // ---- 载入 / 保存 ----

    public void LoadFromStore(KeyboardSettings settings)
    {
        _updatingAppearance = true;
        try
        {
            _accentMode = UiStateStore.Shared.Load().AccentArgb;
            _darkThemeRadio.IsChecked = WpfThemeManager.CurrentKind == UiThemeKind.Windows11;
            _lightThemeRadio.IsChecked = WpfThemeManager.CurrentKind != UiThemeKind.Windows11;
            UpdateAccentSwatches();
            _updateInterval.SelectedIndex = UpdateIntervalToIndex(settings.Update.CheckInterval);
            _userImprovementPlanEnabled.IsChecked = settings.UserImprovementPlan.Enabled;
        }
        finally
        {
            _updatingAppearance = false;
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
        // 更新频率/参与计划走"保存并应用"（由宿主 SaveSettings 持久化），这里只通知宿主刷新保存栏。
        SettingsChangedExternally?.Invoke(this, EventArgs.Empty);
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
            foreach (var (swatch, accentArgb) in _accentSwatches)
            {
                var baseColor = accentArgb == AccentDefault ? defaultAccent : Color.FromArgb(255, (byte)((accentArgb >> 16) & 0xFF), (byte)((accentArgb >> 8) & 0xFF), (byte)(accentArgb & 0xFF));
                swatch.Background = new SolidColorBrush(baseColor);
                var selected = accentArgb == _accentMode;
                swatch.BorderBrush = FindBrush(selected ? "Brush.Primary" : "Brush.Border");
                swatch.BorderThickness = new Thickness(selected ? 2 : 1);
            }
            _accentSummary.Text = $"当前强调色：{_accentMode switch
            {
                AccentDefault => "默认",
                AccentFollowKeyboard => "跟随键盘主色",
                _ => $"#{_accentMode & 0xFFFFFF:X6}"
            }}";
        }
        finally
        {
            _updatingAppearance = false;
        }
    }

    private void AddAccentSwatch(StackPanel row, int accentArgb, string name)
    {
        var swatch = new Button
        {
            Width = 34,
            Height = 26,
            Style = (Style)Application.Current.Resources["UiButton"],
            Margin = new Thickness(0, 0, 10, 0),
            Focusable = false,
            Tag = name
        };
        swatch.Click += (_, _) => SelectAccent(accentArgb);
        _accentSwatches.Add((swatch, accentArgb));
        row.Children.Add(swatch);
    }

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
                ? $"发现新版本 v{result!.LatestVersion!.ToString(3)}，点击打开下载页面"
                : "";
            _updateAvailable.Visibility = available ? Visibility.Visible : Visibility.Collapsed;
            NavigationBadgeChanged?.Invoke(this, EventArgs.Empty);
        });
    }

    /// <summary>导航徽标文本（供宿主设置页签上的"新版本"提示）。</summary>
    public string? UpdateBadge => _updateAvailable.Visibility == Visibility.Visible ? "新版本" : null;

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
                System.Windows.MessageBox.Show($"无法修改开机自启动:{error}", "ClevoLEDKeyboardControl",
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
        _startupState.Text = $"托盘自启:{(trayRegistered ? "已注册" : "未注册")} · 灯效服务:{(serviceAuto ? "自动" : "手动")}";
    }

    // ---- 配置管理 ----

    private void ExportConfiguration()
    {
        var dialog = new Microsoft.Win32.SaveFileDialog
        {
            Filter = "JSON 配置|*.json",
            FileName = $"ClevoLEDKeyboardControl-{DateTime.Now:yyyyMMdd-HHmmss}.json"
        };
        if (dialog.ShowDialog(Window.GetWindow(this)) != true) return;
        var options = new System.Text.Json.JsonSerializerOptions { WriteIndented = true };
        options.Converters.Add(new System.Text.Json.Serialization.JsonStringEnumConverter());
        File.WriteAllText(dialog.FileName, System.Text.Json.JsonSerializer.Serialize(new SettingsStore().Load(), options));
    }

    private void ImportConfiguration()
    {
        var dialog = new Microsoft.Win32.OpenFileDialog { Filter = "JSON 配置|*.json", CheckFileExists = true };
        if (dialog.ShowDialog(Window.GetWindow(this)) != true) return;
        var json = File.ReadAllText(dialog.FileName);
        if (!SettingsStore.TryParse(json, out var settings, out var error))
        {
            System.Windows.MessageBox.Show($"配置验证失败，当前设置未改变：{error}", "导入配置",
                MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }
        new SettingsStore().Save(settings);
        SettingsChangedExternally?.Invoke(this, EventArgs.Empty);
        System.Windows.MessageBox.Show("配置已验证并导入。", "导入配置", MessageBoxButton.OK, MessageBoxImage.Information);
    }

    private void RestoreLastGood()
    {
        var result = new SettingsStore().RestoreLastGood();
        if (result.Success)
        {
            SettingsChangedExternally?.Invoke(this, EventArgs.Empty);
        }
        System.Windows.MessageBox.Show(
            result.Success ? "已恢复到最近的良好配置。" : $"恢复失败：{result.Message}",
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
        grid.Children.Add(new TextBlock { Text = label, VerticalAlignment = VerticalAlignment.Center, Foreground = FindBrush("Brush.Text") });
        control.VerticalAlignment = VerticalAlignment.Center;
        Grid.SetColumn(control, 1);
        grid.Children.Add(control);
        return grid;
    }

    private static UIElement PlainRow(FrameworkElement control)
    {
        var grid = new Grid { MinHeight = 40, Width = UiMetrics.ContentWidth };
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

    private static System.Windows.Controls.RadioButton MakeRadio(string text) => new()
    {
        Content = text,
        Style = (Style)Application.Current.Resources["UiRadioButton"],
        GroupName = "UiTheme"
    };

    private static System.Windows.Controls.CheckBox MakeCheck(string text) => new()
    {
        Content = text,
        Style = (Style)Application.Current.Resources["UiCheckBox"]
    };

    private static System.Windows.Controls.ComboBox MakeCombo(params string[] items)
    {
        var combo = new System.Windows.Controls.ComboBox
        {
            Style = (Style)Application.Current.Resources["UiComboBox"],
            MinWidth = 240
        };
        foreach (var item in items) combo.Items.Add(item);
        return combo;
    }

    private static Button MakeButton(string text, double minWidth = 112) => new()
    {
        Content = text,
        Style = (Style)Application.Current.Resources["UiButton"],
        MinWidth = minWidth
    };

    private static TextBlock MakeMutedLabel(string text) => new()
    {
        Text = text,
        TextWrapping = TextWrapping.Wrap,
        Foreground = FindBrush("Brush.MutedText")
    };

    private static Brush FindBrush(string key) => (Brush)Application.Current.Resources[key];
}
