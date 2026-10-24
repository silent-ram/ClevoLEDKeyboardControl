using ColorfulLedKeyboard.Core;
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

    [Fact]
    public void LanguageDefaultsToAutoAndRoundTrips()
    {
        var store = new UiStateStore(PathName);
        var state = store.Load();
        Assert.Equal("auto", state.Language);

        state.Language = "en-US";
        store.Save(state);
        Assert.Equal("en-US", store.Load().Language);

        // 非法值在 Normalize 时回退 auto
        state.Language = "klingon";
        store.Save(state);
        Assert.Equal("auto", store.Load().Language);
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
}
