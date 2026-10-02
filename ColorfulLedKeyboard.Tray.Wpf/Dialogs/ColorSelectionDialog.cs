using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using ColorfulLedKeyboard.Core;
using ColorfulLedKeyboard.Tray.Wpf.Controls;
using System.Windows.Data;

namespace ColorfulLedKeyboard.Tray.Wpf.Dialogs;

/// <summary>
/// 自定义颜色对话框：WPF 重写版（对应 WinForms ColorSelectionDialog）。
/// 基础颜色网格 + HSV/RGB/HEX 编辑器双向同步；单选/多选两种模式。
/// </summary>
public sealed class ColorSelectionDialog : Window
{
    private sealed class ColorChoiceVm
    {
        public ColorChoiceVm(RgbColor installDefault)
        {
            InstallDefault = installDefault;
            Current = installDefault;
        }

        public RgbColor InstallDefault { get; }

        private RgbColor _current;
        public RgbColor Current
        {
            get => _current;
            set { _current = value; Raise(nameof(Current)); Raise(nameof(CurrentBrush)); }
        }

        private bool _checked;
        public bool Checked
        {
            get => _checked;
            set { _checked = value; Raise(nameof(Checked)); }
        }

        public event System.ComponentModel.PropertyChangedEventHandler? PropertyChanged;
        private void Raise([System.Runtime.CompilerServices.CallerMemberName] string name = "") =>
            PropertyChanged?.Invoke(this, new System.ComponentModel.PropertyChangedEventArgs(name));

        // Background 只接受 Brush，Color 结构直接绑定会静默失败（格子显示为空）
        public SolidColorBrush CurrentBrush =>
            new(Color.FromRgb(Current.R, Current.G, Current.B));
    }

    private readonly bool _singleSelection;
    private readonly List<ColorChoiceVm> _choices;
    private readonly ItemsControl _grid;
    private readonly SvPlane _plane = new();
    private readonly HueBar _hueBar = new();
    private readonly Border _preview = new() { BorderBrush = Brushes.DimGray, BorderThickness = new Thickness(1), Width = 72, Height = 44 };
    private readonly System.Windows.Controls.TextBox _hex = new()
    {
        Width = 84,
        Style = (Style)Application.Current.Resources["UiTextBox"]
    };
    private readonly System.Windows.Controls.TextBox _red = SmallBox();
    private readonly System.Windows.Controls.TextBox _green = SmallBox();
    private readonly System.Windows.Controls.TextBox _blue = SmallBox();
    private readonly System.Windows.Controls.TextBox _hue = SmallBox();
    private readonly System.Windows.Controls.TextBox _saturation = SmallBox();
    private readonly System.Windows.Controls.TextBox _value = SmallBox();
    private bool _updatingInputs;
    private double _currentHue;
    private double _currentSaturation = 1;
    private double _currentValue = 1;
    private ColorChoiceVm? _selected;

