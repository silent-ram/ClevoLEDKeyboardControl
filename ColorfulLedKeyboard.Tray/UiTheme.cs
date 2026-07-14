using System.Drawing.Drawing2D;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;

namespace ColorfulLedKeyboard.Tray;

internal sealed record UiTheme(
    UiThemeKind Kind,
    string DisplayName,
    Color Window,
    Color Surface,
    Color Sidebar,
    Color Field,
    Color Border,
    Color Text,
    Color MutedText,
    Color Primary,
    Color Secondary,
    Color PrimarySoft,
    Color Hover,
    int CornerRadius)
{
    public static UiTheme For(UiThemeKind kind) => kind switch
    {
        UiThemeKind.Technology => new(
            kind, "白色科技风", ColorTranslator.FromHtml("#F6F8FC"), Color.White,
            ColorTranslator.FromHtml("#F1F5FF"), Color.White, ColorTranslator.FromHtml("#DCE4F2"),
            ColorTranslator.FromHtml("#172033"), ColorTranslator.FromHtml("#667085"),
            ColorTranslator.FromHtml("#5965E8"), ColorTranslator.FromHtml("#8069F2"),
            ColorTranslator.FromHtml("#EEF0FF"), ColorTranslator.FromHtml("#E8ECFA"), 5),
        UiThemeKind.Warm => new(
            kind, "柔和暖色风", ColorTranslator.FromHtml("#F7F4EF"), ColorTranslator.FromHtml("#FFFDFC"),
            ColorTranslator.FromHtml("#F1EDE6"), ColorTranslator.FromHtml("#FFFEFC"), ColorTranslator.FromHtml("#E4DDD3"),
            ColorTranslator.FromHtml("#342F2A"), ColorTranslator.FromHtml("#746C64"),
            ColorTranslator.FromHtml("#3F7F78"), ColorTranslator.FromHtml("#6C8F75"),
            ColorTranslator.FromHtml("#E5F0ED"), ColorTranslator.FromHtml("#ECE8E1"), 12),
        _ => new(
            UiThemeKind.Windows11, "Windows 11 简洁风", ColorTranslator.FromHtml("#F3F3F3"), Color.White,
            ColorTranslator.FromHtml("#F7F7F7"), Color.White, ColorTranslator.FromHtml("#E0E0E0"),
            ColorTranslator.FromHtml("#1F1F1F"), ColorTranslator.FromHtml("#606060"),
            ColorTranslator.FromHtml("#0067C0"), ColorTranslator.FromHtml("#3A78B4"),
            ColorTranslator.FromHtml("#E5F1FB"), ColorTranslator.FromHtml("#EEEEEE"), 8)
    };

    public Color Success => ColorTranslator.FromHtml("#218739");
    public Color Warning => ColorTranslator.FromHtml("#B75D00");
    public Color Error => ColorTranslator.FromHtml("#C42B1C");
}

internal static class ThemeManager
{
    private const int DwmUseImmersiveDarkMode = 20;
    private static UiThemeKind _currentKind = UiThemeKind.Windows11;
    private static readonly ConditionalWeakTable<Control, SurfaceRoleHolder> SurfaceRoles = new();

    public static event EventHandler? ThemeChanged;
    public static UiThemeKind CurrentKind => _currentKind;
    public static UiTheme Current => UiTheme.For(_currentKind);

    internal static T SetSurface<T>(T control, ThemeSurfaceRole role) where T : Control
    {
        SurfaceRoles.Remove(control);
        SurfaceRoles.Add(control, new SurfaceRoleHolder(role));
        ApplySurface(control, Current, role);
        return control;
    }

    public static void Initialize(UiThemeKind theme) => _currentKind = Enum.IsDefined(theme) ? theme : UiThemeKind.Windows11;

    public static void SetTheme(UiThemeKind theme)
    {
        if (!Enum.IsDefined(theme)) theme = UiThemeKind.Windows11;
        if (_currentKind == theme) return;
        _currentKind = theme;
        ThemeChanged?.Invoke(null, EventArgs.Empty);
    }

    public static void Apply(Form form)
    {
        var theme = Current;
        form.SuspendLayout();
        form.BackColor = theme.Window;
        form.ForeColor = theme.Text;
        ApplyNativeTitleBar(form);
        ApplyRecursive(form, theme);
        form.ResumeLayout(true);
        form.Invalidate(true);
    }

