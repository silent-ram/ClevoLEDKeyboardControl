using System.Diagnostics;
using System.Reflection;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Threading;
using ColorfulLedKeyboard.Core;
using ColorfulLedKeyboard.Tray;
using ServiceProcess = System.ServiceProcess;
using WinForms = System.Windows.Forms;

namespace ColorfulLedKeyboard.Tray.Wpf;

/// <summary>
/// WPF 托盘宿主：WinForms TrayApplicationContext 的移植件。
/// 菜单仍为"每次重建"语义；NotifyIcon 走 WinForms 互操作，菜单/托盘提示为纯 WPF 视觉。
/// </summary>
public sealed class WpfTrayContext : IDisposable
{
    private const string OpenSettingsEventName = "Local\\ClevoLEDKeyboardControl.OpenSettings";

    private readonly SettingsStore _settingsStore = new();
    private readonly UpdateChecker _updateChecker = new();
    private readonly TypingPulseHook _typingPulseHook = new();
    private readonly MediaSessionMonitor _mediaSessionMonitor = new();
    private readonly NotificationFlashMonitor _notificationFlashMonitor;
    private readonly UsageTelemetryClient _usageTelemetryClient = new();
    private readonly WinForms.NotifyIcon _notifyIcon = new();
    private readonly DispatcherTimer _foregroundTimer = new() { Interval = TimeSpan.FromSeconds(1) };
    private readonly DispatcherTimer _trayStatusTimer = new() { Interval = TimeSpan.FromSeconds(2) };
    private readonly DispatcherTimer _updateTimer = new() { Interval = TimeSpan.FromHours(6) };
    private readonly DispatcherTimer _singleInstanceSignalTimer = new() { Interval = TimeSpan.FromMilliseconds(500) };
    private readonly DispatcherTimer _usageTelemetryTimer = new() { Interval = TimeSpan.FromSeconds(60) };
    private readonly EventWaitHandle _openSettingsEvent;

    private KeyboardSettings _settings;
    private UpdateCheckResult? _availableUpdate;
    private AudioSourceStatusWatcher? _audioStatusWatcher;
    private AudioSourceStatusInfo? _lastAudioStatus;
    private MainWindow? _settingsWindow;
    private ContextMenu? _currentMenu;
    private string? _lastForegroundProcess;
    private DateTimeOffset _lastForegroundStateSaved = DateTimeOffset.MinValue;
    private bool _disposed;

    public WpfTrayContext(bool openSettingsOnStartup)
    {
        if (!EventWaitHandle.TryOpenExisting(OpenSettingsEventName, out var existing))
        {
            existing = new EventWaitHandle(false, EventResetMode.AutoReset, OpenSettingsEventName);
        }
        _openSettingsEvent = existing;
        _settings = _settingsStore.Load();
        _notificationFlashMonitor = new NotificationFlashMonitor(_settingsStore);
        EnsureServiceRunning();
        _ = SyncUsageTelemetryAndScheduleAsync();
        _availableUpdate = _updateChecker.LoadKnownAvailable();

        _notifyIcon.Icon = System.Drawing.Icon.ExtractAssociatedIcon(Environment.ProcessPath ?? AppPaths.SettingsPath);
        _notifyIcon.Text = "ClevoLEDKeyboardControl";
        _notifyIcon.Visible = true;
        _notifyIcon.MouseClick += OnNotifyIconClick;
        _notifyIcon.MouseDoubleClick += (_, _) => OpenSettings();
        RefreshMenu();

        _foregroundTimer.Tick += (_, _) => UpdateForegroundAppState();
        _foregroundTimer.Start();
        _trayStatusTimer.Tick += (_, _) => RefreshMenu(refreshEventMonitors: false, reloadSettings: false);
        _trayStatusTimer.Start();
        _updateTimer.Tick += async (_, _) => await CheckForUpdatesAutomaticallyAsync(initialDelay: false);
        _updateTimer.Start();
        _singleInstanceSignalTimer.Tick += (_, _) =>
        {
            if (_openSettingsEvent.WaitOne(0)) OpenSettings();
        };
        _singleInstanceSignalTimer.Start();
        _usageTelemetryTimer.Tick += async (_, _) => await SyncUsageTelemetryAndScheduleAsync();

        _audioStatusWatcher = new AudioSourceStatusWatcher(OnAudioStatusChanged);
        _audioStatusWatcher.RefreshNow();

        UpdateForegroundAppState();
        _ = CheckForUpdatesAutomaticallyAsync(initialDelay: true);
        WpfThemeManager.ThemeChanged += OnThemeChanged;

        if (openSettingsOnStartup)
        {
            OpenSettings();
        }
    }

    private void OnNotifyIconClick(object? sender, WinForms.MouseEventArgs e)
    {
        if (e.Button != WinForms.MouseButtons.Right) return;
        var menu = _currentMenu;
        if (menu is null) return;
        menu.Placement = System.Windows.Controls.Primitives.PlacementMode.MousePoint;
        menu.IsOpen = true;
    }

    // ---- 菜单构建（移植自 TrayApplicationContext.BuildMenu）----

