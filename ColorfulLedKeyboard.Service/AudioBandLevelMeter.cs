using ColorfulLedKeyboard.Core;
using NAudio.Wave;

namespace ColorfulLedKeyboard.Service;

internal sealed class AudioBandLevelMeter : IDisposable
{
    private static readonly BandDefinition[] AdaptiveBands =
    [
        new(35, 70, 1.25),
        new(70, 140, 1.15),
        new(140, 280, 1.00),
        new(280, 700, 0.95),
        new(700, 2500, 0.85),
        new(2500, 8000, 0.75)
    ];

    private readonly object _sync = new();
    private readonly BandState[] _bandStates = AdaptiveBands.Select(_ => new BandState()).ToArray();
    private readonly AudioSourceProvider _source;
    private WasapiLoopbackCapture? _capture;
    private float[] _samples = [];
    private int _sampleRate = 48000;
    private DateTimeOffset _lastAdaptiveUpdate = DateTimeOffset.MinValue;
    private double _adaptiveNoise;
    private double _rmsAverage = 0.08;
    private double _outputEnvelope;
    private double _heldOutput;
    private DateTimeOffset _holdUntil = DateTimeOffset.MinValue;
    private DateTimeOffset _lastBeatAt = DateTimeOffset.MinValue;
    private double _beatIntervalMs = 260;
    private bool _gateOpen;

    public AudioBandLevelMeter(AudioSourceProvider source)
    {
        _source = source;
        _source.SourceChanged += OnSourceChanged;
    }

    private void OnSourceChanged(object? sender, AudioSourceChangedEventArgs e)
    {
        // ResetCapture 会调用 NAudio 的 StopRecording，必须脱离 COM 回调线程
        // 否则在 IMMNotificationClient 回调链路上同步停 capture 会死锁
        System.Threading.ThreadPool.QueueUserWorkItem(_ => ResetCapture());
    }

    public void PauseCapture()
    {
        ResetCapture();
    }

    public float GetLevel(int lowHz, int highHz)
    {
        EnsureCapture();
        float[] snapshot;
        int sampleRate;
        lock (_sync)
        {
            snapshot = _samples;
            sampleRate = _sampleRate;
        }

        if (snapshot.Length < 256)
        {
            return 0f;
        }

        lowHz = Math.Clamp(lowHz, 20, sampleRate / 2 - 20);
        highHz = Math.Clamp(highHz, lowHz + 10, sampleRate / 2);
        var total = 0d;
        var count = 0;
        for (var hz = lowHz; hz <= highHz; hz += Math.Max(10, (highHz - lowHz) / 8))
        {
            total += Goertzel(snapshot, sampleRate, hz);
            count++;
        }

        if (count == 0)
        {
            return 0f;
        }

        var normalizedEnergy = Math.Sqrt(total / count) / Math.Max(1, snapshot.Length / 2d);
        var level = normalizedEnergy * 1.15;
        return (float)Math.Clamp(level, 0, 1);
    }