    public ColorSelectionDialog(IReadOnlyCollection<string> selectedColors, bool singleSelection)
    {
        _singleSelection = singleSelection;
        _choices = BuildChoices(selectedColors);

        Title = "自定义颜色";
        WindowStartupLocation = WindowStartupLocation.CenterOwner;
        ResizeMode = ResizeMode.NoResize;
        ShowInTaskbar = false;
        Width = 780;
        SizeToContent = SizeToContent.Height;
        MinHeight = 420;
        Background = (Brush)Application.Current.Resources["Brush.Window"];
        FontFamily = (FontFamily)Application.Current.Resources["Font.Body"];
        FontSize = 12;
        Foreground = (Brush)Application.Current.Resources["Brush.Text"];
        SourceInitialized += (_, _) => WpfThemeManager.ApplyTitleBarMode(this);

        var grid = new Grid { Margin = new Thickness(18) };
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(330) });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        grid.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
        grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });

        var leftTitle = SectionText("基础颜色");
        Grid.SetRow(leftTitle, 0);
        grid.Children.Add(leftTitle);

        _grid = new ItemsControl { Margin = new Thickness(0, 10, 0, 0) };
        _grid.ItemsPanel = ItemsPanel();
        _grid.ItemTemplate = ChoiceTemplate();
        Grid.SetRow(_grid, 1);
        Grid.SetColumnSpan(_grid, 1);
        grid.Children.Add(_grid);
        _grid.ItemsSource = _choices;

        var rightTitle = SectionText("颜色参数");
        Grid.SetRow(rightTitle, 0);
        Grid.SetColumn(rightTitle, 1);
        grid.Children.Add(rightTitle);

        var editor = BuildEditor();
        Grid.SetRow(editor, 1);
        Grid.SetColumn(editor, 1);
        Grid.SetRowSpan(editor, 2);
        grid.Children.Add(editor);

        var buttons = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            HorizontalAlignment = HorizontalAlignment.Right,
            Margin = new Thickness(0, 14, 0, 0)
        };
        var cancel = new Button { Content = "取消", Style = ButtonStyle(), MinWidth = 96 };
        cancel.Click += (_, _) => Close();
        var ok = new Button { Content = "确定", Style = ButtonStyle(), MinWidth = 96, Margin = new Thickness(12, 0, 0, 0) };
        ok.Click += (_, _) => Accept();
        buttons.Children.Add(cancel);
        buttons.Children.Add(ok);
        Grid.SetRow(buttons, 3);
        Grid.SetColumnSpan(buttons, 2);
        grid.Children.Add(buttons);

        Content = grid;
        _selected = _choices.FirstOrDefault(item => item.Checked);
        LoadSelectedChoice();
    }

    public List<string> SelectedColors =>
        _choices.Where(choice => choice.Checked).Select(choice => choice.Current.Hex).ToList();

    private static TextBlock SectionText(string text) => new()
    {
        Text = text,
        FontWeight = FontWeights.Bold,
        Margin = new Thickness(0, 0, 0, 4)
    };

    private static Style ButtonStyle() => (Style)Application.Current.Resources["UiButton"];

    private static System.Windows.Controls.TextBox SmallBox() => new()
    {
        Width = 62,
        Style = (Style)Application.Current.Resources["UiTextBox"]
    };

    private ItemsPanelTemplate ItemsPanel()
    {
        var template = new ItemsPanelTemplate();
        var factory = new FrameworkElementFactory(typeof(WrapPanel));
        factory.SetValue(WrapPanel.ItemWidthProperty, 37.0);
        factory.SetValue(WrapPanel.ItemHeightProperty, 37.0);
        template.VisualTree = factory;
        return template;
    }

    private DataTemplate ChoiceTemplate()
    {
        var template = new DataTemplate();
        var gridFactory = new FrameworkElementFactory(typeof(Grid));
        gridFactory.SetValue(Grid.WidthProperty, 34.0);
        gridFactory.SetValue(Grid.HeightProperty, 34.0);

        var swatch = new FrameworkElementFactory(typeof(Border));
        swatch.Name = "Swatch";
        swatch.SetValue(Border.CornerRadiusProperty, new CornerRadius(4));
        swatch.SetValue(Border.BorderBrushProperty, (Brush)Application.Current.Resources["Brush.Border"]);
        swatch.SetValue(Border.BorderThicknessProperty, new Thickness(1));
        swatch.SetBinding(Border.BackgroundProperty, new Binding("CurrentBrush"));
        swatch.SetValue(Grid.RowProperty, 0);
        swatch.AddHandler(Border.MouseDownEvent,
            new MouseButtonEventHandler((sender, _) => OnCellClicked((ColorChoiceVm)((FrameworkElement)sender).DataContext)));
        gridFactory.AppendChild(swatch);

        // 勾选框纯显示（不可点）：勾选状态只在容器的点击处理函数里变更——
        // 与 WinForms ColorGrid 相同的"单点变更"模型，不存在事件时序导致的多勾。
        var check = new FrameworkElementFactory(typeof(CheckBox));
        check.SetBinding(CheckBox.IsCheckedProperty, new Binding("Checked"));
        check.SetValue(HorizontalAlignmentProperty, HorizontalAlignment.Right);
        check.SetValue(VerticalAlignmentProperty, VerticalAlignment.Top);
        check.SetValue(UIElement.IsHitTestVisibleProperty, false);
        check.SetValue(FrameworkElement.FocusableProperty, false);
        gridFactory.AppendChild(check);

        template.VisualTree = gridFactory;
        return template;
    }

    private UIElement BuildEditor()
    {
        var grid = new Grid { Margin = new Thickness(24, 10, 0, 0) };
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });

        _plane.Width = 200;
        _plane.Height = 190;
        grid.Children.Add(_plane);
        _hueBar.Margin = new Thickness(10, 0, 0, 0);
        Grid.SetColumn(_hueBar, 1);
        grid.Children.Add(_hueBar);

        _plane.ColorPicked += (_, _) =>
        {
            if (_updatingInputs) return;
            _currentSaturation = _plane.Saturation;
            _currentValue = _plane.Value;
            ApplyHsvToCurrentColor();
        };
        _hueBar.HuePicked += (_, _) =>
        {
            if (_updatingInputs) return;
            _currentHue = _hueBar.Hue;
            _plane.Hue = _currentHue;
            ApplyHsvToCurrentColor();
        };

        var previewRow = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 12, 0, 0) };
        previewRow.Children.Add(_preview);
        var hexColumn = new StackPanel { Margin = new Thickness(12, 0, 0, 0) };
        hexColumn.Children.Add(new TextBlock { Text = "HEX", Foreground = FindBrush("Brush.MutedText") });
        _hex.Margin = new Thickness(0, 2, 0, 0);
        hexColumn.Children.Add(_hex);
        _hex.TextChanged += (_, _) => ApplyHexInput();
        previewRow.Children.Add(hexColumn);
        Grid.SetRow(previewRow, 1);
        grid.Children.Add(previewRow);

        // R/G/B 与 H/S/V 两列并排（对齐 WinForms 布局），不再挤成一竖列
        var numericGrid = new Grid { Margin = new Thickness(0, 12, 0, 0) };
        numericGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        numericGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(28) });
        numericGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        var rgbStack = new StackPanel();
        AddNumericRow(rgbStack, "R", _red, ApplyRgbInput);
        AddNumericRow(rgbStack, "G", _green, ApplyRgbInput);
        AddNumericRow(rgbStack, "B", _blue, ApplyRgbInput);
        var hsvStack = new StackPanel();
        AddNumericRow(hsvStack, "H", _hue, ApplyHsvInput);
        AddNumericRow(hsvStack, "S", _saturation, ApplyHsvInput);
        AddNumericRow(hsvStack, "V", _value, ApplyHsvInput);
        Grid.SetColumn(rgbStack, 0);
        Grid.SetColumn(hsvStack, 2);
        numericGrid.Children.Add(rgbStack);
        numericGrid.Children.Add(hsvStack);
        Grid.SetRow(numericGrid, 2);
        Grid.SetColumnSpan(numericGrid, 2);
        grid.Children.Add(numericGrid);

        var restore = new Button { Content = "恢复默认", Style = ButtonStyle(), MinWidth = 96 };
        restore.Click += (_, _) => RestoreSelectedDefault();
        restore.Margin = new Thickness(0, 12, 0, 0);
        restore.HorizontalAlignment = HorizontalAlignment.Left;
        Grid.SetRow(restore, 1);
        Grid.SetColumn(restore, 1);
        grid.Children.Add(restore);

        return grid;
    }

    private static void AddNumericRow(StackPanel panel, string label, System.Windows.Controls.TextBox box, System.Action onInput)
    {
        var row = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 2, 0, 2) };
        row.Children.Add(new TextBlock
        {
            Text = label,
            Width = 20,
            VerticalAlignment = VerticalAlignment.Center,
            Foreground = (Brush)Application.Current.Resources["Brush.Text"]
        });
        box.Margin = new Thickness(8, 0, 0, 0);
        row.Children.Add(box);
        panel.Children.Add(row);
        box.TextChanged += (_, _) => onInput();
    }

    private static Brush FindBrush(string key) => (Brush)Application.Current.Resources[key];

    private static List<ColorChoiceVm> BuildChoices(IReadOnlyCollection<string> selectedColors)
    {
        var installPalette = InstallPalette();
        var choices = installPalette.Select(color => new ColorChoiceVm(color)).ToList();
        var normalizedSelected = selectedColors
            .Select(color => RgbColor.FromHex(color).Hex)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();
        var selectedDefaults = new HashSet<string>(normalizedSelected, StringComparer.OrdinalIgnoreCase);

        foreach (var choice in choices)
        {
            if (selectedDefaults.Contains(choice.InstallDefault.Hex))
            {
                choice.Checked = true;
            }
        }

        foreach (var selected in normalizedSelected.Where(selected =>
            choices.All(choice => !string.Equals(choice.InstallDefault.Hex, selected, StringComparison.OrdinalIgnoreCase))))
        {
            var replacement = choices.FirstOrDefault(choice => !choice.Checked);
            if (replacement is null)
            {
                replacement = new ColorChoiceVm(installPalette[choices.Count % installPalette.Count]);
                choices.Add(replacement);
            }

            replacement.Current = RgbColor.FromHex(selected);
            replacement.Checked = true;
        }

        return choices;
    }

    private static List<RgbColor> InstallPalette()
    {
        var colors = new List<RgbColor>();
        var hues = new[] { 0, 30, 60, 90, 120, 150, 180, 210, 240, 270, 300, 330 };
        foreach (var value in new[] { 1.0, 0.72, 0.46 })
        {
            foreach (var hue in hues)
            {
                colors.Add(RgbColor.FromHsv(hue, 1, value));
            }
        }

        colors.AddRange(
        [
            new RgbColor(255, 255, 255),
            new RgbColor(224, 224, 224),
            new RgbColor(192, 192, 192),
            new RgbColor(160, 160, 160),
            new RgbColor(128, 128, 128),
            new RgbColor(96, 96, 96),
            new RgbColor(64, 64, 64),
            new RgbColor(0, 0, 0),
            new RgbColor(255, 210, 161),
            new RgbColor(207, 232, 255),
            new RgbColor(255, 180, 220),
            new RgbColor(180, 255, 210)
        ]);

        return colors;
    }

    private void LoadSelectedChoice()
    {
        if (_selected is { } choice)
        {
            SetEditorColor(choice.Current);
        }
    }

    /// <summary>
    /// 唯一的勾选/选中变更入口（对应 WinForms ColorGrid.OnMouseDown）：
    /// 单选 = 清其余勾选 + 勾中此格；多选 = 翻转此格。
    /// 勾选框纯显示不可点，不存在第二条会改勾选状态的路径。
    /// </summary>
    private void OnCellClicked(ColorChoiceVm choice)
    {
        if (_singleSelection)
        {
            foreach (var item in _choices.Where(item => item != choice)) item.Checked = false;
            choice.Checked = true;
            _selected = choice;
        }
        else
        {
            choice.Checked = !choice.Checked;
            if (choice.Checked) _selected = choice;
        }
        // 用户点击触发（不在容器生成期），全量重绘安全且确定性
        _grid.Items.Refresh();
        SetEditorColor(_selected!.Current);
    }

    private static (double Hue, double Saturation, double Value) ToHsv(RgbColor color)
    {
        var r = color.R / 255d;
        var g = color.G / 255d;
        var b = color.B / 255d;
        var max = Math.Max(r, Math.Max(g, b));
        var min = Math.Min(r, Math.Min(g, b));
        var delta = max - min;
        var hue = delta switch
        {
            0 => 0,
            _ when max == r => 60 * (((g - b) / delta) % 6),
            _ when max == g => 60 * ((b - r) / delta + 2),
            _ => 60 * ((r - g) / delta + 4)
        };

        if (hue < 0)
        {
            hue += 360;
        }

        var saturation = max == 0 ? 0 : delta / max;
        return (hue, saturation, max);
    }

    private void SetEditorColor(RgbColor color)
    {
        var hsv = ToHsv(color);
        _currentHue = hsv.Hue;
        _currentSaturation = hsv.Saturation;
        _currentValue = hsv.Value;

        _updatingInputs = true;
        try
        {
            _plane.Hue = _currentHue;
            _plane.SetPointer(_currentSaturation, _currentValue);
            _hueBar.SetHue(_currentHue);
            _preview.Background = new SolidColorBrush(Color.FromRgb(color.R, color.G, color.B));
            _hex.Text = color.Hex;
            _hex.Background = FindBrush("Brush.Field");
            _red.Text = color.R.ToString();
            _green.Text = color.G.ToString();
            _blue.Text = color.B.ToString();
            _hue.Text = Math.Clamp(Math.Round(_currentHue), 0, 359).ToString();
            _saturation.Text = Math.Clamp(Math.Round(_currentSaturation * 100), 0, 100).ToString();
            _value.Text = Math.Clamp(Math.Round(_currentValue * 100), 0, 100).ToString();
        }
        finally
        {
            _updatingInputs = false;
        }
    }

    private void ApplyHexInput()
    {
        if (_updatingInputs) return;
        if (!UiColorPickerRow.TryParse(_hex.Text, out var color))
        {
            _hex.Background = FindBrush("Brush.FieldInvalid");
            return;
        }
        _hex.Background = FindBrush("Brush.Field");
        SetCurrentChoiceColor(color);
        SetEditorColor(color);
    }

    private void ApplyRgbInput()
    {
        if (_updatingInputs) return;
        if (!byte.TryParse(_red.Text, out var r) || !byte.TryParse(_green.Text, out var g) || !byte.TryParse(_blue.Text, out var b)) return;
        var color = new RgbColor(r, g, b);
        SetCurrentChoiceColor(color);
        SetEditorColor(color);
    }

    private void ApplyHsvInput()
    {
        if (_updatingInputs) return;
        if (!double.TryParse(_hue.Text, out var hue) ||
            !double.TryParse(_saturation.Text, out var saturation) ||
            !double.TryParse(_value.Text, out var value)) return;
        _currentHue = hue;
        _currentSaturation = saturation / 100d;
        _currentValue = value / 100d;
        ApplyHsvToCurrentColor();
    }

    private void ApplyHsvToCurrentColor()
    {
        var color = RgbColor.FromHsv(_currentHue, _currentSaturation, _currentValue);
        SetCurrentChoiceColor(color);
        SetEditorColor(color);
    }

    private void RestoreSelectedDefault()
    {
        if (_selected is null) return;
        SetCurrentChoiceColor(_selected.InstallDefault);
        SetEditorColor(_selected.InstallDefault);
    }

    private void SetCurrentChoiceColor(RgbColor color)
    {
        if (_selected is null) return;
        _selected.Current = color;
        _grid.Items.Refresh();
    }

    private void Accept()
    {
        var selectedCount = SelectedColors.Count;
        if (_singleSelection && selectedCount != 1)
        {
            System.Windows.MessageBox.Show("请只选择 1 种颜色。", "ClevoLEDKeyboardControl",
                MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }

        if (!_singleSelection && selectedCount < 2)
        {
            System.Windows.MessageBox.Show("请至少选择 2 种颜色。", "ClevoLEDKeyboardControl",
                MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }

        DialogResult = true;
    }
}