    private ContextMenu BuildMenu()
    {
        _settings = _settingsStore.Load();
        var menu = new ContextMenu();
        AddRuntimeStatusItems(menu);
        menu.Items.Add(new Separator { Style = (Style)Application.Current.Resources["UiMenuSeparator"] });

        var enabled = MakeItem("启用灯效", isChecked: _settings.Enabled, onClick: () =>
        {
            if (TryUpdateSettings(settings => settings.Enabled = !settings.Enabled, rememberCurrent: false)) RefreshMenu();
        });
        menu.Items.Add(enabled);
        menu.Items.Add(BuildAutomationMenu());
        menu.Items.Add(BuildBaseModeMenu());
        menu.Items.Add(BuildPlayerBindingMenu());
        menu.Items.Add(BuildEventFeedbackMenu());
        menu.Items.Add(BuildBrightnessMenu());
        menu.Items.Add(new Separator { Style = (Style)Application.Current.Resources["UiMenuSeparator"] });

        menu.Items.Add(MakeItem("设置...", onClick: OpenSettings));
        menu.Items.Add(BuildServiceMenu());

        var updateAvailable = _availableUpdate?.Status == UpdateCheckStatus.Available;
        var update = MakeItem(updateAvailable
            ? $"发现新版本 v{_availableUpdate!.LatestVersion?.ToString(3)}（点击下载）"
            : "检查更新", onClick: async () =>
        {
            if (_availableUpdate?.Status == UpdateCheckStatus.Available && !string.IsNullOrWhiteSpace(_availableUpdate.ReleaseUrl))
                UpdateChecker.OpenUrl(_availableUpdate.ReleaseUrl);
            else await CheckForUpdatesManuallyAsync();
        });
        if (updateAvailable) update.Foreground = FindBrush("Brush.Error");
        menu.Items.Add(update);

        menu.Items.Add(MakeItem("关于...", onClick: OpenAbout));
        menu.Items.Add(new Separator { Style = (Style)Application.Current.Resources["UiMenuSeparator"] });
        menu.Items.Add(MakeItem("退出", onClick: ExitApplication));
        return menu;
    }

    private static MenuItem MakeItem(
        string header,
        bool isChecked = false,
        bool isEnabled = true,
        System.Action? onClick = null)
    {
        var item = new MenuItem { Header = header, IsChecked = isChecked, IsEnabled = isEnabled };
        if (onClick is not null) item.Click += (_, _) => onClick();
        return item;
    }

    private void AddRuntimeStatusItems(ContextMenu menu)
    {
        var status = AutomationStatus.Load();
        var fresh = status is not null && DateTimeOffset.UtcNow - status.UpdatedUtc <= TimeSpan.FromSeconds(10);
        var current = !_settings.Enabled
            ? "当前：灯光已关闭"
            : fresh && !string.IsNullOrWhiteSpace(status!.ActiveMusicApplication)
                ? $"当前：{status.ActiveMusicApplication} · 音乐模式"
                : _settings.OperatingMode == OperatingMode.Music ? "当前：音乐模式" : $"当前：灯效模式 · {_settings.Effect.Type}";
        menu.Items.Add(MakeItem(current, isEnabled: false));
        if (fresh && !string.IsNullOrWhiteSpace(status!.TrackTitle))
            menu.Items.Add(MakeItem($"歌曲：{status.TrackTitle}{(string.IsNullOrWhiteSpace(status.TrackArtist) ? "" : " - " + status.TrackArtist)}", isEnabled: false));
        if (fresh && !string.IsNullOrWhiteSpace(status!.ActiveRuleName))
            menu.Items.Add(MakeItem($"场景：{status.ActiveRuleName}", isEnabled: false));
        if (fresh && !string.IsNullOrWhiteSpace(status!.AlbumColor))
            menu.Items.Add(MakeItem($"封面：{status.AlbumColor} · {status.AudioCaptureMode}", isEnabled: false));
    }

    private MenuItem BuildAutomationMenu()
    {
        var parent = MakeItem("场景自动化", isChecked: _settings.Automation.Enabled);
        parent.Items.Add(MakeItem("启用场景自动化", isChecked: _settings.Automation.Enabled,
            onClick: () => ApplyEffect(settings => settings.Automation.Enabled = !settings.Automation.Enabled)));
        parent.Items.Add(NewSeparator());
        parent.Items.Add(BuildRuleToggleMenu("音乐程序", _settings.Automation.MusicApplications.Select(rule => (rule.Id, rule.Name, rule.Enabled)),
            (settings, id) =>
            {
                var rule = settings.Automation.MusicApplications.FirstOrDefault(item => item.Id == id);
                if (rule is not null) rule.Enabled = !rule.Enabled;
            }));
        parent.Items.Add(BuildRuleToggleMenu("灯效程序", _settings.Automation.LightingApplications.Select(rule => (rule.Id, rule.Name, rule.Enabled)),
            (settings, id) =>
            {
                var rule = settings.Automation.LightingApplications.FirstOrDefault(item => item.Id == id);
                if (rule is not null) rule.Enabled = !rule.Enabled;
            }));
        parent.Items.Add(BuildRuleToggleMenu("时间计划", _settings.Automation.ScheduleRules.Select(rule => (rule.Id, rule.Name, rule.Enabled)),
            (settings, id) =>
            {
                var rule = settings.Automation.ScheduleRules.FirstOrDefault(item => item.Id == id);
                if (rule is not null) rule.Enabled = !rule.Enabled;
            }));
        parent.Items.Add(NewSeparator());
        parent.Items.Add(MakeItem("打开场景自动化设置...", onClick: OpenSettings));
        return parent;
    }

