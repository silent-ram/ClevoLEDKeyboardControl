namespace ColorfulLedKeyboard.Service;

using ColorfulLedKeyboard.Core;
using System.Runtime.InteropServices;

public class Worker : BackgroundService
{
    private readonly SettingsStore _settingsStore = new();
    private readonly DchuKeyboardDevice _device = new();
    private readonly SystemAudioLevelMeter _audioLevelMeter = new();
    private readonly AudioBandLevelMeter _audioBandLevelMeter = new();
    private readonly ILogger<Worker> _logger;
    private FileSystemWatcher? _watcher;
    private volatile bool _settingsChanged = true;

    public Worker(ILogger<Worker> logger)
    {
        _logger = logger;
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

    public override Task StopAsync(CancellationToken cancellationToken)
    {
        _watcher?.Dispose();
        _audioLevelMeter.Dispose();
        _audioBandLevelMeter.Dispose();
        return base.StopAsync(cancellationToken);
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
        var nextRuntimeRefresh = DateTimeOffset.UtcNow.AddSeconds(1);
        RgbColor? lastColor = null;

        while (!stoppingToken.IsCancellationRequested && !_settingsChanged)
        {
            if (DateTimeOffset.UtcNow >= nextRuntimeRefresh)
            {
                nextRuntimeRefresh = DateTimeOffset.UtcNow.AddSeconds(1);
                if (ShouldRebuildRuntimeSettings(settings))
                {
                    _settingsChanged = true;
                    return;
                }
            }

            var brightness = ApplyTypingPulseBrightness(settings.Brightness, settings);
            var color = ApplyNotificationFlash(generator.Next(brightness), settings);
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
                    await Task.Delay(1000, stoppingToken);
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
        var musicColors = music.Colors.Select(RgbColor.FromHex).ToList();
        var nextRuntimeRefresh = DateTimeOffset.UtcNow.AddSeconds(1);
        RgbColor? lastColor = null;

        while (!stoppingToken.IsCancellationRequested && !_settingsChanged)
        {
            if (DateTimeOffset.UtcNow >= nextRuntimeRefresh)
            {
                nextRuntimeRefresh = DateTimeOffset.UtcNow.AddSeconds(1);
                if (ShouldRebuildRuntimeSettings(settings))
                {
                    _settingsChanged = true;
                    return;
                }
            }

            var level = music.EqEnabled
                ? Math.Max(_audioBandLevelMeter.GetAdaptiveBeatLevel(music), _audioLevelMeter.GetPeakLevel() * 0.12f)
                : _audioLevelMeter.GetPeakLevel();
            var systemVolume = _audioLevelMeter.GetMasterVolumeScalar();
            var frame = controller.Next(music, level, systemVolume, musicColors.Count);
            var envelope = frame.Envelope;
            var musicBrightness = music.BaseBrightness +
                (music.PeakBrightness - music.BaseBrightness) * Math.Pow(envelope, 0.55);
            var brightness = (int)Math.Clamp(Math.Round(musicBrightness), music.BaseBrightness, music.PeakBrightness);
            var sourceColor = musicColors[frame.ColorIndex % musicColors.Count];
            var color = ApplyNotificationFlash(sourceColor.Scale(brightness), settings);

            if (color != lastColor)
            {
                _device.SetColor(color);
                lastColor = color;
            }

            await Task.Delay(music.IntervalMs, stoppingToken);
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

    private static KeyboardSettings BuildRuntimeSettings(KeyboardSettings settings)
    {
        var runtime = settings.CloneForRuntime();
        ApplySchedule(runtime);
        ApplyAppProfiles(runtime);
        ApplyIdleDim(runtime);
        return runtime.Normalize();
    }

    private static bool ShouldRebuildRuntimeSettings(KeyboardSettings current)
    {
        var next = BuildRuntimeSettings(new SettingsStore().Load());
        return next.Enabled != current.Enabled ||
            next.OperatingMode != current.OperatingMode ||
            next.Brightness != current.Brightness ||
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
        return settings.Schedule.Enabled ||
            settings.IdleDim.Enabled ||
            settings.AppProfiles.Enabled;
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
            left.CustomPresets.Count == right.CustomPresets.Count &&
            left.CustomPresets.Zip(right.CustomPresets).All(pair => MusicPresetEquals(pair.First, pair.Second));
    }

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

    private static void ApplySchedule(KeyboardSettings settings)
    {
        if (!settings.Schedule.Enabled)
        {
            return;
        }

        var now = TimeOnly.FromDateTime(DateTime.Now);
        var rule = settings.Schedule.Rules.FirstOrDefault(item => item.Enabled && item.IsActive(now));
        if (rule is null)
        {
            return;
        }

        settings.Enabled = true;
        settings.Effect = rule.Effect;
    }

    private static void ApplyAppProfiles(KeyboardSettings settings)
    {
        if (!settings.AppProfiles.Enabled || settings.AppProfiles.Rules.Count == 0)
        {
            return;
        }

        var foreground = ForegroundAppState.Load();
        if (foreground is null || DateTimeOffset.UtcNow - foreground.UpdatedUtc > TimeSpan.FromSeconds(10))
        {
            return;
        }

        var processName = foreground.ProcessName;
        if (string.IsNullOrWhiteSpace(processName))
        {
            return;
        }

        var rule = settings.AppProfiles.Rules.FirstOrDefault(item => item.Matches(processName));
        if (rule is null)
        {
            return;
        }

        settings.Enabled = true;
        // AppProfile 不再支持 TargetEffect=Music（已从 EffectType 中移除，规则只能切到灯效模式下的 Static/Breathing）。
        // 用户希望"前台某进程时切到音乐"需要在未来的 AppProfile 改进中重新设计。
        settings.Effect = rule.BuildEffect();
    }

    private static void ApplyIdleDim(KeyboardSettings settings)
    {
        if (!settings.IdleDim.Enabled)
        {
            return;
        }

        if (WindowsIdleTime.GetIdleTime().TotalSeconds < settings.IdleDim.AfterSeconds)
        {
            return;
        }

        if (settings.IdleDim.TurnOff)
        {
            settings.Effect.Type = EffectType.Off;
            return;
        }

        settings.Brightness = Math.Min(settings.Brightness, settings.IdleDim.Brightness);
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
}
