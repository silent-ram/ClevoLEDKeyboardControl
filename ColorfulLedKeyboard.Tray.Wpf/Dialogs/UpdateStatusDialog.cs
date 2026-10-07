using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using ColorfulLedKeyboard.Core;
using ColorfulLedKeyboard.Tray;

namespace ColorfulLedKeyboard.Tray.Wpf.Dialogs;

/// <summary>
/// 更新检查结果的主题化弹窗（替代系统 MessageBox，随深浅主题走）。
/// 三种形态：已是最新 / 发现新版本 / 检查失败。返回 true 表示用户选择了主操作。
/// </summary>
public static class UpdateStatusDialog
{
    public static void ShowUpToDate(string currentVersion)
    {
        Show(AppStrings.Get("upd.upToDate.title"),
            AppStrings.Format("upd.upToDate.body", currentVersion),
            AppStrings.Get("common.close"), secondaryText: null);
    }

    public static void ShowAvailable(string latestVersion, string currentVersion)
    {
        var confirmed = Show(AppStrings.Format("upd.available.title", latestVersion),
            AppStrings.Format("upd.available.body", currentVersion),
            AppStrings.Get("upd.available.cta"), secondaryText: AppStrings.Get("upd.available.later"));
        if (confirmed) UpdateChecker.OpenReleases();
    }

    public static void ShowFailure(string detail)
    {
        Show(AppStrings.Get("upd.fail.title"), detail, AppStrings.Get("common.close"), secondaryText: null);
    }

    private static bool Show(string headline, string body, string primaryText, string? secondaryText)
    {
        var window = new Window
        {
            Title = AppStrings.Get("upd.fail.title") + " - ClevoLEDKeyboardControl",
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

        var stack = new StackPanel { Margin = new Thickness(24, 20, 24, 20) };
        stack.Children.Add(new TextBlock
        {
            Text = headline,
            FontSize = 16,
            FontWeight = FontWeights.Bold,
            Foreground = (Brush)Application.Current.Resources["Brush.Text"]
        });
        stack.Children.Add(new System.Windows.Shapes.Rectangle
        {
            Height = 3,
            Width = 120,
            HorizontalAlignment = HorizontalAlignment.Left,
            Margin = new Thickness(0, 8, 0, 0),
            Fill = (Brush)Application.Current.Resources["Brush.Spectrum"],
            RadiusX = 1.5,
            RadiusY = 1.5
        });
        stack.Children.Add(new TextBlock
        {
            Text = body,
            TextWrapping = TextWrapping.Wrap,
            Margin = new Thickness(0, 12, 0, 0),
            Foreground = (Brush)Application.Current.Resources["Brush.MutedText"]
        });

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
        return window.Tag is true;
    }
}