    private MenuItem BuildRuleToggleMenu(
        string title,
        IEnumerable<(string Id, string Name, bool Enabled)> rules,
        System.Action<KeyboardSettings, string> toggle)
    {
        var parent = MakeItem(title);
        var list = rules.ToList();
        if (list.Count == 0)
        {
            parent.Items.Add(MakeItem("暂无规则", isEnabled: false));
            return parent;
        }
        foreach (var rule in list)
        {
            var id = rule.Id;
            parent.Items.Add(MakeItem(rule.Name, isChecked: rule.Enabled,
                onClick: () => ApplyEffect(settings => toggle(settings, id))));
        }
        return parent;
    }

    private MenuItem BuildBaseModeMenu()
    {
        var parent = MakeItem("基础模式（无场景命中时）");
        parent.Items.Add(BuildEffectMenu());
        parent.Items.Add(BuildMusicModeMenu());
        parent.Items.Add(MakeItem("关闭灯光", isChecked: !_settings.Enabled,
            onClick: () => ApplyEffect(settings => settings.Enabled = false)));
        return parent;
    }

    private MenuItem BuildPlayerBindingMenu()
    {
        var binding = _settings.Effect.Music.PlayerBinding;
        var parent = MakeItem("播放器绑定");
        parent.Items.Add(MakeItem(binding.Enabled ? $"当前绑定：{binding.ProcessName}" : "当前绑定：无", isEnabled: false));
        var bind = MakeItem("绑定当前有声程序");
        var state = AudioApplicationsState.Load();
        var applications = state is not null && DateTimeOffset.UtcNow - state.UpdatedUtc <= TimeSpan.FromSeconds(3)
            ? state.Applications.OrderByDescending(item => item.IsPlaying).ThenByDescending(item => item.PeakLevel).ToList()
            : [];
        if (applications.Count == 0) bind.Items.Add(MakeItem("未检测到有声程序", isEnabled: false));
        foreach (var application in applications)
        {
            var copy = application;
            bind.Items.Add(MakeItem(
                $"{copy.ProcessName} · PID {string.Join(",", copy.ProcessIds)} · {copy.PeakLevel:P0}",
                isChecked: binding.Enabled && string.Equals(binding.ProcessName, copy.ProcessName, StringComparison.OrdinalIgnoreCase),
                onClick: () => ApplyEffect(settings =>
                {
                    settings.Effect.Music.PlayerBinding = new MusicPlayerBinding
                    {
                        Enabled = true,
                        ProcessName = copy.ProcessName,
                        ExecutablePath = copy.ExecutablePath,
                        IncludeChildProcesses = true,
                        MediaSessionId = "",
                        ColorSource = settings.Effect.Music.PlayerBinding.ColorSource
                    };
                })));
        }
        parent.Items.Add(bind);
        var color = MakeItem("颜色来源");
        foreach (var source in Enum.GetValues<MusicColorSource>())
        {
            var sourceCopy = source;
            var label = source switch
            {
                MusicColorSource.AlbumDominant => "封面主色",
                MusicColorSource.AlbumPalette => "封面配色",
                _ => "音乐预设颜色"
            };
            color.Items.Add(MakeItem(label, isChecked: binding.ColorSource == source,
                onClick: () => ApplyEffect(settings => settings.Effect.Music.PlayerBinding.ColorSource = sourceCopy)));
        }
        parent.Items.Add(color);
        parent.Items.Add(BuildMusicResponseMenu());
        parent.Items.Add(MakeItem("取消绑定", isEnabled: binding.Enabled,
            onClick: () => ApplyEffect(settings => settings.Effect.Music.PlayerBinding = new MusicPlayerBinding())));
        parent.Items.Add(NewSeparator());
        parent.Items.Add(MakeItem("打开音乐设置...", onClick: OpenSettings));
        return parent;
    }

    private MenuItem BuildMusicResponseMenu()
    {
        var beatDetectionEnabled = _settings.Effect.Music.EqEnabled;
        var parent = MakeItem($"音乐响应（{(beatDetectionEnabled ? "鼓点响应" : "节奏律动")}）");
        parent.Items.Add(MakeItem("节奏律动", isChecked: !beatDetectionEnabled, onClick: () => SetMusicResponse(beatDetection: false)));
        parent.Items.Add(MakeItem("鼓点响应", isChecked: beatDetectionEnabled, onClick: () => SetMusicResponse(beatDetection: true)));
        return parent;
    }

    private void SetMusicResponse(bool beatDetection)
    {
        ApplyEffect(settings =>
        {
            settings.Enabled = true;
            settings.OperatingMode = OperatingMode.Music;
            settings.Effect.Music.ResponseMode = MusicResponseMode.LevelColor;
            settings.Effect.Music.LevelColorEnabled = true;
            settings.Effect.Music.EqEnabled = beatDetection;
        });
    }

