namespace ColorfulLedKeyboard.Core;

public sealed class LightingFrameGenerator
{
    private readonly KeyboardSettings _settings;
    private readonly DateTimeOffset _startedAt = DateTimeOffset.UtcNow;

    public LightingFrameGenerator(KeyboardSettings settings)
    {
        _settings = settings.Normalize();
    }

    public int IntervalMs => Math.Clamp(_settings.Effect.IntervalMs, 20, 500);

    public RgbColor Next()
    {
        var elapsedMs = (DateTimeOffset.UtcNow - _startedAt).TotalMilliseconds;
        var color = _settings.Effect.Type switch
        {
            EffectType.Off => RgbColor.Black,
            EffectType.Static => RgbColor.FromHex(_settings.Effect.Color),
            EffectType.Breathing => Breathing(RgbColor.FromHex(_settings.Effect.Color), elapsedMs),
            EffectType.Sequence => Sequence(elapsedMs),
            EffectType.Rainbow => Rainbow(elapsedMs),
            _ => RgbColor.Black
        };

        return color.Scale(_settings.Brightness);
    }

    private RgbColor Rainbow(double elapsedMs)
    {
        var degreesPerSecond = Math.Clamp(_settings.Effect.Step, 1, 20) * 9;
        var absoluteSeconds = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds() / 1000d;
        var hue = absoluteSeconds * degreesPerSecond;
        return RgbColor.FromHsv(hue, 1, 1);
    }

    private RgbColor Breathing(RgbColor color, double elapsedMs)
    {
        if (_settings.Effect.HardBlink)
        {
            var on = elapsedMs % _settings.Effect.PeriodMs < _settings.Effect.PeriodMs / 2d;
            return on ? color : RgbColor.Black;
        }

        var phase = elapsedMs % _settings.Effect.PeriodMs / _settings.Effect.PeriodMs;
        var wave = (1 - Math.Cos(phase * Math.PI * 2)) / 2d;
        var min = _settings.Effect.MinimumBrightness / 100d;
        var factor = min + (1 - min) * wave;
        return color.Scale((int)Math.Round(factor * 100));
    }

    private RgbColor Sequence(double elapsedMs)
    {
        var sequence = _settings.Effect.Sequence;
        if (sequence.Count == 0)
        {
            return RgbColor.Black;
        }

        var totalMs = sequence.Sum(item => Math.Max(1, item.HoldMs + item.TransitionMs));
        var cursor = elapsedMs % totalMs;

        for (var i = 0; i < sequence.Count; i++)
        {
            var current = sequence[i];
            var next = sequence[(i + 1) % sequence.Count];
            var segmentMs = Math.Max(1, current.HoldMs + current.TransitionMs);

            if (cursor > segmentMs)
            {
                cursor -= segmentMs;
                continue;
            }

            var currentColor = RgbColor.FromHex(current.Color);
            if (current.Breathing)
            {
                var local = cursor % segmentMs;
                return BreathingColor(currentColor, local, segmentMs);
            }

            if (cursor <= current.HoldMs || current.TransitionMs == 0)
            {
                return currentColor;
            }

            var amount = (cursor - current.HoldMs) / current.TransitionMs;
            return RgbColor.Lerp(currentColor, RgbColor.FromHex(next.Color), amount);
        }

        return RgbColor.FromHex(sequence[0].Color);
    }

    private static RgbColor BreathingColor(RgbColor color, double elapsedMs, double periodMs)
    {
        var phase = elapsedMs % periodMs / periodMs;
        var wave = (1 - Math.Cos(phase * Math.PI * 2)) / 2d;
        return color.Scale((int)Math.Round(wave * 100));
    }
}
