using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using ColorfulLedKeyboard.Core;
using ColorfulLedKeyboard.Tray;

namespace ColorfulLedKeyboard.Tray.Wpf.Dialogs;

/// <summary>
/// 更新检查结果的主题化弹窗（替代系统 MessageBox，随深浅主题走）。
/// 三种形态：已是最新 / 发现新版本 / 检查失败。前两种以“渠道选择”收尾：
/// 主操作拆成 GitHub Releases 与夸克网盘两个并列按钮，由用户自选下载渠道。
/// </summary>
public static class UpdateStatusDialog
{
    public static void ShowUpToDate(string currentVersion)
    {
        ShowChannel(AppStrings.Get("upd.upToDate.title"),
            AppStrings.Format("upd.upToDate.body", currentVersion),
            releaseUrl: null, laterText: null);
    }

    public static void ShowAvailable(string latestVersion, string currentVersion, string? releaseUrl = null)
    {
        ShowChannel(AppStrings.Format("upd.available.title", latestVersion),
            AppStrings.Format("upd.available.body", currentVersion),
            releaseUrl: releaseUrl, laterText: AppStrings.Get("upd.available.later"));
    }

    public static void ShowFailure(string detail)
    {
        Show(AppStrings.Get("upd.fail.title"), detail, AppStrings.Get("common.close"), secondaryText: null);
    }

    /// <summary>渠道选择形态：正文引导 + [稍后] [GitHub Releases] [夸克网盘下载]。</summary>
    private static void ShowChannel(string headline, string body, string? releaseUrl, string? laterText)
    {
        var window = CreateWindow();
        var stack = new StackPanel { Margin = new Thickness(24, 20, 24, 20) };
        stack.Children.Add(Headline(headline));
        stack.Children.Add(AccentRule());
        stack.Children.Add(BodyText(body));

        var buttons = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            HorizontalAlignment = HorizontalAlignment.Right,
            Margin = new Thickness(0, 18, 0, 0)
        };
        if (!string.IsNullOrEmpty(laterText))
        {
            var later = new Button
            {
                Content = laterText,
                Style = (Style)Application.Current.Resources["UiButton"],
                MinWidth = 88,
                Margin = new Thickness(0, 0, 10, 0)
            };
            later.Click += (_, _) => window.Close();
            buttons.Children.Add(later);
        }
        var github = new Button
        {
            Content = AppStrings.Get("upd.channel.github"),
            Style = (Style)Application.Current.Resources["UiButton"],
            MinWidth = 132,
            Margin = new Thickness(0, 0, 10, 0)
        };
        github.Click += (_, _) =>
        {
            if (!string.IsNullOrWhiteSpace(releaseUrl)) UpdateChecker.OpenUrl(releaseUrl);
            else UpdateChecker.OpenReleases();
            window.Close();
        };
        buttons.Children.Add(github);
        var quark = new Button
        {
            Content = AppStrings.Get("upd.channel.quark"),
            Style = (Style)Application.Current.Resources["UiButtonPrimary"],
            MinWidth = 120
        };
        quark.Click += (_, _) =>
        {
            UpdateChecker.OpenQuark();
            window.Close();
        };
        buttons.Children.Add(quark);
        stack.Children.Add(buttons);
        window.Content = stack;
        ShowWindow(window);
    }

    private static Window CreateWindow()
    {
        // 修复：窗口标题此前写死为“检查更新失败”，已是最新/发现新版本形态全部顶着错误标题
        var window = new Window
        {
            Title = AppStrings.Get("upd.window.title") + " - ClevoLEDKeyboardControl",
            SizeToContent = SizeToContent.Height,
            Width = 400,
            ResizeMode = ResizeMode.NoResize,
            ShowInTaskbar = false,
            Background = (Brush)Application.Current.Resources["Brush.Window"],
            FontFamily = (FontFamily)Application.Current.Resources["Font.Body"],
            FontSize = 12,
            Foreground = (Brush)Application.Current.Resources["Brush.Text"]
        };
        window.SourceInitialized += (_, _) => WpfThemeManager.ApplyTitleBarMode(window);
        return window;
    }

    private static TextBlock Headline(string text) => new()
    {
        Text = text,
        FontSize = 16,
        FontWeight = FontWeights.Bold,
        Foreground = (Brush)Application.Current.Resources["Brush.Text"]
    };

    private static System.Windows.Shapes.Rectangle AccentRule() => new()
    {
        Height = 3,
        Width = 120,
        HorizontalAlignment = HorizontalAlignment.Left,
        Margin = new Thickness(0, 8, 0, 0),
        Fill = (Brush)Application.Current.Resources["Brush.Spectrum"],
        RadiusX = 1.5,
        RadiusY = 1.5
    };

    private static TextBlock BodyText(string text) => new()
    {
        Text = text,
        TextWrapping = TextWrapping.Wrap,
        Margin = new Thickness(0, 12, 0, 0),
        Foreground = (Brush)Application.Current.Resources["Brush.MutedText"]
    };

    private static void ShowWindow(Window window)
    {
        try
        {
            var owner = Application.Current.Windows.OfType<Window>().FirstOrDefault(w => w.IsActive);
            if (owner is not null)
            {
                window.Owner = owner;
                window.WindowStartupLocation = WindowStartupLocation.CenterOwner;
            }
            else
            {
                window.WindowStartupLocation = WindowStartupLocation.CenterScreen;
            }
        }
        catch
        {
            window.WindowStartupLocation = WindowStartupLocation.CenterScreen;
        }
        window.ShowDialog();
    }

    /// <summary>失败/旧式单按钮形态（保留给检查失败场景）。</summary>
    private static bool Show(string headline, string body, string primaryText, string? secondaryText)
    {
        var window = CreateWindow();

        var stack = new StackPanel { Margin = new Thickness(24, 20, 24, 20) };
        stack.Children.Add(Headline(headline));
        stack.Children.Add(AccentRule());
        stack.Children.Add(BodyText(body));

        var buttons = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            HorizontalAlignment = HorizontalAlignment.Right,
            Margin = new Thickness(0, 18, 0, 0)
        };
        if (!string.IsNullOrEmpty(secondaryText))
        {
            var secondary = new Button
            {
                Content = secondaryText,
                Style = (Style)Application.Current.Resources["UiButton"],
                MinWidth = 96,
                Margin = new Thickness(0, 0, 10, 0)
            };
            secondary.Click += (_, _) => window.Close();
            buttons.Children.Add(secondary);
        }
        var primary = new Button
        {
            Content = primaryText,
            Style = (Style)Application.Current.Resources["UiButtonPrimary"],
            MinWidth = 112
        };
        primary.Click += (_, _) =>
        {
            window.Tag = true;
            window.Close();
        };
        buttons.Children.Add(primary);
        stack.Children.Add(buttons);
        window.Content = stack;
        ShowWindow(window);
        return window.Tag is true;
    }
}
