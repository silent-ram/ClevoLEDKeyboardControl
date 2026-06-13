using System.Runtime.InteropServices;

namespace ColorfulLedKeyboard.Core;

/// <summary>
/// DCHU 单一面板键盘灯接口。
/// 通过 InsydeDCHU.dll → ACPI _DSM → SCMD 0x67 路径把整块键盘面板设为指定颜色。
///
/// 反向工程详情见 docs/reverse-engineering/dchu-protocol-findings.md。
/// 该硬件没有 zone index 或 key matrix 参数 —— 单分区面板，整块统一上色。
///
/// 实现路径：Local7=0xF / Local4=0..2 三槽位写。
/// 单纯走 Local7=0（FCMD=0xC2）只把颜色装载到 EC 寄存器，但不切换 EC 到静态色 mode，
/// EC 仍按默认 mode 运行，颜色不会显示。Local7=0xF 路径的最后一次写
/// （Local4=2 → FCMD=0xCA mode 5）会把 EC 拉到静态色序列模式，颜色才实际生效。
/// 这是上游 moshuiD/Colorful-Keyborad-Led-Color-Setting 经实测验证可工作的调用序列；
/// 在 P955ET1 这类单分区机型上，三次写入最终落到同一全局 EC 寄存器组，等价于一次"全键盘上色"。
/// </summary>
public sealed class DchuKeyboardDevice
{
    private const int SetDchuLedCommand = 103; // SCMD 0x67

    [DllImport("InsydeDCHU.dll")]
    private static extern int SetDCHU_Data(int command, byte[] buffer, int length);

    /// <summary>
    /// 把整块键盘面板设为指定颜色。
    /// </summary>
    public void SetColor(RgbColor color)
    {
        // 三槽位写：Local4=0/1/2，最后一次触发 EC mode 5 应用。
        WriteSequenceSlot(color, slot: 0);
        WriteSequenceSlot(color, slot: 1);
        WriteSequenceSlot(color, slot: 2);
    }

    /// <summary>
    /// 向 SCMD 0x67 Local7=0xF 路径的指定槽位写入颜色。
    /// ARGS 位字段：bit 31..28 = Local7 = 0xF（多色序列子命令）；
    /// bit 27..24 = Local4 = slot（0..2 选择 mode 3/4/5 槽位）；
    /// bit 23..16 = R 字节；bit 15..8 = G 字节；bit 7..0 = B 字节。
    /// 编码方式与上游 moshuiD/Colorful-Keyborad-Led-Color-Setting 一致：(B&lt;&lt;16) | (R&lt;&lt;8) | G。
    /// </summary>
    internal static int BuildSequenceSlotArgs(RgbColor color, int slot)
    {
        var commandByte = 0xF0 | (slot & 0x0F);
        var encodedColor = (color.B << 16) | (color.R << 8) | color.G;
        return (commandByte << 24) | encodedColor;
    }

    private void WriteSequenceSlot(RgbColor color, int slot)
    {
        var args = BuildSequenceSlotArgs(color, slot);
        var payload = BitConverter.GetBytes(args);
        SetDCHU_Data(SetDchuLedCommand, payload, 4);
    }
}
