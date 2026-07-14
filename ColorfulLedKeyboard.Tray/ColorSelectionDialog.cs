using ColorfulLedKeyboard.Core;
using static ColorfulLedKeyboard.Tray.UiMetrics;

namespace ColorfulLedKeyboard.Tray;

internal sealed class ColorSelectionDialog : ThemedForm
{
    private readonly ColorGrid _grid;
    private readonly ColorPlane _plane = new();
    private readonly HueSlider _hueSlider = new();
    private readonly Panel _preview = new();
    private readonly TextBox _hex = new();
    private readonly NumericUpDown _red = new();
    private readonly NumericUpDown _green = new();
    private readonly NumericUpDown _blue = new();
    private readonly NumericUpDown _hue = new();
    private readonly NumericUpDown _saturation = new();
    private readonly NumericUpDown _value = new();
    private readonly bool _singleSelection;
    private bool _updatingInputs;
    private double _currentHue;
    private double _currentSaturation = 1;
    private double _currentValue = 1;

    public ColorSelectionDialog(IReadOnlyCollection<string> selectedColors, bool singleSelection)
    {
        _singleSelection = singleSelection;
        Text = "自定义颜色";
        StartPosition = FormStartPosition.CenterParent;
        FormBorderStyle = FormBorderStyle.FixedDialog;
        MaximizeBox = false;
        MinimizeBox = false;
        ClientSize = new Size(760, 470);

        _grid = new ColorGrid(BuildChoices(selectedColors), singleSelection)
        {
            Location = new Point(18, 44),
            Size = new Size(314, 300)
        };
        _grid.SelectedIndexChanged += (_, _) => LoadSelectedChoice();
        _grid.ChoiceChanged += (_, _) => LoadSelectedChoice();
        Controls.Add(new Label
        {
            Text = "基础颜色",
            Font = new Font(SystemFonts.MessageBoxFont ?? Control.DefaultFont, FontStyle.Bold),
            Location = new Point(18, 18),
            Size = new Size(160, 22)
        });
        Controls.Add(_grid);

        BuildEditor();
        LoadSelectedChoice();

        var ok = new Button { Text = "确定", Location = new Point(544, 420), Width = 96, Height = ButtonHeight };
        ok.Click += (_, _) => Accept();
        Controls.Add(ok);

        var cancel = new Button { Text = "取消", Location = new Point(652, 420), Width = 96, Height = ButtonHeight };
        cancel.Click += (_, _) => DialogResult = DialogResult.Cancel;
        Controls.Add(cancel);

        AcceptButton = ok;
        CancelButton = cancel;
    }

    public List<string> SelectedColors =>
        _grid.Choices
            .Where(choice => choice.Checked)
            .Select(choice => choice.CurrentColor.Hex)
            .ToList();

    private void BuildEditor()
    {
        Controls.Add(new Label
        {
            Text = "颜色参数",
            Font = new Font(SystemFonts.MessageBoxFont ?? Control.DefaultFont, FontStyle.Bold),
            Location = new Point(360, 18),
            Size = new Size(160, 22)
        });

        _plane.Location = new Point(360, 48);
        _plane.Size = new Size(220, 180);
        _plane.ColorChanged += (_, _) =>
        {
            if (_updatingInputs)
            {
                return;
            }

            _currentSaturation = _plane.Saturation;
            _currentValue = _plane.Value;
            ApplyHsvToCurrentColor();
        };
        Controls.Add(_plane);

        _hueSlider.Location = new Point(594, 48);
        _hueSlider.Size = new Size(28, 180);
        _hueSlider.HueChanged += (_, _) =>
        {
            if (_updatingInputs)
            {
                return;
            }

            _currentHue = _hueSlider.Hue;
            _plane.Hue = _currentHue;
            ApplyHsvToCurrentColor();
        };
        Controls.Add(_hueSlider);

        _preview.Location = new Point(646, 48);
        _preview.Size = new Size(82, 54);
        _preview.BorderStyle = BorderStyle.FixedSingle;
        Controls.Add(_preview);

        Controls.Add(new Label { Text = "HEX", Location = new Point(646, 124), Size = new Size(40, 24) });
        _hex.Location = new Point(690, 121);
        _hex.Width = 58;
        _hex.TextChanged += (_, _) => ApplyHexInput();
        Controls.Add(_hex);

        AddNumericRow("R", _red, 360, 252, 0, 255, ApplyRgbInput);
        AddNumericRow("G", _green, 360, 288, 0, 255, ApplyRgbInput);
        AddNumericRow("B", _blue, 360, 324, 0, 255, ApplyRgbInput);
        AddNumericRow("H", _hue, 492, 252, 0, 359, ApplyHsvInput);
        AddNumericRow("S", _saturation, 492, 288, 0, 100, ApplyHsvInput);
        AddNumericRow("V", _value, 492, 324, 0, 100, ApplyHsvInput);

        var restore = new Button { Text = "恢复默认", Location = new Point(646, 252), Width = 102, Height = ButtonHeight };
        restore.Click += (_, _) => RestoreSelectedDefault();
        Controls.Add(restore);
    }