    private MenuItem BuildEventFeedbackMenu()
    {
        var parent = MakeItem("事件反馈");
        parent.Items.Add(MakeItem("敲字闪烁", isChecked: _settings.TypingPulse.Enabled,
            onClick: () => ApplyEffect(settings => settings.TypingPulse.Enabled = !settings.TypingPulse.Enabled)));
        parent.Items.Add(MakeItem("通知闪烁", isChecked: _settings.NotificationFlash.Enabled,
            onClick: () => ApplyEffect(settings => settings.NotificationFlash.Enabled = !settings.NotificationFlash.Enabled)));
        parent.Items.Add(NewSeparator());
        parent.Items.Add(MakeItem("打开事件反馈设置...", onClick: OpenSettings));
        return parent;
    }

    private MenuItem BuildEffectMenu()
    {
        var effect = MakeItem("灯效模式", isChecked: _settings.OperatingMode == OperatingMode.Lighting);
        AddEffectPresetMenu(effect, EffectType.Static, "固定颜色");
        AddEffectPresetMenu(effect, EffectType.Rainbow, "RGB 循环");
        AddEffectPresetMenu(effect, EffectType.Breathing, "单色呼吸");
        AddEffectPresetMenu(effect, EffectType.Sequence, "循环呼吸");
        AddEffectPresetMenu(effect, EffectType.Pulse, "脉冲");
        AddEffectPresetMenu(effect, EffectType.Heartbeat, "心跳");
        return effect;
    }

    private MenuItem BuildMusicModeMenu()
    {
        var music = MakeItem("音乐模式", isChecked: _settings.OperatingMode == OperatingMode.Music);
        foreach (var preset in MusicSettings.BuiltInPresets.Concat(_settings.Effect.Music.CustomPresets))
        {
            var presetCopy = CloneMusicPreset(preset);
            music.Items.Add(MakeItem(presetCopy.Name,
                isChecked: _settings.OperatingMode == OperatingMode.Music &&
                    string.Equals(_settings.Effect.Music.PresetName, presetCopy.Name, StringComparison.OrdinalIgnoreCase),
                onClick: () => ApplyEffect(settings =>
                {
                    settings.Enabled = true;
                    settings.OperatingMode = OperatingMode.Music;
                    settings.Effect.Music.ApplyPreset(presetCopy);
                })));
        }
        return music;
    }

    private void AddEffectPresetMenu(MenuItem parent, EffectType effectType, string label)
    {
        var mode = MakeItem(label, isChecked: _settings.OperatingMode == OperatingMode.Lighting && _settings.Effect.Type == effectType);
        mode.Items.Add(MakeItem("软件默认配置", onClick: () => ApplyEffect(settings =>
        {
            settings.Enabled = true;
            settings.OperatingMode = OperatingMode.Lighting;
            ApplyEffectToSettings(settings, EffectPresetSettings.CreateSoftwareDefault(effectType));
            settings.SavedEffects.LastUsedLightingEffect = effectType;
        })));
        var presets = _settings.EffectPresets.ForType(effectType);
        if (presets.Count > 0) mode.Items.Add(NewSeparator());
        foreach (var preset in presets)
        {
            var presetCopy = KeyboardSettings.CloneEffectPreset(preset);
            mode.Items.Add(MakeItem(presetCopy.Name, onClick: () => ApplyEffect(settings =>
            {
                settings.Enabled = true;
                settings.OperatingMode = OperatingMode.Lighting;
                ApplyEffectToSettings(settings, presetCopy.Effect);
                settings.SavedEffects.LastUsedLightingEffect = presetCopy.Effect.Type;
            })));
        }
        parent.Items.Add(mode);
    }

    private MenuItem BuildBrightnessMenu()
    {
        var musicMode = _settings.OperatingMode == OperatingMode.Music;
        var current = musicMode ? _settings.Effect.Music.PeakBrightness : _settings.Brightness;
        var brightness = MakeItem(musicMode ? $"音乐峰值亮度 ({current}%)" : $"基础亮度 ({current}%)");
        foreach (var value in new[] { 25, 50, 75, 100 })
        {
            var captured = value;
            brightness.Items.Add(MakeItem($"{value}%", isChecked: current == value, onClick: () => ApplyEffect(settings =>
            {
                settings.Enabled = true;
                if (settings.OperatingMode == OperatingMode.Music)
                {
                    settings.Effect.Music.PeakBrightness = captured;
                    settings.Effect.Music.BaseBrightness = Math.Min(settings.Effect.Music.BaseBrightness, captured);
                }
                else settings.Brightness = captured;
            })));
        }
        return brightness;
    }

    private MenuItem BuildServiceMenu()
    {
        var service = MakeItem("服务");
        service.Items.Add(MakeItem("重启服务", onClick: RestartService));

        var (trayRegistered, serviceAuto) = StartupManager.GetState();
        service.Items.Add(MakeItem("开机自动启动", isChecked: trayRegistered && serviceAuto, onClick: ToggleStartupAsync));

        service.Items.Add(MakeItem("打开配置目录", onClick: () =>
        {
            Directory.CreateDirectory(AppPaths.ProgramDataDirectory);
            Process.Start(new ProcessStartInfo("explorer.exe", AppPaths.ProgramDataDirectory) { UseShellExecute = true });
        }));
        return service;
    }

