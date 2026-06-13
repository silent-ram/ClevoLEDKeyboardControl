using System.Diagnostics;
using System.Reflection;

namespace ColorfulLedKeyboard.Tray;

public sealed class AboutForm : Form
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
        ClientSize = new Size(480, 280);

        BuildUi();
    }

    private void BuildUi()
    {
        var title = new Label
        {
            Text = "ClevoLEDKeyboardControl",
            Font = new Font(SystemFonts.MessageBoxFont ?? Control.DefaultFont, FontStyle.Bold),
            Location = new Point(24, 22),
            Size = new Size(360, 28)
        };

        var version = new Label
        {
            Text = $"v{ReadVersion()}",
            Location = new Point(24, 50),
            Size = new Size(360, 22)
        };

        var description = new Label
        {
            Text = "面向 Clevo 兼容机型的键盘背光灯效控制程序。",
            Location = new Point(24, 80),
            Size = new Size(420, 24)
        };

        var maintainer = new Label
        {
            Text = "Maintained by silent-ram",
            Location = new Point(24, 110),
            Size = new Size(420, 22)
        };

        var thirdParty = new Label
        {
            Text = "Uses InsydeDCHU.dll (Clevo OEM)",
            Location = new Point(24, 134),
            Size = new Size(420, 22)
        };

        var github = new LinkLabel
        {
            Text = "GitHub 仓库",
            Location = new Point(24, 168),
            Size = new Size(160, 22)
        };
        github.LinkClicked += (_, _) => OpenUrl(RepositoryUrl);

        var issues = new LinkLabel
        {
            Text = "反馈 / 报告问题",
            Location = new Point(24, 196),
            Size = new Size(200, 22)
        };
        issues.LinkClicked += (_, _) => OpenUrl(IssuesUrl);

        var close = new Button
        {
            Text = "关闭",
            DialogResult = DialogResult.OK,
            Location = new Point(368, 234),
            Size = new Size(88, 30)
        };

        Controls.Add(title);
        Controls.Add(version);
        Controls.Add(description);
        Controls.Add(maintainer);
        Controls.Add(thirdParty);
        Controls.Add(github);
        Controls.Add(issues);
        Controls.Add(close);

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
