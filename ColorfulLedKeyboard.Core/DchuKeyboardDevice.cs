using System.Runtime.InteropServices;

namespace ColorfulLedKeyboard.Core;

/// <summary>
/// DCHU 单一面板键盘灯接口。
/// 通过 InsydeDCHU.dll → ACPI _DSM → SCMD 0x67 Local7=0 路径，
/// 用 9-bit 三通道紧致编码把整块键盘面板设为指定颜色。
///
/// 反向工程详情见 docs/reverse-engineering/dchu-protocol-findings.md。
/// 该硬件没有 zone index 或 key matrix 参数 —— 单分区面板，整块统一上色。
/// </summary>
public sealed class DchuKeyboardDevice
{
    private const int SetDchuStaticColorCommand = 103; // SCMD 0x67

    [DllImport("InsydeDCHU.dll")]
    private static extern int SetDCHU_Data(int command, byte[] buffer, int length);

    /// <summary>
    /// 把整块键盘面板设为指定颜色。
    /// 颜色会被量化到硬件原生的 9-bit 三通道紧致编码（每通道最高 3 bit，共 512 色）。
    /// </summary>
    public void SetColor(RgbColor color)
    {
        var payload = BitConverter.GetBytes(Pack9BitColor(color));
        SetDCHU_Data(SetDchuStaticColorCommand, payload, 4);
    }

    /// <summary>
    /// 9-bit 三通道紧致颜色编码，每通道取最高 3 bit。
    /// 位字段排布：bit 0..2 = R 高 3 bit，bit 3..5 = G 高 3 bit，bit 6..8 = B 高 3 bit。
    /// 返回值始终在 [0, 0x1FF] 范围内（9 位有效），SetColor 内部依赖此契约省略掩码。
    /// 推导自 DSDT 的 SCMD 0x67 Local0 还原算法，详见 dchu-protocol-findings.md。
    /// </summary>
    internal static int Pack9BitColor(RgbColor color)
    {
        var r3 = (color.R >> 5) & 0x07;
        var g3 = (color.G >> 5) & 0x07;
        var b3 = (color.B >> 5) & 0x07;
        return r3 | (g3 << 3) | (b3 << 6);
    }
}
