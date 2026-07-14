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
    [InlineData(0, "Windows 11 简洁风", 8)]
    [InlineData(1, "白色科技风", 5)]
    [InlineData(2, "柔和暖色风", 12)]
    public void ThemeDefinitionsAreComplete(int themeValue, string name, int radius)
    {
        var kind = (UiThemeKind)themeValue;
        var theme = UiTheme.For(kind);

        Assert.Equal(kind, theme.Kind);
        Assert.Equal(name, theme.DisplayName);
        Assert.Equal(radius, theme.CornerRadius);
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
