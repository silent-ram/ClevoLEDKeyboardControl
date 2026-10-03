using System.Diagnostics;
using System.Reflection;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Media.Effects;

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
    /// 把光谱流光作用到署名文字上（移植自 CodexPlusPlus 的 .brand-title：15 色标铺满
    /// 200% 背景 + background-position 0%→200% 线性循环 + 霓虹 text-shadow）。
    /// WPF 对应做法：单周期色标 + SpreadMethod=Repeat 平铺 + 平移一个整周期，
    /// 视觉上就是整条彩虹以恒速“穿过”文字；外挂 DropShadow 模拟发光。
    /// Loaded/Unloaded 成对订阅，页面切走即停动画，主题切换即重取新刷。
    /// </summary>
    private void ApplySpectrumText()
    {
        var spectrum = Application.Current.Resources["Brush.Spectrum"] as LinearGradientBrush;
        if (spectrum is null || spectrum.GradientStops.Count == 0) return;

        var stops = spectrum.GradientStops;
        var brush = new LinearGradientBrush
        {
            StartPoint = new Point(0, 0.5),
            EndPoint = new Point(1, 0.5),
            SpreadMethod = GradientSpreadMethod.Repeat
        };
        // 单周期：7 色均分 + 首色闭合，Repeat 平铺出无限长的彩虹带
        for (var i = 0; i < stops.Count; i++)
        {
            brush.GradientStops.Add(new GradientStop(stops[i].Color, (double)i / stops.Count));
        }
        brush.GradientStops.Add(new GradientStop(stops[0].Color, 1.0));

        var translate = new TranslateTransform();
        brush.RelativeTransform = translate;
        AuthorText.Foreground = brush;
        AuthorText.Effect = new DropShadowEffect
        {
            Color = (Color)ColorConverter.ConvertFromString("#18D9FF"),
            BlurRadius = 7,
            ShadowDepth = 0,
            Opacity = 0.45
        };

        if (!SystemParameters.ClientAreaAnimation) return; // 用户关闭动画时保持静态渐变
        translate.BeginAnimation(TranslateTransform.XProperty, new DoubleAnimation(0, -1, TimeSpan.FromSeconds(2.8))
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
