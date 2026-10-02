using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Shapes;
using System.Windows.Media;
using ColorfulLedKeyboard.Core;

namespace ColorfulLedKeyboard.Tray.Wpf.Controls;

/// <summary>场景规则卡片列表项。</summary>
public sealed record AutomationRuleListItem(string Title, string Detail, AutomationRuleVisualState State)
{
    public string StateText => State switch
    {
        AutomationRuleVisualState.Active => "生效中",
        AutomationRuleVisualState.Error => "需要处理",
        AutomationRuleVisualState.Disabled => "已停用",
        _ => "已启用"
    };

    public Brush StateBrush => FindBrush(State switch
    {
        AutomationRuleVisualState.Active => "Brush.Success",
        AutomationRuleVisualState.Error => "Brush.Error",
        AutomationRuleVisualState.Disabled => "Brush.MutedText",
        _ => "Brush.Primary"
    });

    private static ListBox MakeRuleList() => new() { MinHeight = 120, MaxHeight = 320 };

    private static Brush FindBrush(string key) => (Brush)Application.Current.Resources[key];
}

public enum AutomationRuleVisualState { Normal, Active, Disabled, Error }

/// <summary>
/// 场景自动化编辑器：三个页签（音乐程序/灯效程序/时间计划）+ 规则卡片列表 + 增删改移。
/// WinForms SceneAutomationEditorV2 的移植件；规则对象经 JSON 克隆进弹窗，确定后才写回。
/// </summary>
public sealed class SceneAutomationEditor : UserControl
{
    private static readonly string[] TabTitles = ["音乐程序", "灯效程序", "时间计划"];

    private AutomationSettings _automation = new();
    private EffectPresetSettings _effectPresets = new();
    private List<MusicPreset> _musicPresets = [];
    private readonly ListBox[] _lists = [MakeRuleList(), MakeRuleList(), MakeRuleList()];
    private readonly TextBlock[] _tabHeaders = new TextBlock[3];
    private readonly Border[] _tabBorders = new Border[3];
    private readonly ContentControl _tabContent = new();
    private int _selectedTab;

    public event EventHandler? Changed;