    private void AddNumericRow(string label, NumericUpDown numeric, int x, int y, int min, int max, Action changed)
    {
        Controls.Add(new Label { Text = label, Location = new Point(x, y + 4), Size = new Size(24, 24) });
        numeric.Location = new Point(x + 30, y);
        numeric.Width = 76;
        numeric.Minimum = min;
        numeric.Maximum = max;
        numeric.ValueChanged += (_, _) => changed();
        Controls.Add(numeric);
    }

    private void Accept()
    {
        var selectedCount = SelectedColors.Count;
        if (_singleSelection && selectedCount != 1)
        {
            MessageBox.Show("请只选择 1 种颜色。", "ClevoLEDKeyboardControl", MessageBoxButtons.OK, MessageBoxIcon.Information);
            return;
        }

        if (!_singleSelection && selectedCount < 2)
        {
            MessageBox.Show("请至少选择 2 种颜色。", "ClevoLEDKeyboardControl", MessageBoxButtons.OK, MessageBoxIcon.Information);
            return;
        }

        DialogResult = DialogResult.OK;
    }

    private void LoadSelectedChoice()
    {
        if (_grid.SelectedChoice is not { } choice)
        {
            return;
        }

        SetEditorColor(choice.CurrentColor);
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
            _hueSlider.Hue = _currentHue;
            _preview.BackColor = Color.FromArgb(color.R, color.G, color.B);
            _hex.Text = color.Hex;
            _hex.BackColor = ThemeManager.Current.Field;
            _red.Value = color.R;
            _green.Value = color.G;
            _blue.Value = color.B;
            _hue.Value = (decimal)Math.Clamp(Math.Round(_currentHue), 0, 359);
            _saturation.Value = (decimal)Math.Clamp(Math.Round(_currentSaturation * 100), 0, 100);
            _value.Value = (decimal)Math.Clamp(Math.Round(_currentValue * 100), 0, 100);
        }
        finally
        {
            _updatingInputs = false;
        }
    }

    private void ApplyHexInput()
    {
        if (_updatingInputs)
        {
            return;
        }

        try
        {
            var color = RgbColor.FromHex(_hex.Text);
            _hex.BackColor = ThemeManager.Current.Field;
            SetCurrentChoiceColor(color);
            SetEditorColor(color);
        }
        catch (FormatException)
        {
            _hex.BackColor = Color.MistyRose;
        }
    }

    private void ApplyRgbInput()
    {
        if (_updatingInputs)
        {
            return;
        }

        var color = new RgbColor((byte)_red.Value, (byte)_green.Value, (byte)_blue.Value);
        SetCurrentChoiceColor(color);
        SetEditorColor(color);
    }

    private void ApplyHsvInput()
    {
        if (_updatingInputs)
        {
            return;
        }

        _currentHue = (double)_hue.Value;
        _currentSaturation = (double)_saturation.Value / 100d;
        _currentValue = (double)_value.Value / 100d;
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
        if (_grid.SelectedChoice is not { } choice)
        {
            return;
        }

        SetCurrentChoiceColor(choice.InstallDefaultColor);
        SetEditorColor(choice.InstallDefaultColor);
    }

    private void SetCurrentChoiceColor(RgbColor color)
    {
        if (_grid.SelectedChoice is null)
        {
            return;
        }

        _grid.SelectedChoice.CurrentColor = color;
        _grid.InvalidateSelected();
    }

    private static List<ColorChoice> BuildChoices(IReadOnlyCollection<string> selectedColors)
    {
        var installPalette = InstallPalette();
        var choices = installPalette
            .Select(color => new ColorChoice(color))
            .ToList();
        var normalizedSelected = selectedColors
            .Select(color => RgbColor.FromHex(color).Hex)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();
        var selectedDefaults = new HashSet<string>(normalizedSelected, StringComparer.OrdinalIgnoreCase);

        foreach (var choice in choices)
        {
            if (selectedDefaults.Contains(choice.InstallDefaultColor.Hex))
            {
                choice.Checked = true;
            }
        }

        foreach (var selected in normalizedSelected.Where(selected =>
            choices.All(choice => !string.Equals(choice.InstallDefaultColor.Hex, selected, StringComparison.OrdinalIgnoreCase))))
        {
            var replacement = choices.FirstOrDefault(choice => !choice.Checked);
            if (replacement is null)
            {
                replacement = new ColorChoice(installPalette[choices.Count % installPalette.Count]);
                choices.Add(replacement);
            }

            replacement.CurrentColor = RgbColor.FromHex(selected);
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
}

internal sealed class ColorChoice
{
    public ColorChoice(RgbColor installDefaultColor)
    {
        InstallDefaultColor = installDefaultColor;
        CurrentColor = installDefaultColor;
    }

    public RgbColor InstallDefaultColor { get; }

    public RgbColor CurrentColor { get; set; }

    public bool Checked { get; set; }
}

internal sealed class ColorGrid : Control
{
    private const int Columns = 8;
    private const int CellSize = 34;
    private const int SwatchPadding = 5;
    private const int CheckHitSize = 22;
    private readonly bool _singleSelection;
    private int _selectedIndex;

    public ColorGrid(List<ColorChoice> choices, bool singleSelection)
    {
        Choices = choices;
        _singleSelection = singleSelection;
        DoubleBuffered = true;
        _selectedIndex = Math.Max(0, Choices.FindIndex(choice => choice.Checked));
    }

    public List<ColorChoice> Choices { get; }

    public ColorChoice? SelectedChoice =>
        _selectedIndex >= 0 && _selectedIndex < Choices.Count ? Choices[_selectedIndex] : null;

    public event EventHandler? SelectedIndexChanged;

    public event EventHandler? ChoiceChanged;

    public void InvalidateSelected()
    {
        Invalidate(CellBounds(_selectedIndex));
        ChoiceChanged?.Invoke(this, EventArgs.Empty);
    }

    protected override void OnMouseDown(MouseEventArgs e)
    {
        base.OnMouseDown(e);
        var index = HitTest(e.Location);
        if (index < 0)
        {
            return;
        }

        SelectIndex(index);
        if (CheckHitBounds(index).Contains(e.Location))
        {
            ToggleIndex(index);
        }
    }

    protected override void OnPaint(PaintEventArgs e)
    {
        base.OnPaint(e);
        e.Graphics.Clear(ThemeManager.Current.Field);
        for (var i = 0; i < Choices.Count; i++)
        {
            var bounds = CellBounds(i);
            if (bounds.Bottom > Height)
            {
                break;
            }

            using var brush = new SolidBrush(Color.FromArgb(
                Choices[i].CurrentColor.R,
                Choices[i].CurrentColor.G,
                Choices[i].CurrentColor.B));
            var swatch = new Rectangle(
                bounds.Left + SwatchPadding,
                bounds.Top + SwatchPadding,
                bounds.Width - SwatchPadding * 2,
                bounds.Height - SwatchPadding * 2);
            e.Graphics.FillRectangle(brush, swatch);
            e.Graphics.DrawRectangle(Pens.Black, swatch);

            var state = Choices[i].Checked
                ? System.Windows.Forms.VisualStyles.CheckBoxState.CheckedNormal
                : System.Windows.Forms.VisualStyles.CheckBoxState.UncheckedNormal;
            CheckBoxRenderer.DrawCheckBox(e.Graphics, CheckBoxBounds(i).Location, state);

            if (i == _selectedIndex)
            {
                using var pen = new Pen(ThemeManager.Current.Primary, 2);
                e.Graphics.DrawRectangle(pen, bounds.Left + 1, bounds.Top + 1, bounds.Width - 3, bounds.Height - 3);
            }
        }
    }

    private void SelectIndex(int index)
    {
        if (index == _selectedIndex)
        {
            return;
        }

        var previous = _selectedIndex;
        _selectedIndex = index;
        Invalidate(CellBounds(previous));
        Invalidate(CellBounds(_selectedIndex));
        SelectedIndexChanged?.Invoke(this, EventArgs.Empty);
    }

    private void ToggleIndex(int index)
    {
        if (_singleSelection)
        {
            foreach (var choice in Choices)
            {
                choice.Checked = false;
            }

            Choices[index].Checked = true;
        }
        else
        {
            Choices[index].Checked = !Choices[index].Checked;
        }

        Invalidate();
        ChoiceChanged?.Invoke(this, EventArgs.Empty);
    }

    private int HitTest(Point point)
    {
        var column = point.X / CellSize;
        var row = point.Y / CellSize;
        if (column < 0 || column >= Columns || row < 0)
        {
            return -1;
        }

        var index = row * Columns + column;
        return index < Choices.Count ? index : -1;
    }

    private static Rectangle CellBounds(int index)
    {
        if (index < 0)
        {
            return Rectangle.Empty;
        }

        var row = index / Columns;
        var column = index % Columns;
        return new Rectangle(column * CellSize, row * CellSize, CellSize, CellSize);
    }

    private static Rectangle CheckBoxBounds(int index)
    {
        var bounds = CellBounds(index);
        return new Rectangle(bounds.Left + 3, bounds.Top + 3, 14, 14);
    }

    private static Rectangle CheckHitBounds(int index)
    {
        var bounds = CellBounds(index);
        return new Rectangle(bounds.Left, bounds.Top, CheckHitSize, CheckHitSize);
    }
}

internal sealed class ColorPlane : Control
{
    private double _hue;
    private double _saturation = 1;
    private double _value = 1;

    public ColorPlane()
    {
        DoubleBuffered = true;
    }

    public double Hue
    {
        get => _hue;
        set
        {
            _hue = Math.Clamp(value, 0, 359);
            Invalidate();
        }
    }

    public double Saturation => _saturation;

    public double Value => _value;

    public event EventHandler? ColorChanged;

    public void SetPointer(double saturation, double value)
    {
        _saturation = Math.Clamp(saturation, 0, 1);
        _value = Math.Clamp(value, 0, 1);
        Invalidate();
    }

    protected override void OnMouseDown(MouseEventArgs e)
    {
        base.OnMouseDown(e);
        Capture = true;
        UpdatePointer(e.Location);
    }

    protected override void OnMouseMove(MouseEventArgs e)
    {
        base.OnMouseMove(e);
        if (Capture && e.Button == MouseButtons.Left)
        {
            UpdatePointer(e.Location);
        }
    }

    protected override void OnMouseUp(MouseEventArgs e)
    {
        base.OnMouseUp(e);
        Capture = false;
    }

    protected override void OnPaint(PaintEventArgs e)
    {
        base.OnPaint(e);
        for (var x = 0; x < Width; x++)
        {
            var saturation = Width <= 1 ? 0 : x / (Width - 1d);
            for (var y = 0; y < Height; y++)
            {
                var value = Height <= 1 ? 0 : 1 - y / (Height - 1d);
                var color = RgbColor.FromHsv(_hue, saturation, value);
                using var pen = new Pen(Color.FromArgb(color.R, color.G, color.B));
                e.Graphics.DrawRectangle(pen, x, y, 1, 1);
            }
        }

        var pointer = new Point(
            (int)Math.Round(_saturation * (Width - 1)),
            (int)Math.Round((1 - _value) * (Height - 1)));
        e.Graphics.DrawEllipse(Pens.White, pointer.X - 5, pointer.Y - 5, 10, 10);
        e.Graphics.DrawEllipse(Pens.Black, pointer.X - 4, pointer.Y - 4, 8, 8);
    }

    private void UpdatePointer(Point point)
    {
        _saturation = Math.Clamp(point.X / Math.Max(1d, Width - 1), 0, 1);
        _value = Math.Clamp(1 - point.Y / Math.Max(1d, Height - 1), 0, 1);
        Invalidate();
        ColorChanged?.Invoke(this, EventArgs.Empty);
    }
}

internal sealed class HueSlider : Control
{
    private double _hue;

    public HueSlider()
    {
        DoubleBuffered = true;
    }

    public double Hue
    {
        get => _hue;
        set
        {
            _hue = Math.Clamp(value, 0, 359);
            Invalidate();
        }
    }

    public event EventHandler? HueChanged;

    protected override void OnMouseDown(MouseEventArgs e)
    {
        base.OnMouseDown(e);
        Capture = true;
        UpdateHue(e.Y);
    }

    protected override void OnMouseMove(MouseEventArgs e)
    {
        base.OnMouseMove(e);
        if (Capture && e.Button == MouseButtons.Left)
        {
            UpdateHue(e.Y);
        }
    }

    protected override void OnMouseUp(MouseEventArgs e)
    {
        base.OnMouseUp(e);
        Capture = false;
    }

    protected override void OnPaint(PaintEventArgs e)
    {
        base.OnPaint(e);
        for (var y = 0; y < Height; y++)
        {
            var hue = Height <= 1 ? 0 : y / (Height - 1d) * 359;
            var color = RgbColor.FromHsv(hue, 1, 1);
            using var pen = new Pen(Color.FromArgb(color.R, color.G, color.B));
            e.Graphics.DrawLine(pen, 0, y, Width - 1, y);
        }

        var markerY = (int)Math.Round(_hue / 359d * Math.Max(0, Height - 1));
        e.Graphics.DrawRectangle(Pens.Black, 0, markerY - 2, Width - 1, 4);
        e.Graphics.DrawRectangle(Pens.White, 1, markerY - 1, Width - 3, 2);
    }

    private void UpdateHue(int y)
    {
        _hue = Math.Clamp(y / Math.Max(1d, Height - 1) * 359, 0, 359);
        Invalidate();
        HueChanged?.Invoke(this, EventArgs.Empty);
    }
}
