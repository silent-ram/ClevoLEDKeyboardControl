using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using ColorfulLedKeyboard.Core;
using ColorfulLedKeyboard.Tray.Wpf.Controls;

namespace ColorfulLedKeyboard.Tray.Wpf.Pages;

/// <summary>场景自动化页：运行状态、场景规则编辑器、空闲最终覆盖。</summary>
public sealed class AutomationPage : UserControl
{
    private readonly TextBlock _statusText = new()
    {
        TextWrapping = TextWrapping.Wrap,
        Foreground = (Brush)Application.Current.Resources["Brush.MutedText"]
    };
    private readonly System.Windows.Controls.CheckBox _automationEnabled = MakeCheck("启用场景自动化");
    private readonly SceneAutomationEditor _editor = new();
    private readonly System.Windows.Controls.CheckBox _idleEnabled = MakeCheck("启用空闲降亮");
    private readonly System.Windows.Controls.ComboBox _idleAfter = MakeCombo(["1 分钟", "3 分钟", "5 分钟", "10 分钟", "30 分钟"]);
    private readonly UiSliderRow _idleBrightness = new("空闲亮度", 0, 100, "%");
    private readonly System.Windows.Controls.CheckBox _idleTurnOff = MakeCheck("空闲后关闭灯效");
    private readonly Button _simulator = MakeButton("场景模拟器...", 150);

    public event EventHandler? Changed;

    public bool IsDirty { get; private set; }

    /// <summary>保存成功后由宿主调用，复位脏状态。</summary>
    public void ResetDirty() => IsDirty = false;

    public event EventHandler? SimulatorRequested;

    public AutomationPage()
    {
        _idleAfter.SelectedIndex = 0;
        _editor.Changed += (_, _) => MarkChanged();
        _automationEnabled.Checked += (_, _) => MarkChanged();
        _automationEnabled.Unchecked += (_, _) => MarkChanged();
        _idleEnabled.Checked += (_, _) => MarkChanged();
        _idleEnabled.Unchecked += (_, _) => MarkChanged();
        _idleTurnOff.Checked += (_, _) => MarkChanged();
        _idleTurnOff.Unchecked += (_, _) => MarkChanged();
        _idleAfter.SelectionChanged += (_, _) => MarkChanged();
        _idleBrightness.ValueChanged += (_, _) => MarkChanged();
        _simulator.Click += (_, _) => SimulatorRequested?.Invoke(this, EventArgs.Empty);

        var priority = new TextBlock
        {
            Text = "有声音乐程序  →  前台灯效程序  →  时间计划  →  手动模式",
            Foreground = (Brush)Application.Current.Resources["Brush.Primary"],
            FontWeight = FontWeights.Bold,
            Margin = new Thickness(0, 4, 0, 0)
        };
        var simulatorRow = new Grid { MinHeight = 40, MaxWidth = UiMetrics.ContentWidth };
        _simulator.HorizontalAlignment = HorizontalAlignment.Left;
        simulatorRow.Children.Add(_simulator);

        var stack = new StackPanel { Margin = new Thickness(18, 18, 18, 28), MaxWidth = 832, HorizontalAlignment = HorizontalAlignment.Center };
        stack.Children.Add(MakeCard("运行状态", _statusText, priority, simulatorRow));
        stack.Children.Add(MakeCard("场景规则", PlainRow(_automationEnabled), Indent(_editor)));
        stack.Children.Add(MakeCard("空闲最终覆盖", PlainRow(_idleEnabled), Row("空闲时间", _idleAfter),
            _idleBrightness, PlainRow(_idleTurnOff)));

        Content = new ScrollViewer
        {
            Content = stack,
            VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
            HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled
        };
    }

    private bool _loadingSettings;

    private void MarkChanged()
    {
        if (_loadingSettings) return;
        IsDirty = true;
        Changed?.Invoke(this, EventArgs.Empty);
    }

    public void LoadFromStore(KeyboardSettings settings)
    {
        _loadingSettings = true;
        IsDirty = false;
        _automationEnabled.IsChecked = settings.Automation.Enabled;
        _editor.SetPresets(settings.EffectPresets, settings.Effect.Music.CustomPresets);
        _editor.Automation = settings.Automation;
        _idleEnabled.IsChecked = settings.IdleDim.Enabled;
        _idleAfter.SelectedIndex = SecondsToIdleIndex(settings.IdleDim.AfterSeconds);
        _idleBrightness.Value = settings.IdleDim.Brightness;
        _idleTurnOff.IsChecked = settings.IdleDim.TurnOff;
        _loadingSettings = false;
    }

