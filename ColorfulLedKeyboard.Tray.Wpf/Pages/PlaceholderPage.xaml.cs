using System.Windows.Controls;

namespace ColorfulLedKeyboard.Tray.Wpf.Pages;

/// <summary>迁移过渡占位页：对应页面尚未迁移时给出明确状态，而不是空白。</summary>
public partial class PlaceholderPage : UserControl
{
    public PlaceholderPage(string title)
    {
        InitializeComponent();
        TitleText.Text = title;
        HintText.Text = "该页面正在迁移到 WPF 版（后续阶段交付），当前请使用 WinForms 版托盘。";
    }
}
