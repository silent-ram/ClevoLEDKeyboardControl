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
    private readonly NotifyIcon _notifyIcon;
    private readonly System.Windows.Forms.Timer _foregroundTimer = new() { Interval = 1000 };
    private string? _balloonReleaseUrl;
    private string? _lastForegroundProcess;
    private DateTimeOffset _lastForegroundStateSaved = DateTimeOffset.MinValue;
    private KeyboardSettings _settings;
    private SettingsForm? _settingsForm;
    private AudioSourceStatusWatcher? _audioStatusWatcher;
    private AudioSourceStatusInfo? _lastAudioStatus;

    public TrayApplicationContext(SettingsStore settingsStore, bool openSettingsOnStartup = false)
    {
        _settingsStore = settingsStore;
        _settings = _settingsStore.Load();
        _notificationFlashMonitor = new NotificationFlashMonitor(_settingsStore);

        EnsureServiceRunning();

        _notifyIcon = new NotifyIcon
        {
            Icon = Icon.ExtractAssociatedIcon(Application.ExecutablePath) ?? SystemIcons.Application,
            Text = "ClevoLEDKeyboardControl",
            Visible = true,
            ContextMenuStrip = BuildMenu()
        };

        _notifyIcon.DoubleClick += (_, _) => OpenSettings();
        _notifyIcon.BalloonTipClicked += (_, _) => OpenBalloonRelease();
        _foregroundTimer.Tick += (_, _) => UpdateForegroundAppState();
        _foregroundTimer.Start();
        _typingPulseHook.SetEnabled(_settings.TypingPulse.Enabled);
        _notificationFlashMonitor.SetEnabled(_settings.NotificationFlash.Enabled);
        UpdateForegroundAppState();
        _ = CheckForUpdatesOnStartupAsync();

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
            _typingPulseHook.Dispose();
            _notificationFlashMonitor.Dispose();
            _audioStatusWatcher?.Dispose();
        }

        base.Dispose(disposing);
    }

    private ContextMenuStrip BuildMenu()
    {
        var menu = new ContextMenuStrip();

        var enabled = new ToolStripMenuItem("启用灯效") { Checked = _settings.Enabled };
        enabled.Click += (_, _) =>
        {
            if (TryUpdateSettings(settings => settings.Enabled = !settings.Enabled, rememberCurrent: false))
            {
                RefreshMenu();
            }
        };

        menu.Items.Add(enabled);
        menu.Items.Add(BuildEffectMenu());
        menu.Items.Add(BuildMusicModeMenu());
        menu.Items.Add(BuildBrightnessMenu());
        menu.Items.Add(new ToolStripSeparator());

        var settingsItem = new ToolStripMenuItem("设置...");
        settingsItem.Click += (_, _) => OpenSettings();
        menu.Items.Add(settingsItem);

        menu.Items.Add(BuildServiceMenu());

        var update = new ToolStripMenuItem("检查更新");
        update.Click += async (_, _) => await CheckForUpdatesManuallyAsync();
        menu.Items.Add(update);

        var about = new ToolStripMenuItem("关于...");
        about.Click += (_, _) => OpenAbout();
        menu.Items.Add(about);

        menu.Items.Add(new ToolStripSeparator());

        var exit = new ToolStripMenuItem("退出");
        exit.Click += (_, _) => ExitApplication();
        menu.Items.Add(exit);

        return menu;
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
        var brightness = new ToolStripMenuItem($"亮度 ({_settings.Brightness}%)");
        if (_settings.OperatingMode == OperatingMode.Music)
        {
            brightness.Enabled = false;
            brightness.Text = "亮度 (由当前模式控制)";
        }

        foreach (var value in new[] { 25, 50, 75, 100 })
        {
            var item = new ToolStripMenuItem($"{value}%")
            {
                Checked = _settings.Brightness == value
            };
            item.Click += (_, _) => ApplyEffect(settings =>
            {
                settings.Enabled = true;
                settings.Brightness = value;
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

    private async Task CheckForUpdatesOnStartupAsync()
    {
        try
        {
            await Task.Delay(TimeSpan.FromSeconds(5));
            var interval = _settingsStore.Load().Update.CheckInterval;
            var result = await _updateChecker.CheckAsync(force: false, interval);
            if (result.Status == UpdateCheckStatus.Available)
            {
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
                ShowUpdateAvailable(result, passive: false);
                return;
            }

            MessageBox.Show(
                $"当前已是最新版本。\n\n当前版本：{result.CurrentVersion.ToString(3)}",
                "ClevoLEDKeyboardControl",
                MessageBoxButtons.OK,
                MessageBoxIcon.Information);
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException or InvalidOperationException)
        {
            MessageBox.Show(
                $"检查更新失败：{ex.Message}",
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
            _balloonReleaseUrl = result.ReleaseUrl;
            _notifyIcon.BalloonTipTitle = "ClevoLEDKeyboardControl 有新版本";
            _notifyIcon.BalloonTipText = $"最新版本：{latest}。点击这里打开下载页面。";
            _notifyIcon.ShowBalloonTip(8000);
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
            _typingPulseHook.SetEnabled(_settings.TypingPulse.Enabled);
            _notificationFlashMonitor.SetEnabled(_settings.NotificationFlash.Enabled);
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

    private void RefreshMenu()
    {
        _settings = _settingsStore.Load();
        _typingPulseHook.SetEnabled(_settings.TypingPulse.Enabled);
        _notificationFlashMonitor.SetEnabled(_settings.NotificationFlash.Enabled);
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

    private void UpdateNotifyIconText()
    {
        var settings = _settings;
        string text;

        if (settings.OperatingMode != OperatingMode.Music || !settings.Enabled)
        {
            text = "ClevoLEDKeyboardControl";
        }
        else
        {
            var info = _lastAudioStatus;
            var deviceName = info?.DeviceFriendlyName ?? "";
            text = string.IsNullOrEmpty(deviceName)
                ? "ClevoLEDKeyboardControl\n音乐：检测中…"
                : $"ClevoLEDKeyboardControl\n音乐：{deviceName}";
        }

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
