namespace ColorfulLedKeyboard.Service;

using ColorfulLedKeyboard.Core;
using System.Runtime.InteropServices;

public class Worker : BackgroundService
{
    private readonly SettingsStore _settingsStore = new();
    private readonly DchuKeyboardDevice _device = new();
    private readonly SystemAudioLevelMeter _audioLevelMeter = new();
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
        return base.StopAsync(cancellationToken);
    }

    private async Task RunEffectAsync(KeyboardSettings settings, CancellationToken stoppingToken)
    {
        if (settings.Effect.Type == EffectType.Music)
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

            var color = ApplyTypingPulse(generator.Next(), settings);
            if (color != lastColor)
            {
                _device.SetAllZones(color);
                lastColor = color;
            }

            if (settings.Effect.Type is EffectType.Static or EffectType.Off)
            {
                if (settings.Effect.Type == EffectType.Static && settings.TypingPulse.Enabled)
                {
                    await Task.Delay(40, stoppingToken);
                    continue;
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
        var controller = new MusicBrightnessController();
        var baseColor = ResolveMusicBaseColor(settings);
        var lowColor = RgbColor.FromHex(music.LowColor);
        var highColor = RgbColor.FromHex(music.HighColor);
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

            var level = _audioLevelMeter.GetPeakLevel();
            var envelope = controller.NextEnvelope(music, level);
            var musicBrightness = music.BaseBrightness +
                (music.PeakBrightness - music.BaseBrightness) * Math.Pow(envelope, 0.65);
            var brightness = ApplyTypingPulseBrightness((int)Math.Round(musicBrightness), settings);
            var sourceColor = music.ResponseMode == MusicResponseMode.LevelColor
                ? RgbColor.Lerp(lowColor, highColor, envelope)
                : baseColor;
            var color = sourceColor.Scale(brightness);

            if (color != lastColor)
            {
                _device.SetAllZones(color);
                lastColor = color;
            }

            await Task.Delay(music.IntervalMs, stoppingToken);
        }
    }

    private static RgbColor ResolveMusicBaseColor(KeyboardSettings settings)
    {
        if (settings.Effect.Type is EffectType.Static or EffectType.Breathing or EffectType.Music)
        {
            return RgbColor.FromHex(settings.Effect.Color);
        }

        return RgbColor.FromHex(settings.StaticColor);
    }

    private static RgbColor ApplyTypingPulse(RgbColor color, KeyboardSettings settings)
    {
        var target = ApplyTypingPulseBrightness(settings.Brightness, settings);
        if (target <= settings.Brightness)
        {
            return color;
        }

        return ScaleBrightnessRatio(color, settings.Brightness, target);
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
            pulseBrightness = (int)Math.Round(pulse.PeakBrightness - (pulse.PeakBrightness - pulse.BaseBrightness) * progress);
        }

        return Math.Max(currentBrightness, pulseBrightness);
    }

    private static RgbColor ScaleBrightnessRatio(RgbColor color, int fromBrightness, int toBrightness)
    {
        if (fromBrightness <= 0)
        {
            return color;
        }

        var ratio = Math.Clamp(toBrightness, 0, 100) / (double)Math.Clamp(fromBrightness, 1, 100);
        return new RgbColor(
            (byte)Math.Clamp(Math.Round(color.R * ratio), 0, 255),
            (byte)Math.Clamp(Math.Round(color.G * ratio), 0, 255),
            (byte)Math.Clamp(Math.Round(color.B * ratio), 0, 255));
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
            next.Brightness != current.Brightness ||
            next.Effect.Type != current.Effect.Type ||
            next.Effect.Color != current.Effect.Color ||
            next.Effect.Step != current.Effect.Step ||
            next.Effect.IntervalMs != current.Effect.IntervalMs ||
            next.Effect.PeriodMs != current.Effect.PeriodMs ||
            next.Effect.MinimumBrightness != current.Effect.MinimumBrightness ||
            next.Effect.HardBlink != current.Effect.HardBlink ||
            !MusicEquals(next.Effect.Music, current.Effect.Music) ||
            next.Effect.Sequence.Count != current.Effect.Sequence.Count ||
            next.Effect.Sequence.Zip(current.Effect.Sequence).Any(pair =>
                pair.First.Color != pair.Second.Color ||
                pair.First.HoldMs != pair.Second.HoldMs ||
                pair.First.TransitionMs != pair.Second.TransitionMs ||
                pair.First.Breathing != pair.Second.Breathing);
    }

    private static bool MusicEquals(MusicSettings left, MusicSettings right)
    {
        return left.LevelColorEnabled == right.LevelColorEnabled &&
            left.PresetName == right.PresetName &&
            left.ResponseMode == right.ResponseMode &&
            left.LowColor == right.LowColor &&
            left.HighColor == right.HighColor &&
            Math.Abs(left.Sensitivity - right.Sensitivity) < 0.001 &&
            left.AttackMs == right.AttackMs &&
            left.ReleaseMs == right.ReleaseMs &&
            left.BaseBrightness == right.BaseBrightness &&
            left.PeakBrightness == right.PeakBrightness &&
            left.IntervalMs == right.IntervalMs &&
            Math.Abs(left.NoiseGate - right.NoiseGate) < 0.001 &&
            Math.Abs(left.BeatThreshold - right.BeatThreshold) < 0.001 &&
            left.PeakHoldMs == right.PeakHoldMs &&
            left.CustomPresets.Count == right.CustomPresets.Count &&
            left.CustomPresets.Zip(right.CustomPresets).All(pair => MusicPresetEquals(pair.First, pair.Second));
    }

    private static bool MusicPresetEquals(MusicPreset left, MusicPreset right)
    {
        return left.Name == right.Name &&
            left.ResponseMode == right.ResponseMode &&
            left.LowColor == right.LowColor &&
            left.HighColor == right.HighColor &&
            Math.Abs(left.Sensitivity - right.Sensitivity) < 0.001 &&
            left.AttackMs == right.AttackMs &&
            left.ReleaseMs == right.ReleaseMs &&
            left.BaseBrightness == right.BaseBrightness &&
            left.PeakBrightness == right.PeakBrightness &&
            left.IntervalMs == right.IntervalMs &&
            Math.Abs(left.NoiseGate - right.NoiseGate) < 0.001 &&
            Math.Abs(left.BeatThreshold - right.BeatThreshold) < 0.001 &&
            left.PeakHoldMs == right.PeakHoldMs;
    }

    private async Task FlashStartupAsync(CancellationToken stoppingToken)
    {
        try
        {
            for (var i = 0; i < 2; i++)
            {
                _device.SetAllZones(new RgbColor(255, 255, 255));
                await Task.Delay(120, stoppingToken);
                _device.SetAllZones(RgbColor.Black);
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
        settings.Brightness = rule.Brightness;
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
        settings.Brightness = rule.Brightness;
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
        if (string.Equals(fileName, AppPaths.SettingsFileName, StringComparison.OrdinalIgnoreCase))
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
