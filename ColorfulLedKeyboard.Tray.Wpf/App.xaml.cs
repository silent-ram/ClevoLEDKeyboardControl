using System.IO;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using ColorfulLedKeyboard.Core;

namespace ColorfulLedKeyboard.Tray.Wpf;

public partial class App : Application
{
    // 单实例互斥名与 WinForms 版一致：安装器升级 KillTray 后由本进程接管，避免双实例。
    private const string SingleInstanceMutexName = "Local\\ClevoLEDKeyboardControl.Tray";
    private const string OpenSettingsEventName = "Local\\ClevoLEDKeyboardControl.OpenSettings";
    private Mutex? _singleInstanceMutex;
    private EventWaitHandle? _openSettingsEvent;
    private WpfTrayContext? _trayContext;
    private string[] _startupArgs = [];

    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);

        _openSettingsEvent = new EventWaitHandle(false, EventResetMode.AutoReset, OpenSettingsEventName);
        _singleInstanceMutex = new Mutex(initiallyOwned: true, SingleInstanceMutexName, out var createdNew);
        if (!createdNew)
        {
            _openSettingsEvent.Set();
            Shutdown();
            return;
        }
        Exit += (_, _) =>
        {
            _trayContext?.Dispose();
            _openSettingsEvent?.Dispose();
            _singleInstanceMutex?.Dispose();
        };

        var args = e.Args;
        _startupArgs = args;
        var openSettings = args.Any(arg =>
            string.Equals(arg, "--settings", StringComparison.OrdinalIgnoreCase) ||
            string.Equals(arg, "/settings", StringComparison.OrdinalIgnoreCase));
        string? screenshotDir = null;
        var screenshotIndex = Array.IndexOf(args, "--screenshot-dir");
        if (screenshotIndex >= 0 && screenshotIndex + 1 < args.Length) screenshotDir = args[screenshotIndex + 1];
        var themeArg = ExtractValue(args, "--theme");
        var accentArg = ExtractValue(args, "--accent");

        var uiState = UiStateStore.Shared.Load();
        var kind = Enum.TryParse<UiThemeKind>(themeArg, ignoreCase: true, out var parsed) ? parsed : uiState.Theme;
        WpfThemeManager.Initialize(kind);
        // 截图验收参数优先；正常启动应用 UiState 持久化的强调色（对照 WinForms Program.cs）。
        if (accentArg is not null && int.TryParse(accentArg, out var accentArgb))
            WpfThemeManager.AccentOverride = WpfThemeManager.ResolveAccent(accentArgb);
        else
            WpfThemeManager.AccentOverride = WpfThemeManager.ResolveAccent(uiState.AccentArgb);

        _trayContext = new WpfTrayContext(openSettingsOnStartup: openSettings && screenshotDir is null);

        if (screenshotDir is not null)
        {
            RunScreenshotMode(screenshotDir);
        }
    }

    private static string? ExtractValue(string[] args, string name)
    {
        var index = Array.IndexOf(args, name);
        return index >= 0 && index + 1 < args.Length ? args[index + 1] : null;
    }

    /// <summary>迁移验收工具：--screenshot-dir 触发，逐页 RenderTargetBitmap 存 PNG 后退出。</summary>
    private void RunScreenshotMode(string directory)
    {
        Directory.CreateDirectory(directory);
        Dispatcher.BeginInvoke(DispatcherPriority.ApplicationIdle, () =>
        {
            _trayContext!.OpenSettings();
            var window = Current.Windows.OfType<MainWindow>().FirstOrDefault();
            if (window is null)
            {
                Shutdown();
                return;
            }

            if (_startupArgs.Contains("--force-lighting")) window.ForceLightingModeForCapture();
            for (var index = 0; index < window.PageCount; index++)
            {
                window.SelectPage(index);
                DoEvents();
                Capture(window, Path.Combine(directory, $"page{index}.png"));
            }

            // 运行时主题切换验收：精确走 SetTheme → ThemeChanged → RebuildPagesForTheme 路径，
            // 切换后仍能截图即证明不闪退且颜色刷新。
            if (ExtractValue(_startupArgs, "--switch-theme") is { } switchArg &&
                Enum.TryParse<UiThemeKind>(switchArg, ignoreCase: true, out var targetKind))
            {
                WpfThemeManager.SetTheme(targetKind);
                DoEvents();
                window.SelectPage(6);
                DoEvents();
                Capture(window, Path.Combine(directory, $"page6-switched-{targetKind}.png"));
            }
            Shutdown();
        });
    }

    private static void DoEvents()
    {
        var frame = new DispatcherFrame();
        Dispatcher.CurrentDispatcher.BeginInvoke(DispatcherPriority.Background,
            new DispatcherOperationCallback(state => { ((DispatcherFrame)state).Continue = false; return null; }), frame);
        Dispatcher.PushFrame(frame);
    }

    private static void Capture(Window window, string path)
    {
        window.UpdateLayout();
        var dpi = VisualTreeHelper.GetDpi(window);
        var width = (int)Math.Ceiling(window.ActualWidth * dpi.DpiScaleX);
        var height = (int)Math.Ceiling(window.ActualHeight * dpi.DpiScaleY);
        if (width <= 0 || height <= 0) return;
        var bitmap = new RenderTargetBitmap(width, height, 96 * dpi.DpiScaleX, 96 * dpi.DpiScaleY, PixelFormats.Pbgra32);
        bitmap.Render(window);
        var encoder = new PngBitmapEncoder();
        encoder.Frames.Add(BitmapFrame.Create(bitmap));
        using var stream = File.Create(path);
        encoder.Save(stream);
    }
}
