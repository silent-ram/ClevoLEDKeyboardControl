namespace ColorfulLedKeyboard.Service;

using ColorfulLedKeyboard.Core;
using System.Runtime.InteropServices;

public class Worker : BackgroundService
{
    private readonly SettingsStore _settingsStore = new();
    private readonly DchuKeyboardDevice _device = new();
    private readonly AudioSourceProvider _audioSource;
    private readonly SystemAudioLevelMeter _audioLevelMeter;
    private readonly AudioBandLevelMeter _audioBandLevelMeter;
    private readonly AudioApplicationMonitor _audioApplicationMonitor = new();
    private readonly ILogger<Worker> _logger;
    private FileSystemWatcher? _watcher;
    private volatile bool _settingsChanged = true;
    private List<RgbColor> _lastRenderedMusicColors = [];
    private DateTimeOffset _lastAutomationStatusWrite = DateTimeOffset.MinValue;
    private string _lastAutomationStatusSignature = "";
    private AudioApplicationsState? _lastAudioApplicationsState;
    private DateTimeOffset _lastAudioApplicationsRead = DateTimeOffset.MinValue;

    public Worker(ILogger<Worker> logger, ILoggerFactory loggerFactory)
    {
        _logger = logger;
        _audioSource = new AudioSourceProvider(loggerFactory.CreateLogger<AudioSourceProvider>());
        _audioLevelMeter = new SystemAudioLevelMeter(_audioSource);
        _audioBandLevelMeter = new AudioBandLevelMeter(_audioSource);
        _audioSource.SourceChanged += OnAudioSourceChanged;

        // 订阅完事件后立刻刷一次状态：让初始（启动那一刻）的设备状态也走 OnAudioSourceChanged
        // 写到文件里，否则 Tray 在用户首次切设备前都看不到任何状态，UI 显示"检测中…"。
        _audioSource.RefreshNow();
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        EnsureConfigWatcher();
        await FlashStartupAsync(stoppingToken);

        while (!stoppingToken.IsCancellationRequested)
        {
            var settings = BuildRuntimeSettings(_settingsStore.Load());
            _settingsChanged = false;

            if (!settings.Enabled)
            {
                TryTurnOffKeyboard();
                await WaitForSettingsChangeAsync(1000, stoppingToken);
                continue;
            }

            try
            {
                await RunEffectAsync(settings, stoppingToken);
            }
            catch (DllNotFoundException ex)
            {
                _logger.LogError(ex, "InsydeDCHU.dll was not found. Copy it next to the service executable.");
                await Task.Delay(5000, stoppingToken);
            }
            catch (EntryPointNotFoundException ex)
            {
                _logger.LogError(ex, "InsydeDCHU.dll does not expose SetDCHU_Data.");
                await Task.Delay(5000, stoppingToken);
            }
            catch (SEHException ex)
            {
                _logger.LogError(ex, "The keyboard LED driver rejected the operation.");
                await Task.Delay(5000, stoppingToken);
            }
        }
    }

    public override async Task StopAsync(CancellationToken cancellationToken)
    {
        _watcher?.Dispose();
        _audioSource.SourceChanged -= OnAudioSourceChanged;

        // NAudio 在某些路径下 StopRecording / Dispose 可能阻塞（COM 回调链路死锁）
        // 加 5 秒超时保护，超时则放弃 dispose 让进程自然终结
        var disposeTask = Task.Run(() =>
        {
            try { _audioBandLevelMeter.Dispose(); } catch (Exception ex) { _logger.LogWarning(ex, "AudioBandLevelMeter.Dispose threw"); }
            try { _audioLevelMeter.Dispose(); } catch (Exception ex) { _logger.LogWarning(ex, "SystemAudioLevelMeter.Dispose threw"); }
            try { _audioSource.Dispose(); } catch (Exception ex) { _logger.LogWarning(ex, "AudioSourceProvider.Dispose threw"); }
            try { _audioApplicationMonitor.Dispose(); } catch (Exception ex) { _logger.LogWarning(ex, "AudioApplicationMonitor.Dispose threw"); }
        });

        var completed = await Task.WhenAny(disposeTask, Task.Delay(TimeSpan.FromSeconds(5), cancellationToken));
        if (completed != disposeTask)
        {
            _logger.LogWarning("Audio dispose timed out after 5s; proceeding with shutdown anyway");
        }

        await base.StopAsync(cancellationToken);
    }

    private void TryTurnOffKeyboard()
    {
        try
        {
            _device.SetColor(RgbColor.Black);
        }
        catch (Exception ex) when (ex is DllNotFoundException or EntryPointNotFoundException or SEHException)
        {
            _logger.LogWarning(ex, "Keyboard LEDs could not be turned off.");
        }
    }

