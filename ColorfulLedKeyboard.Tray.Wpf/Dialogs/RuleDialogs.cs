using System.Diagnostics;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using ColorfulLedKeyboard.Core;

namespace ColorfulLedKeyboard.Tray.Wpf.Dialogs;

/// <summary>自动化规则编辑（音乐/灯效/时间计划三态；WinForms AutomationRuleDialog 移植件）。</summary>
public sealed class AutomationRuleDialog : Window
{
    private enum RuleKind { Music, Lighting, Schedule }

    private static readonly EffectType[] EffectTypes =
    [
        EffectType.Static, EffectType.Rainbow, EffectType.Breathing, EffectType.Sequence, EffectType.Pulse, EffectType.Heartbeat
    ];
    private static readonly DayOfWeek[] DayValues =
    [
        DayOfWeek.Monday, DayOfWeek.Tuesday, DayOfWeek.Wednesday, DayOfWeek.Thursday,
        DayOfWeek.Friday, DayOfWeek.Saturday, DayOfWeek.Sunday
    ];
    private static readonly string[] DayLabels = ["周一", "周二", "周三", "周四", "周五", "周六", "周日"];

    private readonly RuleKind _kind;
    private readonly MusicApplicationRule? _musicRule;
    private readonly LightingApplicationRule? _lightingRule;
    private readonly AutomationScheduleRule? _scheduleRule;
    private readonly EffectPresetSettings _effects;
    private readonly List<MusicPreset> _musicPresets;

    private readonly System.Windows.Controls.TextBox _name = MakeTextBox();
    private readonly System.Windows.Controls.CheckBox _enabled = MakeCheck("启用规则");
    private readonly System.Windows.Controls.TextBox _process = MakeTextBox();
    private readonly System.Windows.Controls.CheckBox _includeChildren = MakeCheck("包含子进程");
    private readonly System.Windows.Controls.CheckBox _timeEnabled = MakeCheck("限制时间");
    private readonly System.Windows.Controls.ComboBox _start = MakeTimeCombo();
    private readonly System.Windows.Controls.ComboBox _end = MakeTimeCombo();
    private readonly System.Windows.Controls.CheckBox[] _days;
    private readonly System.Windows.Controls.ComboBox _target = MakeCombo();
    private readonly System.Windows.Controls.ComboBox _effectType = MakeCombo();
    private readonly System.Windows.Controls.ComboBox _preset = MakeCombo();
    private readonly System.Windows.Controls.ComboBox _colorSource = MakeCombo(["预设颜色", "封面主色", "封面配色"]);
    private readonly System.Windows.Controls.ComboBox _mediaSession = MakeCombo();
    private readonly System.Windows.Controls.CheckBox _brightnessEnabled = MakeCheck("限制最大亮度");
    private readonly System.Windows.Controls.TextBox _brightness = MakeTextBox();
    private readonly System.Windows.Controls.ComboBox _typing = MakeCombo(["继承全局", "强制开启", "强制关闭"]);
    private readonly System.Windows.Controls.ComboBox _notification = MakeCombo(["继承全局", "强制开启", "强制关闭"]);
    private readonly List<UIElement> _musicOnly = [];
    private readonly List<UIElement> _notSchedule = [];
    private bool _loading;