    private async void ToggleStartupAsync()
    {
        var enabled = !IsStartupEnabled();
        var choice = System.Windows.MessageBox.Show(
            $"确定要将开机自启动切换为{(enabled ? "开启" : "关闭")}吗?该设置立即生效,可能需要管理员权限。",
            "ClevoLEDKeyboardControl",
            MessageBoxButton.YesNo,
            MessageBoxImage.Question);
        if (choice != MessageBoxResult.Yes) return;
        var (ok, error) = await System.Threading.Tasks.Task.Run(() =>
        {
            var success = StartupManager.TrySetEnabled(enabled, out var message);
            return (success, message);
        });
        if (!ok)
        {
            System.Windows.MessageBox.Show(
                $"无法修改开机自启动:{error}",
                "ClevoLEDKeyboardControl",
                MessageBoxButton.OK,
                MessageBoxImage.Warning);
        }
        RefreshMenu(refreshEventMonitors: false, reloadSettings: false);
    }

    private static bool IsStartupEnabled()
    {
        var (trayRegistered, serviceAuto) = StartupManager.GetState();
        return trayRegistered && serviceAuto;
    }

    // ---- 设置窗口 / 关于 ----

    internal void OpenSettings()
    {
        if (_settingsWindow is { IsLoaded: true })
        {
            _settingsWindow.ActivateWindow();
            return;
        }
        _settingsWindow = new MainWindow();
        _settingsWindow.SettingsSaved += () =>
        {
            _mediaSessionMonitor.ForceRefresh();
            RefreshMenu();
            _ = SyncUsageTelemetryAndScheduleAsync();
        };
        _settingsWindow.Closed += (_, _) => _settingsWindow = null;
        _settingsWindow.Show();
        _settingsWindow.ActivateWindow();
    }

    private void OpenAbout()
    {
        System.Windows.MessageBox.Show(
            $"ClevoLEDKeyboardControl v{ReadVersion()}\n\n面向 Clevo 兼容机型的键盘背光灯效控制程序。\nMaintained by silent-ram",
            "关于 ClevoLEDKeyboardControl",
            MessageBoxButton.OK,
            MessageBoxImage.Information);
    }

    private static string ReadVersion()
    {
        var informational = typeof(WpfTrayContext).Assembly.GetName().Version?.ToString(3) ?? "0.0.0";
        var informationalAttribute = System.Reflection.Assembly.GetExecutingAssembly()
            .GetCustomAttribute<System.Reflection.AssemblyInformationalVersionAttribute>()?.InformationalVersion;
        if (!string.IsNullOrWhiteSpace(informationalAttribute))
        {
            var plus = informationalAttribute.IndexOf('+');
            return plus > 0 ? informationalAttribute[..plus] : informationalAttribute;
        }
        return informational;
    }

    // ---- 设置变更 / 事件监视 ----

    private void ApplyEffect(System.Action<KeyboardSettings> update)
    {
        if (TryUpdateSettings(update, rememberCurrent: true)) RefreshMenu();
    }

    private bool TryUpdateSettings(System.Action<KeyboardSettings> update, bool rememberCurrent)
    {
        try
        {
            _settings = _settingsStore.Load();
            if (rememberCurrent) RememberCurrentEffect(_settings);
            update(_settings);
            _settingsStore.Save(_settings);
            _mediaSessionMonitor.ForceRefresh();
            RefreshEventMonitors();
            _settingsWindow?.ReloadFromStore();
            return true;
        }
        catch (UnauthorizedAccessException)
        {
            ShowSettingsAccessError();
            return false;
        }
        catch (IOException ex)
        {
            System.Windows.MessageBox.Show(
                $"无法保存配置：{ex.Message}",
                "ClevoLEDKeyboardControl",
                MessageBoxButton.OK,
                MessageBoxImage.Warning);
            return false;
        }
    }

    private void RefreshMenu(bool refreshEventMonitors = true, bool reloadSettings = true)
    {
        if (reloadSettings) _settings = _settingsStore.Load();
        if (refreshEventMonitors) RefreshEventMonitors();
        var oldMenu = _currentMenu;
        _currentMenu = BuildMenu();
        oldMenu?.Items.Clear();
        UpdateNotifyIconText();
    }

    private void OnAudioStatusChanged(AudioSourceStatusInfo? info)
    {
        _lastAudioStatus = info;
        UpdateNotifyIconText();
        _settingsWindow?.UpdateAudioSourceLabel(info);
    }

    private void RefreshEventMonitors()
    {
        var typingRequired = _settings.TypingPulse.Enabled ||
            (_settings.Automation.Enabled && (
            _settings.Automation.MusicApplications.Any(rule => rule.Enabled && rule.TypingPolicy == EventPolicy.Enabled) ||
            _settings.Automation.LightingApplications.Any(rule => rule.Enabled && rule.TypingPolicy == EventPolicy.Enabled)));
        var notificationRequired = _settings.NotificationFlash.Enabled ||
            (_settings.Automation.Enabled && (
            _settings.Automation.MusicApplications.Any(rule => rule.Enabled && rule.NotificationPolicy == EventPolicy.Enabled) ||
            _settings.Automation.LightingApplications.Any(rule => rule.Enabled && rule.NotificationPolicy == EventPolicy.Enabled)));
        _typingPulseHook.SetEnabled(typingRequired);
        _notificationFlashMonitor.SetEnabled(notificationRequired);
    }