    private async Task RunEffectAsync(KeyboardSettings settings, CancellationToken stoppingToken)
    {
        if (settings.OperatingMode == OperatingMode.Music)
        {
            await RunMusicAsync(settings, stoppingToken);
            return;
        }

        var generator = new LightingFrameGenerator(settings);
        var nextRuntimeRefresh = DateTimeOffset.UtcNow.Add(RuntimePollInterval(settings));
        RgbColor? lastColor = null;

        while (!stoppingToken.IsCancellationRequested && !_settingsChanged)
        {
            if (DateTimeOffset.UtcNow >= nextRuntimeRefresh)
            {
                nextRuntimeRefresh = DateTimeOffset.UtcNow.Add(RuntimePollInterval(settings));
                if (ShouldRebuildRuntimeSettings(settings))
                {
                    _settingsChanged = true;
                    return;
                }
            }

            var brightness = ApplyTypingPulseBrightness(settings.Brightness, settings);
            var color = ClampOutputBrightness(
                ApplyNotificationFlash(generator.Next(brightness), settings),
                settings.OutputBrightnessLimit);
            if (color != lastColor)
            {
                _device.SetColor(color);
                lastColor = color;
            }

            if (settings.Effect.Type is EffectType.Static or EffectType.Off)
            {
                if ((settings.Effect.Type == EffectType.Static && settings.TypingPulse.Enabled) ||
                    settings.NotificationFlash.Enabled)
                {
                    await Task.Delay(40, stoppingToken);
                    continue;
                }

                if (NeedsRuntimePolling(settings))
                {
                    await Task.Delay(RuntimePollInterval(settings), stoppingToken);
                    _settingsChanged = true;
                    return;
                }

                await WaitForSettingsChangeAsync(1000, stoppingToken);
                _settingsChanged = true;
                return;
            }

            await Task.Delay(generator.IntervalMs, stoppingToken);
        }
    }

    private async Task RunMusicAsync(KeyboardSettings settings, CancellationToken stoppingToken)
    {
        var music = settings.Effect.Music.Normalize();
        var controller = new MusicPulseController();
        var targetMusicColors = music.Colors.Select(RgbColor.FromHex).ToList();
        var transitionFrom = _lastRenderedMusicColors.Count == 0
            ? targetMusicColors
            : Enumerable.Range(0, targetMusicColors.Count)
                .Select(index => _lastRenderedMusicColors[index % _lastRenderedMusicColors.Count]).ToList();
        var colorTransitionStarted = DateTimeOffset.UtcNow;
        var nextRuntimeRefresh = DateTimeOffset.UtcNow.Add(RuntimePollInterval(settings));
        RgbColor? lastColor = null;

        // 进入音乐模式立刻刷一次状态文件，避免 Tray 看到陈旧值
        _audioSource.RefreshNow();

        try
        {
            while (!stoppingToken.IsCancellationRequested && !_settingsChanged)
            {
                if (DateTimeOffset.UtcNow >= nextRuntimeRefresh)
                {
                    nextRuntimeRefresh = DateTimeOffset.UtcNow.Add(RuntimePollInterval(settings));
                    if (ShouldRebuildRuntimeSettings(settings))
                    {
                        _settingsChanged = true;
                        return;
                    }
                }

                // 永远调 meter（同 v1.3）：静音 → envelope=0 → 灯自然降到 BaseBrightness 颜色保持。
                // HFP 屏蔽在 meter 内部处理（Status==Hfp 时 EnsureCapture 跳过、不激活 SCO）。
                var selectedPeak = GetSelectedApplicationPeak(
                    settings.SelectedAudioProcessName, settings.SelectedAudioExecutablePath, settings.SelectedAudioProcessIds);
                var level = music.EqEnabled
                    ? Math.Max(_audioBandLevelMeter.GetAdaptiveBeatLevel(music), selectedPeak * 0.12f)
                    : selectedPeak;
                var systemVolume = _audioLevelMeter.GetMasterVolumeScalar();
                var transition = Math.Clamp((DateTimeOffset.UtcNow - colorTransitionStarted).TotalMilliseconds / 800d, 0, 1);
                var musicColors = targetMusicColors.Select((color, index) =>
                    RgbColor.Lerp(transitionFrom[index], color, transition)).ToList();
                _lastRenderedMusicColors = musicColors;
                var frame = controller.Next(music, level, systemVolume, musicColors.Count);
                var envelope = frame.Envelope;
                // 单色封面缺少多色切换带来的视觉节奏，扩大明暗范围让鼓点清晰可见。
                var effectiveBaseBrightness = musicColors.Count == 1
                    ? Math.Min(music.BaseBrightness, 8)
                    : music.BaseBrightness;
                var musicBrightness = effectiveBaseBrightness +
                    (music.PeakBrightness - effectiveBaseBrightness) * Math.Pow(envelope, musicColors.Count == 1 ? 0.45 : 0.55);
                var brightness = Math.Min(
                    (int)Math.Clamp(Math.Round(musicBrightness), effectiveBaseBrightness, music.PeakBrightness),
                    settings.OutputBrightnessLimit);
                brightness = Math.Min(
                    ApplyTypingPulseBrightness(brightness, settings),
                    settings.OutputBrightnessLimit);
                var sourceColor = musicColors[frame.ColorIndex % musicColors.Count];
                var color = ClampOutputBrightness(
                    ApplyNotificationFlash(sourceColor.Scale(brightness), settings),
                    settings.OutputBrightnessLimit);

                if (color != lastColor)
                {
                    _device.SetColor(color);
                    lastColor = color;
                }

                await Task.Delay(music.IntervalMs, stoppingToken);
            }
        }
        finally
        {
            _audioBandLevelMeter.PauseCapture();
            _audioLevelMeter.PauseDevice();
        }
    }

