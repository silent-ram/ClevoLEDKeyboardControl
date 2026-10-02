using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Shapes;
using System.Windows.Media;

namespace ColorfulLedKeyboard.Tray.Wpf.Controls;

/// <summary>等宽色带预览（对应 WinForms PalettePreviewControl）。</summary>
public sealed class PalettePreviewControl : UserControl
{
    private readonly UniformGrid _grid = new() { Rows = 1 };

    public PalettePreviewControl()
    {
        Content = new Border
        {
            Child = _grid,
            BorderBrush = (Brush)Application.Current.Resources["Brush.Border"],
            BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(3),
            MinHeight = 22,
            MinWidth = 120
        };
    }

    public List<string> Colors
    {
        set
        {
            _grid.Children.Clear();
            foreach (var hex in value)
            {
                var color = TryColor(hex);
                _grid.Children.Add(new Rectangle
                {
                    Fill = new SolidColorBrush(color)
                });
            }

            if (_grid.Children.Count == 0)
            {
                _grid.Children.Add(new Rectangle
                {
                    Fill = (Brush)Application.Current.Resources["Brush.Window"]
                });
            }
        }
    }

    private static Color TryColor(string hex)
    {
        try
        {
            var rgb = ColorfulLedKeyboard.Core.RgbColor.FromHex(hex);
            return Color.FromRgb(rgb.R, rgb.G, rgb.B);
        }
        catch (FormatException)
        {
            return System.Windows.Media.Colors.Transparent;
        }
    }
}