    private void UpdateNotifyIconText()
    {
        var settings = _settings;
        var status = AutomationStatus.Load();
        var fresh = status is not null && DateTimeOffset.UtcNow - status.UpdatedUtc <= TimeSpan.FromSeconds(10);
        string text;

        if (settings.OperatingMode != OperatingMode.Music || !settings.Enabled)
        {
            text = !settings.Enabled
                ? "ClevoLEDKeyboardControl\n灯光已关闭"
                : fresh && !string.IsNullOrWhiteSpace(status!.ActiveRuleName)
                    ? $"ClevoLEDKeyboardControl\n场景：{status.ActiveRuleName}"
                    : "ClevoLEDKeyboardControl\n灯效模式";
        }
        else
        {
            var player = fresh && !string.IsNullOrWhiteSpace(status!.ActiveMusicApplication)
                ? status.ActiveMusicApplication
                : _lastAudioStatus?.DeviceFriendlyName ?? "检测中…";
            var track = fresh && !string.IsNullOrWhiteSpace(status!.TrackTitle) ? $"\n{status.TrackTitle}" : "";
            text = $"ClevoLEDKeyboardControl\n音乐：{player}{track}";
        }

        if (_availableUpdate?.Status == UpdateCheckStatus.Available)
            text += $"\n可更新：v{_availableUpdate.LatestVersion?.ToString(3)}";

        if (text.Length > 120) text = text[..120] + "…";
        _notifyIcon.Text = text;
    }

    private void UpdateForegroundAppState()
    {
        var processName = ForegroundWindowProcessName.GetName();
        if (string.IsNullOrWhiteSpace(processName) ||
            (string.Equals(processName, _lastForegroundProcess, StringComparison.OrdinalIgnoreCase) &&
             DateTimeOffset.UtcNow - _lastForegroundStateSaved < TimeSpan.FromSeconds(3)))
        {
            return;
        }

        _lastForegroundProcess = processName;
        ForegroundAppState.Save(processName);
        _lastForegroundStateSaved = DateTimeOffset.UtcNow;
    }

    // ---- 更新检查 ----

    private async System.Threading.Tasks.Task CheckForUpdatesAutomaticallyAsync(bool initialDelay)
    {
        try
        {
            if (initialDelay) await System.Threading.Tasks.Task.Delay(TimeSpan.FromSeconds(5));
            var interval = _settingsStore.Load().Update.CheckInterval;
            var result = await _updateChecker.CheckAsync(force: false, interval);
            if (result.Status == UpdateCheckStatus.Available)
            {
                _availableUpdate = result;
                RefreshMenu(refreshEventMonitors: false);
            }
            else if (result.Status == UpdateCheckStatus.UpToDate)
            {
                _availableUpdate = null;
                RefreshMenu(refreshEventMonitors: false);
            }
        }
        catch
        {
        }
    }

    public async System.Threading.Tasks.Task<UpdateCheckResult?> CheckForUpdatesWhenSettingsOpenAsync()
    {
        try
        {
            var result = await _updateChecker.CheckAsync(force: true, UpdateCheckInterval.Daily);
            _availableUpdate = result.Status == UpdateCheckStatus.Available ? result : null;
            RefreshMenu(refreshEventMonitors: false);
            return _availableUpdate;
        }
        catch
        {
            return null;
        }
    }

    private async System.Threading.Tasks.Task CheckForUpdatesManuallyAsync()
    {
        try
        {
            var result = await _updateChecker.CheckAsync(force: true, UpdateCheckInterval.Daily);
            if (result.Status == UpdateCheckStatus.Available)
            {
                _availableUpdate = result;
                RefreshMenu(refreshEventMonitors: false);
                if (result.LatestVersion is not null) UpdateChecker.MarkPrompted(result.LatestVersion);
                ShowUpdateAvailable(result);
                return;
            }

            System.Windows.MessageBox.Show(
                $"当前已是最新版本。\n\n当前版本：{result.CurrentVersion.ToString(3)}",
                "ClevoLEDKeyboardControl",
                MessageBoxButton.OK,
                MessageBoxImage.Information);
        }
        catch (Exception ex) when (ex is UpdateCheckException or System.Net.Http.HttpRequestException or System.Threading.Tasks.TaskCanceledException or InvalidOperationException)
        {
            var detail = ex switch
            {
                UpdateCheckException update => update.Message,
                System.Threading.Tasks.TaskCanceledException => "连接 GitHub 超时，请稍后重试。",
                System.Net.Http.HttpRequestException { StatusCode: System.Net.HttpStatusCode.Forbidden } =>
                    "GitHub 暂时限制了更新检查请求，请稍后重试。",
                System.Net.Http.HttpRequestException { StatusCode: not null } http =>
                    $"GitHub 返回状态码 {(int)http.StatusCode.Value}，请稍后重试。",
                InvalidOperationException => "无法识别 GitHub 最新版本信息，请稍后重试。",
                _ => "暂时无法连接更新服务器，请稍后重试。"
            };
            System.Windows.MessageBox.Show(
                $"检查更新失败：{detail}",
                "ClevoLEDKeyboardControl",
                MessageBoxButton.OK,
                MessageBoxImage.Warning);
        }
    }