    private AutomationRuleDialog(RuleKind kind, object rule, EffectPresetSettings effects, IEnumerable<MusicPreset> music)
    {
        _kind = kind;
        _musicRule = rule as MusicApplicationRule;
        _lightingRule = rule as LightingApplicationRule;
        _scheduleRule = rule as AutomationScheduleRule;
        _effects = effects;
        _musicPresets = music.ToList();

        Title = "编辑自动化规则";
        WindowStartupLocation = WindowStartupLocation.CenterOwner;
        Width = 660;
        Height = 700;
        Background = (Brush)Application.Current.Resources["Brush.Window"];
        FontFamily = (FontFamily)Application.Current.Resources["Font.Body"];
        FontSize = 12;
        Foreground = (Brush)Application.Current.Resources["Brush.Text"];
        SourceInitialized += (_, _) => WpfThemeManager.ApplyTitleBarMode(this);

        var stack = new StackPanel { Margin = new Thickness(14) };
        stack.Children.Add(Row("名称", _name));
        stack.Children.Add(PlainRow(_enabled));
        stack.Children.Add(Row("进程名", _process));
        _musicOnly.Add(PlainRow(_includeChildren));
        stack.Children.Add(_musicOnly[^1]);
        stack.Children.Add(Row("时间条件", _timeEnabled));
        var range = new StackPanel { Orientation = Orientation.Horizontal };
        _start.MinWidth = 100;
        _end.MinWidth = 100;
        range.Children.Add(_start);
        range.Children.Add(new TextBlock { Text = "至", Margin = new Thickness(8, 0, 8, 0), VerticalAlignment = VerticalAlignment.Center });
        range.Children.Add(_end);
        stack.Children.Add(Row("时间段", range));
        var daysRow = new StackPanel { Orientation = Orientation.Horizontal };
        _days = DayLabels.Select(label => MakeCheck(label)).ToArray();
        foreach (var day in _days)
        {
            day.Margin = new Thickness(0, 0, 6, 0);
            daysRow.Children.Add(day);
        }
        stack.Children.Add(Row("星期（不选表示每天）", daysRow));
        _target.Items.Add("灯效预设");
        if (kind != RuleKind.Lighting) _target.Items.Add("音乐预设");
        _target.Items.Add("关闭灯光");
        _target.SelectionChanged += (_, _) => RefreshPresets();
        stack.Children.Add(Row("动作", _target));
        foreach (var label in EffectTypes.Select(EffectLabel)) _effectType.Items.Add(label);
        _effectType.SelectionChanged += (_, _) => RefreshPresets();
        stack.Children.Add(Row("灯效类型", _effectType));
        stack.Children.Add(Row("目标预设", _preset));
        stack.Children.Add(Row("颜色来源", _colorSource));
        _musicOnly.Add(stack.Children[^1]);
        _mediaSession.Items.Add("自动匹配");
        foreach (var session in MediaPlaybackState.Load()?.Sessions ?? []) _mediaSession.Items.Add(session.SourceId);
        stack.Children.Add(Row("媒体会话", _mediaSession));
        _musicOnly.Add(stack.Children[^1]);
        var brightnessRow = new StackPanel { Orientation = Orientation.Horizontal };
        _brightness.Width = 80;
        brightnessRow.Children.Add(_brightnessEnabled);
        _brightness.Margin = new Thickness(12, 0, 0, 0);
        brightnessRow.Children.Add(_brightness);
        stack.Children.Add(Row("亮度", brightnessRow));
        stack.Children.Add(Row("打字反馈", _typing));
        _notSchedule.Add(stack.Children[^1]);
        stack.Children.Add(Row("通知反馈", _notification));
        _notSchedule.Add(stack.Children[^1]);

        var buttons = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            HorizontalAlignment = HorizontalAlignment.Right,
            Margin = new Thickness(0, 14, 0, 0)
        };
        var ok = new Button { Content = "确定", Style = (Style)Application.Current.Resources["UiButtonPrimary"], MinWidth = 96 };
        var cancel = new Button { Content = "取消", Style = (Style)Application.Current.Resources["UiButton"], MinWidth = 96, Margin = new Thickness(12, 0, 0, 0) };
        ok.Click += (_, _) =>
        {
            // 必须设置 DialogResult：仅 Close() 会让 ShowDialog 返回 null，
            // 调用方会误判为"取消"导致规则永远添加不了。
            if (Save())
            {
                DialogResult = true;
                Close();
            }
        };
        cancel.Click += (_, _) => Close();
        buttons.Children.Add(ok);
        buttons.Children.Add(cancel);
        stack.Children.Add(buttons);

