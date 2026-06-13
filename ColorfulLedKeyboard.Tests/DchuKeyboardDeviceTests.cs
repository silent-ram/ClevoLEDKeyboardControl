using ColorfulLedKeyboard.Core;

namespace ColorfulLedKeyboard.Tests;

public sealed class DchuKeyboardDeviceTests
{
    [Fact]
    public void BuildSequenceSlotArgs_Slot0_Black()
    {
        // Local7=0xF, Local4=0, color=(0,0,0)
        // commandByte=0xF0, encodedColor=0 → args = 0xF0000000
        var args = DchuKeyboardDevice.BuildSequenceSlotArgs(new RgbColor(0, 0, 0), 0);
        Assert.Equal(0xF0000000, (uint)args);
    }

    [Fact]
    public void BuildSequenceSlotArgs_Slot2_White()
    {
        // Local7=0xF, Local4=2, color=(255,255,255)
        // commandByte=0xF2, encodedColor = (0xFF<<16)|(0xFF<<8)|0xFF = 0xFFFFFF
        // args = 0xF2FFFFFF
        var args = DchuKeyboardDevice.BuildSequenceSlotArgs(new RgbColor(255, 255, 255), 2);
        Assert.Equal(0xF2FFFFFF, (uint)args);
    }

    [Fact]
    public void BuildSequenceSlotArgs_Slot1_PureRed()
    {
        // commandByte=0xF1, color=(255,0,0)
        // encodedColor = (0<<16)|(255<<8)|0 = 0x00FF00
        // args = 0xF100FF00
        var args = DchuKeyboardDevice.BuildSequenceSlotArgs(new RgbColor(255, 0, 0), 1);
        Assert.Equal(0xF100FF00, (uint)args);
    }

    [Fact]
    public void BuildSequenceSlotArgs_Slot0_PureGreen()
    {
        // commandByte=0xF0, color=(0,255,0)
        // encodedColor = (0<<16)|(0<<8)|255 = 0x0000FF
        // args = 0xF00000FF
        var args = DchuKeyboardDevice.BuildSequenceSlotArgs(new RgbColor(0, 255, 0), 0);
        Assert.Equal(0xF00000FF, (uint)args);
    }

    [Fact]
    public void BuildSequenceSlotArgs_Slot2_PureBlue()
    {
        // commandByte=0xF2, color=(0,0,255)
        // encodedColor = (255<<16)|(0<<8)|0 = 0xFF0000
        // args = 0xF2FF0000
        var args = DchuKeyboardDevice.BuildSequenceSlotArgs(new RgbColor(0, 0, 255), 2);
        Assert.Equal(0xF2FF0000, (uint)args);
    }

    [Fact]
    public void BuildSequenceSlotArgs_MixedColor()
    {
        // commandByte=0xF1, color=(0x12, 0x34, 0x56)
        // encodedColor = (0x56<<16)|(0x12<<8)|0x34 = 0x561234
        // args = 0xF1561234
        var args = DchuKeyboardDevice.BuildSequenceSlotArgs(new RgbColor(0x12, 0x34, 0x56), 1);
        Assert.Equal(0xF1561234, (uint)args);
    }
}
