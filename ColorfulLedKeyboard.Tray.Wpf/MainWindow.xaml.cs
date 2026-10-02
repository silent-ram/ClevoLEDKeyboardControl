using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Threading;
using ColorfulLedKeyboard.Core;
using ColorfulLedKeyboard.Tray.Wpf.Pages;
using ServiceProcess = System.ServiceProcess;

namespace ColorfulLedKeyboard.Tray.Wpf;

/// <summary>
/// 设置窗口骨架：侧边导航 + 状态页头 + 页面宿主 + 底部保存栏。
/// Phase 0 只实现"当前状态""关于"两页，其余页面随迁移阶段挂入。
/// </summary>
public partial class MainWindow : Window
{
    private static readonly (string Title, string Glyph)[] NavItems =
    [
        ("当前状态", "\uE80F"),   // Home
        ("灯效设置", "\uE791"),   // Lightbulb
        ("音乐模式", "\uEC4F"),   // MusicNote
        ("场景自动化", "\uE9D9"), // Flow
        ("事件反馈", "\uE945"),   // LightningBolt
        ("诊断与恢复", "\uE90F"), // Repair
        ("软件设置", "\uE713"),   // Settings
        ("关于", "\uE946"),       // Info
    ];

    private readonly UiStateStore _uiStateStore = UiStateStore.Shared;
    private readonly SettingsStore _settingsStore = new();
    private readonly UiState _initialUiState;
    private readonly List<Control> _pages = [];
    private const string BaseTitle = "ClevoLEDKeyboardControl 设置";
    private EffectPage? _effectPage;
    private MusicPage? _musicPage;
    private AutomationPage? _automationPage;
    private EventFeedbackPage? _eventFeedbackPage;
    private DiagnosticsPage? _diagnosticsPage;
    private SoftwareSettingsPage? _softwareSettingsPage;
    private readonly DispatcherTimer _statusTimer = new() { Interval = TimeSpan.FromSeconds(1) };
    private AudioSourceStatusInfo? _lastAudioStatus;
    private bool _ready;
    private int _lastPage;

    public event Action? SettingsSaved;

    public MainWindow()
    {
        InitializeComponent();
        _initialUiState = _uiStateStore.Load().Clone();
        RestoreWindowState(_initialUiState);

        BuildPages();
        BuildNavigation();
        Navigation.SelectionChanged += (_, _) => ApplySelectedPage(Navigation.SelectedIndex);
        Navigation.SelectedIndex = Math.Clamp(_initialUiState.LastPage, 0, _pages.Count - 1);
        ApplySelectedPage(Navigation.SelectedIndex);

        ApplyButton.Click += (_, _) => SaveSettings();
        RevertButton.Click += (_, _) => RevertChanges();

        _statusTimer.Tick += (_, _) => UpdateStatusHeader();
        _statusTimer.Start();
        UpdateStatusHeader();
        UpdateSaveBar();

        SourceInitialized += (_, _) => WpfThemeManager.ApplyTitleBarMode(this);
        Loaded += (_, _) => { _ready = true; UpdateStatusHeader(); };
        WpfThemeManager.ThemeChanged += OnThemeChanged;
        Closed += (_, _) =>
        {
            _statusTimer.Stop();
            WpfThemeManager.ThemeChanged -= OnThemeChanged;
        };
        Closing += (_, _) => PersistWindowState();
    }

    public int PageCount => _pages.Count;

    /// <summary>截图验收专用。</summary>
    public void ForceLightingModeForCapture() => _effectPage?.ForceLightingModeForCapture();

    public void SelectPage(int index)
    {
        if (index < 0 || index >= _pages.Count) return;
        Navigation.SelectedIndex = index;
    }

    /// <summary>托盘侧音频状态回推；存缓存后由状态定时器带入总览页。</summary>
    public void UpdateAudioSourceLabel(AudioSourceStatusInfo? info)
    {
        _lastAudioStatus = info;
        Dispatcher.BeginInvoke(() =>
        {
            _musicPage?.UpdateAudioSourceLabel(info);
            UpdateStatusHeader();
        });
    }

