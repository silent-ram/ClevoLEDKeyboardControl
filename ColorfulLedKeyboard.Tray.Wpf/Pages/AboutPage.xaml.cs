using System.Diagnostics;
using System.Reflection;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Animation;

namespace ColorfulLedKeyboard.Tray.Wpf.Pages;

public partial class AboutPage : UserControl
{
    private const string RepositoryUrl = "https://github.com/silent-ram/ClevoLEDKeyboardControl";
    private const string IssuesUrl = "https://github.com/silent-ram/ClevoLEDKeyboardControl/issues";

    public AboutPage()
    {
        InitializeComponent();
        VersionText.Text = $"版本 v{ReadVersion()}";
        RuntimeText.Text = $".NET {Environment.Version}";
        Loaded += (_, _) =>
        {
            WpfThemeManager.ThemeChanged += OnThemeChanged;
            ApplySpectrumText();
        };
        Unloaded += (_, _) =>
        {
            WpfThemeManager.ThemeChanged -= OnThemeChanged;
            StopSpectrumAnimation();
        };
    }

    // ---- 光谱流光署名 ----

    private void OnThemeChanged(object? sender, EventArgs e) => ApplySpectrumText();

    /// <summary>
    /// 把主题光谱签名刷作用到署名文字上：克隆两份渐变周期首尾相接，
    /// 再用 RelativeTransform 平移一个周期循环——颜色像流光一样滑过文字。
    /// Loaded/Unloaded 成对订阅，页面切走即停动画，主题切换即重取新刷。
    /// </summary>
    private void ApplySpectrumText()
    {
        var spectrum = Application.Current.Resources["Brush.Spectrum"] as LinearGradientBrush;
        if (spectrum is null || spectrum.GradientStops.Count == 0) return;

        var brush = new LinearGradientBrush { StartPoint = new Point(0, 0), EndPoint = new Point(1, 0) };
        foreach (var stop in spectrum.GradientStops)
        {
            brush.GradientStops.Add(new GradientStop(stop.Color, stop.Offset * 0.5));
            brush.GradientStops.Add(new GradientStop(stop.Color, stop.Offset * 0.5 + 0.5));
        }
        var translate = new TranslateTransform();
        brush.RelativeTransform = translate;
        AuthorText.Foreground = brush;

        if (!SystemParameters.ClientAreaAnimation) return; // 用户关闭动画时保持静态渐变
        translate.BeginAnimation(TranslateTransform.XProperty, new DoubleAnimation(0, 0.5, TimeSpan.FromSeconds(4))
        {
            RepeatBehavior = RepeatBehavior.Forever
        });
    }

    private void StopSpectrumAnimation()
    {
        if (AuthorText.Foreground is LinearGradientBrush brush &&
            brush.RelativeTransform is TranslateTransform translate)
        {
            translate.BeginAnimation(TranslateTransform.XProperty, null);
        }
    }

    private static string ReadVersion()
    {
        var informational = Assembly.GetExecutingAssembly()
            .GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion;
        if (!string.IsNullOrWhiteSpace(informational))
        {
            var plus = informational.IndexOf('+');
            return plus > 0 ? informational[..plus] : informational;
        }
        return Assembly.GetExecutingAssembly().GetName().Version?.ToString(3) ?? "0.0.0";
    }

    private void OpenRepository(object sender, RoutedEventArgs e) => OpenUrl(RepositoryUrl);

    private void OpenIssues(object sender, RoutedEventArgs e) => OpenUrl(IssuesUrl);

    private static void OpenUrl(string url) =>
        Process.Start(new ProcessStartInfo(url) { UseShellExecute = true });
}
