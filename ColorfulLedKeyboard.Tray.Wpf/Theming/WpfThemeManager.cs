using System.IO;
using System.Windows;
using System.Windows.Media;
using ColorfulLedKeyboard.Core;

namespace ColorfulLedKeyboard.Tray.Wpf;

/// <summary>
/// WPF 前端主题管理：深/浅两套 ResourceDictionary 即时切换 + 用户自定义强调色。
/// 调色板数值与 WinForms 版 UiTheme 保持一致；Primary/Secondary/PrimarySoft/PrimaryText
/// 四个强调色派生刷在应用时重算写入 Application 资源，覆盖字典值。
/// </summary>
public static class WpfThemeManager
{
    private const int DwmUseImmersiveDarkMode = 20;

    private static UiThemeKind _currentKind = UiThemeKind.Windows11;
    private static Color? _accentOverride;
    private static readonly Dictionary<UiThemeKind, Uri> DictionaryUris = new()
    {
        [UiThemeKind.Windows11] = new Uri("pack://application:,,,/Themes/Dark.xaml", UriKind.Absolute),
        [UiThemeKind.Technology] = new Uri("pack://application:,,,/Themes/Light.xaml", UriKind.Absolute),
        [UiThemeKind.Warm] = new Uri("pack://application:,,,/Themes/Light.xaml", UriKind.Absolute),
    };

    /// <summary>强调色或主题变化后触发；订阅方须成对退订（静态事件）。</summary>
    public static event EventHandler? ThemeChanged;

    public static UiThemeKind CurrentKind => _currentKind;

    /// <summary>当前深浅档位下调色板的默认强调色（与 Dark/Light.xaml 保持一致）。</summary>
    public static Color DefaultAccent => IsDark
        ? Color.FromRgb(0x38, 0xC8, 0xF0)
        : Color.FromRgb(0x1E, 0x8F, 0xC4);

    public static bool IsDark => _currentKind == UiThemeKind.Windows11;

    /// <summary>用户自定义强调色；null 表示调色板默认。</summary>
    public static Color? AccentOverride
    {
        get => _accentOverride;
        set
        {
            if (_accentOverride == value) return;
            _accentOverride = value;
            ApplyAccentBrushes();
            ThemeChanged?.Invoke(null, EventArgs.Empty);
        }
    }

    public static void Initialize(UiThemeKind kind)
    {
        _currentKind = Enum.IsDefined(kind) ? kind : UiThemeKind.Windows11;
        Apply(_currentKind, _accentOverride);
    }

    public static void SetTheme(UiThemeKind kind)
    {
        if (!Enum.IsDefined(kind)) kind = UiThemeKind.Windows11;
        if (_currentKind == kind) return;
        _currentKind = kind;
        Apply(_currentKind, _accentOverride);
        ThemeChanged?.Invoke(null, EventArgs.Empty);
    }

    private static void Apply(UiThemeKind kind, Color? accent)
    {
        var resources = Application.Current.Resources;
        if (resources.MergedDictionaries.Count > 0)
        {
            resources.MergedDictionaries[0] = new ResourceDictionary { Source = DictionaryUris[kind] };
        }
        else
        {
            resources.MergedDictionaries.Add(new ResourceDictionary { Source = DictionaryUris[kind] });
        }
        ApplyAccentBrushes();
        foreach (var window in Application.Current.Windows.OfType<Window>())
        {
            ApplyTitleBarMode(window);
            window.InvalidateVisual();
        }
    }

    private static void ApplyAccentBrushes()
    {
        var resources = Application.Current.Resources;
        // 基色必须读主题字典（MergedDictionaries[0]）：app 级键会被本方法覆写，
        // 从 app 级读会把旧主题/旧自定义色钉死，切主题与"默认"色板都会失效。
        var merged = resources.MergedDictionaries.Count > 0 ? resources.MergedDictionaries[0] : null;
        Color? baseColor = merged?["Brush.Primary"] is SolidColorBrush brush ? brush.Color : null;
        if (baseColor is null) return;

        var accent = _accentOverride ?? baseColor.Value;
        resources["Brush.Primary"] = new SolidColorBrush(accent);
        resources["Brush.Secondary"] = new SolidColorBrush(Multiply(accent, 0.8f));
        resources["Brush.PrimarySoft"] = new SolidColorBrush(Blend(accent, ReadColor(resources, "Brush.Surface") ?? accent, 0.16f));
        resources["Brush.PrimaryText"] = new SolidColorBrush(
            Luminance(accent) > 0.22 ? Color.FromRgb(0x22, 0x1A, 0x08) : Colors.White);
    }

    private static Color? ReadColor(ResourceDictionary resources, string key) =>
        resources[key] is SolidColorBrush brush ? brush.Color : null;

    private static Color Multiply(Color color, float factor) => Color.FromRgb(
        (byte)Math.Clamp((int)(color.R * factor), 0, 255),
        (byte)Math.Clamp((int)(color.G * factor), 0, 255),
        (byte)Math.Clamp((int)(color.B * factor), 0, 255));

    private static Color Blend(Color overlay, Color baseColor, float alpha) => Color.FromRgb(
        (byte)(overlay.R * alpha + baseColor.R * (1 - alpha)),
        (byte)(overlay.G * alpha + baseColor.G * (1 - alpha)),
        (byte)(overlay.B * alpha + baseColor.B * (1 - alpha)));

    private static float Luminance(Color color)
    {
        static double Channel(byte value)
        {
            var component = value / 255d;
            return component <= 0.04045 ? component / 12.92 : Math.Pow((component + 0.055) / 1.055, 2.4);
        }
        return (float)(0.2126 * Channel(color.R) + 0.7152 * Channel(color.G) + 0.0722 * Channel(color.B));
    }

    /// <summary>标题栏深浅随主题；窗口句柄创建后调用一次（可重复调用，覆盖写）。</summary>
    public static void ApplyTitleBarMode(Window window)
    {
        if (!window.IsLoaded) return;
        try
        {
            var value = IsDark ? 1 : 0;
            DwmSetWindowAttribute(new System.Windows.Interop.WindowInteropHelper(window).Handle,
                DwmUseImmersiveDarkMode, ref value, sizeof(int));
        }
        catch (DllNotFoundException)
        {
        }
    }

    [System.Runtime.InteropServices.DllImport("dwmapi.dll")]
    private static extern int DwmSetWindowAttribute(System.IntPtr hwnd, int attribute, ref int value, int size);

    /// <summary>解析 UiState 里持久化的强调色约定（0=默认，-1=跟随键盘主色，其余 ARGB）。</summary>
    public static Color? ResolveAccent(int accentArgb)
    {
        try
        {
            return accentArgb switch
            {
                UiState.AccentDefault => null,
                UiState.AccentFollowKeyboard => ReadKeyboardAccent(),
                // 界面强调色恒为不透明：alpha 字节强制 FF（持久化方 ArgbOf 已保证，此处兜底）。
                _ => Color.FromArgb(0xFF,
                    (byte)((accentArgb >> 16) & 0xFF),
                    (byte)((accentArgb >> 8) & 0xFF),
                    (byte)(accentArgb & 0xFF))
            };
        }
        catch
        {
            return null;
        }
    }

    private static Color? ReadKeyboardAccent()
    {
        try
        {
            var rgb = RgbColor.FromHex(new SettingsStore().Load().Effect.Color);
            return Color.FromRgb(rgb.R, rgb.G, rgb.B);
        }
        catch
        {
            return null;
        }
    }
}