    /// <summary>托盘侧设置变更后回推；Phase 0 仅刷新状态头。</summary>
    public void ReloadFromStore() => Dispatcher.BeginInvoke(() =>
    {
        _effectPage?.LoadFromStore(new SettingsStore().Load());
        _musicPage?.LoadFromStore(new SettingsStore().Load());
        _automationPage?.LoadFromStore(new SettingsStore().Load());
        _eventFeedbackPage?.LoadFromStore(new SettingsStore().Load());
        _diagnosticsPage?.CollectAll();
        _softwareSettingsPage?.LoadFromStore(new SettingsStore().Load());
        UpdateStatusHeader();
        UpdateSaveBar();
    });

    /// <summary>软件设置页"发现新版本"徽标（WinForms _navigation.SetBadge 对齐）。</summary>
    public void UpdateNavigationBadge()
    {
        var badge = _softwareSettingsPage?.UpdateBadge;
        var items = NavItems.Select(item => new NavItem(item.Title, item.Glyph,
            item.Title == "软件设置" ? badge : null)).ToList();
        var selected = Navigation.SelectedIndex;
        Navigation.ItemsSource = items;
        Navigation.SelectedIndex = selected;
    }

    /// <summary>托盘侧触发的更新检查（结果落到软件设置页与导航徽标）。</summary>
    public void RunUpdateCheck(Func<System.Threading.Tasks.Task<UpdateCheckResult?>> check)
    {
        _ = _softwareSettingsPage?.CheckForUpdatesNowAsync(check);
    }

    public void ActivateWindow()
    {
        if (WindowState == WindowState.Minimized) WindowState = WindowState.Normal;
        Show();
        Activate();
    }

    private void BuildNavigation()
    {
        var items = NavItems.Select(item => new NavItem(item.Title, item.Glyph)).ToList();
        Navigation.ItemsSource = items;
    }

    private void BuildPages()
    {
        var overview = new OverviewPage();
        overview.PageRequested += (_, index) => SelectPage(index);
        _pages.Add(overview);

        var effectPage = new EffectPage();
        effectPage.Changed += (_, _) => UpdateSaveBar();
        effectPage.PageRequested += (_, index) => SelectPage(index);
        _effectPage = effectPage;
        _pages.Add(effectPage);
        effectPage.LoadFromStore(new SettingsStore().Load());

        var musicPage = new MusicPage();
        musicPage.Changed += (_, _) => UpdateSaveBar();
        musicPage.MusicPresetStateChanged += (_, _) => UpdateSaveBar();
        _musicPage = musicPage;
        _pages.Add(musicPage);
        musicPage.LoadFromStore(new SettingsStore().Load());
        musicPage.SetAdvancedExpanded(_initialUiState.MusicAdvancedExpanded);

        var automationPage = new AutomationPage();
        automationPage.Changed += (_, _) => UpdateSaveBar();
        automationPage.SimulatorRequested += (_, _) => new Dialogs.SceneSimulatorDialog(new SettingsStore().Load()) { Owner = this }.ShowDialog();
        _automationPage = automationPage;
        _pages.Add(automationPage);
        automationPage.LoadFromStore(new SettingsStore().Load());

        var eventFeedbackPage = new EventFeedbackPage();
        eventFeedbackPage.Changed += (_, _) => UpdateSaveBar();
        _eventFeedbackPage = eventFeedbackPage;
        _pages.Add(eventFeedbackPage);
        eventFeedbackPage.LoadFromStore(new SettingsStore().Load());

        var diagnosticsPage = new DiagnosticsPage();
        diagnosticsPage.Changed += (_, _) => ReloadFromStore();
        _diagnosticsPage = diagnosticsPage;
        _pages.Add(diagnosticsPage);

        var softwarePage = new SoftwareSettingsPage();
        softwarePage.SettingsChangedExternally += (_, _) => ReloadFromStore();
        softwarePage.NavigationBadgeChanged += (_, _) => UpdateNavigationBadge();
        _softwareSettingsPage = softwarePage;
        _pages.Add(softwarePage);
        softwarePage.LoadFromStore(new SettingsStore().Load());

        _pages.Add(new AboutPage());
    }

