using ColorfulLedKeyboard.Core;
using System.ComponentModel;
using System.Diagnostics;
using System.ServiceProcess;

namespace ColorfulLedKeyboard.Tray;

public sealed class TrayApplicationContext : ApplicationContext
{
    private readonly SettingsStore _settingsStore;
    private readonly UpdateChecker _updateChecker = new();
    private readonly TypingPulseHook _typingPulseHook = new();
    private readonly NotifyIcon _notifyIcon;
    private readonly System.Windows.Forms.Timer _foregroundTimer = new() { Interval = 1000 };
    private string? _balloonReleaseUrl;
    private string? _lastForegroundProcess;
    private DateTimeOffset _lastForegroundStateSaved = DateTimeOffset.MinValue;
    private KeyboardSettings _settings;

    public TrayApplicationContext(SettingsStore settingsStore)
    {
        _settingsStore = settingsStore;
        _settings = _settingsStore.Load();

        _notifyIcon = new NotifyIcon
        {
            Icon = Icon.ExtractAssociatedIcon(Application.ExecutablePath) ?? SystemIcons.Application,
            Text = "ClevoRGBControl",
            Visible = true,
            ContextMenuStrip = BuildMenu()
        };

        _notifyIcon.DoubleClick += (_, _) => PickStaticColor();
        _notifyIcon.BalloonTipClicked += (_, _) => OpenBalloonRelease();
        _foregroundTimer.Tick += (_, _) => UpdateForegroundAppState();
        _foregroundTimer.Start();
        _typingPulseHook.SetEnabled(_settings.TypingPulse.Enabled);
        UpdateForegroundAppState();
        _ = CheckForUpdatesOnStartupAsync();
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
        }