        var scroll = new ScrollViewer
        {
            Content = stack,
            VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
            HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled
        };
        Content = scroll;
        LoadRule();
        _loading = false;
    }

    public static AutomationRuleDialog ForMusic(MusicApplicationRule rule, IEnumerable<MusicPreset> music) =>
        new(RuleKind.Music, rule, new EffectPresetSettings(), music);
    public static AutomationRuleDialog ForLighting(LightingApplicationRule rule, EffectPresetSettings effects) =>
        new(RuleKind.Lighting, rule, effects, []);
    public static AutomationRuleDialog ForSchedule(AutomationScheduleRule rule, EffectPresetSettings effects, IEnumerable<MusicPreset> music) =>
        new(RuleKind.Schedule, rule, effects, music);

    private void LoadRule()
    {
        _loading = true;
        var filter = _musicRule?.TimeFilter ?? _lightingRule?.TimeFilter ?? _scheduleRule!.TimeFilter;
        _name.Text = _musicRule?.Name ?? _lightingRule?.Name ?? _scheduleRule!.Name;
        _enabled.IsChecked = _musicRule?.Enabled ?? _lightingRule?.Enabled ?? _scheduleRule!.Enabled;
        _process.Text = _musicRule?.ProcessName ?? string.Join(", ", _lightingRule?.ProcessNames ?? []);
        _includeChildren.IsChecked = _musicRule?.IncludeChildProcesses ?? false;
        _timeEnabled.IsChecked = filter.TimeEnabled;
        _start.Text = filter.Start;
        _end.Text = filter.End;
        var dayValues = DayValues;
        for (var i = 0; i < dayValues.Length; i++) _days[i].IsChecked = filter.Days.Contains(dayValues[i]);
        var limit = _musicRule?.BrightnessLimit ?? _lightingRule?.Action.BrightnessLimit ?? _scheduleRule?.Action.BrightnessLimit;
        _brightnessEnabled.IsChecked = limit.HasValue;
        _brightness.Text = (limit ?? 100).ToString();
        _typing.SelectedIndex = (int)(_musicRule?.TypingPolicy ?? _lightingRule?.TypingPolicy ?? EventPolicy.Inherit);
        _notification.SelectedIndex = (int)(_musicRule?.NotificationPolicy ?? _lightingRule?.NotificationPolicy ?? EventPolicy.Inherit);
        if (_kind == RuleKind.Music)
        {
            _target.SelectedIndex = (int)SceneTargetKind.MusicPreset;
            _colorSource.SelectedIndex = (int)_musicRule!.ColorSource;
            FindOrAddSession(_musicRule.MediaSessionId);
            RefreshPresets(_musicRule.MusicPresetId);
        }
        else
        {
            var action = _lightingRule?.Action ?? _scheduleRule!.Action;
            _target.SelectedIndex = _kind == RuleKind.Lighting && action.Target == SceneTargetKind.Off
                ? 1
                : Math.Max(0, (int)action.Target);
            _effectType.SelectedIndex = EffectIndex(action.LightingEffectType);
            RefreshPresets(action.PresetId);
        }

        _process.IsEnabled = _kind != RuleKind.Schedule;
        _colorSource.Visibility = _kind == RuleKind.Music ? Visibility.Visible : Visibility.Collapsed;
        foreach (var element in _musicOnly) element.Visibility = _kind == RuleKind.Music ? Visibility.Visible : Visibility.Collapsed;
        foreach (var element in _notSchedule) element.IsEnabled = _kind != RuleKind.Schedule;
        _target.IsEnabled = _kind != RuleKind.Music;
    }

    private bool Save()
    {
        if (string.IsNullOrWhiteSpace(_name.Text))
        {
            System.Windows.MessageBox.Show("请输入规则名称。", "ClevoLEDKeyboardControl", MessageBoxButton.OK, MessageBoxImage.Information);
            return false;
        }

        var filter = _musicRule?.TimeFilter ?? _lightingRule?.TimeFilter ?? _scheduleRule!.TimeFilter;
        filter.TimeEnabled = _timeEnabled.IsChecked == true;
        filter.Start = ParseTime(_start.Text, filter.Start);
        filter.End = ParseTime(_end.Text, filter.End);
        var dayValues = DayValues;
        filter.Days = dayValues.Where((_, index) => _days[index].IsChecked == true).ToList();
        int? limit = _brightnessEnabled.IsChecked == true ? ParseInt(_brightness.Text, 100) : null;
        if (_musicRule is not null)
        {
            _musicRule.Name = _name.Text;
            _musicRule.Enabled = _enabled.IsChecked == true;
            _musicRule.ProcessName = AppProfileRule.NormalizeProcessName(_process.Text);
            _musicRule.IncludeChildProcesses = _includeChildren.IsChecked == true;
            _musicRule.MusicPresetId = SelectedPresetId();
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
            action.PresetId = SelectedPresetId();
            action.BrightnessLimit = limit;
            if (_lightingRule is not null)
            {
                _lightingRule.Name = _name.Text;
                _lightingRule.Enabled = _enabled.IsChecked == true;
                _lightingRule.ProcessNames = _process.Text.Split([',', '，', ';'], StringSplitOptions.RemoveEmptyEntries).ToList();
                _lightingRule.TypingPolicy = (EventPolicy)Math.Max(0, _typing.SelectedIndex);
                _lightingRule.NotificationPolicy = (EventPolicy)Math.Max(0, _notification.SelectedIndex);
            }
            else
            {
                _scheduleRule!.Name = _name.Text;
                _scheduleRule.Enabled = _enabled.IsChecked == true;
            }
        }

        return true;
    }

    private string? SelectedPresetId() => _preset.SelectedItem as string is { } name
        ? _kind == RuleKind.Music || CurrentTarget() == SceneTargetKind.MusicPreset
            ? _musicPresets.FirstOrDefault(item => string.Equals(item.Name, name, StringComparison.OrdinalIgnoreCase))?.Id ?? ""
            : EffectPresetSettings.BuiltInId(EffectTypes[Math.Max(0, _effectType.SelectedIndex)]) is var builtinId && name == "软件默认配置"
                ? builtinId
                : _effects.ForType(EffectTypes[Math.Max(0, _effectType.SelectedIndex)])
                    .FirstOrDefault(item => string.Equals(item.Name, name, StringComparison.OrdinalIgnoreCase))?.Id ?? ""
        : "";

    private SceneTargetKind CurrentTarget() => _kind == RuleKind.Lighting && _target.SelectedIndex == 1
        ? SceneTargetKind.Off
        : (SceneTargetKind)Math.Max(0, _target.SelectedIndex);

    private void RefreshPresets(string? selectedId = null)
    {
        var previousName = _preset.SelectedItem as string;
        _preset.Items.Clear();
        var target = CurrentTarget();
        if (_kind == RuleKind.Music || target == SceneTargetKind.MusicPreset)
        {
            foreach (var item in _musicPresets) _preset.Items.Add(item.Name);
        }
        else if (target == SceneTargetKind.LightingPreset)
        {
            var type = EffectTypes[Math.Max(0, _effectType.SelectedIndex)];
            _preset.Items.Add(Pages.EffectPage.SoftwareDefaultPresetName);
            foreach (var item in _effects.ForType(type)) _preset.Items.Add(item.Name);
        }

        var desiredId = selectedId;
        var index = 0;
        for (var i = 0; i < _preset.Items.Count; i++)
        {
            var name = _preset.Items[i]?.ToString() ?? "";
            var id = name == Pages.EffectPage.SoftwareDefaultPresetName
                ? EffectPresetSettings.BuiltInId(EffectTypes[Math.Max(0, _effectType.SelectedIndex)])
                : _kind == RuleKind.Music || target == SceneTargetKind.MusicPreset
                    ? _musicPresets.FirstOrDefault(item => item.Name == name)?.Id ?? ""
                    : _effects.ForType(EffectTypes[Math.Max(0, _effectType.SelectedIndex)]).FirstOrDefault(item => item.Name == name)?.Id ?? "";
            if (!string.IsNullOrWhiteSpace(desiredId) && id == desiredId)
            {
                index = i;
                break;
            }
            if (string.IsNullOrWhiteSpace(desiredId) && name == previousName)
            {
                index = i;
                break;
            }
        }
        if (_preset.Items.Count > 0) _preset.SelectedIndex = index;
    }

    private void FindOrAddSession(string value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            _mediaSession.SelectedIndex = 0;
            return;
        }
        for (var i = 0; i < _mediaSession.Items.Count; i++)
        {
            if (string.Equals(_mediaSession.Items[i]?.ToString(), value, StringComparison.OrdinalIgnoreCase))
            {
                _mediaSession.SelectedIndex = i;
                return;
            }
        }
        _mediaSession.Items.Add(value);
        _mediaSession.SelectedIndex = _mediaSession.Items.Count - 1;
    }

    private static string ParseTime(string text, string fallback) =>
        System.TimeOnly.TryParseExact(text.Trim(), "HH:mm", out var time) ? time.ToString("HH:mm") : fallback;

    private static int ParseInt(string text, int fallback) => int.TryParse(text, out var value) ? Math.Clamp(value, 0, 100) : fallback;

    private static string EffectLabel(EffectType type) => type switch
    {
        EffectType.Static => "固定颜色",
        EffectType.Rainbow => "RGB 循环",
        EffectType.Breathing => "单色呼吸",
        EffectType.Sequence => "循环呼吸",
        EffectType.Pulse => "脉冲",
        EffectType.Heartbeat => "心跳",
        _ => type.ToString()
    };

    private static int EffectIndex(EffectType type) => Math.Max(0, Array.IndexOf(EffectTypes, type));

    private static System.Windows.Controls.ComboBox MakeTimeCombo()
    {
        // IsEditable：非 15 分钟整的既有时间也能显示原值（对照 WinForms DateTimePicker）
        var combo = new System.Windows.Controls.ComboBox { Style = (Style)Application.Current.Resources["UiComboBox"], MinWidth = 100, IsEditable = true };
        for (var minutes = 0; minutes < 24 * 60; minutes += 15)
        {
            combo.Items.Add($"{minutes / 60:00}:{minutes % 60:00}");
        }
        combo.SelectedIndex = 76; // 19:00
        return combo;
    }

    private static System.Windows.Controls.ComboBox MakeCombo(params string[] items)
    {
        var combo = new System.Windows.Controls.ComboBox { Style = (Style)Application.Current.Resources["UiComboBox"], MinWidth = 350 };
        foreach (var item in items) combo.Items.Add(item);
        return combo;
    }

    private static System.Windows.Controls.TextBox MakeTextBox() => new()
    {
        Style = (Style)Application.Current.Resources["UiTextBox"],
        MinWidth = 350
    };

    private static System.Windows.Controls.CheckBox MakeCheck(string text) => new()
    {
        Content = text,
        Style = (Style)Application.Current.Resources["UiCheckBox"]
    };

    private static UIElement Row(string label, UIElement control)
    {
        var grid = new Grid { Margin = new Thickness(0, 6, 0, 6) };
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(150) });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        grid.Children.Add(new TextBlock { Text = label, VerticalAlignment = VerticalAlignment.Center, Foreground = (Brush)Application.Current.Resources["Brush.Text"] });
        Grid.SetColumn(control, 1);
        grid.Children.Add(control);
        return grid;
    }

    private static UIElement PlainRow(UIElement control) => new Grid
    {
        Margin = new Thickness(0, 6, 0, 6),
        Children = { control }
    };
}

