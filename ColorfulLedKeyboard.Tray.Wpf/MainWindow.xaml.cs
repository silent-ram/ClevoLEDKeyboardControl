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
/// </summary>
public partial class MainWindow : Window
{
    // 标题为 AppStrings key，语言切换后重建导航时即时取新值
    private static readonly (string Key, string Glyph)[] NavItems =
    [
        ("nav.overview", "\uE80F"),   // Home
        ("nav.effect", "\uE791"),   // Lightbulb
        ("nav.music", "\uEC4F"),   // MusicNote
        ("nav.automation", "\uE9D9"), // Flow
        ("nav.feedback", "\uE945"),   // LightningBolt
        ("nav.diagnostics", "\uE90F"), // Repair
        ("nav.settings", "\uE713"),   // Settings
        ("nav.about", "\uE946"),       // Info
    ];

    private static string NavTitle((string Key, string Glyph) item) => AppStrings.Get(item.Key);

    private readonly UiStateStore _uiStateStore = UiStateStore.Shared;
    private readonly SettingsStore _settingsStore = new();
    private readonly UiState _initialUiState;
    private readonly List<Control> _pages = [];
    private static string BaseTitle => AppStrings.Get("title.base");
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
        AppStrings.LanguageChanged += OnLanguageChanged;
        Closed += (_, _) =>
        {
            _statusTimer.Stop();
            WpfThemeManager.ThemeChanged -= OnThemeChanged;
            AppStrings.LanguageChanged -= OnLanguageChanged;
            _softwareSettingsPage?.UnsubscribeEvents();
        };
        Closing += (_, e) =>
        {
            PersistWindowState();
            if (!HasAnyDirty) return;
            var choice = System.Windows.MessageBox.Show(AppStrings.Get("win.closeDirty"),
                "ClevoLEDKeyboardControl", MessageBoxButton.YesNo, MessageBoxImage.Question);
            if (choice != MessageBoxResult.Yes) e.Cancel = true;
        };
    }

    public int PageCount => _pages.Count;

    /// <summary>截图验收专用。</summary>
    public void ForceLightingModeForCapture() => _effectPage?.ForceLightingModeForCapture();

    /// <summary>截图验收专用：强制音乐模式验收灯效页折叠态布局。</summary>
    public void ForceMusicModeForCapture() => _effectPage?.ForceMusicModeForCapture();

    /// <summary>截图验收专用：制造脏状态，验收脏状态下主题热切换。</summary>
    public void MarkDirtyForCapture() => _effectPage?.MarkDirtyForCapture();

    /// <summary>截图验收专用：滚动音乐页到节拍颜色列表。</summary>
    public void ScrollMusicPageForCapture() => _musicPage?.ScrollToBeatColorsForCapture();

    /// <summary>截图验收专用：强制显示强调色块的 ✕ 徽标（模拟悬停）。</summary>
    public void ShowSwatchCloseBadgesForCapture() => _softwareSettingsPage?.ShowSwatchCloseBadgesForCapture();

    /// <summary>截图验收专用：切换场景自动化编辑器页签。</summary>
    public void SelectAutomationEditorTab(int index) => _automationPage?.SelectEditorTab(index);

    public void SelectPage(int index)
    {
        if (index < 0 || index >= _pages.Count) return;
        Navigation.SelectedIndex = index;
    }

    /// <summary>托盘侧音频状态回推；存缓存后由状态定时器带入总览页。</summary>
    public void UpdateAudioSourceLabel(AudioSourceStatusInfo? info)
    {
        // 监视器偶发读到 null（写入瞬间/IPC 超时），不能让它回退掉上一次的有效值——
        // 否则标签会永远停在"检测中…"（状态文件只在设备变化时重写，恢复遥遥无期）。
        if (info is not null) _lastAudioStatus = info;
        var effective = _lastAudioStatus;
        Dispatcher.BeginInvoke(() =>
        {
            _musicPage?.UpdateAudioSourceLabel(effective);
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
        var items = NavItems.Select(item => new NavItem(NavTitle(item), item.Glyph,
            item.Key == "nav.settings" ? badge : null)).ToList();
        var selected = Navigation.SelectedIndex;
        Navigation.ItemsSource = items;
        Navigation.SelectedIndex = selected;
    }

    /// <summary>托盘侧触发的更新检查（结果落到软件设置页与导航徽标）。</summary>
    public void RunUpdateCheck(Func<System.Threading.Tasks.Task<UpdateCheckResult?>> check)
    {
        _ = _softwareSettingsPage?.CheckForUpdatesNowAsync(check);
    }

    /// <summary>关于页“检查更新”回调：由托盘注入手动检查管线（结果以主题化弹窗呈现）。</summary>
    public Func<System.Threading.Tasks.Task>? ManualUpdateCheck { get; set; }

    public void ActivateWindow()
    {
        if (WindowState == WindowState.Minimized) WindowState = WindowState.Normal;
        Show();
        Activate();
    }

    private void BuildNavigation()
    {
        var items = NavItems.Select(item => new NavItem(NavTitle(item), item.Glyph)).ToList();
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
        softwarePage.Changed += (_, _) => UpdateSaveBar();
        softwarePage.NavigationBadgeChanged += (_, _) => UpdateNavigationBadge();
        _softwareSettingsPage = softwarePage;
        _pages.Add(softwarePage);
        softwarePage.LoadFromStore(new SettingsStore().Load());

        var aboutPage = new AboutPage();
        // 关于页被动提示与软件设置页的检查结果同源（自动检查/打开窗口时的检查都会刷新）
        softwarePage.UpdateAvailableChanged += (_, info) => aboutPage.SetUpdateAvailability(string.IsNullOrWhiteSpace(info.Latest) ? null : info.Latest);
        _pages.Add(aboutPage);
    }

    private void ApplySelectedPage(int index)
    {
        if (index < 0 || index >= _pages.Count) return;
        foreach (var page in _pages) page.Visibility = Visibility.Collapsed;
        _pages[index].Visibility = Visibility.Visible;
        PageHost.Content = _pages[index];
        PageTitle.Text = NavTitle(NavItems[index]);
        _lastPage = index;
        if (_ready) _uiStateStore.Update(state => state.LastPage = index);
        if (_pages[index] is OverviewPage overview) UpdateOverview(overview);
    }

    // ---- 保存栏（WinForms SaveSettings/RevertChanges/UpdateSaveBar 的效果页部分）----

    private void SaveSettings()
    {
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
            _softwareSettingsPage?.ResetDirty();
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
            System.Windows.MessageBox.Show(AppStrings.Format("win.saveFail", ex.Message), "ClevoLEDKeyboardControl",
                MessageBoxButton.OK, MessageBoxImage.Warning);
        }
    }

    private void RevertChanges()
    {
        _uiStateStore.Update(state => state.MusicAdvancedExpanded = _initialUiState.MusicAdvancedExpanded);
        _musicPage?.SetAdvancedExpanded(_initialUiState.MusicAdvancedExpanded);
        _effectPage?.LoadFromStore(new SettingsStore().Load());
        _musicPage?.LoadFromStore(new SettingsStore().Load());
        _automationPage?.LoadFromStore(new SettingsStore().Load());
        _eventFeedbackPage?.LoadFromStore(new SettingsStore().Load());
        _softwareSettingsPage?.LoadFromStore(new SettingsStore().Load());
        UpdateStatusHeader();
        UpdateSaveBar();
    }

    private bool HasAnyDirty => _effectPage is { IsDirty: true } || _musicPage is { IsDirty: true } ||
        _automationPage is { IsDirty: true } || _eventFeedbackPage is { IsDirty: true } ||
        _softwareSettingsPage is { IsDirty: true };

    private void UpdateSaveBar()
    {
        var dirty = HasAnyDirty;
        DirtyLabel.Text = _musicPage is { IsMusicPresetChanged: true }
            ? AppStrings.Get("save.musicPresetDirty")
            : dirty ? AppStrings.Get("save.dirty") : AppStrings.Get("save.clean");
        DirtyLabel.Foreground = (Brush)Application.Current.Resources[dirty ? "Brush.Warning" : "Brush.Success"];
        RevertButton.IsEnabled = dirty;
        ApplyButton.IsEnabled = dirty;
        Title = dirty ? BaseTitle + AppStrings.Get("title.dirty") : BaseTitle;
    }

    // ---- 状态头与总览（移植自 WinForms UpdateStatusHeader/UpdateOverviewRuntime）----

    private void UpdateStatusHeader()
    {
        var automationStatus = AutomationStatus.Load();
        _musicPage?.RefreshRuntimeStatus(automationStatus);
        _automationPage?.UpdateStatusText(automationStatus);
        var serviceStatus = GetServiceStatusText();
        var driverStatus = GetDriverStatusText();
        var serviceReady = serviceStatus == AppStrings.Get("diag.svc.running");
        var componentReady = driverStatus.StartsWith(AppStrings.Get("win.driver.installed"), StringComparison.OrdinalIgnoreCase);

        HeaderStatus.Text = serviceReady && componentReady ? AppStrings.Get("head.ok") : AppStrings.Get("head.check");
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
            ? AppStrings.Get("ov.mode.off")
            : fresh && (!string.IsNullOrWhiteSpace(status!.ActiveMusicApplication) || status.TargetDescription.StartsWith("音乐：", StringComparison.Ordinal))
                ? AppStrings.Get("ov.mode.music")
                : settings.OperatingMode == OperatingMode.Music ? AppStrings.Get("ov.mode.music") : AppStrings.Get("ov.mode.lighting");
        var brightness = fresh && !string.IsNullOrWhiteSpace(status!.BrightnessDisplay)
            ? status.BrightnessDisplay
            : settings.OperatingMode == OperatingMode.Music
                ? $"{settings.Effect.Music.BaseBrightness}–{settings.Effect.Music.PeakBrightness}%"
                : $"{settings.Brightness}%";
        var brightnessHint = fresh && !string.IsNullOrWhiteSpace(status!.BrightnessDescription)
            ? status.BrightnessDescription
            : AppStrings.Get("ov.brightness.waiting");
        var rule = fresh && !string.IsNullOrWhiteSpace(status!.ActiveRuleName)
            ? $"{status.ActiveRuleName} → {status.TargetDescription}"
            : settings.Automation.Enabled ? AppStrings.Get("ov.rule.base") : AppStrings.Get("ov.rule.disabled");
        var player = fresh && !string.IsNullOrWhiteSpace(status!.ActiveMusicApplication)
            ? $"{status.ActiveMusicApplication} · {(string.IsNullOrWhiteSpace(status.TrackTitle) ? AppStrings.Get("ov.player.sound") : status.TrackTitle)}"
            : AppStrings.Get("ov.player.none");
        var typing = settings.TypingPulse.Enabled ? AppStrings.Get("ov.typing.on") : AppStrings.Get("ov.typing.off");
        var notification = settings.NotificationFlash.Enabled ? AppStrings.Get("ov.notify.on") : AppStrings.Get("ov.notify.off");
        var events = $"{typing} · {notification}{(fresh && status!.IdleOverrideActive ? " · " + AppStrings.Get("ov.idle.active") : "")}";

        overview.SetRuntime(mode, brightness, brightnessHint, rule, player, events);
    }

    private static string GetServiceStatusText()
    {
        try
        {
            using var controller = new ServiceProcess.ServiceController(AppPaths.ServiceName);
            return controller.Status switch
            {
                ServiceProcess.ServiceControllerStatus.Running => AppStrings.Get("diag.svc.running"),
                ServiceProcess.ServiceControllerStatus.Stopped => AppStrings.Get("diag.svc.stopped"),
                ServiceProcess.ServiceControllerStatus.Paused => AppStrings.Get("diag.svc.paused"),
                ServiceProcess.ServiceControllerStatus.StartPending => AppStrings.Get("diag.svc.startPending"),
                ServiceProcess.ServiceControllerStatus.StopPending => AppStrings.Get("diag.svc.stopPending"),
                _ => controller.Status.ToString()
            };
        }
        catch (InvalidOperationException)
        {
            return AppStrings.Get("diag.svc.notInstalled");
        }
        catch (Exception ex) when (ex is System.ComponentModel.Win32Exception or UnauthorizedAccessException)
        {
            return AppStrings.Format("diag.svc.readFail", ex.Message);
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
                return AppStrings.Format("diag.driver.installed", state.Source ?? AppStrings.Get("diag.driver.unknown"), state.InstalledPath);
            }

            if (string.Equals(state.Status, "Missing", StringComparison.OrdinalIgnoreCase))
            {
                return AppStrings.Get("diag.driver.notFound");
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

        if (File.Exists(serviceDll)) return AppStrings.Format("diag.driver.installed", AppStrings.Get("win.driver.trayDir"), serviceDll);
        if (File.Exists(installedServiceDll)) return AppStrings.Format("diag.driver.installed", AppStrings.Get("win.driver.serviceDir"), installedServiceDll);
        if (File.Exists(controlCenterDll)) return AppStrings.Get("win.driver.inCC");
        return AppStrings.Get("win.driver.notFound");
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
        if (WindowState != WindowState.Normal)
        {
            // 最大化/最小化关窗：保留最近一次正常几何（WinForms 用 RestoreBounds 兜底）。
            if (WindowState == WindowState.Maximized)
            {
                _uiStateStore.Update(state =>
                {
                    state.LastPage = _lastPage;
                    state.MusicAdvancedExpanded = _musicPage?.IsAdvancedExpanded ?? false;
                });
            }
            return;
        }
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

    private void OnLanguageChanged(object? sender, EventArgs e)
    {
        // 与主题切换同一重建路径；有未保存修改时同样保留数据只重建
        if (!HasAnyDirty)
        {
            try { RebuildPagesForTheme(); } catch (Exception ex) { System.Diagnostics.Trace.WriteLine($"RebuildForLanguage failed: {ex}"); }
        }
        UpdateSaveBar();
        if (_pages.Count > 0) PageTitle.Text = NavTitle(NavItems[Math.Clamp(_lastPage, 0, NavItems.Length - 1)]);
    }

    private void OnThemeChanged(object? sender, EventArgs e)
    {
        // 主题/强调色通过画刷原地改色生效（见 WpfThemeManager.Apply）：页面、弹窗
        // 构造期直接捕获的画刷实例即时跟随，无需重建页面，脏状态也不会残留旧主题。
        // 语言切换仍需整页重建，见 OnLanguageChanged。
        WpfThemeManager.ApplyTitleBarMode(this);
    }

    private void RebuildPagesForTheme()
    {
        _softwareSettingsPage?.UnsubscribeEvents();
        _pages.Clear();
        _effectPage = null;
        _musicPage = null;
        _automationPage = null;
        _eventFeedbackPage = null;
        _diagnosticsPage = null;
        _softwareSettingsPage = null;
        var selected = Math.Clamp(_lastPage, 0, NavItems.Length - 1);
        BuildPages();
        Navigation.ItemsSource = NavItems.Select(item => new NavItem(NavTitle(item), item.Glyph)).ToList();
        Navigation.SelectedIndex = selected;
        ApplySelectedPage(selected);
        UpdateSaveBar();
        // 重建出的音乐页没有历史音频状态，标签会退回"检测中…"——状态文件稳态下不再变化，
        // 监视器不会再补发事件，必须在这里用窗口缓存喂一次。
        _musicPage?.UpdateAudioSourceLabel(_lastAudioStatus);
    }

    /// <summary>导航项数据。</summary>
    public sealed record NavItem(string Title, string IconGlyph, string? Badge = null);
}
