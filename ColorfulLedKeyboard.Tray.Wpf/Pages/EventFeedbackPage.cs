using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using ColorfulLedKeyboard.Core;
using ColorfulLedKeyboard.Tray.Wpf.Controls;

namespace ColorfulLedKeyboard.Tray.Wpf.Pages;

/// <summary>事件反馈页：敲字闪烁、通知闪烁（WinForms BuildEventFeedbackPage 移植件）。</summary>
public sealed class EventFeedbackPage : UserControl
{
    private readonly System.Windows.Controls.CheckBox _typingPulseEnabled = MakeCheck("启用敲字闪烁");
    private readonly UiSliderRow _typingPulsePeakBrightness = new("触发亮度", 0, 100, "%");
    private readonly UiSliderRow _typingPulseHold = new("保持时间", 20, 2000, " ms");
    private readonly UiSliderRow _typingPulseFade = new("回落时间", 50, 5000, " ms");
    private readonly System.Windows.Controls.CheckBox _notificationFlashEnabled = MakeCheck("收到 Windows 通知时闪烁键盘");
    private readonly UiColorPickerRow _notificationFlashColor = new(compact: false);
    private readonly UiSliderRow _notificationFlashPulses = new("闪烁次数", 1, 5, " 次");
    private readonly UiSliderRow _notificationFlashCooldown = new("冷却时间", 1, 60, " 秒");

    public event EventHandler? Changed;

    public bool IsDirty { get; private set; }

    /// <summary>保存成功后由宿主调用，复位脏状态。</summary>
    public void ResetDirty() => IsDirty = false;

    public EventFeedbackPage()
    {
        _typingPulseEnabled.Checked += (_, _) => { MarkChanged(); UpdateVisibility(); };
        _typingPulseEnabled.Unchecked += (_, _) => { MarkChanged(); UpdateVisibility(); };
        _notificationFlashEnabled.Checked += (_, _) => { MarkChanged(); UpdateVisibility(); };
        _notificationFlashEnabled.Unchecked += (_, _) => { MarkChanged(); UpdateVisibility(); };
        foreach (var slider in new[] { _typingPulsePeakBrightness, _typingPulseHold, _typingPulseFade, _notificationFlashPulses, _notificationFlashCooldown })
            slider.ValueChanged += (_, _) => MarkChanged();
        _notificationFlashColor.ColorChanged += (_, _) => MarkChanged();
        // 非紧凑模式的"选择..."/色块点击打开取色对话框（WinForms ColorPickerRow 内聚行为）。
        _notificationFlashColor.PickColorRequested += (_, _) => PickNotificationColor();

        var typingRows = new[]
        {
            PlainRow(_typingPulseEnabled), RowHost(_typingPulsePeakBrightness),
            RowHost(_typingPulseHold), RowHost(_typingPulseFade)
        };
        var notificationRows = new[]
        {
            PlainRow(_notificationFlashEnabled), RowHost(_notificationFlashColor),
            RowHost(_notificationFlashPulses), RowHost(_notificationFlashCooldown)
        };
        _typingRows = typingRows;
        _notificationRows = notificationRows;

        var stack = new StackPanel { Margin = new Thickness(18, 18, 18, 28), MaxWidth = 832, HorizontalAlignment = HorizontalAlignment.Left };
        stack.Children.Add(MakeCard("敲字反馈", typingRows));
        stack.Children.Add(MakeCard("通知反馈", notificationRows));
        stack.Children.Add(MakeCard("当前覆盖关系", new TextBlock
        {
            Text = "事件策略按“全局 → 音乐规则 → 前台灯效规则”覆盖；空闲关灯会抑制包括通知在内的全部输出。",
            TextWrapping = TextWrapping.Wrap,
            Foreground = (Brush)Application.Current.Resources["Brush.MutedText"]
        }));

        Content = new ScrollViewer
        {
            Content = stack,
            VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
            HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled
        };
        UpdateVisibility();
    }

    private readonly UIElement[] _typingRows;
    private readonly UIElement[] _notificationRows;

    private bool _loadingSettings;

    public void LoadFromStore(KeyboardSettings settings)
    {
        _loadingSettings = true;
        IsDirty = false;
        _typingPulseEnabled.IsChecked = settings.TypingPulse.Enabled;
        _typingPulsePeakBrightness.Value = settings.TypingPulse.PeakBrightness;
        _typingPulseHold.Value = settings.TypingPulse.HoldMs;
        _typingPulseFade.Value = settings.TypingPulse.FadeMs;
        _notificationFlashEnabled.IsChecked = settings.NotificationFlash.Enabled;
        _notificationFlashColor.ColorHex = settings.NotificationFlash.Color;
        _notificationFlashPulses.Value = settings.NotificationFlash.Pulses;
        _notificationFlashCooldown.Value = settings.NotificationFlash.CooldownSeconds;
        UpdateVisibility();
        _loadingSettings = false;
    }

    public void ApplyTo(KeyboardSettings settings)
    {
        settings.TypingPulse.Enabled = _typingPulseEnabled.IsChecked == true;
        settings.TypingPulse.PeakBrightness = _typingPulsePeakBrightness.Value;
        settings.TypingPulse.HoldMs = _typingPulseHold.Value;
        settings.TypingPulse.FadeMs = _typingPulseFade.Value;
        settings.NotificationFlash.Enabled = _notificationFlashEnabled.IsChecked == true;
        settings.NotificationFlash.Color = _notificationFlashColor.ColorHex;
        settings.NotificationFlash.Pulses = _notificationFlashPulses.Value;
        settings.NotificationFlash.CooldownSeconds = _notificationFlashCooldown.Value;
    }

    private void PickNotificationColor()
    {
        var dialog = new Dialogs.ColorSelectionDialog(
            UiColorPickerRow.TryParse(_notificationFlashColor.ColorHex, out var current)
                ? new List<string> { current.Hex }
                : new List<string> { "#FF0000" }, singleSelection: true)
        { Owner = Window.GetWindow(this) };
        if (dialog.ShowDialog() != true) return;
        var color = dialog.SelectedColors.FirstOrDefault();
        if (!string.IsNullOrWhiteSpace(color)) _notificationFlashColor.ColorHex = color;
    }

    private void MarkChanged()
    {
        if (_loadingSettings) return;
        IsDirty = true;
        Changed?.Invoke(this, EventArgs.Empty);
    }

    private void UpdateVisibility()
    {
        var typing = _typingPulseEnabled.IsChecked == true;
        foreach (var row in _typingRows.Skip(1)) row.Visibility = typing ? Visibility.Visible : Visibility.Collapsed;
        var notification = _notificationFlashEnabled.IsChecked == true;
        foreach (var row in _notificationRows.Skip(1)) row.Visibility = notification ? Visibility.Visible : Visibility.Collapsed;
    }

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
        control.VerticalAlignment = VerticalAlignment.Center;
        control.HorizontalAlignment = HorizontalAlignment.Left;
        grid.Children.Add(control);
        return grid;
    }

    private static System.Windows.Controls.CheckBox MakeCheck(string text) => new()
    {
        Content = text,
        Style = (Style)Application.Current.Resources["UiCheckBox"]
    };
}
