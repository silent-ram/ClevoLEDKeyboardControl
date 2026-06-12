using ColorfulLedKeyboard.Core;

namespace ColorfulLedKeyboard.Tests;

public sealed class DchuKeyboardDeviceTests
{
    [Fact]
    public void Pack9BitColor_Black_ReturnsZero()
    {
        var packed = DchuKeyboardDevice.Pack9BitColor(new RgbColor(0, 0, 0));
        Assert.Equal(0x000, packed);
    }

    [Fact]
    public void Pack9BitColor_White_ReturnsAllNineBitsSet()
    {
        var packed = DchuKeyboardDevice.Pack9BitColor(new RgbColor(0xFF, 0xFF, 0xFF));
        Assert.Equal(0x1FF, packed);
    }

    [Fact]
    public void Pack9BitColor_PureRed_PutsTopThreeBitsInLowField()
    {
        var packed = DchuKeyboardDevice.Pack9BitColor(new RgbColor(0xFF, 0, 0));
        Assert.Equal(0x007, packed);
    }

    [Fact]
    public void Pack9BitColor_PureGreen_PutsTopThreeBitsInMidField()
    {
        var packed = DchuKeyboardDevice.Pack9BitColor(new RgbColor(0, 0xFF, 0));
        Assert.Equal(0x038, packed);
    }

    [Fact]
    public void Pack9BitColor_PureBlue_PutsTopThreeBitsInHighField()
    {
        var packed = DchuKeyboardDevice.Pack9BitColor(new RgbColor(0, 0, 0xFF));
        Assert.Equal(0x1C0, packed);
    }

    [Fact]
    public void Pack9BitColor_LowFiveBitsPerChannelQuantizeToZero()
    {
        // 每通道只设置低 5 bit (0x1F)，最高 3 bit 都是 0，应该全部量化掉
        var packed = DchuKeyboardDevice.Pack9BitColor(new RgbColor(0x1F, 0x1F, 0x1F));
        Assert.Equal(0x000, packed);
    }

    [Fact]
    public void Pack9BitColor_PreservesTopThreeBitsPerChannel()
    {
        // R=0xE0 (top3=0b111=7), G=0xC0 (top3=0b110=6), B=0xA0 (top3=0b101=5)
        // 期望: 7 | (6<<3) | (5<<6) = 0x07 | 0x30 | 0x140 = 0x177
        var packed = DchuKeyboardDevice.Pack9BitColor(new RgbColor(0xE0, 0xC0, 0xA0));
        Assert.Equal(0x177, packed);
    }
}
