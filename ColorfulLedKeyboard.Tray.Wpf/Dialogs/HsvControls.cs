using System.Windows;
using System.Windows.Input;
using System.Windows.Media;

namespace ColorfulLedKeyboard.Tray.Wpf.Dialogs;

/// <summary>HSV 选色面：色相底色 + 白→透明（饱和度）+ 透明→黑（明度）三层渐变 + 拖动指针。</summary>
public sealed class SvPlane : FrameworkElement
{
    private double _hue = 210;

    public event EventHandler? ColorPicked;

    public double Hue
    {
        get => _hue;
        set
        {
            _hue = value;
            InvalidateVisual();
        }
    }

    public double Saturation { get; private set; } = 1;
    public double Value { get; private set; } = 1;

    public SvPlane()
    {
        MinWidth = 150;
        MinHeight = 150;
        Cursor = Cursors.Cross;
        ClipToBounds = true;
    }

    public void SetPointer(double saturation, double value)
    {
        Saturation = Math.Clamp(saturation, 0, 1);
        Value = Math.Clamp(value, 0, 1);
        InvalidateVisual();
    }

    protected override void OnRender(DrawingContext context)
    {
        base.OnRender(context);
        var width = ActualWidth;
        var height = ActualHeight;
        if (width <= 0 || height <= 0) return;

        var baseColor = ColorFromHsv(Hue, 1, 1);
        context.DrawRectangle(new SolidColorBrush(baseColor), null, new Rect(0, 0, width, height));
        context.DrawRectangle(
            new LinearGradientBrush
            {
                GradientStops =
                {
                    new GradientStop(Color.FromRgb(255, 255, 255), 0),
                    new GradientStop(Color.FromArgb(0, 255, 255, 255), 1)
                },
                StartPoint = new Point(0, 0.5),
                EndPoint = new Point(1, 0.5)
            }, null, new Rect(0, 0, width, height));
        context.DrawRectangle(
            new LinearGradientBrush
            {
                GradientStops =
                {
                    new GradientStop(Color.FromArgb(0, 0, 0, 0), 0),
                    new GradientStop(Color.FromRgb(0, 0, 0), 1)
                },
                StartPoint = new Point(0.5, 0),
                EndPoint = new Point(0.5, 1)
            }, null, new Rect(0, 0, width, height));

        var x = Saturation * width;
        var y = (1 - Value) * height;
        context.DrawEllipse(null, new Pen(Brushes.White, 2), new Point(x, y), 6, 6);
        context.DrawEllipse(null, new Pen(Brushes.Black, 1), new Point(x, y), 7.5, 7.5);
    }

    protected override void OnMouseLeftButtonDown(MouseButtonEventArgs e) => Pick(e);
    protected override void OnMouseMove(MouseEventArgs e)
    {
        if (e.LeftButton == MouseButtonState.Pressed) Pick(e);
    }

    private void Pick(MouseEventArgs e)
    {
        var point = e.GetPosition(this);
        Saturation = Math.Clamp(point.X / Math.Max(1, ActualWidth), 0, 1);
        Value = Math.Clamp(1 - point.Y / Math.Max(1, ActualHeight), 0, 1);
        InvalidateVisual();
        ColorPicked?.Invoke(this, EventArgs.Empty);
    }

    public static Color ColorFromHsv(double hue, double saturation, double value)
    {
        var rgb = ColorfulLedKeyboard.Core.RgbColor.FromHsv(hue, saturation, value);
        return Color.FromRgb(rgb.R, rgb.G, rgb.B);
    }
}

/// <summary>垂直色相条 + 拖动指针。</summary>
public sealed class HueBar : FrameworkElement
{
    public event EventHandler? HuePicked;

    public double Hue { get; private set; } = 210;

    public HueBar()
    {
        Width = 26;
        MinHeight = 150;
        Cursor = Cursors.Cross;
        ClipToBounds = true;
    }

    public void SetHue(double hue)
    {
        Hue = Math.Clamp(hue, 0, 359);
        InvalidateVisual();
    }

    protected override void OnRender(DrawingContext context)
    {
        base.OnRender(context);
        var height = ActualHeight;
        if (height <= 0) return;
        var brush = new LinearGradientBrush
        {
            StartPoint = new Point(0.5, 0),
            EndPoint = new Point(0.5, 1),
            GradientStops =
            {
                new GradientStop(SvPlane.ColorFromHsv(0, 1, 1), 0),
                new GradientStop(SvPlane.ColorFromHsv(60, 1, 1), 1.0 / 6),
                new GradientStop(SvPlane.ColorFromHsv(120, 1, 1), 2.0 / 6),
                new GradientStop(SvPlane.ColorFromHsv(180, 1, 1), 0.5),
                new GradientStop(SvPlane.ColorFromHsv(240, 1, 1), 4.0 / 6),
                new GradientStop(SvPlane.ColorFromHsv(300, 1, 1), 5.0 / 6),
                new GradientStop(SvPlane.ColorFromHsv(359, 1, 1), 1)
            }
        };
        context.DrawRectangle(brush, null, new Rect(0, 0, ActualWidth, height));
        var y = Hue / 359 * height;
        context.DrawRectangle(Brushes.Transparent, new Pen(Brushes.White, 2),
            new Rect(1, y - 3, ActualWidth - 2, 6));
        context.DrawRectangle(Brushes.Transparent, new Pen(Brushes.Black, 1),
            new Rect(0.5, y - 4.5, ActualWidth - 1, 9));
    }

    protected override void OnMouseLeftButtonDown(MouseButtonEventArgs e) => Pick(e);
    protected override void OnMouseMove(MouseEventArgs e)
    {
        if (e.LeftButton == MouseButtonState.Pressed) Pick(e);
    }

    private void Pick(MouseEventArgs e)
    {
        Hue = Math.Clamp(e.GetPosition(this).Y / Math.Max(1, ActualHeight), 0, 0.999) * 359;
        InvalidateVisual();
        HuePicked?.Invoke(this, EventArgs.Empty);
    }
}
