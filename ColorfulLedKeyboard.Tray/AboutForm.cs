using System.Diagnostics;

namespace ColorfulLedKeyboard.Tray;

public sealed class AboutForm : Form
{
    private const string RepositoryUrl = "https://github.com/xuha233/ClevoRGBControl";
    private const string BilibiliUrl = "https://space.bilibili.com/498047812";

    public AboutForm()
    {
        Text = "关于 ClevoRGBControl";
        StartPosition = FormStartPosition.CenterScreen;
        FormBorderStyle = FormBorderStyle.FixedDialog;
        MaximizeBox = false;
        MinimizeBox = false;
        ClientSize = new Size(430, 220);

        BuildUi();
    }

    private void BuildUi()
    {
        var title = new Label
        {
            Text = "ClevoRGBControl",
            Font = new Font(SystemFonts.MessageBoxFont ?? Control.DefaultFont, FontStyle.Bold),
            Location = new Point(24, 22),
            Size = new Size(360, 28)
        };

        var description = new Label
        {
            Text = "Clevo 系列键盘 RGB 控制工具",
            Location = new Point(24, 56),
            Size = new Size(360, 24)
        };

        var github = new LinkLabel
        {
            Text = "GitHub 仓库",
            Location = new Point(24, 100),
            Size = new Size(360, 24)
        };
        github.LinkClicked += (_, _) => OpenUrl(RepositoryUrl);

        var bilibili = new LinkLabel
        {
            Text = "@吴楔橙",
            Location = new Point(24, 130),
            Size = new Size(360, 24)
        };
        bilibili.LinkClicked += (_, _) => OpenUrl(BilibiliUrl);

        var close = new Button
        {
            Text = "关闭",
            DialogResult = DialogResult.OK,
            Location = new Point(318, 174),
            Size = new Size(88, 30)
        };

        Controls.Add(title);
        Controls.Add(description);
        Controls.Add(github);
        Controls.Add(bilibili);
        Controls.Add(close);

        AcceptButton = close;
        CancelButton = close;
    }

    private static void OpenUrl(string url)
    {
        Process.Start(new ProcessStartInfo(url) { UseShellExecute = true });
    }
}
