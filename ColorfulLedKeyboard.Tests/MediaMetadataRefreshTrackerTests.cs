using ColorfulLedKeyboard.Tray;

namespace ColorfulLedKeyboard.Tests;

public sealed class MediaMetadataRefreshTrackerTests
{
    private static readonly DateTimeOffset Start = new(2026, 8, 14, 0, 0, 0, TimeSpan.Zero);

    [Fact]
    public void NewSession_ReadsMetadataAndArtworkImmediately()
    {
        var tracker = new MediaMetadataRefreshTracker();

        var plan = tracker.Plan("QQMusic.exe", hasPrevious: false, isPlaying: false, Start);

        Assert.True(plan.ReadMetadata);
        Assert.True(plan.ForceArtwork);
        Assert.True(tracker.ShouldReadArtwork("QQMusic.exe", "track-1", plan, Start));
    }

    [Fact]
    public void SuccessfulArtwork_IsNotReadAgainForUnchangedTrack()
    {
        var tracker = new MediaMetadataRefreshTracker();
        var initial = tracker.Plan("QQMusic.exe", false, true, Start);
        tracker.RecordMetadataResult("QQMusic.exe", "track-1", true, Start, true, true);

        var immediate = tracker.Plan("QQMusic.exe", true, true, Start.AddSeconds(1));
        var periodic = tracker.Plan(
            "QQMusic.exe",
            true,
            true,
            Start + MediaMetadataRefreshTracker.PlayingMetadataInterval);

        Assert.False(immediate.ReadMetadata);
        Assert.True(periodic.ReadMetadata);
        Assert.False(tracker.ShouldReadArtwork("QQMusic.exe", "track-1", periodic, Start.AddSeconds(15)));
    }

    [Fact]
    public void ChangedTrack_ReadsArtworkEvenWithoutEvent()
    {
        var tracker = new MediaMetadataRefreshTracker();
        tracker.RecordMetadataResult("QQMusic.exe", "track-1", true, Start, true, true);
        var periodic = tracker.Plan(
            "QQMusic.exe",
            true,
            true,
            Start + MediaMetadataRefreshTracker.PlayingMetadataInterval);

        Assert.True(tracker.ShouldReadArtwork("QQMusic.exe", "track-2", periodic, Start.AddSeconds(15)));
    }

    [Fact]
    public void MediaEvent_ForcesImmediateMetadataAndArtworkRefresh()
    {
        var tracker = new MediaMetadataRefreshTracker();
        tracker.RecordMetadataResult("QQMusic.exe", "track-1", false, Start, true, true);

        tracker.MarkDirty("QQMusic.exe", Start);
        var plan = tracker.Plan("QQMusic.exe", true, false, Start.AddSeconds(1));

        Assert.True(plan.ReadMetadata);
        Assert.True(plan.ForceArtwork);
    }

    [Fact]
    public void PausedSession_UsesSlowerMetadataFallback()
    {
        var tracker = new MediaMetadataRefreshTracker();
        tracker.RecordMetadataResult("QQMusic.exe", "track-1", false, Start, true, true);

        Assert.False(tracker.Plan("QQMusic.exe", true, false, Start.AddMinutes(1)).ReadMetadata);
        Assert.True(tracker.Plan(
            "QQMusic.exe",
            true,
            false,
            Start + MediaMetadataRefreshTracker.PausedMetadataInterval).ReadMetadata);
    }

    [Fact]
    public void MissingArtwork_RetriesAfterBackoffOnly()
    {
        var tracker = new MediaMetadataRefreshTracker();
        tracker.RecordMetadataResult("QQMusic.exe", "track-1", false, Start, true, false);

        Assert.False(tracker.Plan("QQMusic.exe", true, false, Start.AddSeconds(29)).ReadMetadata);
        var retry = tracker.Plan(
            "QQMusic.exe",
            true,
            false,
            Start + MediaMetadataRefreshTracker.MissingArtworkRetryInterval);
        Assert.True(retry.ReadMetadata);
        Assert.True(tracker.ShouldReadArtwork("QQMusic.exe", "track-1", retry, Start.AddSeconds(30)));
    }

    [Fact]
    public void SuccessfulArtwork_IsPeriodicallyRevalidated()
    {
        var tracker = new MediaMetadataRefreshTracker();
        tracker.RecordMetadataResult("QQMusic.exe", "track-1", false, Start, true, true);

        var plan = tracker.Plan(
            "QQMusic.exe",
            true,
            false,
            Start + MediaMetadataRefreshTracker.ArtworkValidationInterval);

        Assert.True(plan.ReadMetadata);
        Assert.True(plan.ForceArtwork);
    }

    [Fact]
    public void RemovedSession_DoesNotReuseOldRefreshState()
    {
        var tracker = new MediaMetadataRefreshTracker();
        tracker.RecordMetadataResult("QQMusic.exe", "track-1", false, Start, true, true);

        tracker.Remove("QQMusic.exe");
        var plan = tracker.Plan("QQMusic.exe", hasPrevious: false, isPlaying: false, Start.AddSeconds(1));

        Assert.True(plan.ReadMetadata);
        Assert.True(plan.ForceArtwork);
    }

