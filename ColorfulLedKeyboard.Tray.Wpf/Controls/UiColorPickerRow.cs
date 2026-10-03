using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using ColorfulLedKeyboard.Core;

namespace ColorfulLedKeyboard.Tray.Wpf.Controls;

/// <summary>颜色选择行：色块 + HEX 输入（紧凑模式），可展开选择按钮与快速调色板（对应 WinForms ColorPickerRow）。</summary>
public sealed class UiColorPickerRow : UserControl
{
    private readonly Button _swatch = new() { Width = 34, Height = 24, Focusable = false };
    private readonly System.Windows.Controls.TextBox _hex = new() { Width = 88 };
    private bool _suppressEvents;
    private bool _invalid;

    public event EventHandler? ColorChanged;

    public UiColorPickerRow(bool compact)
    {
        var row = new StackPanel { Orientation = Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Center };
        var label = new TextBlock
        {
            Text = "颜色",
            Width = 130,
            VerticalAlignment = VerticalAlignment.Center
        };
        label.SetResourceReference(TextBlock.ForegroundProperty, "Brush.Text");
        row.Children.Add(label);
        row.Children.Add(_swatch);
        _swatch.Margin = new Thickness(0, 0, 10, 0);
        _swatch.Click += (_, _) =>
        {
            if (!Compact) PickColorRequested?.Invoke(this, EventArgs.Empty);
        };
        row.Children.Add(_hex);
        _hex.TextChanged += (_, _) => ApplyHexInput();

        if (!compact)
        {
            var pick = new Button { Content = "选择...", MinWidth = 76, Margin = new Thickness(10, 0, 0, 0) };
            pick.SetResourceReference(StyleProperty, "UiButton");
            pick.Click += (_, _) => PickColorRequested?.Invoke(this, EventArgs.Empty);
            row.Children.Add(pick);
        }

        Content = row;
        MinHeight = 40;
        Compact = compact;
    }

    public bool Compact { get; }

    /// <summary>紧凑模式下点击色块的请求（由宿主打开取色对话框）。</summary>
    public event EventHandler? PickColorRequested;

    public string ColorHex
    {
        get => _hex.Text;
        set
        {
            _suppressEvents = true;
            _hex.Text = value;
            ApplyHexInput();
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

    private void ApplyHexInput()
    {
        var text = _hex.Text.Trim();
        var valid = TryParse(text, out var color);
        SetInvalid(!valid);
        if (valid)
        {
            _swatch.Background = new SolidColorBrush(Color.FromRgb(color.R, color.G, color.B));
            if (!_suppressEvents) ColorChanged?.Invoke(this, EventArgs.Empty);
        }
    }

    public static bool TryParse(string text, out RgbColor color)
    {
        try
        {
            color = RgbColor.FromHex(text);
            return true;
        }
        catch (FormatException)
        {
            color = default;
            return false;
        }
    }

    private void SetInvalid(bool invalid)
    {
        if (_invalid == invalid) return;
        _invalid = invalid;
        _hex.Background = (Brush)Application.Current.TryFindResource(invalid ? "Brush.FieldInvalid" : "Brush.Field");
    }
}