    private static RgbColor ApplyNotificationFlash(RgbColor color, KeyboardSettings settings)
    {
        var flash = settings.NotificationFlash.Normalize();
        if (!flash.Enabled)
        {
            return color;
        }

        var state = NotificationFlashState.Load();
        if (state is null)
        {
            return color;
        }

        var elapsedMs = (DateTimeOffset.UtcNow - state.TriggeredUtc).TotalMilliseconds;
        var cycleMs = flash.PulseMs * 2;
        var totalMs = cycleMs * flash.Pulses;
        if (elapsedMs < 0 || elapsedMs > totalMs)
        {
            return color;
        }

        var phase = elapsedMs % cycleMs;
        return phase < flash.PulseMs ? RgbColor.FromHex(flash.Color) : RgbColor.Black;
    }

    private static RgbColor ClampOutputBrightness(RgbColor color, int limit)
    {
        limit = Math.Clamp(limit, 0, 100);
        var maximum = Math.Max(color.R, Math.Max(color.G, color.B));
        var allowed = limit * 255 / 100d;
        if (maximum == 0 || maximum <= allowed)
        {
            return color;
        }

        var scale = allowed / maximum;
        return new RgbColor(
            (byte)Math.Round(color.R * scale),
            (byte)Math.Round(color.G * scale),
            (byte)Math.Round(color.B * scale));
    }

    private static int ApplyTypingPulseBrightness(int currentBrightness, KeyboardSettings settings)
    {
        var pulse = settings.TypingPulse.Normalize();
        if (!pulse.Enabled)
        {
            return currentBrightness;
        }

        var state = TypingPulseState.Load();
        if (state is null)
        {
            return currentBrightness;
        }

        var elapsedMs = (DateTimeOffset.UtcNow - state.LastKeyUtc).TotalMilliseconds;
        if (elapsedMs < 0 || elapsedMs > pulse.HoldMs + pulse.FadeMs)
        {
            return currentBrightness;
        }

        var pulseBrightness = pulse.PeakBrightness;
        if (elapsedMs > pulse.HoldMs)
        {
            var progress = Math.Clamp((elapsedMs - pulse.HoldMs) / Math.Max(1, pulse.FadeMs), 0, 1);
            pulseBrightness = (int)Math.Round(pulse.PeakBrightness - (pulse.PeakBrightness - currentBrightness) * progress);
        }

        return Math.Max(currentBrightness, pulseBrightness);
    }

    private KeyboardSettings BuildRuntimeSettings(KeyboardSettings settings)
    {
        var runtime = settings.CloneForRuntime();
        var status = ApplyAutomation(runtime);
        status.IdleOverrideActive = ApplyIdleDim(runtime);
        status.UpdatedUtc = DateTimeOffset.UtcNow;
        PublishAutomationStatus(status);
        return runtime.Normalize();
    }

    private void PublishAutomationStatus(AutomationStatus status)
    {
        var signature = string.Join("|", status.ActiveRuleId, status.ForegroundProcessName,
            status.ActiveMusicApplication, string.Join(",", status.ActiveProcessIds), status.TrackTitle,
            status.AlbumColor, status.IdleOverrideActive, status.InvalidReason);
        if (signature == _lastAutomationStatusSignature &&
            DateTimeOffset.UtcNow - _lastAutomationStatusWrite < TimeSpan.FromSeconds(1)) return;
        _lastAutomationStatusSignature = signature;
        _lastAutomationStatusWrite = DateTimeOffset.UtcNow;
        status.Save();
    }

    private bool ShouldRebuildRuntimeSettings(KeyboardSettings current)
    {
        var next = BuildRuntimeSettings(new SettingsStore().Load());
        return next.Enabled != current.Enabled ||
            next.OperatingMode != current.OperatingMode ||
            next.Brightness != current.Brightness ||
            next.OutputBrightnessLimit != current.OutputBrightnessLimit ||
            next.SelectedAudioProcessName != current.SelectedAudioProcessName ||
            next.SelectedAudioExecutablePath != current.SelectedAudioExecutablePath ||
            !next.SelectedAudioProcessIds.SequenceEqual(current.SelectedAudioProcessIds) ||
            !TypingPulseEquals(next.TypingPulse, current.TypingPulse) ||
            !NotificationFlashEquals(next.NotificationFlash, current.NotificationFlash) ||
            next.Effect.Type != current.Effect.Type ||
            next.Effect.Color != current.Effect.Color ||
            next.Effect.Step != current.Effect.Step ||
            next.Effect.IntervalMs != current.Effect.IntervalMs ||
            next.Effect.PeriodMs != current.Effect.PeriodMs ||
            next.Effect.MinimumBrightness != current.Effect.MinimumBrightness ||
            next.Effect.HardBlink != current.Effect.HardBlink ||
            next.Effect.CustomSequenceColorsEnabled != current.Effect.CustomSequenceColorsEnabled ||
            !MusicEquals(next.Effect.Music, current.Effect.Music) ||
            next.Effect.Sequence.Count != current.Effect.Sequence.Count ||
            next.Effect.Sequence.Zip(current.Effect.Sequence).Any(pair =>
                pair.First.Color != pair.Second.Color ||
                pair.First.HoldMs != pair.Second.HoldMs ||
                pair.First.TransitionMs != pair.Second.TransitionMs ||
                pair.First.Breathing != pair.Second.Breathing);
    }

