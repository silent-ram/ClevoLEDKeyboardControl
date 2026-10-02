using ColorfulLedKeyboard.Core;
using ColorfulLedKeyboard.Tray;
using System.Drawing;

namespace ColorfulLedKeyboard.Tests;

public sealed class UiStateTests : IDisposable
{
    private readonly string _directory = Path.Combine(Path.GetTempPath(), $"clevo-ui-state-{Guid.NewGuid():N}");
    private string PathName => Path.Combine(_directory, "ui-state.json");

    [Fact]
    public void MissingStateUsesWindows11Theme()
    {
        var state = new UiStateStore(PathName).Load();

        Assert.Equal(UiThemeKind.Windows11, state.Theme);
        Assert.Equal(UiState.CurrentVersion, state.Version);
        Assert.Equal(1180, state.WindowWidth);
        Assert.Equal(800, state.WindowHeight);
    }

    [Theory]
    [InlineData(0, "深色仪器风", true)]
    [InlineData(1, "浅色工作台", false)]
    [InlineData(2, "浅色工作台", false)]
    public void ThemeDefinitionsMatchDesignLanguage(int themeValue, string name, bool isDark)
    {
        var kind = (UiThemeKind)themeValue;
        var theme = UiTheme.For(kind);

        // "仪器面板"设计语言的深浅两套变体；kind 仅为旧配置兼容保留（Technology/Warm → 浅色）。
        Assert.Equal(kind, theme.Kind);
        Assert.Equal(name, theme.DisplayName);
        Assert.Equal(isDark, theme.IsDark);
        Assert.NotEqual(theme.Window, theme.Text);
        Assert.NotEqual(theme.Primary, theme.Surface);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    [InlineData(2)]
    public void ThemeTextMeetsReadableContrast(int themeValue)
    {
        var theme = UiTheme.For((UiThemeKind)themeValue);

        Assert.True(Contrast(theme.Text, theme.Surface) >= 4.5);
        Assert.True(Contrast(theme.MutedText, theme.Surface) >= 4.5);
        Assert.True(Contrast(theme.Text, theme.Window) >= 4.5);
        Assert.True(Contrast(theme.PrimaryText, theme.Primary) >= 4.5);
    }

    [Theory]
    [InlineData(0)]       // 默认（琥珀/琥珀铜）
    [InlineData(-1)]      // 跟随键盘：解析失败时回退默认
    [InlineData(0x38C8F0)] // 青
    [InlineData(0xE05C8C)] // 玫红
    [InlineData(0x1E63D0)] // 深蓝
    public void AccentOverrideKeepsReadableContrast(int accentArgb)
    {
        foreach (var kind in Enum.GetValues<UiThemeKind>())
        {
            var baseTheme = UiTheme.For(kind);
            var theme = accentArgb switch
            {
                0 => baseTheme,
                -1 => baseTheme,
                _ => baseTheme.WithAccent(Color.FromArgb(255, (accentArgb >> 16) & 0xFF, (accentArgb >> 8) & 0xFF, accentArgb & 0xFF))
            };

            Assert.True(Contrast(theme.PrimaryText, theme.Primary) >= 4.5,
                $"{kind} accent #{accentArgb:X8}: {Contrast(theme.PrimaryText, theme.Primary):F2}");
            Assert.True(Contrast(theme.Text, theme.PrimarySoft) >= 3.0,
                $"{kind} accent #{accentArgb:X8} soft: {Contrast(theme.Text, theme.PrimarySoft):F2}");
        }
    }

    [Fact]
    public void StateRoundTripsSelectedThemeAndLayout()
    {
        var store = new UiStateStore(PathName);
        store.Save(new UiState
        {
            Theme = UiThemeKind.Warm,
            WindowX = 120,
            WindowY = 80,
            WindowWidth = 1440,
            WindowHeight = 900,
            LastPage = 4,
            MusicAdvancedExpanded = true
        });

        var state = store.Load();

        Assert.Equal(UiThemeKind.Warm, state.Theme);
        Assert.Equal(120, state.WindowX);
        Assert.Equal(1440, state.WindowWidth);
        Assert.Equal(4, state.LastPage);
        Assert.True(state.MusicAdvancedExpanded);
        Assert.False(File.Exists(PathName + ".tmp"));
    }

    [Fact]
    public void CorruptStateFallsBackWithoutAffectingApplicationSettings()
    {
        Directory.CreateDirectory(_directory);
        File.WriteAllText(PathName, "{not json");

        var state = new UiStateStore(PathName).Load();

        Assert.Equal(UiThemeKind.Windows11, state.Theme);
        Assert.Equal(1180, state.WindowWidth);
    }

    [Fact]
    public void WindowBoundsAreClampedIntoVisibleWorkArea()
    {
        var visible = UiStateStore.EnsureVisible(
            new Rectangle(9000, 9000, 1500, 1000),
            [new Rectangle(0, 0, 1920, 1040)]);

        Assert.Equal(new Rectangle(420, 40, 1500, 1000), visible);
    }

    [Theory]
    [InlineData(96, 205)]
    [InlineData(144, 308)]
    [InlineData(192, 410)]
    public void NavigationWidthScalesWithDpi(int dpi, int expected)
    {
        Assert.Equal(expected, UiMetrics.ScaleForDpi(205, dpi));
    }

    public void Dispose()
    {
        if (Directory.Exists(_directory)) Directory.Delete(_directory, true);
    }

    private static double Contrast(Color left, Color right)
    {
        var light = Math.Max(Luminance(left), Luminance(right));
        var dark = Math.Min(Luminance(left), Luminance(right));
        return (light + 0.05) / (dark + 0.05);
    }

    private static double Luminance(Color color)
    {
        static double Channel(byte value)
        {
            var component = value / 255d;
            return component <= 0.04045 ? component / 12.92 : Math.Pow((component + 0.055) / 1.055, 2.4);
        }
        return 0.2126 * Channel(color.R) + 0.7152 * Channel(color.G) + 0.0722 * Channel(color.B);
    }
}