    public static void Apply(ContextMenuStrip menu)
    {
        var theme = Current;
        menu.RenderMode = ToolStripRenderMode.Professional;
        menu.Renderer = new ThemedToolStripRenderer(theme);
        menu.BackColor = theme.Surface;
        menu.ForeColor = theme.Text;
        menu.Font = new Font("Segoe UI", 9F);
        foreach (ToolStripItem item in menu.Items) ApplyMenuItem(item, theme);
    }

    private static void ApplyMenuItem(ToolStripItem item, UiTheme theme)
    {
        item.BackColor = theme.Surface;
        if (item.ForeColor == SystemColors.ControlText || item.ForeColor == Color.Empty) item.ForeColor = theme.Text;
        if (item is ToolStripMenuItem menuItem)
            foreach (ToolStripItem child in menuItem.DropDownItems) ApplyMenuItem(child, theme);
    }

    private static void ApplyRecursive(Control parent, UiTheme theme)
    {
        foreach (Control control in parent.Controls)
        {
            var hasSurfaceRole = SurfaceRoles.TryGetValue(control, out var surfaceRole);
            if (hasSurfaceRole) ApplySurface(control, theme, surfaceRole!.Role);
            switch (control)
            {
                case UiCard card:
                    card.ApplyTheme(theme);
                    break;
                case NavigationListBox navigation:
                    navigation.ApplyTheme(theme);
                    break;
                case Button button:
                    StyleButton(button, theme);
                    break;
                case TextBoxBase textBox:
                    textBox.BackColor = textBox.ReadOnly ? theme.Window : theme.Field;
                    textBox.ForeColor = theme.Text;
                    textBox.BorderStyle = BorderStyle.FixedSingle;
                    break;
                case ComboBox combo:
                    combo.BackColor = theme.Field;
                    combo.ForeColor = theme.Text;
                    break;
                case NumericUpDown numeric:
                    numeric.BackColor = theme.Field;
                    numeric.ForeColor = theme.Text;
                    break;
                case ListBox list:
                    list.BackColor = theme.Field;
                    list.ForeColor = theme.Text;
                    list.BorderStyle = BorderStyle.FixedSingle;
                    break;
                case ListView listView:
                    listView.BackColor = theme.Field;
                    listView.ForeColor = theme.Text;
                    listView.BorderStyle = BorderStyle.FixedSingle;
                    break;
                case DateTimePicker dateTime:
                    dateTime.CalendarForeColor = theme.Text;
                    dateTime.CalendarMonthBackground = theme.Field;
                    dateTime.CalendarTitleBackColor = theme.Primary;
                    dateTime.CalendarTitleForeColor = Color.White;
                    break;
                case LinkLabel link:
                    link.LinkColor = theme.Primary;
                    link.ActiveLinkColor = theme.Secondary;
                    link.VisitedLinkColor = theme.Primary;
                    link.BackColor = Color.Transparent;
                    break;
                case Label label:
                    label.ForeColor = MapTextColor(label.ForeColor, theme);
                    break;
                case CheckBox or RadioButton:
                    control.ForeColor = theme.Text;
                    control.BackColor = parent.BackColor;
                    break;
                case TrackBar trackBar:
                    trackBar.BackColor = parent.BackColor;
                    trackBar.ForeColor = theme.Text;
                    break;
                case TabPage tabPage:
                    tabPage.BackColor = theme.Surface;
                    tabPage.ForeColor = theme.Text;
                    break;
            }

            if (!hasSurfaceRole && control is Panel or FlowLayoutPanel or TableLayoutPanel or SplitContainer or UserControl &&
                control is not UiCard && control is not NavigationListBox)
                control.BackColor = parent.BackColor;

            ApplyRecursive(control, theme);
        }
    }

    private static Color MapTextColor(Color color, UiTheme theme)
    {
        if (color == SystemColors.GrayText || IsThemeColor(color, candidate => candidate.MutedText)) return theme.MutedText;
        if (color is { } && (color == Color.Firebrick || IsThemeColor(color, candidate => candidate.Error))) return theme.Error;
        if (color == Color.DarkOrange || IsThemeColor(color, candidate => candidate.Warning)) return theme.Warning;
        if (color == Color.DarkGreen || color == Color.ForestGreen || IsThemeColor(color, candidate => candidate.Success)) return theme.Success;
        return theme.Text;
    }