    private static bool NeedsRuntimePolling(KeyboardSettings settings)
    {
        // 音乐页的播放器 PID 绑定依赖实时会话列表，自动化关闭时也必须持续刷新。
        return true;
    }

    private static bool NotificationFlashEquals(NotificationFlashSettings left, NotificationFlashSettings right)
    {
        return left.Enabled == right.Enabled &&
            left.Color == right.Color &&
            left.Pulses == right.Pulses &&
            left.PulseMs == right.PulseMs &&
            left.CooldownSeconds == right.CooldownSeconds;
    }

    private static bool MusicEquals(MusicSettings left, MusicSettings right)
    {
        return left.LevelColorEnabled == right.LevelColorEnabled &&
            left.PresetName == right.PresetName &&
            left.ResponseMode == right.ResponseMode &&
            left.LowColor == right.LowColor &&
            left.HighColor == right.HighColor &&
            left.Colors.Count == right.Colors.Count &&
            left.Colors.SequenceEqual(right.Colors, StringComparer.OrdinalIgnoreCase) &&
            Math.Abs(left.Sensitivity - right.Sensitivity) < 0.001 &&
            left.AttackMs == right.AttackMs &&
            left.ReleaseMs == right.ReleaseMs &&
            left.BaseBrightness == right.BaseBrightness &&
            left.PeakBrightness == right.PeakBrightness &&
            left.IntervalMs == right.IntervalMs &&
            Math.Abs(left.NoiseGate - right.NoiseGate) < 0.001 &&
            Math.Abs(left.BeatThreshold - right.BeatThreshold) < 0.001 &&
            left.PeakHoldMs == right.PeakHoldMs &&
            left.FollowSystemVolume == right.FollowSystemVolume &&
            left.EqEnabled == right.EqEnabled &&
            left.EqLowHz == right.EqLowHz &&
            left.EqHighHz == right.EqHighHz &&
            PlayerBindingEquals(left.PlayerBinding, right.PlayerBinding) &&
            left.CustomPresets.Count == right.CustomPresets.Count &&
            left.CustomPresets.Zip(right.CustomPresets).All(pair => MusicPresetEquals(pair.First, pair.Second));
    }

    private static bool PlayerBindingEquals(MusicPlayerBinding left, MusicPlayerBinding right) =>
        left.Enabled == right.Enabled &&
        left.ProcessName == right.ProcessName &&
        left.ExecutablePath == right.ExecutablePath &&
        left.IncludeChildProcesses == right.IncludeChildProcesses &&
        left.MediaSessionId == right.MediaSessionId &&
        left.ColorSource == right.ColorSource;

    private static bool MusicPresetEquals(MusicPreset left, MusicPreset right)
    {
        return left.Name == right.Name &&
            left.ResponseMode == right.ResponseMode &&
            left.LowColor == right.LowColor &&
            left.HighColor == right.HighColor &&
            left.Colors.Count == right.Colors.Count &&
            left.Colors.SequenceEqual(right.Colors, StringComparer.OrdinalIgnoreCase) &&
            Math.Abs(left.Sensitivity - right.Sensitivity) < 0.001 &&
            left.AttackMs == right.AttackMs &&
            left.ReleaseMs == right.ReleaseMs &&
            left.BaseBrightness == right.BaseBrightness &&
            left.PeakBrightness == right.PeakBrightness &&
            left.IntervalMs == right.IntervalMs &&
            Math.Abs(left.NoiseGate - right.NoiseGate) < 0.001 &&
            Math.Abs(left.BeatThreshold - right.BeatThreshold) < 0.001 &&
            left.PeakHoldMs == right.PeakHoldMs &&
            left.FollowSystemVolume == right.FollowSystemVolume &&
            left.EqEnabled == right.EqEnabled &&
            left.EqLowHz == right.EqLowHz &&
            left.EqHighHz == right.EqHighHz;
    }