/// <summary>选择前台应用进程（WinForms RunningAppsForm 移植件）。</summary>
public sealed class RunningAppsDialog : Window
{
    private readonly ListView _list = new() { MinHeight = 320 };
    private List<(string ProcessName, string Title, string? IconColor)> _items = [];

    public string? SelectedProcessName { get; private set; }
    public string? SelectedIconColor { get; private set; }

    public RunningAppsDialog()
    {
        Title = "选择应用进程";
        WindowStartupLocation = WindowStartupLocation.CenterOwner;
        Width = 660;
        Height = 480;
        Background = (Brush)Application.Current.Resources["Brush.Window"];
        FontFamily = (FontFamily)Application.Current.Resources["Font.Body"];
        FontSize = 12;
        Foreground = (Brush)Application.Current.Resources["Brush.Text"];
        SourceInitialized += (_, _) => WpfThemeManager.ApplyTitleBarMode(this);
        SourceInitialized += (_, _) => WpfThemeManager.ApplyTitleBarMode(this);

        var gridView = new GridView();
        gridView.Columns.Add(Column("进程名", "ProcessName", 180));
        gridView.Columns.Add(Column("窗口标题", "Title", 300));
        gridView.Columns.Add(Column("图标色", "IconColor", 110));
        _list.View = gridView;
        ThemedListView.Apply(_list);
        System.Windows.Controls.ScrollViewer.SetHorizontalScrollBarVisibility(_list, ScrollBarVisibility.Disabled);
        _list.MouseDoubleClick += (_, _) => Confirm();

        var buttons = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            HorizontalAlignment = HorizontalAlignment.Right,
            Margin = new Thickness(0, 12, 0, 0)
        };
        var refresh = new Button { Content = "刷新", Style = (Style)Application.Current.Resources["UiButton"], MinWidth = 90 };
        refresh.Click += (_, _) => LoadApps();
        var ok = new Button { Content = "确定", Style = (Style)Application.Current.Resources["UiButtonPrimary"], MinWidth = 85, Margin = new Thickness(12, 0, 0, 0) };
        ok.Click += (_, _) => Confirm();
        var cancel = new Button { Content = "取消", Style = (Style)Application.Current.Resources["UiButton"], MinWidth = 85, Margin = new Thickness(12, 0, 0, 0) };
        cancel.Click += (_, _) => Close();
        buttons.Children.Add(refresh);
        buttons.Children.Add(ok);
        buttons.Children.Add(cancel);

