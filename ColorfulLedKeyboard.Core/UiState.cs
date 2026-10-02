using System.Drawing;
using System.Text.Json;

namespace ColorfulLedKeyboard.Core;

// UI 状态的持久化类型。从 WinForms 托盘项目迁入 Core，供 WinForms 与 WPF 两个前端共享，
// 避免 ui-state.json 的读写逻辑出现两份漂移。

/// <summary>界面主题枚举。仅为兼容旧配置文件保留三个值：
/// Windows11 映射"深色仪器风"，Technology/Warm 映射"浅色工作台"。</summary>
public enum UiThemeKind
{
    Windows11,
    Technology,
    Warm
}

/// <summary>窗口几何、主题、强调色等界面偏好。AccentArgb 约定：
/// 0 = 调色板默认强调色；-1 = 跟随键盘主色；其余为 ARGB。</summary>
public sealed class UiState
{
    public const int CurrentVersion = 1;
    public const int AccentDefault = 0;
    public const int AccentFollowKeyboard = -1;

    public int Version { get; set; } = CurrentVersion;
    public UiThemeKind Theme { get; set; } = UiThemeKind.Windows11;
    public int AccentArgb { get; set; } = AccentDefault;
    public int WindowX { get; set; } = int.MinValue;
    public int WindowY { get; set; } = int.MinValue;
    public int WindowWidth { get; set; } = 1180;
    public int WindowHeight { get; set; } = 800;
    public int LastPage { get; set; }
    public bool MusicAdvancedExpanded { get; set; }

    /// <summary>外观卡用户添加的自定义强调色（ARGB）。</summary>
    public List<int> CustomAccents { get; set; } = [];

    public UiState Clone() => new()
    {
        Version = Version,
        Theme = Theme,
        AccentArgb = AccentArgb,
        CustomAccents = [.. CustomAccents],
        WindowX = WindowX,
        WindowY = WindowY,
        WindowWidth = WindowWidth,
        WindowHeight = WindowHeight,
        LastPage = LastPage,
        MusicAdvancedExpanded = MusicAdvancedExpanded
    };
}

public sealed class UiStateStore
{
    private const string FileName = "ui-state.json";
    private static readonly JsonSerializerOptions JsonOptions = new() { WriteIndented = true };
    private readonly object _sync = new();
    private readonly string _path;

    public UiStateStore(string? path = null)
    {
        _path = path ?? Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            AppPaths.ProgramDataFolderName,
            FileName);
    }

    public static UiStateStore Shared { get; } = new();

    public UiState Load()
    {
        lock (_sync)
        {
            try
            {
                if (!File.Exists(_path)) return new UiState();
                var state = JsonSerializer.Deserialize<UiState>(File.ReadAllText(_path), JsonOptions);
                return Normalize(state);
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or JsonException)
            {
                return new UiState();
            }
        }
    }

    public void Save(UiState state)
    {
        lock (_sync)
        {
            var temporary = _path + ".tmp";
            try
            {
                var normalized = Normalize(state);
                var directory = Path.GetDirectoryName(_path)!;
                Directory.CreateDirectory(directory);
                File.WriteAllText(temporary, JsonSerializer.Serialize(normalized, JsonOptions));
                File.Move(temporary, _path, true);
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                try { if (File.Exists(temporary)) File.Delete(temporary); } catch { }
            }
        }
    }

    public void Update(Action<UiState> update)
    {
        var state = Load();
        update(state);
        Save(state);
    }

    internal static UiState Normalize(UiState? state)
    {
        state ??= new UiState();
        state.Version = UiState.CurrentVersion;
        if (!Enum.IsDefined(state.Theme)) state.Theme = UiThemeKind.Windows11;
        state.WindowWidth = Math.Clamp(state.WindowWidth, 1040, 3840);
        state.WindowHeight = Math.Clamp(state.WindowHeight, 720, 2160);
        state.LastPage = Math.Clamp(state.LastPage, 0, 7);
        return state;
    }

    internal static Rectangle EnsureVisible(Rectangle bounds, IReadOnlyCollection<Rectangle> workAreas)
    {
        var width = Math.Clamp(bounds.Width, 1040, 3840);
        var height = Math.Clamp(bounds.Height, 720, 2160);
        if (workAreas.Count == 0) return new Rectangle(bounds.X, bounds.Y, width, height);
        var normalized = new Rectangle(bounds.X, bounds.Y, bounds.Width, bounds.Height);
        var target = workAreas.FirstOrDefault(area => area.IntersectsWith(normalized));
        if (target == Rectangle.Empty) target = workAreas.First();
        width = Math.Min(width, target.Width);
        height = Math.Min(height, target.Height);
        var x = Math.Clamp(normalized.X, target.Left, Math.Max(target.Left, target.Right - width));
        var y = Math.Clamp(normalized.Y, target.Top, Math.Max(target.Top, target.Bottom - height));
        return new Rectangle(x, y, width, height);
    }
}
