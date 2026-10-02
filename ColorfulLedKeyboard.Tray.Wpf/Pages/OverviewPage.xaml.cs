using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;

namespace ColorfulLedKeyboard.Tray.Wpf.Pages;

public partial class OverviewPage : UserControl
{
    public OverviewPage()
    {
        InitializeComponent();
        OpenLighting.Click += (_, _) => RequestPage(1);
        OpenMusic.Click += (_, _) => RequestPage(2);
        OpenAutomation.Click += (_, _) => RequestPage(3);
    }

    /// <summary>WinForms 版同款交互：快捷按钮跳转导航页，经宿主窗口处理。</summary>
    public event EventHandler<int>? PageRequested;

    private void RequestPage(int index) => PageRequested?.Invoke(this, index);

    public void SetRuntime(string mode, string brightness, string brightnessHint, string rule, string player, string events)
    {
        ModeValue.Text = mode;
        BrightnessValue.Text = brightness;
        BrightnessHint.Text = brightnessHint;
        RuleValue.Text = rule;
        PlayerValue.Text = player;
        EventsValue.Text = events;
    }

    public void SetServiceState(string serviceStatus, bool componentReady)
    {
        var ready = serviceStatus == "运行中" && componentReady;
        ServiceValue.Text = ready ? "输出正常" : "需要检查";
        ServiceValue.Foreground = (Brush)Application.Current.TryFindResource(
            ready ? "Brush.Success" : "Brush.Warning");
    }
}
