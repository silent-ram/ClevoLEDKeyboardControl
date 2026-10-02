using System.Drawing.Drawing2D;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;

namespace ColorfulLedKeyboard.Tray;

// 全新视觉方案：深色仪器风。
// 设计立场：背光键盘活在暗处，RGB 光效在深色底上最有表现力——因此全套界面
// 采用深色仪器面板式配色；工作色用克制的琥珀金（仪表指示灯的语言），
// 唯一的"彩色"留给产品本身：一条 RGB 光谱条（UiSpectrum）作为签名元素，
// 只出现在导航选中态与页头两处，呼应软件真正控制的东西——彩色的光。
// UiThemeKind 仅为兼容旧配置文件与持久化测试保留，所有 kind 返回同一套设计。
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
    private static Color Hex(string value) => ColorTranslator.FromHtml(value);

    public static UiTheme For(UiThemeKind kind) => new(
        kind,
        "深色仪器风",
        Hex("#14161B"),      // Window：深炭底，带一点冷调，不用纯黑
        Hex("#1B1F27"),      // Surface：卡片面
        Hex("#171A21"),      // Sidebar：比窗体更深一档
        Hex("#232833"),      // Field：输入控件，比卡片再亮一档
        Hex("#2B313D"),      // Border
        Hex("#E8EBF2"),      // Text
        Hex("#99A1B0"),      // MutedText
        Hex("#E9A94A"),      // Primary：琥珀金
        Hex("#C98731"),      // Secondary：按压态的深琥珀
        Hex("#332B1B"),      // PrimarySoft：琥珀染色的选中底
        Hex("#262B36"),      // Hover：中性悬停
        10);

    // 主按钮文字用近黑，保证琥珀底上的对比度；其余深色场景文本统一走 Text。
    public Color PrimaryText => Hex("#221A08");
    public Color Success => Hex("#6FBE83");
    public Color Warning => Hex("#E08845");
    public Color Error => Hex("#E26A5E");
}

internal static class UiFonts
{
    // 中文界面统一落到 Microsoft YaHei UI：显式指定避免 Segoe UI 回退造成的字重/字距不稳。
    public const string CjkFamily = "Microsoft YaHei UI";

    private static readonly bool HasFluentIcons = FontFamily.Families.Any(f => f.Name == "Segoe Fluent Icons");
    private static readonly bool HasMdl2Assets = FontFamily.Families.Any(f => f.Name == "Segoe MDL2 Assets");

    public static Font Body(float size) => new(CjkFamily, size);
    public static Font Title(float size) => new(CjkFamily, size, FontStyle.Bold);
    public static Font Bold(float size) => new(CjkFamily, size, FontStyle.Bold);

    /// <summary>系统矢量图标字体；空串表示本机没有，调用方需退回文本符号。</summary>
    public static string IconFamily => HasFluentIcons ? "Segoe Fluent Icons" : HasMdl2Assets ? "Segoe MDL2 Assets" : "";
}

/// <summary>RGB 光谱：产品签名元素。只用于少数几处小面积点缀，不要大面积铺。</summary>
internal static class UiSpectrum
{
    private static readonly (Color Color, float Position)[] Stops =
    [
        (ColorTranslator.FromHtml("#FF4E42"), 0f),
        (ColorTranslator.FromHtml("#FF9A3C"), 0.18f),
        (ColorTranslator.FromHtml("#FFD84A"), 0.34f),
        (ColorTranslator.FromHtml("#4ADE80"), 0.52f),
        (ColorTranslator.FromHtml("#38C8F0"), 0.70f),
        (ColorTranslator.FromHtml("#6E8BFF"), 0.86f),
        (ColorTranslator.FromHtml("#E05CE8"), 1f),
    ];

    public static void Draw(Graphics graphics, Rectangle rectangle)
    {
        if (rectangle.Width <= 0 || rectangle.Height <= 0) return;
        using var brush = new LinearGradientBrush(rectangle, Color.Red, Color.Magenta, 0f);
        var blend = new ColorBlend(Stops.Length)
        {
            Colors = Stops.Select(stop => stop.Color).ToArray(),
            Positions = Stops.Select(stop => stop.Position).ToArray()
        };
        brush.InterpolationColors = blend;
        var previous = graphics.SmoothingMode;
        graphics.SmoothingMode = SmoothingMode.AntiAlias;
        graphics.FillRectangle(brush, rectangle);
        graphics.SmoothingMode = previous;
    }
}

