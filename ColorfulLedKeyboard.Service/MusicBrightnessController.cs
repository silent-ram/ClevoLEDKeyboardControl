using ColorfulLedKeyboard.Core;

namespace ColorfulLedKeyboard.Service;

internal sealed class MusicBrightnessController
{
    private double _current;
    private double _heldPeak;
    private DateTimeOffset _holdUntil = DateTimeOffset.MinValue;

    public int NextBrightness(MusicSettings settings, double level)
    {
        var envelope = NextEnvelope(settings, level);
        var brightness = settings.BaseBrightness +
            (settings.PeakBrightness - settings.BaseBrightness) * Math.Pow(envelope, 0.65);

        return (int)Math.Clamp(Math.Round(brightness), settings.BaseBrightness, settings.PeakBrightness);
    }

    public double NextEnvelope(MusicSettings settings, double level)
    {
        settings.Normalize();
        level = NormalizeLevel(settings, level);

        var intervalSeconds = settings.IntervalMs / 1000d;
        var timeConstantMs = level > _current ? settings.AttackMs : settings.ReleaseMs;
        var alpha = 1 - Math.Exp(-intervalSeconds / Math.Max(0.001, timeConstantMs / 1000d));
        _current += (level - _current) * alpha;
        return Math.Clamp(_current, 0, 1);
    }

    private double NormalizeLevel(MusicSettings settings, double level)
    {
        level = Math.Clamp(level * settings.Sensitivity, 0, 1);
        if (level < settings.NoiseGate)
        {
            level = 0;
        }
        else
        {
            level = (level - settings.NoiseGate) / Math.Max(0.001, 1 - settings.NoiseGate);
        }

        var now = DateTimeOffset.UtcNow;
        if (level >= _heldPeak + settings.BeatThreshold)
        {
            _heldPeak = level;
            _holdUntil = now.AddMilliseconds(settings.PeakHoldMs);
            return level;
        }

        if (now < _holdUntil)
        {
            return Math.Max(level, _heldPeak);
        }

        _heldPeak = Math.Max(level, _heldPeak * 0.92);
        return level;
    }
}