    private async Task FlashStartupAsync(CancellationToken stoppingToken)
    {
        try
        {
            for (var i = 0; i < 2; i++)
            {
                _device.SetColor(new RgbColor(255, 255, 255));
                await Task.Delay(120, stoppingToken);
                _device.SetColor(RgbColor.Black);
                await Task.Delay(120, stoppingToken);
            }
        }
        catch (Exception ex) when (ex is DllNotFoundException or EntryPointNotFoundException or SEHException)
        {
            _logger.LogWarning(ex, "Startup flash could not be sent to the keyboard.");
        }
    }

    private AutomationStatus ApplyAutomation(KeyboardSettings settings)
    {
        var manualMusicMode = settings.OperatingMode == OperatingMode.Music;
        var foreground = ForegroundAppState.Load();
        var foregroundAvailable = foreground is not null &&
            DateTimeOffset.UtcNow - foreground.UpdatedUtc <= TimeSpan.FromSeconds(10);
        var audioApplications = GetAudioApplications(foreground?.ProcessName ?? "");
        var audioStates = audioApplications.Select(app => new AudioApplicationState(
            app.ProcessName, app.ExecutablePath, app.ProcessIds, app.PeakLevel, app.IsPlaying, app.IsForeground)).ToList();
        AddProcessTreeStates(settings.Automation.MusicApplications, audioStates);
        AddPlayerBindingState(settings.Effect.Music.PlayerBinding, audioStates);
        var selection = AutomationResolver.Resolve(
            settings.Automation, DateTime.Now, foregroundAvailable ? foreground?.ProcessName ?? "" : "", audioStates,
            rule => ValidateMusicRule(settings, rule),
            action => ValidateSceneAction(settings, action));

        var status = new AutomationStatus
        {
            ForegroundProcessName = foreground?.ProcessName ?? "",
            ForegroundAvailable = foregroundAvailable,
            AudioApplications = audioApplications.ToList()
        };
        status.InvalidReason = selection.InvalidReason ?? "";

        if (selection.Music is not null && selection.Audio is not null)
        {
            var error = ValidateMusicRule(settings, selection.Music);
            if (error is null)
            {
                ApplyMusicRule(settings, selection.Music);
                settings.SelectedAudioProcessIds = selection.Audio.ProcessIds.ToList();
                var media = ApplyMediaColors(settings, selection.Music);
                status.ActiveRuleId = selection.Music.Id;
                status.ActiveRuleName = selection.Music.Name;
                status.ActiveMusicApplication = selection.Music.ProcessName;
                status.ActiveProcessIds = selection.Audio.ProcessIds.ToList();
                status.TargetDescription = DescribeMusicRule(settings, selection.Music);
                status.AudioCaptureMode = settings.Effect.Music.EqEnabled
                    ? "程序电平 + 系统混音频段分析"
                    : "程序音频会话电平";
                if (media is not null)
                {
                    status.TrackTitle = media.Title;
                    status.TrackArtist = media.Artist;
                    status.AlbumColor = media.DominantColor;
                }
            }
            else status.InvalidReason = $"{selection.Music.Name}：{error}";
        }
        else if (selection.Lighting is not null)
        {
            var error = ValidateSceneAction(settings, selection.Lighting.Action);
            if (error is null)
            {
                ApplySceneAction(settings, selection.Lighting.Action);
                status.ActiveRuleId = selection.Lighting.Id;
                status.ActiveRuleName = selection.Lighting.Name;
                status.TargetDescription = DescribeSceneAction(settings, selection.Lighting.Action);
            }
            else status.InvalidReason = $"{selection.Lighting.Name}：{error}";
        }
        else if (selection.Schedule is not null)
        {
            var error = ValidateSceneAction(settings, selection.Schedule.Action);
            if (error is null)
            {
                ApplySceneAction(settings, selection.Schedule.Action);
                status.ActiveRuleId = selection.Schedule.Id;
                status.ActiveRuleName = selection.Schedule.Name;
                status.TargetDescription = DescribeSceneAction(settings, selection.Schedule.Action);
            }
            else status.InvalidReason = $"{selection.Schedule.Name}：{error}";
        }
        else if (manualMusicMode && settings.Effect.Music.PlayerBinding.Enabled)
        {
            ApplyPlayerBinding(settings, audioStates, status);
        }

        if (selection.Music is not null)
        {
            ApplyBrightnessLimit(settings, selection.Music.BrightnessLimit);
            ApplyEventPolicy(settings, selection.Music.TypingPolicy, selection.Music.NotificationPolicy);
        }
        if (selection.Lighting is not null)
        {
            ApplyBrightnessLimit(settings, selection.Lighting.Action.BrightnessLimit);
            ApplyEventPolicy(settings, selection.Lighting.TypingPolicy, selection.Lighting.NotificationPolicy);
        }
        return status;
    }

    private static TimeSpan RuntimePollInterval(KeyboardSettings settings) =>
        (settings.Automation.Enabled && settings.Automation.MusicApplications.Count > 0) ||
        settings.Effect.Music.PlayerBinding.Enabled
            ? TimeSpan.FromMilliseconds(100)
            : TimeSpan.FromSeconds(1);

