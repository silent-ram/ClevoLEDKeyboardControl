using System.Drawing;
using System.Drawing.Drawing2D;
using System.Windows.Forms;

namespace ColorfulLedKeyboard.Installer;

/// <summary>
/// 现代扁平化的经典向导主题：白底内容区 + 左侧蓝色品牌栏 + 底部导航。
/// 结构沿用经典安装向导，控件工艺现代化：实心主按钮、细线边框、连体圆角进度条。
/// 安装器是独立自包含 exe，不共享 WPF 资源，这里手写常量与控件工厂。
/// </summary>
internal static class InstallerTheme
{
    public static readonly Color Window = Color.White;
    public static readonly Color Card = Color.White;
    public static readonly Color CardBorder = Color.FromArgb(217, 217, 217);
    public static readonly Color Border = Color.FromArgb(207, 207, 207);
    public static readonly Color Text = Color.FromArgb(31, 31, 31);
    public static readonly Color Muted = Color.FromArgb(95, 95, 95);
    public static readonly Color Faint = Color.FromArgb(138, 138, 138);
    public static readonly Color Accent = Color.FromArgb(0, 104, 214);
    public static readonly Color AccentHover = Color.FromArgb(26, 123, 232);
    public static readonly Color Danger = Color.FromArgb(196, 44, 28);
    public static readonly Color DangerHover = Color.FromArgb(216, 66, 50);
    public static readonly Color Success = Color.FromArgb(22, 133, 61);

    public static readonly Color ButtonFace = Color.White;
    public static readonly Color ButtonHoverFace = Color.FromArgb(240, 246, 252);
    public static readonly Color HoverBorder = Color.FromArgb(0, 120, 215);

    public static readonly Color Track = Color.FromArgb(230, 230, 230);
    public static readonly Color BarGreen = Color.FromArgb(38, 166, 91);

    public static readonly Color SidebarTop = Color.FromArgb(11, 111, 211);
    public static readonly Color SidebarBottom = Color.FromArgb(7, 74, 152);
    public static readonly Color SidebarText = Color.White;

    public static Font BaseFont { get; } = new("Microsoft YaHei UI", 9.5F);
    public static Font TitleFont { get; } = new("Microsoft YaHei UI", 12.5F, FontStyle.Bold);
    public static Font SectionFont { get; } = new("Microsoft YaHei UI", 10F, FontStyle.Bold);

    public static Label MakeLabel(string text, Font? font = null, Color? color = null)
    {
        return new Label
        {
            Text = text,
            Font = font ?? BaseFont,
            ForeColor = color ?? Text,
            AutoSize = true,
            BackColor = Color.Transparent
        };
    }

    /// <summary>现代扁平按钮：白底细线；primary = 蓝底白字加粗；danger = 红底白字。</summary>
    public static Button MakeButton(string text, string kind = "normal")
    {
        var button = new Button
        {
            Text = text,
            FlatStyle = FlatStyle.Flat,
            Font = new Font(BaseFont.FontFamily, 9.5F, kind == "primary" ? FontStyle.Bold : FontStyle.Regular),
            Height = 32,
            Cursor = Cursors.Hand,
            UseVisualStyleBackColor = false
        };
        switch (kind)
        {
            case "primary":
                button.BackColor = Accent;
                button.ForeColor = Color.White;
                button.FlatAppearance.BorderSize = 0;
                button.FlatAppearance.MouseOverBackColor = AccentHover;
                button.FlatAppearance.MouseDownBackColor = AccentHover;
                break;
            case "danger":
                button.BackColor = Danger;
                button.ForeColor = Color.White;
                button.FlatAppearance.BorderSize = 0;
                button.FlatAppearance.MouseOverBackColor = DangerHover;
                button.FlatAppearance.MouseDownBackColor = DangerHover;
                break;
            default:
                button.BackColor = ButtonFace;
                button.ForeColor = Text;
                button.FlatAppearance.BorderColor = Border;
                button.FlatAppearance.BorderSize = 1;
                button.FlatAppearance.MouseOverBackColor = ButtonHoverFace;
                button.FlatAppearance.MouseDownBackColor = ButtonHoverFace;
                break;
        }
        return button;
    }
}

/// <summary>连体圆角进度条：浅灰轨道 + 扁平绿填充（替代 WinForms 默认的分段绿块）。</summary>
internal sealed class ModernProgressBar : Control
{
    public int Percent { get; set; }

    public ModernProgressBar()
    {
        Height = 8;
        TabStop = false;
    }

    protected override void OnPaint(PaintEventArgs e)
    {
        var g = e.Graphics;
        g.SmoothingMode = SmoothingMode.AntiAlias;
        var track = Rounded(ClientRectangle, Height / 2);
        using (var trackBrush = new SolidBrush(InstallerTheme.Track))
        {
            g.FillPath(trackBrush, track);
        }
        if (Percent > 0)
        {
            var w = (int)(Width * Math.Clamp(Percent / 100d, 0d, 1d));
            if (w >= Height)
            {
                var fill = new Rectangle(0, 0, w, Height);
                using var brush = new SolidBrush(InstallerTheme.BarGreen);
                g.FillPath(brush, Rounded(fill, Height / 2));
            }
        }
    }

    private static GraphicsPath Rounded(Rectangle bounds, int radius)
    {
        var r = Math.Max(1, Math.Min(radius, bounds.Height / 2));
        var path = new GraphicsPath();
        path.AddArc(bounds.X, bounds.Y, r * 2, r * 2, 180, 90);
        path.AddArc(bounds.Right - r * 2, bounds.Y, r * 2, r * 2, 270, 90);
        path.AddArc(bounds.Right - r * 2, bounds.Bottom - r * 2, r * 2, r * 2, 0, 90);
        path.AddArc(bounds.X, bounds.Bottom - r * 2, r * 2, r * 2, 90, 90);
        path.CloseFigure();
        return path;
    }
}
