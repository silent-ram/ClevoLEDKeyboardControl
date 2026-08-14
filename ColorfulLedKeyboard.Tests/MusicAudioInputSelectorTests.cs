using ColorfulLedKeyboard.Core;
using ColorfulLedKeyboard.Service;

namespace ColorfulLedKeyboard.Tests;

public sealed class MusicAudioInputSelectorTests
{
    private static readonly MusicSettings BeatSettings = new()
    {
        EqEnabled = true,
        AllowSystemMixFallback = true
    };

    [Fact]
    public void BoundPlayer_IgnoresOtherApplicationsInSystemMix()
    {
        var level = MusicAudioInputSelector.Select(
            BeatSettings,
            hasSelectedApplication: true,
            selectedPeak: 0f,
            systemMixLevel: 0.95f);

        Assert.Equal(0f, level);
    }

    [Fact]
    public void BoundPlayer_UsesOnlyItsOwnSessionPeak()
    {
        var level = MusicAudioInputSelector.Select(
            BeatSettings,
            hasSelectedApplication: true,
            selectedPeak: 0.32f,
            systemMixLevel: 0.95f);

        Assert.Equal(0.32f, level);
    }

    [Fact]
    public void UnboundMode_CanUseSystemMixFallback()
    {
        var level = MusicAudioInputSelector.Select(
            BeatSettings,
            hasSelectedApplication: false,
            selectedPeak: 0.32f,
            systemMixLevel: 0.75f);

        Assert.Equal(0.75f, level);
    }

    [Fact]
    public void DisabledFallback_UsesSelectedPeak()
    {
        var settings = new MusicSettings
        {
            EqEnabled = true,
            AllowSystemMixFallback = false
        };

        var level = MusicAudioInputSelector.Select(
            settings,
            hasSelectedApplication: false,
            selectedPeak: 0.32f,
            systemMixLevel: 0.95f);

        Assert.Equal(0.32f, level);
    }
}