    private void ShowUpdateAvailable(UpdateCheckResult result)
    {
        var latest = result.LatestVersion?.ToString(3) ?? "未知";
        var choice = System.Windows.MessageBox.Show(
            $"发现新版本：{latest}\n当前版本：{result.CurrentVersion.ToString(3)}\n\n是否打开下载页面？",
            "ClevoLEDKeyboardControl",
            MessageBoxButton.YesNo,
            MessageBoxImage.Information);
        if (choice == MessageBoxResult.Yes) UpdateChecker.OpenReleases();
    }

    // ---- 遥测 ----

    private async System.Threading.Tasks.Task SyncUsageTelemetryAndScheduleAsync()
    {
        _usageTelemetryTimer.Stop();
        await _usageTelemetryClient.SyncAsync(_settings);
        if (_settings.UserImprovementPlan?.Enabled == true)
        {
            var delay = _usageTelemetryClient.GetNextCheckDelay();
            _usageTelemetryTimer.Interval = TimeSpan.FromMilliseconds(
                Math.Clamp((long)delay.TotalMilliseconds, 1000L, int.MaxValue));
            _usageTelemetryTimer.Start();
        }
    }

    // ---- 效果记忆 / 服务控制（移植自 WinForms 版）----

    private static void RememberCurrentEffect(KeyboardSettings settings)
    {
        settings.SavedEffects ??= new EffectMemorySettings();
        var copy = KeyboardSettings.CloneEffect(settings.Effect);
        copy.Normalize();

        switch (copy.Type)
        {
            case EffectType.Static:
                settings.SavedEffects.Static = copy;
                break;
            case EffectType.Rainbow:
                copy.CustomSequenceColorsEnabled = true;
                settings.SavedEffects.Rainbow = copy;
                break;
            case EffectType.Breathing:
                settings.SavedEffects.Breathing = copy;
                break;
            case EffectType.Sequence:
                settings.SavedEffects.Sequence = copy;
                break;
            case EffectType.Pulse:
                settings.SavedEffects.Pulse = copy;
                break;
            case EffectType.Heartbeat:
                settings.SavedEffects.Heartbeat = copy;
                break;
        }

        settings.SavedEffects.Normalize();
    }

    private static void ApplyEffectToSettings(KeyboardSettings settings, LightingEffectSettings effect)
    {
        settings.Effect = KeyboardSettings.CloneEffect(effect);
        settings.Effect.Normalize();
        if (settings.Effect.Type == EffectType.Rainbow)
        {
            settings.Effect.CustomSequenceColorsEnabled = true;
        }

        settings.Mode = settings.Effect.Type switch
        {
            EffectType.Static => KeyboardMode.Static,
            EffectType.Rainbow => KeyboardMode.Rainbow,
            EffectType.Breathing => KeyboardMode.Breathing,
            EffectType.Sequence => KeyboardMode.Sequence,
            EffectType.Pulse => KeyboardMode.Pulse,
            EffectType.Heartbeat => KeyboardMode.Heartbeat,
            EffectType.Off => KeyboardMode.Off,
            _ => settings.Mode
        };
    }

    private static MusicPreset CloneMusicPreset(MusicPreset preset)
    {
        return new MusicPreset
        {
            Name = preset.Name,
            ResponseMode = preset.ResponseMode,
            LowColor = preset.LowColor,
            HighColor = preset.HighColor,
            Colors = [.. preset.Colors],
            Sensitivity = preset.Sensitivity,
            AttackMs = preset.AttackMs,
            ReleaseMs = preset.ReleaseMs,
            BaseBrightness = preset.BaseBrightness,
            PeakBrightness = preset.PeakBrightness,
            IntervalMs = preset.IntervalMs,
            NoiseGate = preset.NoiseGate,
            BeatThreshold = preset.BeatThreshold,
            PeakHoldMs = preset.PeakHoldMs,
            FollowSystemVolume = preset.FollowSystemVolume,
            EqEnabled = preset.EqEnabled,
            EqLowHz = preset.EqLowHz,
            EqHighHz = preset.EqHighHz
        }.Normalize();
    }