    public float GetAdaptiveBeatLevel(MusicSettings settings)
    {
        EnsureCapture();
        float[] snapshot;
        int sampleRate;
        lock (_sync)
        {
            snapshot = _samples;
            sampleRate = _sampleRate;
        }

        if (snapshot.Length < 256)
        {
            return 0f;
        }

        var now = DateTimeOffset.UtcNow;
        var dt = _lastAdaptiveUpdate == DateTimeOffset.MinValue
            ? settings.IntervalMs / 1000d
            : (now - _lastAdaptiveUpdate).TotalSeconds;
        _lastAdaptiveUpdate = now;
        dt = Math.Clamp(dt, 0.005, 0.2);
        var rms = CalculateRms(snapshot);
        _rmsAverage = Smooth(_rmsAverage, rms, dt, rms > _rmsAverage ? 4.0 : 0.9);
        var gain = Math.Clamp(0.10 / Math.Max(0.015, _rmsAverage), 0.55, 2.6);

        var weightedTotal = 0d;
        var weightTotal = 0d;
        var weightedMax = 0d;

        for (var i = 0; i < AdaptiveBands.Length; i++)
        {
            var band = AdaptiveBands[i];
            var state = _bandStates[i];
            var preference = BandPreference(band, settings.EqLowHz, settings.EqHighHz);
            var raw = GetBandLevel(snapshot, sampleRate, band.LowHz, band.HighHz) * band.Gain * gain;
            raw = Math.Clamp(raw, 0, 1);

            state.Short = Smooth(state.Short, raw, dt, raw > state.Short ? 0.006 : 0.038);
            state.Long = Smooth(state.Long, raw, dt, 0.22);

            var noiseTime = raw < state.Noise ? 0.08 : 1.8;
            state.Noise = Smooth(state.Noise, raw, dt, noiseTime);

            var floor = Math.Max(state.Long, state.Noise * 1.35);
            var onset = Math.Max(0, state.Short - floor);
            var normalizedOnset = Math.Clamp(onset * 7.5, 0, 1);
            var signal = Math.Clamp((state.Short - state.Noise) * 5.0, 0, 1);
            var nonSustained = 1 - Math.Clamp((state.Long - state.Noise) * 2.2, 0, 0.85);
            state.TargetWeight = signal * nonSustained * preference;
            state.Weight = Smooth(state.Weight, state.TargetWeight, dt, 0.55);

            weightedTotal += normalizedOnset * state.Weight;
            weightTotal += state.Weight;
            weightedMax = Math.Max(weightedMax, normalizedOnset * Math.Max(0.25, state.Weight));
        }

        var fused = weightTotal > 0.001
            ? weightedTotal / weightTotal
            : weightedMax;
        fused = Math.Max(fused, weightedMax * 0.85);

        var noiseTimeConstant = fused < _adaptiveNoise ? 0.12 : 1.8;
        _adaptiveNoise = Smooth(_adaptiveNoise, fused, dt, noiseTimeConstant);

        var openThreshold = Math.Clamp(_adaptiveNoise + settings.NoiseGate * 0.55 + settings.BeatThreshold * 0.14, 0.015, 0.75);
        var closeThreshold = Math.Max(0.01, openThreshold * 0.68);
        if (!_gateOpen && fused < openThreshold)
        {
            return FollowOutput(0, settings, dt, now);
        }

        if (_gateOpen && fused < closeThreshold)
        {
            _gateOpen = false;
            return FollowOutput(0, settings, dt, now);
        }

        _gateOpen = true;
        var opened = (fused - closeThreshold) / Math.Max(0.001, 1 - closeThreshold);
        var compressed = Math.Pow(Math.Clamp(opened, 0, 1), 0.72);
        return FollowOutput(compressed, settings, dt, now);
    }

    public void Dispose()
    {
        _source.SourceChanged -= OnSourceChanged;
        ResetCapture();
    }

    private void EnsureCapture()
    {
        if (_capture is not null) return;
        if (_source.Status != AudioSourceStatus.Active) return;

        var device = _source.CurrentDevice;
        if (device is null) return;

        try
        {
            _capture = new WasapiLoopbackCapture(device);
            _sampleRate = _capture.WaveFormat.SampleRate;
            _capture.DataAvailable += OnDataAvailable;
            _capture.RecordingStopped += (_, _) => ResetCapture();
            _capture.StartRecording();
        }
        catch
        {
            ResetCapture();
        }
    }

    private void ResetCapture()
    {
        var capture = _capture;
        _capture = null;
        if (capture is null)
        {
            return;
        }

        try
        {
            capture.DataAvailable -= OnDataAvailable;
            capture.StopRecording();
        }
        catch
        {
        }
        finally
        {
            capture.Dispose();
        }
    }

    private void OnDataAvailable(object? sender, WaveInEventArgs args)
    {
        if (sender is not WasapiLoopbackCapture capture)
        {
            return;
        }

        _source.ReportSamples();

        var format = capture.WaveFormat;
        var channels = Math.Max(1, format.Channels);
        var bytesPerSample = format.BitsPerSample / 8;
        if (bytesPerSample <= 0)
        {
            return;
        }

        var frames = args.BytesRecorded / (bytesPerSample * channels);
        var sampleCount = Math.Min(frames, format.SampleRate / 16);
        var samples = new float[sampleCount];
        var sourceFrame = Math.Max(0, frames - sampleCount);
        for (var i = 0; i < sampleCount; i++, sourceFrame++)
        {
            var sum = 0f;
            for (var channel = 0; channel < channels; channel++)
            {
                var offset = ((sourceFrame * channels) + channel) * bytesPerSample;
                sum += ReadSample(args.Buffer, offset, format);
            }

            samples[i] = sum / channels;
        }

        lock (_sync)
        {
            _sampleRate = format.SampleRate;
            _samples = samples;
        }
    }

    private static float ReadSample(byte[] buffer, int offset, WaveFormat format)
    {
        if (offset < 0 || offset >= buffer.Length)
        {
            return 0;
        }

        if (format.Encoding == WaveFormatEncoding.IeeeFloat && format.BitsPerSample == 32 && offset + 4 <= buffer.Length)
        {
            return BitConverter.ToSingle(buffer, offset);
        }

        if (format.BitsPerSample == 16 && offset + 2 <= buffer.Length)
        {
            return BitConverter.ToInt16(buffer, offset) / 32768f;
        }

        if (format.BitsPerSample == 32 && offset + 4 <= buffer.Length)
        {
            return BitConverter.ToInt32(buffer, offset) / 2147483648f;
        }

        return 0;
    }