    private static bool IsThemeColor(Color color, Func<UiTheme, Color> selector) =>
        Enum.GetValues<UiThemeKind>().Any(kind => color.ToArgb() == selector(UiTheme.For(kind)).ToArgb());

    private static void ApplySurface(Control control, UiTheme theme, ThemeSurfaceRole role) =>
        control.BackColor = role switch
        {
            ThemeSurfaceRole.Surface => theme.Surface,
            ThemeSurfaceRole.Sidebar => theme.Sidebar,
            _ => theme.Window
        };

    internal static void StyleButton(Button button, UiTheme theme)
    {
        if (button.AccessibleDescription == "ColorSwatch")
        {
            button.FlatStyle = FlatStyle.Flat;
            button.FlatAppearance.BorderColor = theme.Border;
            button.FlatAppearance.BorderSize = 1;
            return;
        }
        button.Height = Math.Max(button.Height, UiMetrics.ButtonHeight);
        button.TextAlign = ContentAlignment.MiddleCenter;
        var primary = button.AccessibleDescription == "PrimaryAction";
        button.FlatStyle = FlatStyle.Flat;
        button.UseVisualStyleBackColor = false;
        button.BackColor = !button.Enabled ? theme.Window : primary ? theme.Primary : theme.Surface;
        button.ForeColor = !button.Enabled ? theme.MutedText : primary ? Color.White : theme.Text;
        button.FlatAppearance.BorderColor = !button.Enabled ? theme.Border : primary ? theme.Primary : theme.Border;
        button.FlatAppearance.BorderSize = 1;
        button.Cursor = Cursors.Hand;
    }

    private static void ApplyNativeTitleBar(Form form)
    {
        if (!form.IsHandleCreated) return;
        try
        {
            var value = 0;
            _ = DwmSetWindowAttribute(form.Handle, DwmUseImmersiveDarkMode, ref value, sizeof(int));
        }
        catch (DllNotFoundException)
        {
        }
    }

    [DllImport("dwmapi.dll")]
    private static extern int DwmSetWindowAttribute(IntPtr hwnd, int attribute, ref int value, int size);

    private sealed record SurfaceRoleHolder(ThemeSurfaceRole Role);
}

internal enum ThemeSurfaceRole { Window, Surface, Sidebar }

public class ThemedForm : Form
{
    protected override void OnShown(EventArgs e)
    {
        ThemeManager.Apply(this);
        base.OnShown(e);
    }
}

internal sealed class UiCard : FlowLayoutPanel
{
    public UiCard(string title, params Control[] controls)
    {
        Width = UiMetrics.ContentWidth + 32;
        AutoSize = true;
        AutoSizeMode = AutoSizeMode.GrowAndShrink;
        FlowDirection = FlowDirection.TopDown;
        WrapContents = false;
        Padding = new Padding(16, 12, 16, 14);
        Margin = new Padding(0, 0, 0, 14);
        DoubleBuffered = true;
        if (!string.IsNullOrWhiteSpace(title))
        {
            Controls.Add(new Label
            {
                Text = title,
                AutoSize = false,
                Width = UiMetrics.ContentWidth,
                Height = 32,
                Font = new Font("Segoe UI Semibold", 10F),
                AccessibleRole = AccessibleRole.StaticText
            });
        }
        Controls.AddRange(controls);
        ApplyTheme(ThemeManager.Current);
    }

    public void ApplyTheme(UiTheme theme)
    {
        BackColor = theme.Surface;
        ForeColor = theme.Text;
        UpdateRegion(theme.CornerRadius);
        Invalidate();
    }

    protected override void OnResize(EventArgs eventargs)
    {
        base.OnResize(eventargs);
        UpdateRegion(ThemeManager.Current.CornerRadius);
    }

    protected override void OnPaint(PaintEventArgs e)
    {
        base.OnPaint(e);
        var theme = ThemeManager.Current;
        using var pen = new Pen(theme.Border);
        var rectangle = ClientRectangle;
        rectangle.Width -= 1;
        rectangle.Height -= 1;
        using var path = RoundedRectangle(rectangle, theme.CornerRadius);
        e.Graphics.SmoothingMode = SmoothingMode.AntiAlias;
        e.Graphics.DrawPath(pen, path);
    }

