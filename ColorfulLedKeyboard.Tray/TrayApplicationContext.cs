using ColorfulLedKeyboard.Core;
using System.ComponentModel;
using System.Diagnostics;
using System.ServiceProcess;

namespace ColorfulLedKeyboard.Tray;

public sealed class TrayApplicationContext : ApplicationContext
{
    private const int DefaultBreathingPeriodMs = EffectPresetSettings.DefaultPeriodMs;
    private readonly SettingsStore _settingsStore;
    private readonly UpdateChecker _updateChecker = new();
    private readonly TypingPulseHook _typingPulseHook = new();
    private readonly NotificationFlashMonitor _notificationFlashMonitor;
    private readonly MediaSessionMonitor _mediaSessionMonitor = new();
    private readonly AudioSessionMonitor _audioSessionMonitor = new();
    private readonly NotifyIcon _notifyIcon;
    private readonly System.Windows.Forms.Timer _foregroundTimer = new() { Interval = 1000 };
    private readonly System.Windows.Forms.Timer _trayStatusTimer = new() { Interval = 2000 };
    private readonly System.Windows.Forms.Timer _updateTimer = new() { Interval = 6 * 60 * 60 * 1000 };
    private string? _balloonReleaseUrl;
    private string? _lastForegroundProcess;
    private DateTimeOffset _lastForegroundStateSaved = DateTimeOffset.MinValue;
    private KeyboardSettings _settings;
    private SettingsForm? _settingsForm;
    private AudioSourceStatusWatcher? _audioStatusWatcher;
    private AudioSourceStatusInfo? _lastAudioStatus;
    private UpdateCheckResult? _availableUpdate;

    public TrayApplicationContext(SettingsStore settingsStore, bool openSettingsOnStartup = false)
    {
        _settingsStore = settingsStore;
        EnsureServiceRunning();
        _settings = _settingsStore.Load();
        _availableUpdate = _updateChecker.LoadKnownAvailable();
        _notificationFlashMonitor = new NotificationFlashMonitor(_settingsStore);

        _notifyIcon = new NotifyIcon
        {
            Icon = Icon.ExtractAssociatedIcon(Application.ExecutablePath) ?? SystemIcons.Application,
            Text = "ClevoLEDKeyboardControl",
            Visible = true,
            ContextMenuStrip = BuildMenu()
        };

        _notifyIcon.DoubleClick += (_, _) => OpenSettings();
        _notifyIcon.BalloonTipClicked += (_, _) => OpenBalloonRelease();
        ThemeManager.ThemeChanged += OnThemeChanged;
        _foregroundTimer.Tick += (_, _) => UpdateForegroundAppState();
        _foregroundTimer.Start();
        _trayStatusTimer.Tick += (_, _) =>
        {
            if (!(_notifyIcon.ContextMenuStrip?.Visible ?? false)) RefreshMenu(refreshEventMonitors: false, reloadSettings: false);
            else UpdateNotifyIconText();
        };
        _trayStatusTimer.Start();
        _updateTimer.Tick += async (_, _) => await CheckForUpdatesAutomaticallyAsync(initialDelay: false);
        _updateTimer.Start();
        RefreshEventMonitors();
        UpdateForegroundAppState();
        _ = CheckForUpdatesAutomaticallyAsync(initialDelay: true);

        _audioStatusWatcher = new AudioSourceStatusWatcher(OnAudioStatusChanged);
        _audioStatusWatcher.RefreshNow();

        if (openSettingsOnStartup)
        {
            OpenSettingsAfterStartup();
        }
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            _notifyIcon.Visible = false;
            _notifyIcon.Dispose();
            _foregroundTimer.Stop();
            _foregroundTimer.Dispose();
            _trayStatusTimer.Stop();
            _trayStatusTimer.Dispose();
            _updateTimer.Stop();
            _updateTimer.Dispose();
            _typingPulseHook.Dispose();
            _notificationFlashMonitor.Dispose();
            _mediaSessionMonitor.Dispose();
            _audioSessionMonitor.Dispose();
            _audioStatusWatcher?.Dispose();
            ThemeManager.ThemeChanged -= OnThemeChanged;
        }

