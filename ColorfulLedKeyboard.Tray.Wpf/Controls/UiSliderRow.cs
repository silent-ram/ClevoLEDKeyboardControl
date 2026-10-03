using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;

namespace ColorfulLedKeyboard.Tray.Wpf.Controls;

/// <summary>标签 + 滑杆 + 数值行的共享控件（对应 WinForms SliderRow）。</summary>
public sealed class UiSliderRow : UserControl
{
    private readonly TextBlock _label;
    private readonly Slider _slider;
    private readonly TextBlock _valueText;
    private readonly string _suffix;
    private bool _suppressEvents;

    public event EventHandler? ValueChanged;

    public UiSliderRow(string labelText, int min, int max, string suffix)
    {
        _suffix = suffix;
        SetResourceReference(Control.ForegroundProperty, "Brush.Text");
        _label = new TextBlock { Text = labelText, VerticalAlignment = VerticalAlignment.Center };
        _valueText = new TextBlock { VerticalAlignment = VerticalAlignment.Center, MinWidth = 64 };
        _slider = new Slider
        {
            Minimum = min,
            Maximum = max,
            TickFrequency = Math.Max(1, (max - min) / 10),
            IsMoveToPointEnabled = true,
            VerticalAlignment = VerticalAlignment.Center,
            Margin = new Thickness(0, 0, 12, 0)
        };
        _slider.ValueChanged += (_, _) =>
        {
            UpdateValueText();
            if (!_suppressEvents) ValueChanged?.Invoke(this, EventArgs.Empty);
        };

        var grid = new Grid();
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(130) });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        Grid.SetColumn(_label, 0);
        Grid.SetColumn(_slider, 1);
        Grid.SetColumn(_valueText, 2);
        grid.Children.Add(_label);
        grid.Children.Add(_slider);
        grid.Children.Add(_valueText);
        Content = grid;
        MinHeight = 40;
        UpdateValueText();
    }

    public int Value
    {
        get => (int)Math.Round(_slider.Value);
        set
        {
            _suppressEvents = true;
            _slider.Value = Math.Clamp(value, _slider.Minimum, _slider.Maximum);
            UpdateValueText();
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

    public void SetLabelText(string text) => _label.Text = text;

    private void UpdateValueText() => _valueText.Text = $"{(int)Math.Round(_slider.Value)}{_suffix}";
}
