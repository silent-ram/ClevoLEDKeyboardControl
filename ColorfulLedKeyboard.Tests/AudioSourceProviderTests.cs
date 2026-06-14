using ColorfulLedKeyboard.Core;
using ColorfulLedKeyboard.Service;

namespace ColorfulLedKeyboard.Tests;

public class AudioSourceProviderTests
{
    private sealed class FakeAudioDeviceProbe : IAudioDeviceProbe
    {
        private readonly Dictionary<string, DeviceSnapshot> _devices = new();
        public string? DefaultDeviceId { get; set; }
        public Func<string, DeviceSnapshot?>? InspectThrows { get; set; }

        public void Add(DeviceSnapshot snapshot) => _devices[snapshot.Id] = snapshot;

        public DeviceSnapshot? GetDefaultRenderDevice()
        {
            if (DefaultDeviceId is null) return null;
            return GetDevice(DefaultDeviceId);
        }

        public DeviceSnapshot? GetDevice(string id)
        {
            if (InspectThrows is { } thrower) return thrower(id);
            return _devices.TryGetValue(id, out var snapshot) ? snapshot : null;
        }
    }

    private static DeviceSnapshot Speaker(string id = "spk") =>
        new(id, "扬声器 (Realtek)", 48000, 2);

    private static DeviceSnapshot A2dp(string id = "bt-a2dp") =>
        new(id, "iQOO TWS Air3", 48000, 2);

    private static DeviceSnapshot Hfp(string id = "bt-hfp") =>
        new(id, "iQOO TWS Air3 Hands-Free", 16000, 1);

    [Fact]
    public void Initial_WithSpeaker_StatusIsActive()
    {
        var probe = new FakeAudioDeviceProbe();
        probe.Add(Speaker());
        probe.DefaultDeviceId = "spk";

        using var sut = new AudioSourceProvider(probe);

        Assert.Equal(AudioSourceStatus.Active, sut.Status);
        Assert.Equal("扬声器 (Realtek)", sut.DeviceFriendlyName);
    }

    [Fact]
    public void Initial_WithHfpDevice_StatusIsHfp()
    {
        var probe = new FakeAudioDeviceProbe();
        probe.Add(Hfp());
        probe.DefaultDeviceId = "bt-hfp";

        using var sut = new AudioSourceProvider(probe);

        Assert.Equal(AudioSourceStatus.Hfp, sut.Status);
    }

    [Fact]
    public void Initial_NoDevice_StatusIsUnavailable()
    {
        var probe = new FakeAudioDeviceProbe();
        using var sut = new AudioSourceProvider(probe);

        Assert.Equal(AudioSourceStatus.Unavailable, sut.Status);
    }

    [Fact]
    public void DefaultDeviceChanged_ToA2dp_FiresSwitchingThenActive()
    {
        var probe = new FakeAudioDeviceProbe();
        probe.Add(Speaker());
        probe.DefaultDeviceId = "spk";
        using var sut = new AudioSourceProvider(probe);

        var events = new List<AudioSourceStatus>();
        sut.SourceChanged += (_, e) => events.Add(e.Status);

        probe.Add(A2dp());
        probe.DefaultDeviceId = "bt-a2dp";
        sut.TestOnly_SimulateDefaultDeviceChanged("bt-a2dp");

        Assert.Equal(new[] { AudioSourceStatus.Switching, AudioSourceStatus.Active }, events);
        Assert.Equal(AudioSourceStatus.Active, sut.Status);
        Assert.Equal("iQOO TWS Air3", sut.DeviceFriendlyName);
    }

    [Fact]
    public void DefaultDeviceChanged_ToHfp_FiresSwitchingThenHfp()
    {
        var probe = new FakeAudioDeviceProbe();
        probe.Add(Speaker());
        probe.DefaultDeviceId = "spk";
        using var sut = new AudioSourceProvider(probe);

        var events = new List<AudioSourceStatus>();
        sut.SourceChanged += (_, e) => events.Add(e.Status);

        probe.Add(Hfp());
        probe.DefaultDeviceId = "bt-hfp";
        sut.TestOnly_SimulateDefaultDeviceChanged("bt-hfp");

        Assert.Equal(new[] { AudioSourceStatus.Switching, AudioSourceStatus.Hfp }, events);
    }

    [Fact]
    public void ReportSamples_WithinFallbackWindow_DoesNotFireUnavailable()
    {
        var probe = new FakeAudioDeviceProbe();
        probe.Add(Speaker());
        probe.DefaultDeviceId = "spk";
        using var sut = new AudioSourceProvider(probe);

        var events = new List<AudioSourceStatus>();
        sut.SourceChanged += (_, e) => events.Add(e.Status);

        sut.ReportSamples();
        sut.TestOnly_AdvanceFallbackClock(TimeSpan.FromMilliseconds(1000));

        Assert.Empty(events);
        Assert.Equal(AudioSourceStatus.Active, sut.Status);
    }