    public void ApplyTo(KeyboardSettings settings)
    {
        settings.Automation.Enabled = _automationEnabled.IsChecked == true;
        _editor.SetPresets(settings.EffectPresets, settings.Effect.Music.CustomPresets);
        var automation = _editor.Automation;
        settings.Automation.MusicApplications = automation.MusicApplications;
        settings.Automation.LightingApplications = automation.LightingApplications;
        settings.Automation.ScheduleRules = automation.ScheduleRules;
        settings.Automation.Rules.Clear();
        settings.IdleDim.Enabled = _idleEnabled.IsChecked == true;
        settings.IdleDim.AfterSeconds = IdleIndexToSeconds(_idleAfter.SelectedIndex);
        settings.IdleDim.Brightness = _idleBrightness.Value;
        settings.IdleDim.TurnOff = _idleTurnOff.IsChecked == true;
    }

    public void UpdateStatusText(AutomationStatus? status)
    {
        if (status is null || DateTimeOffset.UtcNow - status.UpdatedUtc > TimeSpan.FromSeconds(10))
        {
            _statusText.Text = "服务状态尚未更新。应用条件需要托盘程序保持运行。";
            return;
        }

        var foreground = status.ForegroundAvailable
            ? $"前台：{status.ForegroundProcessName}"
            : "应用检测不可用";
        var active = string.IsNullOrWhiteSpace(status.ActiveRuleName)
            ? "当前：基础设置"
            : $"当前：{status.ActiveRuleName} → {status.TargetDescription}";
        var idle = status.IdleOverrideActive ? "；空闲覆盖生效" : "";
        var invalid = string.IsNullOrWhiteSpace(status.InvalidReason) ? "" : $"；提示：{status.InvalidReason}";
        var audio = string.IsNullOrWhiteSpace(status.ActiveMusicApplication)
            ? "音频：无有声程序"
            : $"音频：{status.ActiveMusicApplication}（PID {string.Join(",", status.ActiveProcessIds)}）";
        _statusText.Text = $"{foreground}；{active}{idle}{invalid}；{audio}";
        if (_editor.Visibility == Visibility.Visible) _editor.RefreshRuntimeState();
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

    private static Border MakeCard(string title, params UIElement[] children)
    {
        var stack = new StackPanel();
        stack.Children.Add(new TextBlock
        {
            Text = title,
            FontSize = 13,
            FontWeight = FontWeights.Bold,
            Foreground = (Brush)Application.Current.Resources["Brush.Text"],
            Margin = new Thickness(0, 0, 0, 8)
        });
        foreach (var child in children) stack.Children.Add(child);
        return new Border { Style = (Style)Application.Current.Resources["UiCard"], Child = stack };
    }

    private static UIElement Row(string label, FrameworkElement control)
    {
        var grid = new Grid { MinHeight = 40, MaxWidth = UiMetrics.ContentWidth };
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(130) });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        grid.Children.Add(new TextBlock { Text = label, VerticalAlignment = VerticalAlignment.Center, Foreground = (Brush)Application.Current.Resources["Brush.Text"] });
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

    private static UIElement Indent(FrameworkElement control)
    {
        control.HorizontalAlignment = HorizontalAlignment.Left;
        control.Margin = new Thickness(0);
        return control;
    }

    private static System.Windows.Controls.ComboBox MakeCombo(params string[] items)
    {
        var combo = new System.Windows.Controls.ComboBox
        {
            Style = (Style)Application.Current.Resources["UiComboBox"],
            Width = 280,
            HorizontalAlignment = HorizontalAlignment.Left
        };
        foreach (var item in items) combo.Items.Add(item);
        return combo;
    }

    private static System.Windows.Controls.CheckBox MakeCheck(string text) => new()
    {
        Content = text,
        Style = (Style)Application.Current.Resources["UiCheckBox"]
    };

    private static Button MakeButton(string text, double minWidth = 112) => new()
    {
        Content = text,
        Style = (Style)Application.Current.Resources["UiButton"],
        MinWidth = minWidth
    };
}
