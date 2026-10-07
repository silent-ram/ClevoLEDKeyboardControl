using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using ColorfulLedKeyboard.Core;
using ColorfulLedKeyboard.Tray.Wpf.Controls;

namespace ColorfulLedKeyboard.Tray.Wpf.Pages;

/// <summary>场景自动化页：运行状态、场景规则编辑器、空闲最终覆盖。</summary>
public sealed class AutomationPage : UserControl
{
    private readonly TextBlock _statusText = MakeStatusText();

    private static TextBlock MakeStatusText()
    {
        var textBlock = new TextBlock { TextWrapping = TextWrapping.Wrap };
        textBlock.SetResourceReference(TextBlock.ForegroundProperty, "Brush.MutedText");
        return textBlock;
    }
    private readonly System.Windows.Controls.CheckBox _automationEnabled = MakeCheck("auto.enable");
    private readonly SceneAutomationEditor _editor = new();
    private readonly System.Windows.Controls.CheckBox _idleEnabled = MakeCheck("auto.idle.enable");
    private readonly System.Windows.Controls.ComboBox _idleAfter = MakeCombo(new[] { AppStrings.Format("auto.minutes", 1), AppStrings.Format("auto.minutes", 3), AppStrings.Format("auto.minutes", 5), AppStrings.Format("auto.minutes", 10), AppStrings.Format("auto.minutes", 30) });
    private readonly UiSliderRow _idleBrightness = new("auto.idle.brightness", 0, 100, "%", keyLabel: true);
    private readonly System.Windows.Controls.CheckBox _idleTurnOff = MakeCheck("auto.idle.off");
    private readonly Button _simulator = MakeButton("auto.simulator", 150);

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
            Text = AppStrings.Get("auto.priority"),
            FontWeight = FontWeights.Bold,
            Margin = new Thickness(0, 4, 0, 0)
        };
        priority.SetResourceReference(TextBlock.ForegroundProperty, "Brush.Primary");
        var simulatorRow = new Grid { MinHeight = 40, MaxWidth = UiMetrics.ContentWidth };
        _simulator.HorizontalAlignment = HorizontalAlignment.Left;
        simulatorRow.Children.Add(_simulator);

        var stack = new StackPanel { Margin = new Thickness(18, 18, 18, 28), MaxWidth = 832 };
        stack.Children.Add(MakeCard("auto.card.status", _statusText, priority, simulatorRow));
        stack.Children.Add(MakeCard("auto.card.rules", PlainRow(_automationEnabled), Indent(_editor)));
        stack.Children.Add(MakeCard("auto.card.idle", PlainRow(_idleEnabled), Row("auto.idle.after", _idleAfter),
            _idleBrightness, PlainRow(_idleTurnOff)));

        Content = new ScrollViewer
        {
            Style = (Style)Application.Current.Resources["DarkScrollViewer"],
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

    /// <summary>截图验收专用：切换编辑器页签。</summary>
    public void SelectEditorTab(int index) => _editor.SelectTab(index);

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
            _statusText.Text = AppStrings.Get("auto.status.stale");
            return;
        }

        var foreground = status.ForegroundAvailable
            ? AppStrings.Format("auto.status.foreground", status.ForegroundProcessName)
            : AppStrings.Get("auto.status.unavailable");
        var active = string.IsNullOrWhiteSpace(status.ActiveRuleName)
            ? AppStrings.Get("auto.status.base")
            : AppStrings.Format("auto.status.rule", status.ActiveRuleName, status.TargetDescription);
        var idle = status.IdleOverrideActive ? AppStrings.Get("auto.status.idleOverride") : "";
        var invalid = string.IsNullOrWhiteSpace(status.InvalidReason) ? "" : AppStrings.Format("auto.status.hint", status.InvalidReason);
        var audio = string.IsNullOrWhiteSpace(status.ActiveMusicApplication)
            ? AppStrings.Get("auto.status.noAudio")
            : AppStrings.Format("auto.status.audio", status.ActiveMusicApplication, string.Join(",", status.ActiveProcessIds));
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
            Width = 280,
            HorizontalAlignment = HorizontalAlignment.Left
        };
        combo.SetResourceReference(StyleProperty, "UiComboBox");
        foreach (var item in items) combo.Items.Add(item);
        return combo;
    }

    private static System.Windows.Controls.CheckBox MakeCheck(string key)
    {
        var check = new System.Windows.Controls.CheckBox { Content = AppStrings.Get(key) };
        check.SetResourceReference(StyleProperty, "UiCheckBox");
        return check;
    }

    private static Button MakeButton(string key, double minWidth = 112)
    {
        var button = new Button { Content = AppStrings.Get(key), MinWidth = minWidth };
        button.SetResourceReference(StyleProperty, "UiButton");
        return button;
    }
}