    private static void RestartService()
    {
        try
        {
            using var controller = new ServiceProcess.ServiceController(AppPaths.ServiceName);
            if (controller.Status != ServiceProcess.ServiceControllerStatus.Stopped &&
                controller.Status != ServiceProcess.ServiceControllerStatus.StopPending)
            {
                controller.Stop();
                controller.WaitForStatus(ServiceProcess.ServiceControllerStatus.Stopped, TimeSpan.FromSeconds(20));
            }

            controller.Start();
            controller.WaitForStatus(ServiceProcess.ServiceControllerStatus.Running, TimeSpan.FromSeconds(20));
        }
        catch (UnauthorizedAccessException)
        {
            RunElevatedServiceCommand("Restart-Service -Name ClevoLEDKeyboardControlService -Force");
        }
        catch (System.ComponentModel.Win32Exception ex) when (ex.NativeErrorCode == 5)
        {
            RunElevatedServiceCommand("Restart-Service -Name ClevoLEDKeyboardControlService -Force");
        }
        catch (InvalidOperationException ex) when (ex.InnerException is System.ComponentModel.Win32Exception { NativeErrorCode: 5 })
        {
            RunElevatedServiceCommand("Restart-Service -Name ClevoLEDKeyboardControlService -Force");
        }
        catch (Exception ex)
        {
            System.Windows.MessageBox.Show(
                $"无法重启服务：{ex.Message}",
                "ClevoLEDKeyboardControl",
                MessageBoxButton.OK,
                MessageBoxImage.Warning);
        }
    }

    private void ExitApplication()
    {
        StopServiceForExit();
        Application.Current.Shutdown();
    }

    private static void StopServiceForExit()
    {
        try
        {
            using var controller = new ServiceProcess.ServiceController(AppPaths.ServiceName);
            if (controller.Status == ServiceProcess.ServiceControllerStatus.Stopped ||
                controller.Status == ServiceProcess.ServiceControllerStatus.StopPending)
            {
                return;
            }

            controller.Stop();
            controller.WaitForStatus(ServiceProcess.ServiceControllerStatus.Stopped, TimeSpan.FromSeconds(20));
        }
        catch (UnauthorizedAccessException)
        {
            RunElevatedServiceCommand($"Stop-Service -Name {AppPaths.ServiceName} -Force");
        }
        catch (System.ComponentModel.Win32Exception ex) when (ex.NativeErrorCode == 5)
        {
            RunElevatedServiceCommand($"Stop-Service -Name {AppPaths.ServiceName} -Force");
        }
        catch (InvalidOperationException ex) when (ex.InnerException is System.ComponentModel.Win32Exception { NativeErrorCode: 5 })
        {
            RunElevatedServiceCommand($"Stop-Service -Name {AppPaths.ServiceName} -Force");
        }
        catch
        {
            // 服务可能未安装或已停止，忽略以保证退出顺畅
        }
    }

    private static void RunElevatedServiceCommand(string command)
    {
        try
        {
            using var process = Process.Start(new ProcessStartInfo("powershell.exe", $"-NoProfile -ExecutionPolicy Bypass -Command \"{command}\"")
            {
                UseShellExecute = true,
                Verb = "runas",
                WindowStyle = ProcessWindowStyle.Hidden
            });
            process?.WaitForExit(15000);
        }
        catch
        {
            // 用户取消 UAC 或失败时忽略
        }
    }

    private static void EnsureServiceRunning()
    {
        try
        {
            using var controller = new ServiceProcess.ServiceController(AppPaths.ServiceName);
            if (controller.Status == ServiceProcess.ServiceControllerStatus.Running ||
                controller.Status == ServiceProcess.ServiceControllerStatus.StartPending)
            {
                return;
            }

            controller.Start();
            controller.WaitForStatus(ServiceProcess.ServiceControllerStatus.Running, TimeSpan.FromSeconds(20));
        }
        catch (UnauthorizedAccessException)
        {
            RunElevatedServiceCommand($"Start-Service -Name {AppPaths.ServiceName}");
        }
        catch (System.ComponentModel.Win32Exception ex) when (ex.NativeErrorCode == 5)
        {
            RunElevatedServiceCommand($"Start-Service -Name {AppPaths.ServiceName}");
        }
        catch (InvalidOperationException ex) when (ex.InnerException is System.ComponentModel.Win32Exception { NativeErrorCode: 5 })
        {
            RunElevatedServiceCommand($"Start-Service -Name {AppPaths.ServiceName}");
        }
        catch
        {
            // 服务未安装或其他错误时不打扰用户
        }
    }

    private static void ShowSettingsAccessError()
    {
        System.Windows.MessageBox.Show(
            $"无法保存配置，请重新安装新版程序以修复权限，或确认当前用户可写入：{AppPaths.SettingsPath}",
            "ClevoLEDKeyboardControl",
            MessageBoxButton.OK,
            MessageBoxImage.Warning);
    }

    private void OnThemeChanged(object? sender, EventArgs e) => RefreshMenu(refreshEventMonitors: false, reloadSettings: false);

    private static Separator NewSeparator() => new() { Style = (Style)Application.Current.Resources["UiMenuSeparator"] };

    private static System.Windows.Media.Brush? FindBrush(string key) =>
        Application.Current.TryFindResource(key) as System.Windows.Media.Brush;

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        WpfThemeManager.ThemeChanged -= OnThemeChanged;
        _foregroundTimer.Stop();
        _trayStatusTimer.Stop();
        _updateTimer.Stop();
        _singleInstanceSignalTimer.Stop();
        _usageTelemetryTimer.Stop();
        _typingPulseHook.Dispose();
        _notificationFlashMonitor.Dispose();
        _mediaSessionMonitor.Dispose();
        _audioStatusWatcher?.Dispose();
        _usageTelemetryClient.Dispose();
        _notifyIcon.Visible = false;
        _notifyIcon.Dispose();
        _openSettingsEvent.Dispose();
    }
}