    public SceneAutomationEditor()
    {
        for (var i = 0; i < _lists.Length; i++)
        {
            _lists[i].ItemTemplate = RuleCardTemplate();
            _lists[i].ItemContainerStyle = RuleItemStyle();
            _lists[i].MaxHeight = 320;
            _lists[i].Background = Brushes.Transparent;
            _lists[i].BorderThickness = new Thickness(0);
            _lists[i].SelectionChanged += (_, _) => { };
        }

        var headerRow = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 0, 0, 8) };
        for (var i = 0; i < TabTitles.Length; i++)
        {
            var index = i;
            var header = new TextBlock
            {
                Text = TabTitles[i],
                Padding = new Thickness(14, 6, 14, 6),
                FontSize = 12
            };
            var border = new Border
            {
                Child = header,
                CornerRadius = new CornerRadius(6, 6, 0, 0),
                Background = i == 0 ? FindBrush("Brush.Surface") : FindBrush("Brush.Window"),
                BorderBrush = FindBrush("Brush.Border"),
                BorderThickness = new Thickness(1, 1, 1, 0),
                Margin = new Thickness(0, 0, 4, 0),
                Cursor = System.Windows.Input.Cursors.Hand
            };
            border.MouseLeftButtonDown += (_, _) => SelectTab(index);
            _tabHeaders[i] = header;
            _tabBorders[i] = border;
            headerRow.Children.Add(border);
        }

        var tabHost = new Border
        {
            Background = FindBrush("Brush.Surface"),
            BorderBrush = FindBrush("Brush.Border"),
            BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(0, 8, 8, 8),
            Padding = new Thickness(10)
        };
        var tabPanel = new DockPanel();
        var buttons = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            Margin = new Thickness(0, 10, 0, 0)
        };
        DockPanel.SetDock(buttons, Dock.Bottom);
        tabPanel.Children.Add(buttons);
        tabPanel.Children.Add(_tabContent);
        tabHost.Child = tabPanel;

        var root = new StackPanel();
        root.Children.Add(headerRow);
        root.Children.Add(tabHost);
        Content = root;

        SelectTab(0);
        RefreshTabButtons();
    }

    public void SetPresets(EffectPresetSettings effects, IEnumerable<MusicPreset> music)
    {
        _effectPresets = KeyboardSettings.CloneEffectPresets(effects);
        _musicPresets = MusicSettings.BuiltInPresets.Concat(music).Select(CloneMusicPreset).ToList();
    }

    public AutomationSettings Automation
    {
        get => System.Text.Json.JsonSerializer.Deserialize<AutomationSettings>(
            System.Text.Json.JsonSerializer.Serialize(_automation))!;
        set
        {
            _automation = System.Text.Json.JsonSerializer.Deserialize<AutomationSettings>(
                System.Text.Json.JsonSerializer.Serialize(value))!;
            RefreshLists();
        }
    }

    public void RefreshRuntimeState() => RefreshLists(_lists[0].SelectedIndex, _lists[1].SelectedIndex, _lists[2].SelectedIndex);

    private void SelectTab(int index)
    {
        _selectedTab = index;
        for (var i = 0; i < _tabBorders.Length; i++)
        {
            _tabBorders[i].Background = FindBrush(i == index ? "Brush.Surface" : "Brush.Window");
            _tabHeaders[i].Foreground = FindBrush(i == index ? "Brush.Text" : "Brush.MutedText");
        }
        _tabContent.Content = BuildTabPage(index);
    }

    private UIElement BuildTabPage(int index)
    {
        var panel = new DockPanel();
        var buttons = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 10, 0, 0) };
        void Add(string text, System.Action action)
        {
            var button = new Button { Content = text, Style = (Style)Application.Current.Resources["UiButton"], MinWidth = 112, Margin = new Thickness(0, 0, 10, 0) };
            button.Click += (_, _) => action();
            buttons.Children.Add(button);
        }

        switch (index)
        {
            case 0:
                Add("绑定有声程序", AddMusic);
                Add("编辑", EditMusic);
                Add("删除", RemoveMusic);
                Add("上移", () => MoveMusic(-1));
                Add("下移", () => MoveMusic(1));
                break;
            case 1:
                Add("添加", AddLighting);
                Add("编辑", EditLighting);
                Add("删除", RemoveLighting);
                Add("上移", () => MoveLighting(-1));
                Add("下移", () => MoveLighting(1));
                break;
            default:
                Add("添加", AddSchedule);
                Add("编辑", EditSchedule);
                Add("删除", RemoveSchedule);
                Add("上移", () => MoveSchedule(-1));
                Add("下移", () => MoveSchedule(1));
                break;
        }

        DockPanel.SetDock(buttons, Dock.Bottom);
        panel.Children.Add(buttons);
        panel.Children.Add(_lists[index]);
        return panel;
    }

    private void RefreshTabButtons()
    {
        // 页签按钮随 Changed 重建，无需额外状态
    }

    private void AddMusic()
    {
        var picker = new Dialogs.AudioApplicationPickerDialog { Owner = Window.GetWindow(this) };
        if (picker.ShowDialog() != true || picker.Selected is null) return;
        var rule = new MusicApplicationRule
        {
            Name = picker.Selected.ProcessName,
            ProcessName = picker.Selected.ProcessName,
            ExecutablePath = picker.Selected.ExecutablePath,
            MediaSessionId = ""
        }.Normalize();
        var dialog = Dialogs.AutomationRuleDialog.ForMusic(rule, _musicPresets);
        dialog.Owner = Window.GetWindow(this);
        if (dialog.ShowDialog() != true) return;
        _automation.MusicApplications.Add(rule.Normalize());
        RefreshLists();
        _lists[0].SelectedIndex = _automation.MusicApplications.Count - 1;
        Changed?.Invoke(this, EventArgs.Empty);
    }

    private void EditMusic()
    {
        if (_lists[0].SelectedIndex < 0) return;
        var rule = Clone(_automation.MusicApplications[_lists[0].SelectedIndex]);
        var dialog = Dialogs.AutomationRuleDialog.ForMusic(rule, _musicPresets);
        dialog.Owner = Window.GetWindow(this);
        if (dialog.ShowDialog() != true) return;
        _automation.MusicApplications[_lists[0].SelectedIndex] = rule.Normalize();
        RefreshLists(_lists[0].SelectedIndex, -1, -1);
        Changed?.Invoke(this, EventArgs.Empty);
    }

    private void AddLighting()
    {
        var picker = new Dialogs.RunningAppsDialog { Owner = Window.GetWindow(this) };
        if (picker.ShowDialog() != true || string.IsNullOrWhiteSpace(picker.SelectedProcessName)) return;
        var rule = new LightingApplicationRule
        {
            Name = picker.SelectedProcessName,
            ProcessNames = [picker.SelectedProcessName]
        }.Normalize();
        var dialog = Dialogs.AutomationRuleDialog.ForLighting(rule, _effectPresets);
        dialog.Owner = Window.GetWindow(this);
        if (dialog.ShowDialog() != true) return;
        _automation.LightingApplications.Add(rule.Normalize());
        RefreshLists();
        _lists[1].SelectedIndex = _automation.LightingApplications.Count - 1;
        Changed?.Invoke(this, EventArgs.Empty);
    }

    private void EditLighting()
    {
        if (_lists[1].SelectedIndex < 0) return;
        var rule = Clone(_automation.LightingApplications[_lists[1].SelectedIndex]);
        var dialog = Dialogs.AutomationRuleDialog.ForLighting(rule, _effectPresets);
        dialog.Owner = Window.GetWindow(this);
        if (dialog.ShowDialog() != true) return;
        _automation.LightingApplications[_lists[1].SelectedIndex] = rule.Normalize();
        RefreshLists(-1, _lists[1].SelectedIndex, -1);
        Changed?.Invoke(this, EventArgs.Empty);
    }

    private void AddSchedule()
    {
        var rule = new AutomationScheduleRule
        {
            TimeFilter = new AutomationTimeFilter { TimeEnabled = true, Start = "19:00", End = "23:00" }
        }.Normalize();
        var dialog = Dialogs.AutomationRuleDialog.ForSchedule(rule, _effectPresets, _musicPresets);
        dialog.Owner = Window.GetWindow(this);
        if (dialog.ShowDialog() != true) return;
        _automation.ScheduleRules.Add(rule.Normalize());
        RefreshLists();
        _lists[2].SelectedIndex = _automation.ScheduleRules.Count - 1;
        Changed?.Invoke(this, EventArgs.Empty);
    }

    private void EditSchedule()
    {
        if (_lists[2].SelectedIndex < 0) return;
        var rule = Clone(_automation.ScheduleRules[_lists[2].SelectedIndex]);
        var dialog = Dialogs.AutomationRuleDialog.ForSchedule(rule, _effectPresets, _musicPresets);
        dialog.Owner = Window.GetWindow(this);
        if (dialog.ShowDialog() != true) return;
        _automation.ScheduleRules[_lists[2].SelectedIndex] = rule.Normalize();
        RefreshLists(-1, -1, _lists[2].SelectedIndex);
        Changed?.Invoke(this, EventArgs.Empty);
    }

    private void RemoveMusic()
    {
        if (_lists[0].SelectedIndex >= 0)
        {
            _automation.MusicApplications.RemoveAt(_lists[0].SelectedIndex);
            RefreshLists();
            Changed?.Invoke(this, EventArgs.Empty);
        }
    }

    private void RemoveLighting()
    {
        if (_lists[1].SelectedIndex >= 0)
        {
            _automation.LightingApplications.RemoveAt(_lists[1].SelectedIndex);
            RefreshLists();
            Changed?.Invoke(this, EventArgs.Empty);
        }
    }

    private void RemoveSchedule()
    {
        if (_lists[2].SelectedIndex >= 0)
        {
            _automation.ScheduleRules.RemoveAt(_lists[2].SelectedIndex);
            RefreshLists();
            Changed?.Invoke(this, EventArgs.Empty);
        }
    }

    private void MoveMusic(int offset) => MoveRule(_automation.MusicApplications, _lists[0], offset);
    private void MoveLighting(int offset) => MoveRule(_automation.LightingApplications, _lists[1], offset);
    private void MoveSchedule(int offset) => MoveRule(_automation.ScheduleRules, _lists[2], offset);

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
        _lists[0].Items.Clear();
        foreach (var rule in _automation.MusicApplications)
        {
            var missingPreset = !_musicPresets.Any(preset => preset.Id == rule.MusicPresetId);
            var state = !rule.Enabled ? AutomationRuleVisualState.Disabled :
                string.IsNullOrWhiteSpace(rule.ProcessName) || missingPreset ? AutomationRuleVisualState.Error :
                rule.Id == activeRuleId ? AutomationRuleVisualState.Active : AutomationRuleVisualState.Normal;
            var reason = string.IsNullOrWhiteSpace(rule.ProcessName) ? "未配置进程" : missingPreset ? "音乐预设不存在" :
                $"{rule.ProcessName} · {ColorLabel(rule.ColorSource)} · {PresetName(rule.MusicPresetId, _musicPresets)}";
            _lists[0].Items.Add(new AutomationRuleListItem(rule.Name, reason, state));
        }
        _lists[1].Items.Clear();
        foreach (var rule in _automation.LightingApplications)
        {
            var invalid = rule.ProcessNames.Count == 0 || !ActionExists(rule.Action);
            var state = !rule.Enabled ? AutomationRuleVisualState.Disabled : invalid ? AutomationRuleVisualState.Error :
                rule.Id == activeRuleId ? AutomationRuleVisualState.Active : AutomationRuleVisualState.Normal;
            var process = rule.ProcessNames.Count == 0 ? "未配置前台进程" : string.Join("、", rule.ProcessNames);
            _lists[1].Items.Add(new AutomationRuleListItem(rule.Name, $"{process} · {ActionLabel(rule.Action)}", state));
        }
        _lists[2].Items.Clear();
        foreach (var rule in _automation.ScheduleRules)
        {
            var state = !rule.Enabled ? AutomationRuleVisualState.Disabled : !ActionExists(rule.Action) ? AutomationRuleVisualState.Error :
                rule.Id == activeRuleId ? AutomationRuleVisualState.Active : AutomationRuleVisualState.Normal;
            var time = rule.TimeFilter.TimeEnabled ? $"{rule.TimeFilter.Start}–{rule.TimeFilter.End}" : "全天";
            _lists[2].Items.Add(new AutomationRuleListItem(rule.Name, $"{time} · {ActionLabel(rule.Action)}", state));
        }
        if (music >= 0 && music < _lists[0].Items.Count) _lists[0].SelectedIndex = music;
        if (lighting >= 0 && lighting < _lists[1].Items.Count) _lists[1].SelectedIndex = lighting;
        if (schedule >= 0 && schedule < _lists[2].Items.Count) _lists[2].SelectedIndex = schedule;
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

    private static T Clone<T>(T value) => System.Text.Json.JsonSerializer.Deserialize<T>(
        System.Text.Json.JsonSerializer.Serialize(value))!;

    private static MusicPreset CloneMusicPreset(MusicPreset value) => Clone(value);

    private DataTemplate RuleCardTemplate()
    {
        var template = new DataTemplate();
        var gridFactory = new FrameworkElementFactory(typeof(Grid));
        gridFactory.SetValue(Grid.BackgroundProperty, FindBrush("Brush.Surface"));
        gridFactory.SetValue(Grid.MarginProperty, new Thickness(2));
        gridFactory.SetValue(Grid.MarginProperty, new Thickness(0));
        gridFactory.AppendChild(new FrameworkElementFactory(typeof(RowDefinition)));
        gridFactory.AppendChild(new FrameworkElementFactory(typeof(RowDefinition)));

        var titleGrid = new FrameworkElementFactory(typeof(Grid));
        titleGrid.SetValue(Grid.RowProperty, 0);
        var title = new FrameworkElementFactory(typeof(TextBlock));
        title.SetBinding(TextBlock.TextProperty, new Binding("Title"));
        title.SetValue(FontWeightProperty, FontWeights.Bold);
        title.SetValue(TextBlock.ForegroundProperty, FindBrush("Brush.Text"));
        title.SetValue(TextBlock.MarginProperty, new Thickness(16, 0, 0, 0));
        titleGrid.AppendChild(title);
        var stateText = new FrameworkElementFactory(typeof(TextBlock));
        stateText.SetBinding(TextBlock.TextProperty, new Binding("StateText"));
        stateText.SetValue(HorizontalAlignmentProperty, HorizontalAlignment.Right);
        stateText.SetBinding(TextBlock.ForegroundProperty, new Binding("StateBrush"));
        titleGrid.AppendChild(stateText);

        var detail = new FrameworkElementFactory(typeof(TextBlock));
        detail.SetValue(Grid.RowProperty, 1);
        detail.SetBinding(TextBlock.TextProperty, new Binding("Detail"));
        detail.SetValue(TextBlock.ForegroundProperty, FindBrush("Brush.MutedText"));
        detail.SetValue(TextBlock.MarginProperty, new Thickness(16, 0, 0, 0));
        detail.SetValue(TextBlock.TextTrimmingProperty, TextTrimming.CharacterEllipsis);

        var dot = new FrameworkElementFactory(typeof(Ellipse));
        dot.SetValue(Ellipse.WidthProperty, 8.0);
        dot.SetValue(Ellipse.HeightProperty, 8.0);
        dot.SetBinding(Shape.FillProperty, new Binding("StateBrush"));
        dot.SetValue(Grid.RowSpanProperty, 2);
        dot.SetValue(FrameworkElement.VerticalAlignmentProperty, VerticalAlignment.Center);
        dot.SetValue(FrameworkElement.HorizontalAlignmentProperty, HorizontalAlignment.Left);
        dot.SetValue(FrameworkElement.MarginProperty, new Thickness(2, 0, 0, 0));

        gridFactory.AppendChild(titleGrid);
        gridFactory.AppendChild(detail);
        gridFactory.AppendChild(dot);
        template.VisualTree = gridFactory;
        return template;
    }

    private Style RuleItemStyle()
    {
        var style = new Style(typeof(ListBoxItem));
        style.Setters.Add(new Setter(PaddingProperty, new Thickness(0)));
        style.Setters.Add(new Setter(MarginProperty, new Thickness(0, 0, 0, 6)));
        style.Setters.Add(new Setter(ForegroundProperty, FindBrush("Brush.Text")));
        style.Setters.Add(new Setter(BackgroundProperty, Brushes.Transparent));
        var selectedTrigger = new Trigger { Property = ListBoxItem.IsSelectedProperty, Value = true };
        selectedTrigger.Setters.Add(new Setter(BackgroundProperty, Brushes.Transparent));
        selectedTrigger.Setters.Add(new Setter(BorderBrushProperty, FindBrush("Brush.Primary")));
        style.Triggers.Add(selectedTrigger);
        return style;
    }

    private static ListBox MakeRuleList() => new() { MinHeight = 120, MaxHeight = 320 };

    private static Brush FindBrush(string key) => (Brush)Application.Current.Resources[key];
}