        var root = new DockPanel { Margin = new Thickness(14) };
        DockPanel.SetDock(buttons, Dock.Bottom);
        root.Children.Add(buttons);
        root.Children.Add(_list);
        Content = root;
        Loaded += (_, _) => LoadApps();
    }

    private static GridViewColumn Column(string header, string property, double width) => new()
    {
        Header = header,
        Width = width,
        DisplayMemberBinding = new System.Windows.Data.Binding(property)
    };

    private void LoadApps()
    {
        var rows = new List<(string, string, string?)>();
        foreach (var process in Process.GetProcesses())
        using (process)
        {
            try
            {
                if (string.IsNullOrWhiteSpace(process.MainWindowTitle)) continue;
                var color = ProcessIconColor.TryGetColor(process.ProcessName, out var iconColor) ? iconColor : null;
                rows.Add((process.ProcessName, process.MainWindowTitle, color));
            }
            catch
            {
            }
        }

        _items = rows.OrderBy(row => row.Item1, StringComparer.OrdinalIgnoreCase).ToList();
        _list.ItemsSource = _items.Select(item => new
        {
            ProcessName = item.Item1,
            Title = item.Item2,
            IconColor = item.Item3 ?? "—"
        }).ToList();
    }

    private void Confirm()
    {
        if (_list.SelectedIndex < 0 || _list.SelectedIndex >= _items.Count) return;
        var (processName, _, iconColor) = _items[_list.SelectedIndex];
        SelectedProcessName = processName;
        SelectedIconColor = iconColor;
        DialogResult = true;
        Close();
    }
}

