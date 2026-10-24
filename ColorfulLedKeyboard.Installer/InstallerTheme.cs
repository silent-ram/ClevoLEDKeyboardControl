using System.Drawing;
using System.Drawing.Drawing2D;
using System.Runtime.InteropServices;
using System.Windows.Forms;

namespace ColorfulLedKeyboard.Installer;

/// <summary>
/// 安装器轻量主题：与托盘程序同体系的深色仪器面板视觉。
/// 安装器是独立自包含 exe，不共享 WPF 资源，这里手写常量与控件工厂。
/// </summary>
internal static class InstallerTheme
{
    public static readonly Color Window = Color.FromArgb(14, 20, 32);
    public static readonly Color Card = Color.FromArgb(21, 27, 42);
    public static readonly Color Border = Color.FromArgb(42, 53, 76);
    public static readonly Color Text = Color.FromArgb(233, 238, 247);
    public static readonly Color Muted = Color.FromArgb(147, 160, 182);
    public static readonly Color Faint = Color.FromArgb(109, 122, 146);
    public static readonly Color Accent = Color.FromArgb(0, 128, 255);
    public static readonly Color AccentHover = Color.FromArgb(84, 138, 255);
    public static readonly Color Danger = Color.FromArgb(214, 69, 56);
    public static readonly Color DangerHover = Color.FromArgb(235, 92, 80);
    public static readonly Color Success = Color.FromArgb(74, 222, 128);

    public static readonly Color[] Spectrum =
    [
        Color.FromArgb(255, 78, 66),
        Color.FromArgb(255, 154, 60),
        Color.FromArgb(255, 216, 74),
        Color.FromArgb(74, 222, 128),
        Color.FromArgb(56, 200, 240),
        Color.FromArgb(110, 139, 255),
        Color.FromArgb(224, 92, 232),
    ];

    public static Font BaseFont { get; } = new("Microsoft YaHei UI", 9.5f);
    public static Font TitleFont { get; } = new("Microsoft YaHei UI", 15f, FontStyle.Bold);
    public static Font SectionFont { get; } = new("Microsoft YaHei UI", 12f, FontStyle.Bold);

    [DllImport("dwmapi.dll")]
    private static extern int DwmSetWindowAttribute(IntPtr hwnd, int attribute, ref int value, int size);

    /// <summary>深色标题栏（Win10 1903+ 属性 20，旧版回退 19）。</summary>
    public static void ApplyDarkTitleBar(IWin32Window window)
    {
        try
        {
            var handle = window.Handle;
            var on = 1;
            if (DwmSetWindowAttribute(handle, 20, ref on, sizeof(int)) != 0)
            {
                DwmSetWindowAttribute(handle, 19, ref on, sizeof(int));
            }
        }
        catch
        {
            // 旧系统不支持深色标题栏时保持默认
        }
    }

    /// <summary>光谱签名条：七色线性渐变，复刻托盘关于页的视觉签名。</summary>
    public sealed class SpectrumBar : Control
    {
        public SpectrumBar()
        {
            Height = 3;
            TabStop = false;
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            if (ClientRectangle.Width <= 0 || ClientRectangle.Height <= 0) return;
            using var brush = new LinearGradientBrush(ClientRectangle, Color.Empty, Color.Empty, 0f);
            var blend = new ColorBlend(Spectrum.Length)
            {
                Colors = Spectrum,
                Positions = Enumerable.Range(0, Spectrum.Length)
                    .Select(i => (float)i / (Spectrum.Length - 1)).ToArray()
            };
            brush.InterpolationColors = blend;
            e.Graphics.FillRectangle(brush, ClientRectangle);
        }

        protected override void OnResize(EventArgs eventargs)
        {
            base.OnResize(eventargs);
            Invalidate();
        }
    }

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

    public static Button MakeButton(string text, string kind = "ghost")
    {
        var button = new Button
        {
            Text = text,
            FlatStyle = FlatStyle.Flat,
            Font = BaseFont,
            Height = 36,
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
                button.BackColor = Card;
                button.ForeColor = Text;
                button.FlatAppearance.BorderColor = Border;
                button.FlatAppearance.BorderSize = 1;
                button.FlatAppearance.MouseOverBackColor = Color.FromArgb(30, 38, 58);
                button.FlatAppearance.MouseDownBackColor = Color.FromArgb(38, 47, 71);
                break;
        }
        return button;
    }
}