    private static bool TypingPulseEquals(TypingPulseSettings left, TypingPulseSettings right) =>
        left.Enabled == right.Enabled &&
        left.BaseBrightness == right.BaseBrightness &&
        left.PeakBrightness == right.PeakBrightness &&
        left.HoldMs == right.HoldMs &&
        left.FadeMs == right.FadeMs;

    private static string? ValidateMusicRule(KeyboardSettings settings, MusicApplicationRule rule) =>
        MusicSettings.BuiltInPresets.Any(item => item.Id == rule.MusicPresetId) ||
        settings.Effect.Music.CustomPresets.Any(item => item.Id == rule.MusicPresetId)
            ? null
            : "引用的音乐预设不存在";

    private static void ApplyMusicRule(KeyboardSettings settings, MusicApplicationRule rule)
    {
        settings.Enabled = true;
        settings.OperatingMode = OperatingMode.Music;
        var preset = MusicSettings.BuiltInPresets.Concat(settings.Effect.Music.CustomPresets)
            .First(item => item.Id == rule.MusicPresetId);
        settings.Effect.Music.ApplyPreset(preset);
        settings.SelectedAudioProcessName = rule.ProcessName;
        settings.SelectedAudioExecutablePath = rule.ExecutablePath;
        ApplyBrightnessLimit(settings, rule.BrightnessLimit);
    }

    private static string DescribeMusicRule(KeyboardSettings settings, MusicApplicationRule rule) =>
        "音乐：" + MusicSettings.BuiltInPresets.Concat(settings.Effect.Music.CustomPresets)
            .First(item => item.Id == rule.MusicPresetId).Name;

    private static MediaSessionState? ApplyMediaColors(KeyboardSettings settings, MusicApplicationRule rule)
    {
        if (rule.ColorSource == MusicColorSource.Preset) return null;
        var state = MediaPlaybackState.Load();
        if (state is null || DateTimeOffset.UtcNow - state.UpdatedUtc > TimeSpan.FromSeconds(10)) return null;
        var media = state.Find(rule);
        if (media is null) return null;
        IEnumerable<string> colors = rule.ColorSource == MusicColorSource.AlbumDominant
            ? new[] { media.DominantColor }
            : media.Palette;
        var normalized = colors.Where(color => !string.IsNullOrWhiteSpace(color)).ToList();
        if (normalized.Count == 0) return null;
        settings.Effect.Music.Colors = normalized;
        settings.Effect.Music.LowColor = normalized[0];
        settings.Effect.Music.HighColor = normalized[^1];
        return media;
    }

    private static MediaSessionState? ApplyMediaColors(KeyboardSettings settings, MusicPlayerBinding binding)
    {
        if (binding.ColorSource == MusicColorSource.Preset) return null;
        var state = MediaPlaybackState.Load();
        if (state is null || DateTimeOffset.UtcNow - state.UpdatedUtc > TimeSpan.FromSeconds(10)) return null;
        var media = state.Find(binding);
        if (media is null) return null;
        IEnumerable<string> colors = binding.ColorSource == MusicColorSource.AlbumDominant
            ? new[] { media.DominantColor }
            : media.Palette;
        var normalized = colors.Where(color => !string.IsNullOrWhiteSpace(color)).ToList();
        if (normalized.Count == 0) return null;
        settings.Effect.Music.Colors = normalized;
        settings.Effect.Music.LowColor = normalized[0];
        settings.Effect.Music.HighColor = normalized[^1];
        return media;
    }

    private static void ApplyPlayerBinding(
        KeyboardSettings settings,
        IReadOnlyList<AudioApplicationState> audioStates,
        AutomationStatus status)
    {
        var binding = settings.Effect.Music.PlayerBinding;
        var audio = audioStates.FirstOrDefault(state =>
            string.Equals(state.ProcessName, binding.ProcessName, StringComparison.OrdinalIgnoreCase) &&
            (string.IsNullOrWhiteSpace(binding.ExecutablePath) ||
             string.Equals(state.ExecutablePath, binding.ExecutablePath, StringComparison.OrdinalIgnoreCase)));
        settings.SelectedAudioProcessName = binding.ProcessName;
        settings.SelectedAudioExecutablePath = binding.ExecutablePath;
        settings.SelectedAudioProcessIds = audio?.ProcessIds.ToList() ?? [];
        var media = ApplyMediaColors(settings, binding);
        status.ActiveRuleName = "音乐页播放器绑定";
        status.ActiveMusicApplication = binding.ProcessName;
        status.ActiveProcessIds = settings.SelectedAudioProcessIds;
        status.TargetDescription = binding.ColorSource == MusicColorSource.Preset ? "音乐：预设颜色" : "音乐：歌曲封面颜色";
        status.AudioCaptureMode = audio is null ? "等待播放器音频会话" : "程序音频会话电平";
        if (audio is null) status.InvalidReason = "已绑定播放器未检测到音频会话";
        if (media is not null)
        {
            status.TrackTitle = media.Title;
            status.TrackArtist = media.Artist;
            status.AlbumColor = media.DominantColor;
        }
    }