internal static class UiShapes
{
    public static GraphicsPath RoundedRectangle(Rectangle bounds, int radius)
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

internal static class ThemeManager
{
    private const int DwmUseImmersiveDarkMode = 20;
    private static UiThemeKind _currentKind = UiThemeKind.Windows11;
    private static readonly ConditionalWeakTable<Control, SurfaceRoleHolder> SurfaceRoles = new();
    private static readonly ConditionalWeakTable<Control, object?> RoundedControls = new();

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
        menu.Font = UiFonts.Body(9F);
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
                    // 视觉样式下的 ComboBox 会忽略 BackColor，改用 Flat 绘制以吃进主题色。
                    combo.FlatStyle = FlatStyle.Flat;
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
                    dateTime.CalendarTitleForeColor = Color.Black;
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
        button.Font = UiFonts.Body(9F);
        var primary = button.AccessibleDescription == "PrimaryAction";
        button.FlatStyle = FlatStyle.Flat;
        button.UseVisualStyleBackColor = false;
        button.Cursor = Cursors.Hand;
        if (!button.Enabled)
        {
            button.BackColor = theme.Surface;
            button.ForeColor = theme.MutedText;
            button.FlatAppearance.BorderColor = theme.Border;
            button.FlatAppearance.BorderSize = 1;
            button.FlatAppearance.MouseOverBackColor = button.BackColor;
            button.FlatAppearance.MouseDownBackColor = button.BackColor;
        }
        else if (primary)
        {
            button.BackColor = theme.Primary;
            button.ForeColor = theme.PrimaryText;
            button.FlatAppearance.BorderColor = theme.Primary;
            button.FlatAppearance.BorderSize = 1;
            button.FlatAppearance.MouseOverBackColor = Color.White;
            button.FlatAppearance.MouseDownBackColor = theme.Secondary;
        }
        else
        {
            button.BackColor = theme.Field;
            button.ForeColor = theme.Text;
            button.FlatAppearance.BorderColor = theme.Border;
            button.FlatAppearance.BorderSize = 1;
            button.FlatAppearance.MouseOverBackColor = theme.Hover;
            button.FlatAppearance.MouseDownBackColor = theme.Surface;
        }
        ApplyRoundedRegion(button, 6);
    }

    /// <summary>给控件套圆角裁剪。WinForms 的 Region 不抗锯齿，深色底上小圆角的锯齿可接受；
    /// 按钮/卡片统一走这里，保证半径一致。首次调用后挂 Resize，尺寸变化时同步重建。</summary>
    internal static void ApplyRoundedRegion(Control control, int radius)
    {
        if (control.IsDisposed || control.Width <= 0 || control.Height <= 0) return;
        using var path = UiShapes.RoundedRectangle(new Rectangle(0, 0, control.Width, control.Height), radius);
        control.Region?.Dispose();
        control.Region = new Region(path);
        if (RoundedControls.TryGetValue(control, out _)) return;
        RoundedControls.Add(control, null);
        control.Resize += (_, _) => ApplyRoundedRegion(control, radius);
    }

    private static void ApplyNativeTitleBar(Form form)
    {
        if (!form.IsHandleCreated) return;
        try
        {
            // 深色界面配深色标题栏，避免出现"深色窗体 + 白色标题条"的割裂感。
            var value = 1;
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
    protected ThemedForm()
    {
        Font = UiFonts.Body(9F);
    }

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
                Font = UiFonts.Title(10F),
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
        using var path = UiShapes.RoundedRectangle(rectangle, theme.CornerRadius);
        e.Graphics.SmoothingMode = SmoothingMode.AntiAlias;
        e.Graphics.DrawPath(pen, path);
    }

    private void UpdateRegion(int radius)
    {
        if (Width <= 0 || Height <= 0) return;
        using var path = UiShapes.RoundedRectangle(ClientRectangle, radius);
        Region?.Dispose();
        Region = new Region(path);
    }
}

internal sealed class NavigationListBox : ListBox
{
    private UiTheme _theme = ThemeManager.Current;
    private readonly Dictionary<int, string> _badges = [];
    private int _hoverIndex = -1;

    private static string? IconFamily =>
        UiFonts.IconFamily.Length == 0 ? null : UiFonts.IconFamily;

    public NavigationListBox()
    {
        DrawMode = DrawMode.OwnerDrawFixed;
        ItemHeight = 40;
        BorderStyle = BorderStyle.None;
        IntegralHeight = false;
        Font = UiFonts.Body(9.5F);
        MouseMove += (_, e) =>
        {
            var index = IndexFromPoint(e.Location);
            if (index == _hoverIndex) return;
            var previous = _hoverIndex;
            _hoverIndex = index;
            if (previous >= 0) Invalidate(GetItemRectangle(previous));
            if (index >= 0) Invalidate(GetItemRectangle(index));
        };
        MouseLeave += (_, _) =>
        {
            if (_hoverIndex < 0) return;
            var previous = _hoverIndex;
            _hoverIndex = -1;
            Invalidate(GetItemRectangle(previous));
        };
    }

