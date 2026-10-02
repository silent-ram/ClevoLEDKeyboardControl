using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Threading;

namespace ColorfulLedKeyboard.Tray.Wpf.Theming;

/// <summary>
/// 滚动条自动隐藏行为：滚动或鼠标悬停时亮起，停止约 1.2 秒后淡出为淡影。
/// 用法：在 ScrollViewer 样式或实例上设置 AutoHideScroll.Enabled=True。
/// </summary>
public static class AutoHideScroll
{
    private static readonly Dictionary<ScrollBar, DispatcherTimer> Timers = new();

    public static readonly System.Windows.DependencyProperty EnabledProperty =
        System.Windows.DependencyProperty.RegisterAttached(
            "Enabled", typeof(bool), typeof(AutoHideScroll),
            new System.Windows.PropertyMetadata(false, OnEnabledChanged));

    public static bool GetEnabled(System.Windows.DependencyObject o) => (bool)o.GetValue(EnabledProperty);
    public static void SetEnabled(System.Windows.DependencyObject o, bool v) => o.SetValue(EnabledProperty, v);

    private static void OnEnabledChanged(System.Windows.DependencyObject d, System.Windows.DependencyPropertyChangedEventArgs e)
    {
        if (d is not ScrollViewer viewer) return;
        if ((bool)e.NewValue)
        {
            viewer.ScrollChanged += OnScrollChanged;
            viewer.MouseEnter += (_, _) => Show(viewer);
            viewer.MouseLeave += (_, _) => ScheduleFade(viewer);
        }
        else
        {
            viewer.ScrollChanged -= OnScrollChanged;
            viewer.MouseEnter -= (_, _) => Show(viewer);
            viewer.MouseLeave -= (_, _) => ScheduleFade(viewer);
        }
    }

    private static void OnScrollChanged(object sender, ScrollChangedEventArgs e)
    {
        if (sender is ScrollViewer viewer) Show(viewer);
    }

    private static void Show(ScrollViewer viewer)
    {
        if (FindBar(viewer) is not { } bar) return;
        bar.Opacity = 1;
        GetTimer(bar).Stop();
    }

    private static void ScheduleFade(ScrollViewer viewer)
    {
        if (FindBar(viewer) is not { } bar) return;
        var timer = GetTimer(bar);
        timer.Stop();
        timer.Start();
    }

    private static ScrollBar? FindBar(ScrollViewer viewer) =>
        viewer.Template?.FindName("PART_VerticalScrollBar", viewer) as ScrollBar;

    private static DispatcherTimer GetTimer(ScrollBar bar)
    {
        if (!Timers.TryGetValue(bar, out var timer))
        {
            timer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(1200) };
            timer.Tick += (_, _) =>
            {
                timer.Stop();
                if (!bar.IsMouseOver) bar.Opacity = 0.35;
            };
            Timers[bar] = timer;
        }
        return timer;
    }
}