    private static void ApplyBrightnessLimit(KeyboardSettings settings, int? limit)
    {
        if (!limit.HasValue) return;
        settings.OutputBrightnessLimit = Math.Min(settings.OutputBrightnessLimit, limit.Value);
        if (settings.OperatingMode == OperatingMode.Lighting)
            settings.Brightness = Math.Min(settings.Brightness, limit.Value);
    }

    private static void ApplyEventPolicy(KeyboardSettings settings, EventPolicy typing, EventPolicy notification)
    {
        if (typing != EventPolicy.Inherit) settings.TypingPulse.Enabled = typing == EventPolicy.Enabled;
        if (notification != EventPolicy.Inherit) settings.NotificationFlash.Enabled = notification == EventPolicy.Enabled;
    }

    private float GetSelectedApplicationPeak(string processName, string executablePath, IReadOnlyCollection<int> processIds)
    {
        if (string.IsNullOrWhiteSpace(processName)) return _audioLevelMeter.GetPeakLevel();
        var foreground = ForegroundAppState.Load()?.ProcessName ?? "";
        var matches = GetAudioApplications(foreground)
            .Where(item => (string.Equals(item.ProcessName, processName, StringComparison.OrdinalIgnoreCase) &&
                (string.IsNullOrWhiteSpace(executablePath) || string.Equals(item.ExecutablePath, executablePath, StringComparison.OrdinalIgnoreCase)) ||
                item.ProcessIds.Any(processIds.Contains)))
            .ToList();
        return matches.Count == 0 ? 0f : matches.Max(item => item.PeakLevel);
    }

    private IReadOnlyList<AudioApplicationStatus> GetAudioApplications(string foregroundProcessName)
    {
        var now = DateTimeOffset.UtcNow;
        if (now - _lastAudioApplicationsRead >= TimeSpan.FromMilliseconds(50))
        {
            _lastAudioApplicationsRead = now;
            _lastAudioApplicationsState = AudioApplicationsState.Load() ?? _lastAudioApplicationsState;
        }
        var state = _lastAudioApplicationsState;
        if (state is not null && DateTimeOffset.UtcNow - state.UpdatedUtc <= TimeSpan.FromSeconds(2))
            return state.Applications;

        // 仅作为非服务宿主/测试环境的兼容回退。Windows 服务 Session 0 看不到用户会话音频。
        return _audioApplicationMonitor.Poll(foregroundProcessName, DateTimeOffset.UtcNow);
    }

    private static void AddPlayerBindingState(MusicPlayerBinding binding, List<AudioApplicationState> states)
    {
        if (!binding.Enabled || !binding.IncludeChildProcesses) return;
        var roots = ProcessTree.FindRoots(binding.ProcessName, binding.ExecutablePath);
        var tree = ProcessTree.Expand(roots);
        var children = states.Where(state => state.ProcessIds.Any(tree.Contains)).ToList();
        if (children.Count == 0) return;
        states.RemoveAll(state => string.Equals(state.ProcessName, binding.ProcessName, StringComparison.OrdinalIgnoreCase));
        states.Add(new AudioApplicationState(
            binding.ProcessName,
            binding.ExecutablePath,
            children.SelectMany(item => item.ProcessIds).Distinct().ToList(),
            children.Max(item => item.PeakLevel),
            children.Any(item => item.IsPlaying),
            children.Any(item => item.IsForeground)));
    }

    private static void AddProcessTreeStates(
        IEnumerable<MusicApplicationRule> rules,
        List<AudioApplicationState> states)
    {
        foreach (var rule in rules.Where(item => item.Enabled && item.IncludeChildProcesses))
        {
            var roots = ProcessTree.FindRoots(rule.ProcessName, rule.ExecutablePath);
            var tree = ProcessTree.Expand(roots);
            var children = states.Where(state => state.ProcessIds.Any(tree.Contains)).ToList();
            if (children.Count == 0) continue;
            states.RemoveAll(state => string.Equals(state.ProcessName, rule.ProcessName, StringComparison.OrdinalIgnoreCase));
            states.Add(new AudioApplicationState(
                rule.ProcessName,
                rule.ExecutablePath,
                children.SelectMany(item => item.ProcessIds).Distinct().ToList(),
                children.Max(item => item.PeakLevel),
                children.Any(item => item.IsPlaying),
                children.Any(item => item.IsForeground)));
        }
    }

    private static string? ValidateSceneAction(KeyboardSettings settings, SceneAction action)
    {
        return action.Target switch
        {
            SceneTargetKind.Off => null,
            SceneTargetKind.LightingPreset when
                action.PresetId == EffectPresetSettings.BuiltInId(action.LightingEffectType) ||
                settings.EffectPresets.ForType(action.LightingEffectType).Any(item => item.Id == action.PresetId) => null,
            SceneTargetKind.MusicPreset when
                MusicSettings.BuiltInPresets.Any(item => item.Id == action.PresetId) ||
                settings.Effect.Music.CustomPresets.Any(item => item.Id == action.PresetId) => null,
            _ => "引用的预设不存在"
        };
    }