    private void UpdateRegion(int radius)
    {
        if (Width <= 0 || Height <= 0) return;
        using var path = RoundedRectangle(ClientRectangle, radius);
        Region?.Dispose();
        Region = new Region(path);
    }

    private static GraphicsPath RoundedRectangle(Rectangle bounds, int radius)
    {
        var path = new GraphicsPath();
        var diameter = Math.Max(2, radius * 2);
        var arc = new Rectangle(bounds.Location, new Size(diameter, diameter));
        path.AddArc(arc, 180, 90);
        arc.X = bounds.Right - diameter;
        path.AddArc(arc, 270, 90);
        arc.Y = bounds.Bottom - diameter;
        path.AddArc(arc, 0, 90);
        arc.X = bounds.Left;
        path.AddArc(arc, 90, 90);
        path.CloseFigure();
        return path;
    }
}

internal sealed class NavigationListBox : ListBox
{
    private UiTheme _theme = ThemeManager.Current;

    public NavigationListBox()
    {
        DrawMode = DrawMode.OwnerDrawFixed;
        ItemHeight = 42;
        BorderStyle = BorderStyle.None;
        IntegralHeight = false;
        Font = new Font("Segoe UI", 9.5F);
    }

    public void ApplyTheme(UiTheme theme)
    {
        _theme = theme;
        BackColor = theme.Sidebar;
        ForeColor = theme.Text;
        Invalidate();
    }

    protected override void OnDrawItem(DrawItemEventArgs e)
    {
        if (e.Index < 0 || e.Index >= Items.Count) return;
        var selected = (e.State & DrawItemState.Selected) != 0;
        using var background = new SolidBrush(selected ? _theme.PrimarySoft : _theme.Sidebar);
        e.Graphics.FillRectangle(background, e.Bounds);
        if (selected)
        {
            using var accent = new SolidBrush(_theme.Primary);
            e.Graphics.FillRectangle(accent, e.Bounds.Left, e.Bounds.Top + 7, 3, e.Bounds.Height - 14);
        }
        var text = Items[e.Index]?.ToString() ?? "";
        var glyph = text switch
        {
            "当前状态" => "●",
            "灯效设置" => "✦",
            "音乐模式" => "♪",
            "场景自动化" => "◇",
            "事件反馈" => "⚡",
            "诊断与恢复" => "✓",
            "软件设置" => "⚙",
            "关于" => "i",
            _ => "•"
        };
        var color = selected ? _theme.Primary : _theme.Text;
        using var glyphFont = new Font("Segoe UI Symbol", 10F);
        TextRenderer.DrawText(e.Graphics, glyph, glyphFont,
            new Rectangle(e.Bounds.X + 15, e.Bounds.Y, 24, e.Bounds.Height), color,
            TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter);
        TextRenderer.DrawText(e.Graphics, text, Font, new Rectangle(e.Bounds.X + 45, e.Bounds.Y, e.Bounds.Width - 50, e.Bounds.Height),
            color, TextFormatFlags.Left | TextFormatFlags.VerticalCenter | TextFormatFlags.EndEllipsis);
        if ((e.State & DrawItemState.Focus) != 0) e.DrawFocusRectangle();
    }
}

internal sealed class ThemedToolStripRenderer : ToolStripProfessionalRenderer
{
    public ThemedToolStripRenderer(UiTheme theme) : base(new ThemeColorTable(theme))
    {
        RoundedEdges = true;
    }

    private sealed class ThemeColorTable(UiTheme theme) : ProfessionalColorTable
    {
        public override Color ToolStripDropDownBackground => theme.Surface;
        public override Color ImageMarginGradientBegin => theme.Surface;
        public override Color ImageMarginGradientMiddle => theme.Surface;
        public override Color ImageMarginGradientEnd => theme.Surface;
        public override Color MenuItemSelected => theme.PrimarySoft;
        public override Color MenuItemBorder => theme.PrimarySoft;
        public override Color MenuItemSelectedGradientBegin => theme.PrimarySoft;
        public override Color MenuItemSelectedGradientEnd => theme.PrimarySoft;
        public override Color MenuItemPressedGradientBegin => theme.Hover;
        public override Color MenuItemPressedGradientEnd => theme.Hover;
        public override Color SeparatorDark => theme.Border;
        public override Color SeparatorLight => theme.Surface;
    }
}