        base.Dispose(disposing);
    }

    private ContextMenuStrip BuildMenu()
    {
        var menu = new ContextMenuStrip();

        var enabled = new ToolStripMenuItem("启用灯效") { Checked = _settings.Enabled };
        enabled.Click += (_, _) =>
        {
            if (TryUpdateSettings(settings => settings.Enabled = !settings.Enabled))
            {
                RefreshMenu();
            }
        };

        menu.Items.Add(enabled);
        menu.Items.Add(BuildEffectMenu());
        menu.Items.Add(BuildBrightnessMenu());
        menu.Items.Add(BuildSpeedMenu());
        menu.Items.Add(BuildPresetMenu());
        menu.Items.Add(new ToolStripSeparator());

        var settingsItem = new ToolStripMenuItem("设置...");
        settingsItem.Click += (_, _) => OpenSettings();
        menu.Items.Add(settingsItem);

        menu.Items.Add(BuildServiceMenu());
        menu.Items.Add(BuildExperimentalMenu());

        var update = new ToolStripMenuItem("检查更新");
        update.Click += async (_, _) => await CheckForUpdatesManuallyAsync();
        menu.Items.Add(update);

        var about = new ToolStripMenuItem("关于...");
        about.Click += (_, _) => OpenAbout();
        menu.Items.Add(about);

        menu.Items.Add(new ToolStripSeparator());

        var exit = new ToolStripMenuItem("退出托盘");
        exit.Click += (_, _) => ExitThread();
        menu.Items.Add(exit);

        return menu;
    }

    private ToolStripMenuItem BuildEffectMenu()
    {
        var effect = new ToolStripMenuItem("效果");

        var staticColor = new ToolStripMenuItem($"固定颜色 {_settings.Effect.Color}")
        {
            Checked = _settings.Effect.Type == EffectType.Static
        };
        staticColor.Click += (_, _) => PickStaticColor();

        var rainbow = new ToolStripMenuItem("RGB 循环")
        {
            Checked = _settings.Effect.Type == EffectType.Rainbow
        };
        rainbow.Click += (_, _) => ApplyEffect(settings =>
        {
            settings.Enabled = true;
            settings.Effect.Type = EffectType.Rainbow;
            settings.Mode = KeyboardMode.Rainbow;
        });

        var breathing = new ToolStripMenuItem("单色呼吸...")
        {
            Checked = _settings.Effect.Type == EffectType.Breathing
        };
        breathing.Click += (_, _) => PickBreathingColor();

        var sequence = new ToolStripMenuItem("色彩序列")
        {
            Checked = _settings.Effect.Type == EffectType.Sequence
        };
        sequence.Click += (_, _) => ApplyEffect(LightingPresets.ApplyRedBluePulse);

        var music = new ToolStripMenuItem("音乐模式")
        {
            Checked = _settings.Effect.Type == EffectType.Music
        };
        music.Click += (_, _) => ApplyEffect(settings =>
        {
            settings.Enabled = true;
            settings.Effect.Type = EffectType.Music;
            settings.Mode = KeyboardMode.Music;
        });

        var off = new ToolStripMenuItem("关闭灯效")
        {
            Checked = _settings.Effect.Type == EffectType.Off || !_settings.Enabled
        };
        off.Click += (_, _) => ApplyEffect(settings =>
        {
            settings.Enabled = true;
            settings.Effect.Type = EffectType.Off;
            settings.Mode = KeyboardMode.Off;
        });

        effect.DropDownItems.Add(staticColor);
        effect.DropDownItems.Add(rainbow);
        effect.DropDownItems.Add(breathing);
        effect.DropDownItems.Add(sequence);
        effect.DropDownItems.Add(music);
        effect.DropDownItems.Add(new ToolStripSeparator());
        effect.DropDownItems.Add(off);

        return effect;
    }

    private ToolStripMenuItem BuildBrightnessMenu()
    {
        var brightness = new ToolStripMenuItem($"亮度 ({_settings.Brightness}%)");
        if (_settings.Effect.Type == EffectType.Music || _settings.TypingPulse.Enabled)
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

    private ToolStripMenuItem BuildSpeedMenu()
    {
        var speed = new ToolStripMenuItem("速度");
        AddSpeedItem(speed, "非常慢", 1, 160);
        AddSpeedItem(speed, "慢", 1, 80);
        AddSpeedItem(speed, "正常", 3, 40);
        AddSpeedItem(speed, "快", 6, 30);
        AddSpeedItem(speed, "很快", 10, 20);
        return speed;
    }

    private ToolStripMenuItem BuildPresetMenu()
    {
        var preset = new ToolStripMenuItem("预设");
        AddPreset(preset, "暖白", LightingPresets.ApplyWarmWhite);
        AddPreset(preset, "自然白", LightingPresets.ApplyNeutralWhite);
        AddPreset(preset, "冷白", LightingPresets.ApplyCoolWhite);
        preset.DropDownItems.Add(new ToolStripSeparator());
        AddPreset(preset, "红蓝呼吸", LightingPresets.ApplyRedBluePulse);
        AddPreset(preset, "柔和彩虹", LightingPresets.ApplySoftRainbow);
        return preset;
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

    private ToolStripMenuItem BuildExperimentalMenu()
    {
        var experimental = new ToolStripMenuItem("实验性功能");

        var zoneTest = new ToolStripMenuItem("分区控制测试");
        zoneTest.Click += (_, _) => RunZoneTest();

        experimental.DropDownItems.Add(zoneTest);
        return experimental;
    }

    private void AddSpeedItem(ToolStripMenuItem parent, string label, int step, int intervalMs)
    {
        var item = new ToolStripMenuItem(label)
        {
            Checked = _settings.Effect.Step == step && _settings.Effect.IntervalMs == intervalMs
        };

        item.Click += (_, _) => ApplyEffect(settings =>
        {
            settings.Enabled = true;
            settings.Effect.Step = step;
            settings.Effect.IntervalMs = intervalMs;
        });

        parent.DropDownItems.Add(item);
    }

    private void AddPreset(ToolStripMenuItem parent, string label, Action<KeyboardSettings> preset)
    {
        var item = new ToolStripMenuItem(label);
        item.Click += (_, _) => ApplyEffect(preset);
        parent.DropDownItems.Add(item);
    }

    private void PickStaticColor()
    {
        using var dialog = new ColorDialog
        {
            FullOpen = true,
            Color = ColorTranslator.FromHtml(_settings.Effect.Color)
        };

        if (dialog.ShowDialog() != DialogResult.OK)
        {
            return;
        }

        ApplyEffect(settings =>
        {
            settings.Enabled = true;
            settings.Effect.Type = EffectType.Static;
            settings.Mode = KeyboardMode.Static;
            settings.Effect.Color = ToHex(dialog.Color);
        });
    }

    private void PickBreathingColor()
    {
        using var dialog = new ColorDialog
        {
            FullOpen = true,
            Color = ColorTranslator.FromHtml(_settings.Effect.Color)
        };

        if (dialog.ShowDialog() != DialogResult.OK)
        {
            return;
        }

        ApplyEffect(settings =>
        {
            settings.Enabled = true;
            settings.Effect.Type = EffectType.Breathing;
            settings.Mode = KeyboardMode.Breathing;
            settings.Effect.Color = ToHex(dialog.Color);
            settings.Effect.PeriodMs = 2200;
            settings.Effect.MinimumBrightness = 0;
            settings.Effect.HardBlink = false;
        });
    }

    private void OpenSettings()
    {
        using var form = new SettingsForm(_settingsStore);
        form.ShowDialog();
        RefreshMenu();
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
                "ClevoRGBControl",
                MessageBoxButtons.OK,
                MessageBoxIcon.Information);
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException or InvalidOperationException)
        {
            MessageBox.Show(
                $"检查更新失败：{ex.Message}",
                "ClevoRGBControl",
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
            _notifyIcon.BalloonTipTitle = "ClevoRGBControl 有新版本";
            _notifyIcon.BalloonTipText = $"最新版本：{latest}。点击这里打开下载页面。";
            _notifyIcon.ShowBalloonTip(8000);
            return;
        }

        var choice = MessageBox.Show(
            $"发现新版本：{latest}\n当前版本：{result.CurrentVersion.ToString(3)}\n\n是否打开下载页面？",
            "ClevoRGBControl",
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
        if (TryUpdateSettings(update))
        {
            RefreshMenu();
        }
    }

    private bool TryUpdateSettings(Action<KeyboardSettings> update)
    {
        try
        {
            _settings = _settingsStore.Load();
            update(_settings);
            _settingsStore.Save(_settings);
            _typingPulseHook.SetEnabled(_settings.TypingPulse.Enabled);
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
                "ClevoRGBControl",
                MessageBoxButtons.OK,
                MessageBoxIcon.Warning);
            return false;
        }
    }

    private void RefreshMenu()
    {
        _settings = _settingsStore.Load();
        _typingPulseHook.SetEnabled(_settings.TypingPulse.Enabled);
        var oldMenu = _notifyIcon.ContextMenuStrip;
        _notifyIcon.ContextMenuStrip = BuildMenu();
        oldMenu?.Dispose();
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
                "ClevoRGBControl",
                MessageBoxButtons.OK,
                MessageBoxIcon.Warning);
        }
    }

    private static void RestartServiceElevated()
    {
        try
        {
            var command = "Restart-Service -Name ClevoRGBControlService -Force";
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
                "ClevoRGBControl",
                MessageBoxButtons.OK,
                MessageBoxIcon.Warning);
        }
    }

    private static void RunZoneTest()
    {
        var exe = Path.Combine(
            AppContext.BaseDirectory,
            "..",
            "Experimental",
            "ColorfulLedKeyboard.ZoneTest.exe");
        exe = Path.GetFullPath(exe);

        if (!File.Exists(exe))
        {
            MessageBox.Show(
                $"找不到实验性工具：{exe}",
                "ClevoRGBControl",
                MessageBoxButtons.OK,
                MessageBoxIcon.Warning);
            return;
        }

        try
        {
            Process.Start(new ProcessStartInfo(exe)
            {
                UseShellExecute = true,
                WorkingDirectory = Path.GetDirectoryName(exe)
            });
        }
        catch (Exception ex)
        {
            MessageBox.Show(
                $"无法启动分区控制测试：{ex.Message}",
                "ClevoRGBControl",
                MessageBoxButtons.OK,
                MessageBoxIcon.Warning);
        }
    }

    private static void ShowSettingsAccessError()
    {
        MessageBox.Show(
            $"无法保存配置，请重新安装新版程序以修复权限，或确认当前用户可写入：{AppPaths.SettingsPath}",
            "ClevoRGBControl",
            MessageBoxButtons.OK,
            MessageBoxIcon.Warning);
    }

    private static string ToHex(Color color) => $"#{color.R:X2}{color.G:X2}{color.B:X2}";
}