    private static void ApplySceneAction(KeyboardSettings settings, SceneAction action)
    {
        settings.Enabled = true;
        if (action.Target == SceneTargetKind.Off)
        {
            settings.OperatingMode = OperatingMode.Lighting;
            settings.Effect.Type = EffectType.Off;
        }
        else if (action.Target == SceneTargetKind.LightingPreset)
        {
            settings.OperatingMode = OperatingMode.Lighting;
            var preset = settings.EffectPresets.ForType(action.LightingEffectType)
                .FirstOrDefault(item => item.Id == action.PresetId);
            settings.Effect = preset is null
                ? EffectPresetSettings.CreateSoftwareDefault(action.LightingEffectType)
                : KeyboardSettings.CloneEffect(preset.Effect);
        }
        else
        {
            settings.OperatingMode = OperatingMode.Music;
            var preset = MusicSettings.BuiltInPresets
                .Concat(settings.Effect.Music.CustomPresets)
                .First(item => item.Id == action.PresetId);
            settings.Effect.Music.ApplyPreset(preset);
        }

        if (action.BrightnessLimit.HasValue)
        {
            settings.OutputBrightnessLimit = action.BrightnessLimit.Value;
            if (settings.OperatingMode == OperatingMode.Lighting)
            {
                settings.Brightness = action.BrightnessLimit.Value;
            }
        }
    }

    private static string DescribeSceneAction(KeyboardSettings settings, SceneAction action)
    {
        if (action.Target == SceneTargetKind.Off) return "关闭灯光";
        if (action.Target == SceneTargetKind.MusicPreset)
        {
            return "音乐：" + MusicSettings.BuiltInPresets
                .Concat(settings.Effect.Music.CustomPresets)
                .First(item => item.Id == action.PresetId).Name;
        }
        if (action.PresetId == EffectPresetSettings.BuiltInId(action.LightingEffectType))
            return $"灯效：{action.LightingEffectType} 软件默认";
        return "灯效：" + settings.EffectPresets.ForType(action.LightingEffectType)
            .First(item => item.Id == action.PresetId).Name;
    }

    private static bool ApplyIdleDim(KeyboardSettings settings)
    {
        if (!settings.IdleDim.Enabled)
        {
            return false;
        }

        if (WindowsIdleTime.GetIdleTime().TotalSeconds < settings.IdleDim.AfterSeconds)
        {
            return false;
        }

        if (settings.IdleDim.TurnOff)
        {
            settings.OperatingMode = OperatingMode.Lighting;
            settings.Effect.Type = EffectType.Off;
            settings.OutputBrightnessLimit = 0;
            return true;
        }

        settings.OutputBrightnessLimit = Math.Min(settings.OutputBrightnessLimit, settings.IdleDim.Brightness);
        settings.Brightness = Math.Min(settings.Brightness, settings.IdleDim.Brightness);
        return true;
    }

    private void EnsureConfigWatcher()
    {
        Directory.CreateDirectory(AppPaths.ProgramDataDirectory);
        _watcher = new FileSystemWatcher(AppPaths.ProgramDataDirectory)
        {
            NotifyFilter = NotifyFilters.LastWrite | NotifyFilters.CreationTime | NotifyFilters.Size | NotifyFilters.FileName,
            EnableRaisingEvents = true
        };

        _watcher.Changed += (_, args) => MarkSettingsChanged(args.Name);
        _watcher.Created += (_, args) => MarkSettingsChanged(args.Name);
        _watcher.Deleted += (_, args) => MarkSettingsChanged(args.Name);
        _watcher.Renamed += (_, args) => MarkSettingsChanged(args.Name);
    }

    private void MarkSettingsChanged(string? fileName)
    {
        if (string.Equals(fileName, AppPaths.SettingsFileName, StringComparison.OrdinalIgnoreCase) ||
            string.Equals(fileName, AppPaths.NotificationFlashStateFileName, StringComparison.OrdinalIgnoreCase))
        {
            _settingsChanged = true;
        }
    }

    private async Task WaitForSettingsChangeAsync(int pollIntervalMs, CancellationToken stoppingToken)
    {
        while (!stoppingToken.IsCancellationRequested && !_settingsChanged)
        {
            await Task.Delay(pollIntervalMs, stoppingToken);
        }
    }

    private void OnAudioSourceChanged(object? sender, AudioSourceChangedEventArgs e)
    {
        // 这个回调可能在 NAudio COM 回调线程里触发；文件 IO 必须脱离它
        var snapshot = new AudioSourceStatusInfo
        {
            Status = e.Status,
            DeviceFriendlyName = e.DeviceFriendlyName,
            DeviceId = e.DeviceId,
            UpdatedAt = DateTimeOffset.UtcNow,
        };
        System.Threading.ThreadPool.QueueUserWorkItem(_ =>
        {
            try
            {
                AudioSourceStatusFile.Write(snapshot);
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Failed to write audio source status file");
            }
        });
    }
}