        base.Dispose(disposing);
    }

    private ContextMenuStrip BuildMenu()
    {
        _settings = _settingsStore.Load();
        var menu = new ContextMenuStrip();
        AddRuntimeStatusItems(menu);
        menu.Items.Add(new ToolStripSeparator());

        var enabled = new ToolStripMenuItem("启用灯效") { Checked = _settings.Enabled };
        enabled.Click += (_, _) =>
        {
            if (TryUpdateSettings(settings => settings.Enabled = !settings.Enabled, rememberCurrent: false))
            {
                RefreshMenu();
            }
        };

        menu.Items.Add(enabled);
        menu.Items.Add(BuildAutomationMenu());
        menu.Items.Add(BuildBaseModeMenu());
        menu.Items.Add(BuildPlayerBindingMenu());
        menu.Items.Add(BuildEventFeedbackMenu());
        menu.Items.Add(BuildBrightnessMenu());
        menu.Items.Add(new ToolStripSeparator());

        var settingsItem = new ToolStripMenuItem("设置...");
        settingsItem.Click += (_, _) => OpenSettings();
        menu.Items.Add(settingsItem);

        menu.Items.Add(BuildServiceMenu());

        var update = new ToolStripMenuItem(_availableUpdate?.Status == UpdateCheckStatus.Available
            ? $"发现新版本 v{_availableUpdate.LatestVersion?.ToString(3)}（点击下载）"
            : "检查更新");
        update.ForeColor = _availableUpdate?.Status == UpdateCheckStatus.Available ? ThemeManager.Current.Error : ThemeManager.Current.Text;
        update.Click += async (_, _) =>
        {
            if (_availableUpdate?.Status == UpdateCheckStatus.Available && !string.IsNullOrWhiteSpace(_availableUpdate.ReleaseUrl))
                UpdateChecker.OpenUrl(_availableUpdate.ReleaseUrl);
            else await CheckForUpdatesManuallyAsync();
        };
        menu.Items.Add(update);

        var about = new ToolStripMenuItem("关于...");
        about.Click += (_, _) => OpenAbout();
        menu.Items.Add(about);

        menu.Items.Add(new ToolStripSeparator());

        var exit = new ToolStripMenuItem("退出");
        exit.Click += (_, _) => ExitApplication();
        menu.Items.Add(exit);
        ThemeManager.Apply(menu);
        return menu;
    }

    private void OnThemeChanged(object? sender, EventArgs e) => RefreshMenu(refreshEventMonitors: false, reloadSettings: false);

    private void AddRuntimeStatusItems(ContextMenuStrip menu)
    {
        var status = AutomationStatus.Load();
        var fresh = status is not null && DateTimeOffset.UtcNow - status.UpdatedUtc <= TimeSpan.FromSeconds(10);
        var current = !_settings.Enabled
            ? "当前：灯光已关闭"
            : fresh && !string.IsNullOrWhiteSpace(status!.ActiveMusicApplication)
                ? $"当前：{status.ActiveMusicApplication} · 音乐模式"
                : _settings.OperatingMode == OperatingMode.Music ? "当前：音乐模式" : $"当前：灯效模式 · {_settings.Effect.Type}";
        menu.Items.Add(new ToolStripMenuItem(current) { Enabled = false });
        if (fresh && !string.IsNullOrWhiteSpace(status!.TrackTitle))
            menu.Items.Add(new ToolStripMenuItem($"歌曲：{status.TrackTitle}{(string.IsNullOrWhiteSpace(status.TrackArtist) ? "" : " - " + status.TrackArtist)}") { Enabled = false });
        if (fresh && !string.IsNullOrWhiteSpace(status!.ActiveRuleName))
            menu.Items.Add(new ToolStripMenuItem($"场景：{status.ActiveRuleName}") { Enabled = false });
        if (fresh && !string.IsNullOrWhiteSpace(status!.AlbumColor))
            menu.Items.Add(new ToolStripMenuItem($"封面：{status.AlbumColor} · {status.AudioCaptureMode}") { Enabled = false });
    }

    private ToolStripMenuItem BuildAutomationMenu()
    {
        var parent = new ToolStripMenuItem("场景自动化") { Checked = _settings.Automation.Enabled };
        var enabled = new ToolStripMenuItem("启用场景自动化") { Checked = _settings.Automation.Enabled };
        enabled.Click += (_, _) => ApplyEffect(settings => settings.Automation.Enabled = !settings.Automation.Enabled);
        parent.DropDownItems.Add(enabled);
        parent.DropDownItems.Add(new ToolStripSeparator());
        parent.DropDownItems.Add(BuildRuleToggleMenu("音乐程序", _settings.Automation.MusicApplications.Select(rule => (rule.Id, rule.Name, rule.Enabled)),
            (settings, id) =>
            {
                var rule = settings.Automation.MusicApplications.FirstOrDefault(item => item.Id == id);
                if (rule is not null) rule.Enabled = !rule.Enabled;
            }));
        parent.DropDownItems.Add(BuildRuleToggleMenu("灯效程序", _settings.Automation.LightingApplications.Select(rule => (rule.Id, rule.Name, rule.Enabled)),
            (settings, id) =>
            {
                var rule = settings.Automation.LightingApplications.FirstOrDefault(item => item.Id == id);
                if (rule is not null) rule.Enabled = !rule.Enabled;
            }));
        parent.DropDownItems.Add(BuildRuleToggleMenu("时间计划", _settings.Automation.ScheduleRules.Select(rule => (rule.Id, rule.Name, rule.Enabled)),
            (settings, id) =>
            {
                var rule = settings.Automation.ScheduleRules.FirstOrDefault(item => item.Id == id);
                if (rule is not null) rule.Enabled = !rule.Enabled;
            }));
        parent.DropDownItems.Add(new ToolStripSeparator());
        var open = new ToolStripMenuItem("打开场景自动化设置...");
        open.Click += (_, _) => OpenSettings();
        parent.DropDownItems.Add(open);
        return parent;
    }

    private ToolStripMenuItem BuildRuleToggleMenu(
        string title,
        IEnumerable<(string Id, string Name, bool Enabled)> rules,
        Action<KeyboardSettings, string> toggle)
    {
        var parent = new ToolStripMenuItem(title);
        var list = rules.ToList();
        if (list.Count == 0)
        {
            parent.DropDownItems.Add(new ToolStripMenuItem("暂无规则") { Enabled = false });
            return parent;
        }
        foreach (var rule in list)
        {
            var id = rule.Id;
            var item = new ToolStripMenuItem(rule.Name) { Checked = rule.Enabled };
            item.Click += (_, _) => ApplyEffect(settings => toggle(settings, id));
            parent.DropDownItems.Add(item);
        }
        return parent;
    }

    private ToolStripMenuItem BuildBaseModeMenu()
    {
        var parent = new ToolStripMenuItem("基础模式（无场景命中时）");
        parent.DropDownItems.Add(BuildEffectMenu());
        parent.DropDownItems.Add(BuildMusicModeMenu());
        var off = new ToolStripMenuItem("关闭灯光") { Checked = !_settings.Enabled };
        off.Click += (_, _) => ApplyEffect(settings => settings.Enabled = false);
        parent.DropDownItems.Add(off);
        return parent;
    }

    private ToolStripMenuItem BuildPlayerBindingMenu()
    {
        var binding = _settings.Effect.Music.PlayerBinding;
        var parent = new ToolStripMenuItem("播放器绑定");
        parent.DropDownItems.Add(new ToolStripMenuItem(binding.Enabled ? $"当前绑定：{binding.ProcessName}" : "当前绑定：无") { Enabled = false });
        var bind = new ToolStripMenuItem("绑定当前有声程序");
        var state = AudioApplicationsState.Load();
        var applications = state is not null && DateTimeOffset.UtcNow - state.UpdatedUtc <= TimeSpan.FromSeconds(3)
            ? state.Applications.OrderByDescending(item => item.IsPlaying).ThenByDescending(item => item.PeakLevel).ToList()
            : [];
        if (applications.Count == 0) bind.DropDownItems.Add(new ToolStripMenuItem("未检测到有声程序") { Enabled = false });
        foreach (var application in applications)
        {
            var copy = application;
            var item = new ToolStripMenuItem($"{copy.ProcessName} · PID {string.Join(",", copy.ProcessIds)} · {copy.PeakLevel:P0}")
            {
                Checked = binding.Enabled && string.Equals(binding.ProcessName, copy.ProcessName, StringComparison.OrdinalIgnoreCase)
            };
            item.Click += (_, _) => ApplyEffect(settings =>
            {
                settings.Effect.Music.PlayerBinding = new MusicPlayerBinding
                {
                    Enabled = true,
                    ProcessName = copy.ProcessName,
                    ExecutablePath = copy.ExecutablePath,
                    IncludeChildProcesses = true,
                    MediaSessionId = MediaPlaybackState.Load()?.Sessions.FirstOrDefault(session =>
                        session.SourceId.Contains(copy.ProcessName, StringComparison.OrdinalIgnoreCase))?.SourceId ?? "",
                    ColorSource = settings.Effect.Music.PlayerBinding.ColorSource
                };
            });
            bind.DropDownItems.Add(item);
        }
        parent.DropDownItems.Add(bind);
        var color = new ToolStripMenuItem("颜色来源");
        foreach (var source in Enum.GetValues<MusicColorSource>())
        {
            var sourceCopy = source;
            var label = source switch
            {
                MusicColorSource.AlbumDominant => "封面主色",
                MusicColorSource.AlbumPalette => "封面配色",
                _ => "音乐预设颜色"
            };
            var item = new ToolStripMenuItem(label) { Checked = binding.ColorSource == source };
            item.Click += (_, _) => ApplyEffect(settings => settings.Effect.Music.PlayerBinding.ColorSource = sourceCopy);
            color.DropDownItems.Add(item);
        }
        parent.DropDownItems.Add(color);
        var clear = new ToolStripMenuItem("取消绑定") { Enabled = binding.Enabled };
        clear.Click += (_, _) => ApplyEffect(settings => settings.Effect.Music.PlayerBinding = new MusicPlayerBinding());
        parent.DropDownItems.Add(clear);
        parent.DropDownItems.Add(new ToolStripSeparator());
        var open = new ToolStripMenuItem("打开音乐设置...");
        open.Click += (_, _) => OpenSettings();
        parent.DropDownItems.Add(open);
        return parent;
    }

    private ToolStripMenuItem BuildEventFeedbackMenu()
    {
        var parent = new ToolStripMenuItem("事件反馈");
        var typing = new ToolStripMenuItem("敲字闪烁") { Checked = _settings.TypingPulse.Enabled };
        typing.Click += (_, _) => ApplyEffect(settings => settings.TypingPulse.Enabled = !settings.TypingPulse.Enabled);
        var notification = new ToolStripMenuItem("通知闪烁") { Checked = _settings.NotificationFlash.Enabled };
        notification.Click += (_, _) => ApplyEffect(settings => settings.NotificationFlash.Enabled = !settings.NotificationFlash.Enabled);
        parent.DropDownItems.Add(typing);
        parent.DropDownItems.Add(notification);
        parent.DropDownItems.Add(new ToolStripSeparator());
        var open = new ToolStripMenuItem("打开事件反馈设置...");
        open.Click += (_, _) => OpenSettings();
        parent.DropDownItems.Add(open);
        return parent;
    }

    private ToolStripMenuItem BuildEffectMenu()
    {
        var effect = new ToolStripMenuItem("灯效模式")
        {
            Checked = _settings.OperatingMode == OperatingMode.Lighting
        };

        AddEffectPresetMenu(effect, EffectType.Static, "固定颜色");
        AddEffectPresetMenu(effect, EffectType.Rainbow, "RGB 循环");
        AddEffectPresetMenu(effect, EffectType.Breathing, "单色呼吸");
        AddEffectPresetMenu(effect, EffectType.Sequence, "循环呼吸");
        AddEffectPresetMenu(effect, EffectType.Pulse, "脉冲");
        AddEffectPresetMenu(effect, EffectType.Heartbeat, "心跳");

        return effect;
    }

    private ToolStripMenuItem BuildMusicModeMenu()
    {
        var music = new ToolStripMenuItem("音乐模式")
        {
            Checked = _settings.OperatingMode == OperatingMode.Music
        };

        foreach (var preset in MusicSettings.BuiltInPresets.Concat(_settings.Effect.Music.CustomPresets))
        {
            var presetCopy = CloneMusicPreset(preset);
            var item = new ToolStripMenuItem(presetCopy.Name)
            {
                Checked = _settings.OperatingMode == OperatingMode.Music &&
                    string.Equals(_settings.Effect.Music.PresetName, presetCopy.Name, StringComparison.OrdinalIgnoreCase)
            };
            item.Click += (_, _) => ApplyEffect(settings =>
            {
                settings.Enabled = true;
                settings.OperatingMode = OperatingMode.Music;
                settings.Effect.Music.ApplyPreset(presetCopy);
            });
            music.DropDownItems.Add(item);
        }

        return music;
    }

    private void AddEffectPresetMenu(ToolStripMenuItem parent, EffectType effectType, string label)
    {
        var mode = new ToolStripMenuItem(label)
        {
            Checked = _settings.OperatingMode == OperatingMode.Lighting && _settings.Effect.Type == effectType
        };

        var softwareDefault = new ToolStripMenuItem("软件默认配置");
        softwareDefault.Click += (_, _) => ApplyEffect(settings =>
        {
            settings.Enabled = true;
            settings.OperatingMode = OperatingMode.Lighting;
            ApplyEffectToSettings(settings, EffectPresetSettings.CreateSoftwareDefault(effectType));
            settings.SavedEffects.LastUsedLightingEffect = effectType;
        });
        mode.DropDownItems.Add(softwareDefault);

        var presets = _settings.EffectPresets.ForType(effectType);
        if (presets.Count > 0)
        {
            mode.DropDownItems.Add(new ToolStripSeparator());
        }

        foreach (var preset in presets)
        {
            var presetCopy = KeyboardSettings.CloneEffectPreset(preset);
            var item = new ToolStripMenuItem(presetCopy.Name);
            item.Click += (_, _) => ApplyEffect(settings =>
            {
                settings.Enabled = true;
                settings.OperatingMode = OperatingMode.Lighting;
                ApplyEffectToSettings(settings, presetCopy.Effect);
                settings.SavedEffects.LastUsedLightingEffect = presetCopy.Effect.Type;
            });
            mode.DropDownItems.Add(item);
        }

        parent.DropDownItems.Add(mode);
    }

    private ToolStripMenuItem BuildBrightnessMenu()
    {
        var musicMode = _settings.OperatingMode == OperatingMode.Music;
        var current = musicMode ? _settings.Effect.Music.PeakBrightness : _settings.Brightness;
        var brightness = new ToolStripMenuItem(musicMode
            ? $"音乐峰值亮度 ({current}%)"
            : $"基础亮度 ({current}%)");

        foreach (var value in new[] { 25, 50, 75, 100 })
        {
            var item = new ToolStripMenuItem($"{value}%")
            {
                Checked = current == value
            };
            item.Click += (_, _) => ApplyEffect(settings =>
            {
                settings.Enabled = true;
                if (settings.OperatingMode == OperatingMode.Music)
                {
                    settings.Effect.Music.PeakBrightness = value;
                    settings.Effect.Music.BaseBrightness = Math.Min(settings.Effect.Music.BaseBrightness, value);
                }
                else settings.Brightness = value;
            });
            brightness.DropDownItems.Add(item);
        }

        return brightness;
    }

    private ToolStripMenuItem BuildServiceMenu()
    {
        var service = new ToolStripMenuItem("服务");

        var restart = new ToolStripMenuItem("重启服务");
        restart.Click += (_, _) => RestartService();

        var folder = new ToolStripMenuItem("打开配置目录");
        folder.Click += (_, _) =>
        {
            Directory.CreateDirectory(AppPaths.ProgramDataDirectory);
            Process.Start(new ProcessStartInfo("explorer.exe", AppPaths.ProgramDataDirectory) { UseShellExecute = true });
        };

        service.DropDownItems.Add(restart);
        service.DropDownItems.Add(folder);
        return service;
    }

    private void OpenSettings()
    {
        if (_settingsForm is { IsDisposed: false })
        {
            ActivateSettingsWindow(_settingsForm);
            return;
        }

        _settingsForm = new SettingsForm(_settingsStore);
        _settingsForm.SettingsSaved += (_, _) => RefreshMenu();
        _settingsForm.FormClosed += (_, _) =>
        {
            _settingsForm = null;
            RefreshMenu();
        };
        _settingsForm.Show();
        ActivateSettingsWindow(_settingsForm);
    }

    private void OpenSettingsAfterStartup()
    {
        var timer = new System.Windows.Forms.Timer { Interval = 500 };
        timer.Tick += (_, _) =>
        {
            timer.Stop();
            timer.Dispose();
            OpenSettings();
        };
        timer.Start();
    }

    private static void OpenAbout()
    {
        using var form = new AboutForm();
        form.ShowDialog();
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

    private async Task CheckForUpdatesAutomaticallyAsync(bool initialDelay)
    {
        try
        {
            if (initialDelay) await Task.Delay(TimeSpan.FromSeconds(5));
            var interval = _settingsStore.Load().Update.CheckInterval;
            var result = await _updateChecker.CheckAsync(force: false, interval);
            if (result.Status == UpdateCheckStatus.Available)
            {
                _availableUpdate = result;
                RefreshMenu(refreshEventMonitors: false);
                ShowUpdateAvailable(result, passive: true);
            }
        }
        catch
        {
        }
    }

    private async Task CheckForUpdatesManuallyAsync()
    {
        try
        {
            var result = await _updateChecker.CheckAsync(force: true, UpdateCheckInterval.Daily);
            if (result.Status == UpdateCheckStatus.Available)
            {
                _availableUpdate = result;
                RefreshMenu(refreshEventMonitors: false);
                if (result.LatestVersion is not null) UpdateChecker.MarkPrompted(result.LatestVersion);
                ShowUpdateAvailable(result, passive: false);
                return;
            }

            MessageBox.Show(
                $"当前已是最新版本。\n\n当前版本：{result.CurrentVersion.ToString(3)}",
                "ClevoLEDKeyboardControl",
                MessageBoxButtons.OK,
                MessageBoxIcon.Information);
        }
        catch (Exception ex) when (ex is UpdateCheckException or HttpRequestException or TaskCanceledException or InvalidOperationException)
        {
            var detail = ex switch
            {
                UpdateCheckException update => update.Message,
                TaskCanceledException => "连接 GitHub 超时，请稍后重试。",
                HttpRequestException http when http.StatusCode == System.Net.HttpStatusCode.Forbidden =>
                    "GitHub 暂时限制了更新检查请求，请稍后重试。",
                HttpRequestException http when http.StatusCode.HasValue =>
                    $"GitHub 返回状态码 {(int)http.StatusCode.Value}，请稍后重试。",
                InvalidOperationException => "无法识别 GitHub 最新版本信息，请稍后重试。",
                _ => "暂时无法连接更新服务器，请稍后重试。"
            };
            MessageBox.Show(
                $"检查更新失败：{detail}",
                "ClevoLEDKeyboardControl",
                MessageBoxButtons.OK,
                MessageBoxIcon.Warning);
        }
    }

    private void ShowUpdateAvailable(UpdateCheckResult result, bool passive)
    {
        var latest = result.LatestVersion?.ToString(3) ?? "未知";
        if (passive)
        {
            if (result.LatestVersion is null || !UpdateChecker.ShouldPrompt(result.LatestVersion)) return;
            UpdateChecker.MarkPrompted(result.LatestVersion);
            _balloonReleaseUrl = result.ReleaseUrl;
            _notifyIcon.BalloonTipTitle = "ClevoLEDKeyboardControl 有新版本";
            _notifyIcon.BalloonTipText = $"最新版本：{latest}。托盘菜单会持续显示更新入口。";
            _notifyIcon.ShowBalloonTip(8000);
            var passiveChoice = MessageBox.Show(
                $"发现新版本：{latest}\n当前版本：{result.CurrentVersion.ToString(3)}\n\n是否立即打开下载页面？\n选择“否”后仍可从托盘菜单下载。",
                "ClevoLEDKeyboardControl 自动更新提醒",
                MessageBoxButtons.YesNo,
                MessageBoxIcon.Information);
            if (passiveChoice == DialogResult.Yes && !string.IsNullOrWhiteSpace(result.ReleaseUrl))
                UpdateChecker.OpenUrl(result.ReleaseUrl);
            return;
        }

        var choice = MessageBox.Show(
            $"发现新版本：{latest}\n当前版本：{result.CurrentVersion.ToString(3)}\n\n是否打开下载页面？",
            "ClevoLEDKeyboardControl",
            MessageBoxButtons.YesNo,
            MessageBoxIcon.Information);

        if (choice == DialogResult.Yes)
        {
            UpdateChecker.OpenReleases();
        }
    }

    private void OpenBalloonRelease()
    {
        if (string.IsNullOrWhiteSpace(_balloonReleaseUrl))
        {
            return;
        }

        UpdateChecker.OpenUrl(_balloonReleaseUrl);
    }

    private void ApplyEffect(Action<KeyboardSettings> update)
    {
        if (TryUpdateSettings(update, rememberCurrent: true))
        {
            RefreshMenu();
        }
    }

    private bool TryUpdateSettings(Action<KeyboardSettings> update, bool rememberCurrent)
    {
        try
        {
            _settings = _settingsStore.Load();
            if (rememberCurrent)
            {
                RememberCurrentEffect(_settings);
            }

            update(_settings);
            _settingsStore.Save(_settings);
            RefreshEventMonitors();
            _settingsForm?.ReloadFromStore();
            return true;
        }
        catch (UnauthorizedAccessException)
        {
            ShowSettingsAccessError();
            return false;
        }
        catch (IOException ex)
        {
            MessageBox.Show(
                $"无法保存配置：{ex.Message}",
                "ClevoLEDKeyboardControl",
                MessageBoxButtons.OK,
                MessageBoxIcon.Warning);
            return false;
        }
    }

    private void RefreshMenu(bool refreshEventMonitors = true, bool reloadSettings = true)
    {
        if (reloadSettings) _settings = _settingsStore.Load();
        if (refreshEventMonitors) RefreshEventMonitors();
        var oldMenu = _notifyIcon.ContextMenuStrip;
        _notifyIcon.ContextMenuStrip = BuildMenu();
        oldMenu?.Dispose();
        UpdateNotifyIconText();
    }

    private void OnAudioStatusChanged(AudioSourceStatusInfo? info)
    {
        _lastAudioStatus = info;
        UpdateNotifyIconText();
        if (_settingsForm is { } form && !form.IsDisposed)
        {
            try { form.UpdateAudioSourceLabel(info); }
            catch (ObjectDisposedException) { }
            catch (InvalidOperationException) { }
        }
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

    private static void ActivateSettingsWindow(Form form)
    {
        if (form.WindowState == FormWindowState.Minimized)
        {
            form.WindowState = FormWindowState.Normal;
        }

        form.Show();
        form.Activate();
        form.BringToFront();
        form.TopMost = true;
        form.TopMost = false;
    }

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

    private static void RestoreSavedEffect(KeyboardSettings settings, EffectType effect)
    {
        settings.SavedEffects ??= new EffectMemorySettings();
        settings.SavedEffects.Normalize();
        settings.Effect = effect switch
        {
            EffectType.Static => KeyboardSettings.CloneEffect(settings.SavedEffects.Static),
            EffectType.Rainbow => KeyboardSettings.CloneEffect(settings.SavedEffects.Rainbow),
            EffectType.Breathing => KeyboardSettings.CloneEffect(settings.SavedEffects.Breathing),
            EffectType.Sequence => KeyboardSettings.CloneEffect(settings.SavedEffects.Sequence),
            EffectType.Pulse => KeyboardSettings.CloneEffect(settings.SavedEffects.Pulse),
            EffectType.Heartbeat => KeyboardSettings.CloneEffect(settings.SavedEffects.Heartbeat),
            _ => KeyboardSettings.CloneEffect(settings.Effect)
        };
        settings.Effect.Type = effect;
        if (effect == EffectType.Rainbow)
        {
            settings.Effect.CustomSequenceColorsEnabled = true;
        }

        settings.Mode = effect switch
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
            using var controller = new ServiceController(AppPaths.ServiceName);
            if (controller.Status != ServiceControllerStatus.Stopped &&
                controller.Status != ServiceControllerStatus.StopPending)
            {
                controller.Stop();
                controller.WaitForStatus(ServiceControllerStatus.Stopped, TimeSpan.FromSeconds(20));
            }

            controller.Start();
            controller.WaitForStatus(ServiceControllerStatus.Running, TimeSpan.FromSeconds(20));
        }
        catch (UnauthorizedAccessException)
        {
            RestartServiceElevated();
        }
        catch (Win32Exception ex) when (ex.NativeErrorCode == 5)
        {
            RestartServiceElevated();
        }
        catch (InvalidOperationException ex) when (ex.InnerException is Win32Exception { NativeErrorCode: 5 })
        {
            RestartServiceElevated();
        }
        catch (Exception ex)
        {
            MessageBox.Show(
                $"无法重启服务：{ex.Message}",
                "ClevoLEDKeyboardControl",
                MessageBoxButtons.OK,
                MessageBoxIcon.Warning);
        }
    }

    private void ExitApplication()
    {
        StopServiceForExit();
        ExitThread();
    }

    private static void StopServiceForExit()
    {
        try
        {
            using var controller = new ServiceController(AppPaths.ServiceName);
            if (controller.Status == ServiceControllerStatus.Stopped ||
                controller.Status == ServiceControllerStatus.StopPending)
            {
                return;
            }

            controller.Stop();
            controller.WaitForStatus(ServiceControllerStatus.Stopped, TimeSpan.FromSeconds(20));
        }
        catch (UnauthorizedAccessException)
        {
            StopServiceElevated();
        }
        catch (Win32Exception ex) when (ex.NativeErrorCode == 5)
        {
            StopServiceElevated();
        }
        catch (InvalidOperationException ex) when (ex.InnerException is Win32Exception { NativeErrorCode: 5 })
        {
            StopServiceElevated();
        }
        catch
        {
            // 服务可能未安装或已停止，忽略以保证退出顺畅
        }
    }

    private static void StopServiceElevated()
    {
        try
        {
            var command = $"Stop-Service -Name {AppPaths.ServiceName} -Force";
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
            // 用户取消 UAC 或失败时，忽略——我们仍然要退出托盘
        }
    }

    private static void EnsureServiceRunning()
    {
        try
        {
            using var controller = new ServiceController(AppPaths.ServiceName);
            if (controller.Status == ServiceControllerStatus.Running ||
                controller.Status == ServiceControllerStatus.StartPending)
            {
                return;
            }

            controller.Start();
            controller.WaitForStatus(ServiceControllerStatus.Running, TimeSpan.FromSeconds(20));
        }
        catch (UnauthorizedAccessException)
        {
            StartServiceElevated();
        }
        catch (Win32Exception ex) when (ex.NativeErrorCode == 5)
        {
            StartServiceElevated();
        }
        catch (InvalidOperationException ex) when (ex.InnerException is Win32Exception { NativeErrorCode: 5 })
        {
            StartServiceElevated();
        }
        catch
        {
            // 服务未安装或其他错误时不打扰用户
        }
    }

    private static void StartServiceElevated()
    {
        try
        {
            var command = $"Start-Service -Name {AppPaths.ServiceName}";
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
            // 用户取消 UAC 或失败时不打扰
        }
    }

    private static void RestartServiceElevated()
    {
        try
        {
            var command = "Restart-Service -Name ClevoLEDKeyboardControlService -Force";
            Process.Start(new ProcessStartInfo("powershell.exe", $"-NoProfile -ExecutionPolicy Bypass -Command \"{command}\"")
            {
                UseShellExecute = true,
                Verb = "runas",
                WindowStyle = ProcessWindowStyle.Hidden
            });
        }
        catch (Exception ex)
        {
            MessageBox.Show(
                $"需要管理员权限重启服务：{ex.Message}",
                "ClevoLEDKeyboardControl",
                MessageBoxButtons.OK,
                MessageBoxIcon.Warning);
        }
    }

    private static void ShowSettingsAccessError()
    {
        MessageBox.Show(
            $"无法保存配置，请重新安装新版程序以修复权限，或确认当前用户可写入：{AppPaths.SettingsPath}",
            "ClevoLEDKeyboardControl",
            MessageBoxButtons.OK,
            MessageBoxIcon.Warning);
    }

}