    private void ApplySelectedPage(int index)
    {
        if (index < 0 || index >= _pages.Count) return;
        foreach (var page in _pages) page.Visibility = Visibility.Collapsed;
        _pages[index].Visibility = Visibility.Visible;
        PageHost.Content = _pages[index];
        PageTitle.Text = NavItems[index].Title;
        _lastPage = index;
        if (_ready) _uiStateStore.Update(state => state.LastPage = index);
        if (_pages[index] is OverviewPage overview) UpdateOverview(overview);
    }

    // ---- 保存栏（WinForms SaveSettings/RevertChanges/UpdateSaveBar 的效果页部分）----

    private void SaveSettings()
    {
        // 音乐预设两段式保存的硬拦截（WinForms 语义一致）：预设未暂存时先去保存预设。
        if (_musicPage is { IsMusicPresetChanged: true })
        {
            SelectPage(2);
            System.Windows.MessageBox.Show(_musicPage.MusicPresetBlockedMessage, "ClevoLEDKeyboardControl",
                MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }

        try
        {
            var settings = _settingsStore.Load();
            _effectPage?.ApplyTo(settings);
            _musicPage?.ApplyTo(settings);
            _automationPage?.ApplyTo(settings);
            _eventFeedbackPage?.ApplyTo(settings);
            _softwareSettingsPage?.ApplyTo(settings);
            _settingsStore.Save(settings);
            _effectPage?.OnSaved(settings);
            _musicPage?.OnSaved(settings);
            _automationPage.ResetDirty();
            _eventFeedbackPage.ResetDirty();
            UpdateStatusHeader();
            UpdateSaveBar();
            // "跟随键盘主色"模式下，灯色改了界面强调色要跟着走（WinForms 1181 行语义）。
            if (UiStateStore.Shared.Load().AccentArgb == UiState.AccentFollowKeyboard)
            {
                WpfThemeManager.AccentOverride = WpfThemeManager.ResolveAccent(UiState.AccentFollowKeyboard);
            }
            // 展开态即时持久化（对照 WinForms 1177-1179）。
            _uiStateStore.Update(state => state.MusicAdvancedExpanded = _musicPage?.IsAdvancedExpanded ?? false);
            SettingsSaved?.Invoke();
        }
        catch (Exception ex) when (ex is FormatException or IOException or UnauthorizedAccessException)
        {
            System.Windows.MessageBox.Show($"无法保存设置：{ex.Message}", "ClevoLEDKeyboardControl",
                MessageBoxButton.OK, MessageBoxImage.Warning);
        }
    }

    private void RevertChanges()
    {
        _effectPage?.LoadFromStore(new SettingsStore().Load());
        _musicPage?.LoadFromStore(new SettingsStore().Load());
        _automationPage?.LoadFromStore(new SettingsStore().Load());
        _eventFeedbackPage?.LoadFromStore(new SettingsStore().Load());
        _softwareSettingsPage?.LoadFromStore(new SettingsStore().Load());
        UpdateStatusHeader();
        UpdateSaveBar();
    }

    private void UpdateSaveBar()
    {
        var dirty = _effectPage is { IsDirty: true } || _musicPage is { IsDirty: true } ||
            _automationPage is { IsDirty: true } || _eventFeedbackPage is { IsDirty: true };
        DirtyLabel.Text = dirty ? "● 有尚未保存的修改" : "✓ 设置已保存";
        DirtyLabel.Foreground = (Brush)Application.Current.Resources[dirty ? "Brush.Warning" : "Brush.Success"];
        RevertButton.IsEnabled = dirty;
        ApplyButton.IsEnabled = dirty;
        Title = dirty ? BaseTitle + " - 有未应用的更改" : BaseTitle;
    }

    // ---- 状态头与总览（移植自 WinForms UpdateStatusHeader/UpdateOverviewRuntime）----

    private void UpdateStatusHeader()
    {
        var automationStatus = AutomationStatus.Load();
        _musicPage?.RefreshRuntimeStatus(automationStatus);
        _automationPage?.UpdateStatusText(automationStatus);
        var serviceStatus = GetServiceStatusText();
        var driverStatus = GetDriverStatusText();
        var serviceReady = serviceStatus == "运行中";
        var componentReady = driverStatus.StartsWith("已安装", StringComparison.OrdinalIgnoreCase);

        HeaderStatus.Text = serviceReady && componentReady ? "● 服务与灯控正常" : "⚠ 需要检查运行状态";
        HeaderStatus.Foreground = FindBrush(serviceReady && componentReady ? "Brush.Success" : "Brush.Warning");

        if (_pages.Count == 0) return;
        if (_pages[Math.Max(0, Navigation.SelectedIndex)] is OverviewPage overview)
        {
            overview.SetServiceState(serviceStatus, componentReady);
            UpdateOverview(overview);
        }
    }

    private void UpdateOverview(OverviewPage overview)
    {
        var settings = new SettingsStore().Load();
        var status = AutomationStatus.Load();
        var fresh = status is not null && DateTimeOffset.UtcNow - status.UpdatedUtc <= TimeSpan.FromSeconds(10);
        var mode = !settings.Enabled || fresh && status!.FinalBrightnessLimit == 0
            ? "灯光已关闭"
            : fresh && (!string.IsNullOrWhiteSpace(status!.ActiveMusicApplication) || status.TargetDescription.StartsWith("音乐：", StringComparison.Ordinal))
                ? "音乐模式"
                : settings.OperatingMode == OperatingMode.Music ? "音乐模式" : "灯效模式";
        var brightness = fresh && !string.IsNullOrWhiteSpace(status!.BrightnessDisplay)
            ? status.BrightnessDisplay
            : settings.OperatingMode == OperatingMode.Music
                ? $"{settings.Effect.Music.BaseBrightness}–{settings.Effect.Music.PeakBrightness}%"
                : $"{settings.Brightness}%";
        var brightnessHint = fresh && !string.IsNullOrWhiteSpace(status!.BrightnessDescription)
            ? status.BrightnessDescription
            : "等待服务计算最终输出";
        var rule = fresh && !string.IsNullOrWhiteSpace(status!.ActiveRuleName)
            ? $"{status.ActiveRuleName} → {status.TargetDescription}"
            : settings.Automation.Enabled ? "当前使用基础设置" : "场景自动化未启用";
        var player = fresh && !string.IsNullOrWhiteSpace(status!.ActiveMusicApplication)
            ? $"{status.ActiveMusicApplication} · {(string.IsNullOrWhiteSpace(status.TrackTitle) ? "检测到声音" : status.TrackTitle)}"
            : "当前没有匹配的有声程序";
        var typing = settings.TypingPulse.Enabled ? "打字开启" : "打字关闭";
        var notification = settings.NotificationFlash.Enabled ? "通知开启" : "通知关闭";
        var events = $"{typing} · {notification}{(fresh && status!.IdleOverrideActive ? " · 空闲覆盖中" : "")}";

        overview.SetRuntime(mode, brightness, brightnessHint, rule, player, events);
    }

    private static string GetServiceStatusText()
    {
        try
        {
            using var controller = new ServiceProcess.ServiceController(AppPaths.ServiceName);
            return controller.Status switch
            {
                ServiceProcess.ServiceControllerStatus.Running => "运行中",
                ServiceProcess.ServiceControllerStatus.Stopped => "已停止",
                ServiceProcess.ServiceControllerStatus.Paused => "已暂停",
                ServiceProcess.ServiceControllerStatus.StartPending => "正在启动",
                ServiceProcess.ServiceControllerStatus.StopPending => "正在停止",
                _ => controller.Status.ToString()
            };
        }
        catch (InvalidOperationException)
        {
            return "未安装";
        }
        catch (Exception ex) when (ex is System.ComponentModel.Win32Exception or UnauthorizedAccessException)
        {
            return $"无法读取：{ex.Message}";
        }
    }

    private static string GetDriverStatusText()
    {
        var state = LoadDriverComponentState();
        if (state is not null)
        {
            if (string.Equals(state.Status, "Installed", StringComparison.OrdinalIgnoreCase) &&
                !string.IsNullOrWhiteSpace(state.InstalledPath) &&
                File.Exists(state.InstalledPath))
            {
                return $"已安装（来源：{state.Source ?? "未知"}）：{state.InstalledPath}";
            }

            if (string.Equals(state.Status, "Missing", StringComparison.OrdinalIgnoreCase))
            {
                return "未找到（安装器最近检查未命中）";
            }
        }

        var serviceDll = Path.Combine(AppContext.BaseDirectory, "InsydeDCHU.dll");
        var installedServiceDll = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles),
            "ClevoLEDKeyboardControl",
            "Service",
            "InsydeDCHU.dll");
        var controlCenterDll = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86),
            "ControlCenter",
            "InsydeDCHU.dll");

        if (File.Exists(serviceDll)) return $"已安装（来源：托盘目录）：{serviceDll}";
        if (File.Exists(installedServiceDll)) return $"已安装（来源：服务目录）：{installedServiceDll}";
        if (File.Exists(controlCenterDll)) return "OEM Control Center 中存在，未复制到服务目录";
        return "未找到";
    }

    private static DriverComponentState? LoadDriverComponentState()
    {
        try
        {
            if (!File.Exists(AppPaths.DriverComponentStatePath)) return null;
            var json = File.ReadAllText(AppPaths.DriverComponentStatePath);
            return System.Text.Json.JsonSerializer.Deserialize<DriverComponentState>(json);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or System.Text.Json.JsonException)
        {
            return null;
        }
    }

    private sealed record DriverComponentState(string? Status, string? Source, string? InstalledPath);

    private static System.Windows.Media.Brush FindBrush(string key) =>
        (System.Windows.Media.Brush)Application.Current.TryFindResource(key);

    // ---- 窗口状态 ----

    private void RestoreWindowState(UiState state)
    {
        if (state.WindowX == int.MinValue || state.WindowY == int.MinValue)
        {
            WindowStartupLocation = WindowStartupLocation.CenterScreen;
            Width = state.WindowWidth;
            Height = state.WindowHeight;
            return;
        }

        var workAreas = System.Windows.Forms.Screen.AllScreens.Select(screen => screen.WorkingArea).ToArray();
        var bounds = UiStateStore.EnsureVisible(
            new System.Drawing.Rectangle(state.WindowX, state.WindowY, state.WindowWidth, state.WindowHeight), workAreas);
        WindowStartupLocation = WindowStartupLocation.Manual;
        WindowState = WindowState.Normal;
        Left = bounds.X;
        Top = bounds.Y;
        Width = bounds.Width;
        Height = bounds.Height;
    }

    private void PersistWindowState()
    {
        if (WindowState != WindowState.Normal) return;
        _uiStateStore.Update(state =>
        {
            state.WindowX = (int)Left;
            state.WindowY = (int)Top;
            state.WindowWidth = (int)ActualWidth;
            state.WindowHeight = (int)ActualHeight;
            state.LastPage = _lastPage;
            state.MusicAdvancedExpanded = _musicPage?.IsAdvancedExpanded ?? false;
        });
    }

    private void OnThemeChanged(object? sender, EventArgs e) => WpfThemeManager.ApplyTitleBarMode(this);

    /// <summary>导航项数据。</summary>
    public sealed record NavItem(string Title, string IconGlyph, string? Badge = null);
}