    private static double GetBandLevel(float[] samples, int sampleRate, int lowHz, int highHz)
    {
        lowHz = Math.Clamp(lowHz, 20, sampleRate / 2 - 20);
        highHz = Math.Clamp(highHz, lowHz + 10, sampleRate / 2);
        var total = 0d;
        var count = 0;
        for (var hz = lowHz; hz <= highHz; hz += Math.Max(10, (highHz - lowHz) / 8))
        {
            total += Goertzel(samples, sampleRate, hz);
            count++;
        }

        if (count == 0)
        {
            return 0;
        }

        return Math.Sqrt(total / count) / Math.Max(1, samples.Length / 2d);
    }

    private float FollowOutput(double target, MusicSettings settings, double dt, DateTimeOffset now)
    {
        var beatThreshold = Math.Clamp(0.20 + settings.BeatThreshold * 0.45, 0.10, 0.60);
        var minCooldownMs = Math.Clamp(settings.AttackMs * 1.2 + 20, 28, 110);
        var adaptiveCooldownMs = Math.Clamp(_beatIntervalMs * 0.48, minCooldownMs, 165);
        var canTrigger = now - _lastBeatAt > TimeSpan.FromMilliseconds(adaptiveCooldownMs);

        if (target >= beatThreshold && canTrigger)
        {
            if (_lastBeatAt != DateTimeOffset.MinValue)
            {
                var interval = (now - _lastBeatAt).TotalMilliseconds;
                if (interval is >= 90 and <= 1200)
                {
                    _beatIntervalMs = Smooth(_beatIntervalMs, interval, 0.2, 0.65);
                }
            }

            _lastBeatAt = now;
            _heldOutput = Math.Max(_heldOutput, target);
            _holdUntil = now.AddMilliseconds(Math.Clamp(settings.PeakHoldMs, 8, 80));
        }

        if (now < _holdUntil)
        {
            target = Math.Max(target, _heldOutput);
        }
        else
        {
            _heldOutput = Math.Max(0, _heldOutput - dt * 5.0);
        }

        var attackSeconds = Math.Clamp(settings.AttackMs / 1000d * 0.22, 0.002, 0.035);
        var releaseSeconds = Math.Clamp(settings.ReleaseMs / 1000d * 0.48, 0.025, 0.24);
        _outputEnvelope = Smooth(_outputEnvelope, target, dt, target > _outputEnvelope ? attackSeconds : releaseSeconds);
        if (_outputEnvelope < 0.015)
        {
            _outputEnvelope = 0;
        }

        return (float)Math.Clamp(_outputEnvelope, 0, 1);
    }

    private static double CalculateRms(float[] samples)
    {
        if (samples.Length == 0)
        {
            return 0;
        }

        var total = 0d;
        foreach (var sample in samples)
        {
            total += sample * sample;
        }

        return Math.Sqrt(total / samples.Length);
    }

    private static double BandPreference(BandDefinition band, int preferredLowHz, int preferredHighHz)
    {
        preferredLowHz = Math.Clamp(preferredLowHz, 20, 1000);
        preferredHighHz = Math.Clamp(preferredHighHz, preferredLowHz + 10, 8000);
        var overlap = Math.Max(0, Math.Min(band.HighHz, preferredHighHz) - Math.Max(band.LowHz, preferredLowHz));
        var bandWidth = Math.Max(1, band.HighHz - band.LowHz);
        var overlapRatio = overlap / (double)bandWidth;
        return 0.75 + overlapRatio * 0.5;
    }

    private static double Smooth(double current, double target, double dtSeconds, double timeConstantSeconds)
    {
        var alpha = 1 - Math.Exp(-dtSeconds / Math.Max(0.001, timeConstantSeconds));
        return current + (target - current) * alpha;
    }

    private static double Goertzel(float[] samples, int sampleRate, int targetHz)
    {
        var omega = 2.0 * Math.PI * targetHz / sampleRate;
        var coeff = 2.0 * Math.Cos(omega);
        var q0 = 0d;
        var q1 = 0d;
        var q2 = 0d;
        for (var i = 0; i < samples.Length; i++)
        {
            var window = 0.5 - 0.5 * Math.Cos(2 * Math.PI * i / Math.Max(1, samples.Length - 1));
            q0 = coeff * q1 - q2 + samples[i] * window;
            q2 = q1;
            q1 = q0;
        }

        return q1 * q1 + q2 * q2 - coeff * q1 * q2;
    }

    private sealed class BandState
    {
        public double Short { get; set; }
        public double Long { get; set; }
        public double Noise { get; set; }
        public double TargetWeight { get; set; }
        public double Weight { get; set; }
    }

    private readonly record struct BandDefinition(int LowHz, int HighHz, double Gain);
}