    [Fact]
    public void NoSamplesFor1500ms_FromActive_TransitionsToUnavailable()
    {
        var probe = new FakeAudioDeviceProbe();
        probe.Add(Speaker());
        probe.DefaultDeviceId = "spk";
        using var sut = new AudioSourceProvider(probe);

        var events = new List<AudioSourceStatus>();
        sut.SourceChanged += (_, e) => events.Add(e.Status);

        sut.ReportSamples();
        sut.TestOnly_AdvanceFallbackClock(TimeSpan.FromMilliseconds(1600));

        Assert.Contains(AudioSourceStatus.Unavailable, events);
        Assert.Equal(AudioSourceStatus.Unavailable, sut.Status);
    }

    [Fact]
    public void ReportSamples_AfterUnavailable_TransitionsBackToActive()
    {
        var probe = new FakeAudioDeviceProbe();
        probe.Add(Speaker());
        probe.DefaultDeviceId = "spk";
        using var sut = new AudioSourceProvider(probe);

        sut.ReportSamples();
        sut.TestOnly_AdvanceFallbackClock(TimeSpan.FromMilliseconds(1600));
        Assert.Equal(AudioSourceStatus.Unavailable, sut.Status);

        var events = new List<AudioSourceStatus>();
        sut.SourceChanged += (_, e) => events.Add(e.Status);

        sut.ReportSamples();
        sut.TestOnly_AdvanceFallbackClock(TimeSpan.FromMilliseconds(100));

        Assert.Contains(AudioSourceStatus.Active, events);
        Assert.Equal(AudioSourceStatus.Active, sut.Status);
    }

    [Fact]
    public void InspectDevice_Throws_StatusFallsBackToSwitching()
    {
        var probe = new FakeAudioDeviceProbe();
        probe.Add(Speaker());
        probe.DefaultDeviceId = "spk";
        using var sut = new AudioSourceProvider(probe);

        probe.InspectThrows = _ => throw new InvalidOperationException("simulated COM");
        probe.DefaultDeviceId = "broken";
        sut.TestOnly_SimulateDefaultDeviceChanged("broken");

        Assert.Equal(AudioSourceStatus.Switching, sut.Status);
    }

    [Fact]
    public void NotificationCallback_Throws_DoesNotPropagate()
    {
        var probe = new FakeAudioDeviceProbe();
        probe.Add(Speaker());
        probe.DefaultDeviceId = "spk";
        using var sut = new AudioSourceProvider(probe);

        sut.SourceChanged += (_, _) => throw new InvalidOperationException("subscriber bug");

        probe.Add(A2dp());
        probe.DefaultDeviceId = "bt-a2dp";
        var ex = Record.Exception(() => sut.TestOnly_SimulateDefaultDeviceChanged("bt-a2dp"));

        Assert.Null(ex);
    }

    [Fact]
    public void Dispose_Twice_DoesNotThrow()
    {
        var probe = new FakeAudioDeviceProbe();
        probe.Add(Speaker());
        probe.DefaultDeviceId = "spk";
        var sut = new AudioSourceProvider(probe);

        sut.Dispose();
        var ex = Record.Exception(() => sut.Dispose());

        Assert.Null(ex);
    }

    [Fact]
    public void ReportSamples_AfterDispose_DoesNotThrow()
    {
        var probe = new FakeAudioDeviceProbe();
        probe.Add(Speaker());
        probe.DefaultDeviceId = "spk";
        var sut = new AudioSourceProvider(probe);
        sut.Dispose();

        var ex = Record.Exception(() => sut.ReportSamples());
        Assert.Null(ex);
    }

    [Fact]
    public void OneSubscriberThrows_OtherSubscribersStillReceive()
    {
        var probe = new FakeAudioDeviceProbe();
        probe.Add(Speaker());
        probe.DefaultDeviceId = "spk";
        using var sut = new AudioSourceProvider(probe);

        var second = new List<AudioSourceStatus>();
        sut.SourceChanged += (_, _) => throw new InvalidOperationException("first sub bug");
        sut.SourceChanged += (_, e) => second.Add(e.Status);

        probe.Add(A2dp());
        probe.DefaultDeviceId = "bt-a2dp";
        sut.TestOnly_SimulateDefaultDeviceChanged("bt-a2dp");

        Assert.Contains(AudioSourceStatus.Switching, second);
        Assert.Contains(AudioSourceStatus.Active, second);
    }
}
