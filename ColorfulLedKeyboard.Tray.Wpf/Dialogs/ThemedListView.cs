using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;

namespace ColorfulLedKeyboard.Tray.Wpf.Dialogs;

/// <summary>
/// ListView 深色化：默认 Aero2 模板的悬停/选中是硬编码的白色/系统蓝，
/// 深色主题里刺眼（真机反馈"鼠标把某一行变成白色"）。这里整体替换为主题色。
/// </summary>
internal static class ThemedListView
{
    public static void Apply(ListView list)
    {
        var foreground = FindBrush("Brush.Text");
        var hover = FindBrush("Brush.Hover");
        var selected = FindBrush("Brush.PrimarySoft");
        var border = FindBrush("Brush.Border");
        var surface = FindBrush("Brush.Surface");

        list.Background = FindBrush("Brush.Field");
        list.Foreground = foreground;
        list.BorderBrush = border;

        // 选中态：替换系统高亮刷（含失焦态）
        list.Resources[SystemColors.HighlightBrushKey] = selected;
        list.Resources[SystemColors.HighlightTextBrushKey] = foreground;
        list.Resources[SystemColors.InactiveSelectionHighlightBrushKey] = selected;
        list.Resources[SystemColors.InactiveSelectionHighlightTextBrushKey] = foreground;

        // 悬停态：默认模板硬编码白色，必须整体替换模板
        var itemTemplate = new ControlTemplate(typeof(ListViewItem));
        var borderFactory = new FrameworkElementFactory(typeof(Border));
        borderFactory.Name = "Bd";
        borderFactory.SetValue(Border.BackgroundProperty, Brushes.Transparent);
        borderFactory.SetValue(Border.PaddingProperty, new Thickness(6, 4, 6, 4));
        // GridView 行必须用 GridViewRowPresenter 按列绑定渲染；普通 ContentPresenter
        // 会把数据项整个 ToString 成一行。
        var presenter = new FrameworkElementFactory(typeof(GridViewRowPresenter));
        borderFactory.AppendChild(presenter);
        itemTemplate.VisualTree = borderFactory;
        var hoverTrigger = new Trigger { Property = UIElement.IsMouseOverProperty, Value = true };
        hoverTrigger.Setters.Add(new Setter(Border.BackgroundProperty, hover, "Bd"));
        itemTemplate.Triggers.Add(hoverTrigger);
        var selectedTrigger = new Trigger { Property = ListBoxItem.IsSelectedProperty, Value = true };
        selectedTrigger.Setters.Add(new Setter(Border.BackgroundProperty, selected, "Bd"));
        itemTemplate.Triggers.Add(selectedTrigger);
        var itemStyle = new Style(typeof(ListViewItem));
        itemStyle.Setters.Add(new Setter(Control.TemplateProperty, itemTemplate));
        itemStyle.Setters.Add(new Setter(Control.ForegroundProperty, foreground));
        itemStyle.Setters.Add(new Setter(Control.CursorProperty, System.Windows.Input.Cursors.Hand));
        list.ItemContainerStyle = itemStyle;

        // 表头：默认是浅色渐变金属条
        var headerTemplate = new ControlTemplate(typeof(GridViewColumnHeader));
        var headerBorder = new FrameworkElementFactory(typeof(Border));
        headerBorder.Name = "HeaderBd";
        headerBorder.SetValue(Border.BackgroundProperty, surface);
        headerBorder.SetValue(Border.BorderBrushProperty, border);
        headerBorder.SetValue(Border.BorderThicknessProperty, new Thickness(0, 0, 1, 1));
        headerBorder.SetValue(Border.PaddingProperty, new Thickness(0));
        var headerPresenter = new FrameworkElementFactory(typeof(ContentPresenter));
        headerPresenter.SetValue(FrameworkElement.MarginProperty, new Thickness(10, 6, 10, 6));
        headerPresenter.SetValue(TextBlock.ForegroundProperty, foreground);
        headerBorder.AppendChild(headerPresenter);
        headerTemplate.VisualTree = headerBorder;
        var headerStyle = new Style(typeof(GridViewColumnHeader));
        headerStyle.Setters.Add(new Setter(Control.TemplateProperty, headerTemplate));
        headerStyle.Setters.Add(new Setter(Control.ForegroundProperty, foreground));
        headerStyle.Setters.Add(new Setter(Control.FontWeightProperty, FontWeights.Bold));
        headerStyle.Setters.Add(new Setter(Control.CursorProperty, System.Windows.Input.Cursors.Arrow));
        list.Resources[typeof(GridViewColumnHeader)] = headerStyle;
    }

    private static Brush FindBrush(string key) => (Brush)Application.Current.Resources[key];
}