/// <summary>场景模拟器（WinForms 内联模拟窗的 WPF 移植件）。</summary>
public sealed class SceneSimulatorDialog : Window
{
    public SceneSimulatorDialog(KeyboardSettings settings)
    {
        Title = "场景模拟器";
        WindowStartupLocation = WindowStartupLocation.CenterOwner;
        Width = 680;
        Height = 560;
        Background = (Brush)Application.Current.Resources["Brush.Window"];
        FontFamily = (FontFamily)Application.Current.Resources["Font.Body"];
        FontSize = 12;
        Foreground = (Brush)Application.Current.Resources["Brush.Text"];
        SourceInitialized += (_, _) => WpfThemeManager.ApplyTitleBarMode(this);

        var timeText = MakeBox();
        timeText.Text = DateTime.Now.ToString("yyyy-MM-dd HH:mm");
        var foreground = MakeBox();
        foreground.Text = "";
        var audio = new System.Windows.Controls.ComboBox { Style = (Style)Application.Current.Resources["UiComboBox"], MinWidth = 260, IsEditable = true };
        foreach (var name in settings.Automation.MusicApplications.Select(rule => rule.ProcessName).Where(name => name.Length > 0).Distinct())
            audio.Items.Add(name);
        var playing = MakeCheck("模拟该程序正在播放");
        var level = MakeBox();
        level.Text = "30";
        var idle = MakeCheck("模拟已进入空闲状态");
        var output = new System.Windows.Controls.TextBox
        {
            Style = (Style)Application.Current.Resources["UiTextBox"],
            MinHeight = 180,
            IsReadOnly = true,
            TextWrapping = TextWrapping.Wrap,
            AcceptsReturn = true,
            VerticalScrollBarVisibility = ScrollBarVisibility.Auto
        };
        var run = new Button { Content = "模拟", Style = (Style)Application.Current.Resources["UiButton"], MinWidth = 100 };

        var stack = new StackPanel { Margin = new Thickness(14) };
        stack.Children.Add(Row("本地时间", timeText));
        stack.Children.Add(Row("前台程序", foreground));
        stack.Children.Add(Row("播放程序", audio));
        stack.Children.Add(PlainRow(playing));
        stack.Children.Add(Row("模拟音量（%）", level));
        stack.Children.Add(PlainRow(idle));
        run.Margin = new Thickness(0, 10, 0, 10);
        stack.Children.Add(run);
        stack.Children.Add(output);
        Content = new ScrollViewer
        {
            Content = stack,
            VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
            HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled
        };

        run.Click += (_, _) =>
        {
            try
            {
                var localTime = System.DateTime.Parse(timeText.Text);
                var process = AppProfileRule.NormalizeProcessName(audio.Text);
                var levelValue = int.TryParse(level.Text, out var parsed) ? Math.Clamp(parsed, 0, 100) : 30;
                var states = playing.IsChecked == true && process.Length > 0
                    ? new AudioApplicationState[] { new(process, "", [1234], (float)levelValue / 100f, true,
                        string.Equals(process, AppProfileRule.NormalizeProcessName(foreground.Text), StringComparison.OrdinalIgnoreCase)) }
                    : [];
                var result = AutomationSimulator.Simulate(settings,
                    new AutomationSimulationInput(localTime, foreground.Text, states, idle.IsChecked == true));
                var issues = result.Health.Count == 0 ? "无" : string.Join(Environment.NewLine,
                    result.Health.Select(issue => $"[{issue.Severity}] {issue.RuleName}：{issue.Reason}"));
                output.Text = $"优先级结果：{result.PriorityTrace}{Environment.NewLine}" +
                    $"最终亮度上限：{result.FinalBrightnessLimit}%{Environment.NewLine}" +
                    $"跳过提示：{result.Selection.InvalidReason ?? "无"}{Environment.NewLine}{Environment.NewLine}规则健康检查：{Environment.NewLine}{issues}";
            }
            catch (FormatException)
            {
                output.Text = "本地时间格式无法解析，请使用 yyyy-MM-dd HH:mm。";
            }
        };
    }

    private static System.Windows.Controls.TextBox MakeBox() => new()
    {
        Style = (Style)Application.Current.Resources["UiTextBox"],
        MinWidth = 260
    };

    private static System.Windows.Controls.CheckBox MakeCheck(string text) => new()
    {
        Content = text,
        Style = (Style)Application.Current.Resources["UiCheckBox"]
    };

    private static UIElement Row(string label, UIElement control)
    {
        var grid = new Grid { Margin = new Thickness(0, 6, 0, 6) };
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(130) });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        grid.Children.Add(new TextBlock { Text = label, VerticalAlignment = VerticalAlignment.Center, Foreground = (Brush)Application.Current.Resources["Brush.Text"] });
        Grid.SetColumn(control, 1);
        grid.Children.Add(control);
        return grid;
    }

    private static UIElement PlainRow(UIElement control) => new Grid { Margin = new Thickness(0, 6, 0, 6), Children = { control } };
}
