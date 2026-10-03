using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Media;
using ColorfulLedKeyboard.Core;

namespace ColorfulLedKeyboard.Tray.Wpf.Controls;

/// <summary>循环颜色列表：色块 + HEX 行 + 删除/上移/下移/随机排序（对应 WinForms SequenceEditor）。</summary>
public sealed class UiSequenceEditor : UserControl
{
    private readonly ListBox _list;
    private readonly StackPanel _buttonColumn;
    private bool _suppressEvents;

    public event EventHandler? ColorsChanged;

    public UiSequenceEditor()
    {
        _list = new ListBox
        {
            MinHeight = 96,
            MaxHeight = 168,
            BorderThickness = new Thickness(1)
        };
        _list.SetResourceReference(BackgroundProperty, "Brush.Field");
        _list.SetResourceReference(BorderBrushProperty, "Brush.Border");

        var root = new Grid();
        root.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        root.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

        _list.ItemTemplate = ColorRowTemplate();
        var itemStyle = new Style(typeof(ListBoxItem));
        itemStyle.Setters.Add(new Setter(PaddingProperty, new Thickness(8, 5, 8, 5)));
        itemStyle.Setters.Add(new Setter(ForegroundProperty, new DynamicResourceExtension("Brush.Text")));
        var selectedTrigger = new Trigger { Property = ListBoxItem.IsSelectedProperty, Value = true };
        selectedTrigger.Setters.Add(new Setter(BackgroundProperty, new DynamicResourceExtension("Brush.PrimarySoft")));
        itemStyle.Triggers.Add(selectedTrigger);
        var hoverTrigger = new Trigger { Property = UIElement.IsMouseOverProperty, Value = true };
        hoverTrigger.Setters.Add(new Setter(BackgroundProperty, new DynamicResourceExtension("Brush.Hover")));
        itemStyle.Triggers.Add(hoverTrigger);
        _list.ItemContainerStyle = itemStyle;
        root.Children.Add(_list);

        _buttonColumn = new StackPanel { Orientation = Orientation.Vertical, Margin = new Thickness(10, 0, 0, 0) };
        AddButton("删除选中", RemoveSelected);
        AddButton("上移", () => Move(-1));
        AddButton("下移", () => Move(+1));
        AddButton("随机排序", Shuffle);
        Grid.SetColumn(_buttonColumn, 1);
        root.Children.Add(_buttonColumn);
        Content = root;
    }

    public List<string> Colors
    {
        get => _list.Items.OfType<string>().ToList();
        set
        {
            _suppressEvents = true;
            _list.Items.Clear();
            foreach (var color in value) _list.Items.Add(color);
            _suppressEvents = false;
        }
    }

    public new bool IsEnabled
    {
        get => base.IsEnabled;
        set
        {
            base.IsEnabled = value;
            Opacity = value ? 1 : 0.45;
        }
    }

    private DataTemplate ColorRowTemplate()
    {
        var template = new DataTemplate();
        var factory = new FrameworkElementFactory(typeof(StackPanel));
        factory.SetValue(StackPanel.OrientationProperty, Orientation.Horizontal);

        var swatch = new FrameworkElementFactory(typeof(Border));
        swatch.Name = "Swatch";
        swatch.SetValue(Border.WidthProperty, 22.0);
        swatch.SetValue(Border.HeightProperty, 16.0);
        swatch.SetValue(Border.CornerRadiusProperty, new CornerRadius(3));
        swatch.SetValue(Border.BorderBrushProperty, new DynamicResourceExtension("Brush.Border"));
        swatch.SetValue(Border.BorderThicknessProperty, new Thickness(1));
        swatch.SetBinding(Border.BackgroundProperty, new Binding("."));
        factory.AppendChild(swatch);

        var text = new FrameworkElementFactory(typeof(TextBlock));
        text.SetBinding(TextBlock.TextProperty, new Binding("."));
        text.SetValue(TextBlock.MarginProperty, new Thickness(10, 0, 0, 0));
        text.SetValue(TextBlock.VerticalAlignmentProperty, VerticalAlignment.Center);
        factory.AppendChild(text);

        template.VisualTree = factory;
        return template;
    }

    private void AddButton(string label, System.Action onClick)
    {
        var button = new Button
        {
            Content = label,
            MinWidth = 96,
            Margin = new Thickness(0, 0, 0, 8)
        };
        button.SetResourceReference(StyleProperty, "UiButton");
        button.Click += (_, _) => onClick();
        _buttonColumn.Children.Add(button);
    }

    private void RemoveSelected()
    {
        var index = _list.SelectedIndex;
        if (index < 0) return;
        _list.Items.RemoveAt(index);
        NotifyChanged();
    }

    private void Move(int delta)
    {
        var index = _list.SelectedIndex;
        var target = index + delta;
        if (index < 0 || target < 0 || target >= _list.Items.Count) return;
        var item = _list.Items[index]!;
        _list.Items.RemoveAt(index);
        _list.Items.Insert(target, item);
        _list.SelectedIndex = target;
        NotifyChanged();
    }

    private void Shuffle()
    {
        var colors = Colors;
        if (colors.Count < 2) return;
        var random = new Random();
        for (var i = colors.Count - 1; i > 0; i--)
        {
            var j = random.Next(i + 1);
            (colors[i], colors[j]) = (colors[j], colors[i]);
        }
        Colors = colors;
        NotifyChanged();
    }

    /// <summary>宿主在取色对话框确定后回填颜色。</summary>
    public void SetColorsNotify(List<string> colors)
    {
        Colors = colors;
        NotifyChanged();
    }

    private void NotifyChanged()
    {
        if (!_suppressEvents) ColorsChanged?.Invoke(this, EventArgs.Empty);
    }
}