    public void ApplyTheme(UiTheme theme)
    {
        _theme = theme;
        BackColor = theme.Sidebar;
        ForeColor = theme.Text;
        Invalidate();
    }

    public void SetBadge(int index, string? text)
    {
        if (string.IsNullOrWhiteSpace(text)) _badges.Remove(index);
        else _badges[index] = text;
        Invalidate(GetItemRectangle(index));
    }

    private static string GlyphOf(string text) => text switch
    {
        "当前状态" => "\uE80F",   // Home
        "灯效设置" => "\uE791",   // Lightbulb
        "音乐模式" => "\uEC4F",   // MusicNote
        "场景自动化" => "\uE9D9", // Flow / 自动化
        "事件反馈" => "\uE945",   // LightningBolt
        "诊断与恢复" => "\uE90F", // Repair
        "软件设置" => "\uE713",   // Settings
        "关于" => "\uE946",       // Info
        _ => "\uE7C3"             // Flag，兜底
    };

    private static string GlyphOfFallback(string text) => text switch
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

    protected override void OnDrawItem(DrawItemEventArgs e)
    {
        if (e.Index < 0 || e.Index >= Items.Count) return;
        var selected = (e.State & DrawItemState.Selected) != 0;
        var hovered = e.Index == _hoverIndex && !selected;

        var pill = new Rectangle(e.Bounds.X + 5, e.Bounds.Y + 3, e.Bounds.Width - 10, e.Bounds.Height - 6);
        using (var background = new SolidBrush(_theme.Sidebar))
        {
            e.Graphics.FillRectangle(background, e.Bounds);
        }
        if (selected || hovered)
        {
            using var path = UiShapes.RoundedRectangle(pill, 8);
            using var fill = new SolidBrush(selected ? _theme.PrimarySoft : _theme.Hover);
            e.Graphics.SmoothingMode = SmoothingMode.AntiAlias;
            e.Graphics.FillPath(fill, path);
        }
        if (selected)
        {
            // 签名元素：选中项左侧的 RGB 光谱短条，呼应"这是一盏可以调色的灯"。
            var accentWidth = Math.Max(2, (int)Math.Round(3 * e.Graphics.DpiX / 96f));
            var accent = new Rectangle(pill.X, pill.Y + 6, accentWidth, pill.Height - 12);
            UiSpectrum.Draw(e.Graphics, accent);
        }

        var text = Items[e.Index]?.ToString() ?? "";
        var color = selected ? _theme.Text : hovered ? _theme.Text : _theme.MutedText;
        var badge = _badges.GetValueOrDefault(e.Index);
        using var badgeFont = UiFonts.Bold(8.5F);
        var badgeWidth = string.IsNullOrWhiteSpace(badge)
            ? 0
            : TextRenderer.MeasureText(e.Graphics, badge, badgeFont, Size.Empty, TextFormatFlags.NoPadding).Width + 12;

        var family = IconFamily;
        if (family is not null)
        {
            using var iconFont = new Font(family, 10.5F);
            TextRenderer.DrawText(e.Graphics, GlyphOf(text), iconFont,
                new Rectangle(e.Bounds.X + 15, e.Bounds.Y, 24, e.Bounds.Height), color,
                TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter);
        }
        else
        {
            using var glyphFont = new Font("Segoe UI Symbol", 10F);
            TextRenderer.DrawText(e.Graphics, GlyphOfFallback(text), glyphFont,
                new Rectangle(e.Bounds.X + 15, e.Bounds.Y, 24, e.Bounds.Height), color,
                TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter);
        }
        TextRenderer.DrawText(e.Graphics, text, Font, new Rectangle(e.Bounds.X + 45, e.Bounds.Y, e.Bounds.Width - 50 - badgeWidth, e.Bounds.Height),
            color, TextFormatFlags.Left | TextFormatFlags.VerticalCenter | TextFormatFlags.EndEllipsis);
        if (!string.IsNullOrWhiteSpace(badge))
            TextRenderer.DrawText(e.Graphics, badge, badgeFont,
                new Rectangle(e.Bounds.Right - badgeWidth - 6, e.Bounds.Y, badgeWidth, e.Bounds.Height),
                _theme.Error, TextFormatFlags.Right | TextFormatFlags.VerticalCenter | TextFormatFlags.NoPadding);
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
