using System.Diagnostics;
using System.Drawing.Drawing2D;
using System.Reflection;

namespace ColorfulLedKeyboard.Tray;

public sealed class AboutForm : ThemedForm
{
    private const string RepositoryUrl = "https://github.com/silent-ram/ClevoLEDKeyboardControl";
    private const string IssuesUrl = "https://github.com/silent-ram/ClevoLEDKeyboardControl/issues";

    public AboutForm()
    {
        Text = "关于 ClevoLEDKeyboardControl";
        StartPosition = FormStartPosition.CenterScreen;
        FormBorderStyle = FormBorderStyle.FixedDialog;
        MaximizeBox = false;
        MinimizeBox = false;
        ClientSize = new Size(500, 300);

        BuildUi();
    }

    private void BuildUi()
    {
        var title = new Label
        {
            Text = "ClevoLEDKeyboardControl",
            Font = UiFonts.Title(14F),
            Location = new Point(26, 24),
            Size = new Size(440, 32)
        };

        var version = new Label
        {
            Text = $"版本 v{ReadVersion()}",
            Font = UiFonts.Body(9F),
            ForeColor = SystemColors.GrayText,
            Location = new Point(26, 62),
            Size = new Size(440, 22)
        };

        var description = new Label
        {
            Text = "面向 Clevo 兼容机型的键盘背光灯效控制程序。",
            Font = UiFonts.Body(9F),
            Location = new Point(26, 92),
            Size = new Size(440, 24)
        };

        var maintainer = new Label
        {
            Text = "Maintained by silent-ram · Uses InsydeDCHU.dll (Clevo OEM)",
            Font = UiFonts.Body(9F),
            ForeColor = SystemColors.GrayText,
            Location = new Point(26, 120),
            Size = new Size(440, 22)
        };

        var github = new LinkLabel
        {
            Text = "GitHub 仓库",
            Font = UiFonts.Body(9F),
            AutoSize = true,
            Location = new Point(26, 168)
        };
        github.LinkClicked += (_, _) => OpenUrl(RepositoryUrl);

        var issues = new LinkLabel
        {
            Text = "反馈 / 报告问题",
            Font = UiFonts.Body(9F),
            AutoSize = true,
            Location = new Point(140, 168)
        };
        issues.LinkClicked += (_, _) => OpenUrl(IssuesUrl);

        var close = new Button
        {
            Text = "关闭",
            DialogResult = DialogResult.OK,
            Location = new Point(388, 246),
            Size = new Size(88, UiMetrics.ButtonHeight)
        };

        Controls.Add(title);
        Controls.Add(version);
        Controls.Add(description);
        Controls.Add(maintainer);
        Controls.Add(github);
        Controls.Add(issues);
        Controls.Add(close);

        // 签名元素：标题下的一条 RGB 光谱短条，与主窗口页头呼应。
        Paint += (_, e) =>
        {
            using var path = UiShapes.RoundedRectangle(new Rectangle(26, 56, 148, 3), 1);
            e.Graphics.SmoothingMode = SmoothingMode.AntiAlias;
            var previousClip = e.Graphics.Clip;
            e.Graphics.SetClip(new Rectangle(26, 56, 148, 4));
            UiSpectrum.Draw(e.Graphics, new Rectangle(26, 55, 148, 5));
            e.Graphics.Clip = previousClip;
        };

        AcceptButton = close;
        CancelButton = close;
    }

    private static string ReadVersion()
    {
        var assembly = Assembly.GetExecutingAssembly();
        var informational = assembly
            .GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion;
        if (!string.IsNullOrWhiteSpace(informational))
        {
            // 去掉 +commitHash 后缀（如果有）
            var plus = informational.IndexOf('+');
            return plus > 0 ? informational[..plus] : informational;
        }

        return assembly.GetName().Version?.ToString(3) ?? "0.0.0";
    }

    private static void OpenUrl(string url)
    {
        Process.Start(new ProcessStartInfo(url) { UseShellExecute = true });
    }
}
