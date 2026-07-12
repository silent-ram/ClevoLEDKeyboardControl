using ColorfulLedKeyboard.Service;

namespace ColorfulLedKeyboard.Tests;

public sealed class AudioApplicationMonitorTests
{
    [Fact]
    public void PlaybackLatch_EntersAfter300MsAndLeavesAfter2Seconds()
    {
        var latch = new AudioApplicationMonitor.PlaybackLatch();
        var start = DateTimeOffset.Parse("2026-07-11T12:00:00Z");

        AudioApplicationMonitor.UpdateLatch(latch, 0.2f, start);
        AudioApplicationMonitor.UpdateLatch(latch, 0.2f, start.AddMilliseconds(299));
        Assert.False(latch.IsPlaying);
        AudioApplicationMonitor.UpdateLatch(latch, 0.2f, start.AddMilliseconds(300));
        Assert.True(latch.IsPlaying);
        AudioApplicationMonitor.UpdateLatch(latch, 0f, start.AddMilliseconds(2300));
        Assert.False(latch.IsPlaying);
    }
}