    [Fact]
    public void MetadataFailure_BacksOffEvenBeforeFirstSuccessfulRead()
    {
        var tracker = new MediaMetadataRefreshTracker();
        var initial = tracker.Plan("QQMusic.exe", hasPrevious: false, isPlaying: false, Start);

        tracker.RecordMetadataFailure("QQMusic.exe", Start, initial.ForceArtwork);

        Assert.False(tracker.Plan("QQMusic.exe", false, false, Start.AddMilliseconds(500)).ReadMetadata);
        Assert.True(tracker.Plan(
            "QQMusic.exe",
            false,
            false,
            Start + MediaMetadataRefreshTracker.MissingArtworkRetryInterval).ReadMetadata);
    }

    [Fact]
    public void PlaybackStart_RefreshesMetadataAndArtwork()
    {
        var tracker = new MediaMetadataRefreshTracker();
        tracker.Plan("QQMusic.exe", false, false, Start);
        tracker.RecordMetadataResult("QQMusic.exe", "track-1", false, Start, true, true);

        var plan = tracker.Plan("QQMusic.exe", true, true, Start.AddSeconds(1));

        Assert.True(plan.ReadMetadata);
        Assert.True(plan.ForceArtwork);
        Assert.True(tracker.ShouldReadArtwork("QQMusic.exe", "track-1", plan, Start.AddSeconds(1)));
    }

    [Fact]
    public void ForcedRefresh_MarksExistingSessionsDirty()
    {
        var tracker = new MediaMetadataRefreshTracker();
        tracker.RecordMetadataResult("QQMusic.exe", "track-1", false, Start, true, true);

        tracker.MarkAllDirty();
        var plan = tracker.Plan("QQMusic.exe", true, false, Start.AddSeconds(1));

        Assert.True(plan.ReadMetadata);
        Assert.True(plan.ForceArtwork);
    }

    [Fact]
    public void FailedArtworkValidation_RetriesAfterBackoff()
    {
        var tracker = new MediaMetadataRefreshTracker();
        tracker.RecordMetadataResult("QQMusic.exe", "track-1", false, Start, true, true);
        var validationTime = Start + MediaMetadataRefreshTracker.ArtworkValidationInterval;
        var validation = tracker.Plan("QQMusic.exe", true, false, validationTime);

        tracker.RecordMetadataFailure("QQMusic.exe", validationTime, validation.ForceArtwork);

        Assert.False(tracker.Plan("QQMusic.exe", true, false, validationTime.AddSeconds(1)).ReadMetadata);
        Assert.True(tracker.Plan(
            "QQMusic.exe",
            true,
            false,
            validationTime + MediaMetadataRefreshTracker.MissingArtworkRetryInterval).ReadMetadata);
    }

    [Fact]
    public void RepeatedMediaEvents_DoNotRepeatedlyReadUnchangedArtwork()
    {
        var tracker = new MediaMetadataRefreshTracker();
        tracker.RecordMetadataResult("QQMusic.exe", "track-1", true, Start, true, true);

        tracker.MarkDirty("QQMusic.exe", Start.AddSeconds(1));
        var firstEvent = tracker.Plan("QQMusic.exe", true, true, Start.AddSeconds(1));

        Assert.True(firstEvent.ReadMetadata);
        Assert.True(firstEvent.ForceArtwork);
        Assert.True(tracker.ShouldReadArtwork("QQMusic.exe", "track-1", firstEvent, Start.AddSeconds(1)));

        tracker.RecordMetadataResult("QQMusic.exe", "track-1", true, Start.AddSeconds(1), true, true);
        tracker.MarkDirty("QQMusic.exe", Start.AddSeconds(2));
        var throttled = tracker.Plan("QQMusic.exe", true, true, Start.AddSeconds(2));

        Assert.True(throttled.ReadMetadata);
        Assert.False(throttled.ForceArtwork);
        Assert.False(tracker.ShouldReadArtwork("QQMusic.exe", "track-1", throttled, Start.AddSeconds(2)));
        Assert.True(tracker.ShouldReadArtwork("QQMusic.exe", "track-2", throttled, Start.AddSeconds(2)));

        tracker.RecordMetadataResult("QQMusic.exe", "track-1", true, Start.AddSeconds(2), false, false);
        tracker.MarkDirty("QQMusic.exe", Start.AddSeconds(2) + MediaMetadataRefreshTracker.EventArtworkThrottleInterval);
        var later = tracker.Plan(
            "QQMusic.exe",
            true,
            true,
            Start.AddSeconds(2) + MediaMetadataRefreshTracker.EventArtworkThrottleInterval);

        Assert.True(later.ForceArtwork);
    }

    [Fact]
    public void EventDuringRead_RemainsDirtyForTheNextRefresh()
    {
        var tracker = new MediaMetadataRefreshTracker();
        var read = tracker.Plan("QQMusic.exe", false, true, Start);

        tracker.MarkDirty("QQMusic.exe", Start.AddMilliseconds(100));
        tracker.RecordMetadataResult("QQMusic.exe", "track-1", true, Start, true, true, read);

        var next = tracker.Plan("QQMusic.exe", true, true, Start.AddMilliseconds(200));

        Assert.True(next.ReadMetadata);
    }
}
