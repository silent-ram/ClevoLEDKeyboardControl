using System.Drawing;
using System.Windows.Forms;

namespace ColorfulLedKeyboard.Installer;

/// <summary>
/// 经典 NSIS/MUI2 风格主题：白底内容区、两行页头、左侧蓝色品牌栏、标准灰色导航按钮、绿色进度条。
/// 安装器是独立自包含 exe，不共享 WPF 资源，这里手写常量与控件工厂。
/// </summary>
internal static class InstallerTheme
{
    public static readonly Color Window = Color.White;
    public static readonly Color Card = Color.White;
    public static readonly Color CardBorder = Color.FromArgb(208, 208, 208);
    public static readonly Color Border = Color.FromArgb(160, 160, 160);
    public static readonly Color Text = Color.FromArgb(32, 32, 32);
    public static readonly Color Muted = Color.FromArgb(72, 72, 72);
    public static readonly Color Faint = Color.FromArgb(128, 128, 128);
    public static readonly Color Accent = Color.FromArgb(0, 102, 204);
    public static readonly Color Danger = Color.FromArgb(196, 43, 28);
    public static readonly Color Success = Color.FromArgb(16, 124, 16);

    public static readonly Color ButtonFace = Color.FromArgb(225, 225, 225);
    public static readonly Color ButtonHover = Color.FromArgb(213, 225, 242);
    public static readonly Color ButtonHoverBorder = Color.FromArgb(0, 120, 215);

    public static readonly Color SidebarTop = Color.FromArgb(10, 118, 204);
    public static readonly Color SidebarBottom = Color.FromArgb(4, 76, 156);
    public static readonly Color SidebarText = Color.White;

    public static Font BaseFont { get; } = new("Microsoft YaHei UI", 9F);
    public static Font TitleFont { get; } = new("Microsoft YaHei UI", 11.5F, FontStyle.Bold);
    public static Font SectionFont { get; } = new("Microsoft YaHei UI", 9.75F, FontStyle.Bold);

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

    /// <summary>经典灰色按钮：primary = 加粗主按钮（下一步/安装/完成）；danger = 红字（卸载）。</summary>
    public static Button MakeButton(string text, string kind = "normal")
    {
        var button = new Button
        {
            Text = text,
            FlatStyle = FlatStyle.Flat,
            Font = new Font(BaseFont.FontFamily, 9F, kind == "primary" ? FontStyle.Bold : FontStyle.Regular),
            ForeColor = kind == "danger" ? Danger : Text,
            BackColor = ButtonFace,
            Height = 26,
            Cursor = Cursors.Hand,
            UseVisualStyleBackColor = false
        };
        button.FlatAppearance.BorderColor = Border;
        button.FlatAppearance.BorderSize = 1;
        button.FlatAppearance.MouseOverBackColor = ButtonHover;
        button.FlatAppearance.MouseDownBackColor = ButtonHover;
        return button;
    }
}
