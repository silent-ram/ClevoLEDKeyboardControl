# DCHU 协议反向工程结果（P955ET1）

> 日期：2026-06-12  
> 数据源：本机 `HKLM\HARDWARE\ACPI\DSDT/SSD*`、`InsydeDCHU.dll`、`DCHUService.exe`、伪 zone 工具实机观察（该工具已于本次重构中删除，实测结论见第八节）

## 一、传输栈

```
应用 (.NET)
  → SetDCHU_Data(int command, byte[] buffer, int length)        [InsydeDCHU.dll 出口]
    → DeviceIoControl(IOCTL_ACPIBIOS_READ_METHOD)
      → AcpiBridge.sys
        → ACPI _DSM (\_SB.DCHU._DSM)
          → SCMD / GCMD / CPKG / OCWR / CC30   [按 Arg1 即 command 分派]
            → EC 寄存器 (FCMD/FDAT/FBUF/FBF1/FBF2/FBF3)
```

- `InsydeDCHU.dll` 内不存在命令分支，纯透传。
- `_DSM` GUID = `{93F224E4-FBDC-4BBF-ADD6-DB71BDC0AFAD}`（Insyde / Clevo 私有 DCHU 接口）。
- DSDT 中 `_DSM` 把 `Arg2`（function index = `command`）按集合分派给四个内部子方法：
  - `CPKG`：`{0x02}`（取设备能力包，带返回结构）
  - `OCWR`：`{0x03}`
  - `CC30`：`{0x04, 0x07, 0x0C, 0x0D, 0x0E, 0x11}`
  - `GCMD`：`{0x01, 0x05, 0x06, 0x08, 0x09, 0x0A, 0x10, 0x12, 0x32, 0x33, 0x34, 0x38, 0x39, 0x3B-0x3F, 0x41-0x43, 0x45, 0x51, 0x52, 0x60, 0x62-0x64, 0x6E-0x71, 0x73, 0x77, 0x7A}`
  - `SCMD`：**`{0x13, 0x14, 0x1D, 0x1F, 0x20, 0x21, 0x22, 0x26, 0x27, 0x2A, 0x2C, 0x31, 0x46-0x4A, 0x4C, 0x4E, 0x4F, 0x55-0x57, 0x5A, 0x5B, 0x5E, 0x65-0x6D, 0x74-0x76, 0x79}`**
  - 不在以上集合的 command 直接返回 `0x80000002`（不支持）。
- 其他子集合不会再写键盘灯，请专注 `SCMD` 的 0x65/0x66/**0x67**/0x68/0x69/0x6A/0x6B/0x6C/0x6D 这些 0x60s 命令。

## 二、当前代码的 `SetDCHU_Data(103, …)` 实际语义

`command = 103 = 0x67`，进入 `SCMD` 的 0x67 分支。  
ARGS（4 字节小端整数，由 buffer 前 4 字节组成）按位拆分：

```
bit 31..28 : Local7   = sub-command (子命令)
bit 27..24 : Local4   = sub-argument
bit 23..16 : Local3   = 8-bit 值 (RGB 中常见的 R 或 B)
bit 15..12 : Local2   = 亮度档 0..9 (映射到 EC 0xFF / 0xE6 / … / 0x18)
bit 11..0  : 颜色 9-bit 三通道紧致编码 (Local0)
```

颜色 9-bit 编码（仅 Local7=0 时使用）：

```
Local0 = (ARGS & 0x07)            // R 高 3 bit
       | ((ARGS >> 1) & 0x38)     // G 高 3 bit
       | ((ARGS >> 2) & 0x1C0)    // B 高 3 bit
```

每个通道只取最高 3 bit，**总共 512 种颜色，不是 24-bit RGB**（这是 EC 老协议的常见特征）。

### Local7（子命令）枚举

| Local7 | EC 写入 | 含义 |
|--------|---------|------|
| 0x0 | FCMD=`0xC2`, FDAT=Local0&0xFF, FBUF=Local0>>8 | **静态色**（9-bit 紧致颜色）|
| 0x1 | FCMD=`0xC4`, FDAT=`0x03`, FBUF=Local3 | 灯效模式 3 |
| 0x2 | FCMD=`0xC4`, FDAT=`0x04`, FBUF=Local3 | 灯效模式 4 |
| 0x3 | FCMD=`0xC4`, FDAT=`0x06`, FBUF=Local3, FBF1=Local4 | 灯效模式 6（带参数）|
| 0x4 | FCMD=`0xC4`, FDAT=`0x0D`(开)/`0x0E`(关) | **键盘灯总开关** |
| 0x7..0xB | FCMD=`0xC4`, FDAT=Local7 | 直透 EC mode 7..11 |
| 0xC | (空) | 占位 |
| 0xD | FCMD=`0xC4`, FDAT=`0x02`, FBUF=Local2 | **亮度**（0..9 → 0xFF..0x18）|
| 0xE | FCMD=`0xC4`, FDAT=`0x0C`, FBUF=Local1(6-bit + bit5 选项) | 灯效定时/选项 |
| 0xF | FCMD=`0xCA`, FDAT/FBUF/FBF1/FBF2 = 多字节，由 Local4 选 mode 3/4/5/7/6（差不多就是"序列槽位写"） | **多色序列 / 图案** |

## 三、关键事实：当前代码的 Zone 是"假分区"

`DchuKeyboardDevice.SetZone` 当前构造：

```csharp
// 仿真后的等价计算
encodedColor = (B << 16) | (R << 8) | G;          // 24-bit packed BRG
payload      = (commandByte << 24) | encodedColor; // 4 字节，commandByte ∈ {0xF0, 0xF1, 0xF2}
```

把 payload 解释成 ARGS 看：

| commandByte | Local7 | Local4 | Local3 (R 字段) | 实际触发的 EC 路径 |
|-------------|--------|--------|------------------|---------------------|
| 0xF0 | 0xF | 0x0 | R 字节 | 多色序列 mode 3 槽位写入（Local4<3 走 `Local0=Local4+3=3`，FCMD=0xCA, FDAT=3） |
| 0xF1 | 0xF | 0x1 | R 字节 | 同上，槽位 4（FDAT=4） |
| 0xF2 | 0xF | 0x2 | R 字节 | 同上，槽位 5（FDAT=5） |

**结论**：`SetZone(1/2/3, color)` 三次调用并不是"独立设置 3 个分区"，它们是在向 EC 的"多色序列模式"的连续槽位**写颜色**，最终切换到那个 mode。  
单分区硬件上看到的现象是"最后一次写入的颜色生效到全键盘"，这跟 ZoneTest 实测**完全吻合**。

> 该实现来自上游 [moshuiD/Colorful-Keyborad-Led-Color-Setting](https://github.com/moshuiD/Colorful-Keyborad-Led-Color-Setting)，对多分区 Clevo 机型可能恰好生效；在 P955ET1 这类**单分区硬件**上 240/241/242 退化为同一个全键盘命令。

## 四、本机硬件能力的真实集合

通过 0x67 子命令我们其实有以下软件可用的真实能力：

1. **静态色（512 色）**：Local7=0，按 9-bit 编码 → 比当前 `EffectType.Static` 的"24-bit"更接近硬件原色
2. **键盘灯总开关**：Local7=4 → 替代当前用 `RgbColor.Black` 模拟的关灯
3. **亮度档（0..9）**：Local7=0xD → 不必再用软件缩放 `color.Scale(brightness)`
4. **硬件灯效**（不依赖 CPU 渲染）：
   - Local7=1/2/3 → mode 3/4/6
   - Local7=7..0xB → mode 7..11
   - Local7=0xE → mode 12（带 6-bit 速度+选项）
   - Local7=0xF → mode 3/4/5/6/7/0xA（多色序列，需要前置写入颜色 + 末次切 mode）
5. **没有真正的多分区/单键能力**（DSDT 的 0x67 写的全部是全局 EC 寄存器；Local4 只是 mode 选择器，不是 zone index）

## 五、其他可能值得探索的 SCMD command（未触及）

| Cmd | 意义猜测 | 备注 |
|-----|---------|------|
| 0x65 | ? | 在 SCMD 集合，未读 |
| 0x66 | ? | 在 SCMD 集合，未读 |
| 0x68 | 写 4 字节配置到 EC FDAT 1/2/3/4 | DSDT 已读，看起来是某种长配置 |
| 0x69 | 多 bit 标志写入 EC | DSDT 已读 |
| 0x6A..0x6D, 0x74..0x76, 0x79 | ? | 未读 |
| `GCMD 0x6E/0x6F/0x70/0x71/0x73/0x77/0x7A` | 读类命令 | 可用 `GetDCHU_Data_Buffer/Integer` |

下一步如果要进一步确认，可以：
- 把 `SCMD` 的 0x65~0x6D、0x74~0x79 全部抽出来人工读
- 写一个 `DchuExplorer` 工具，对每个 cmd 各试几种 ARGS，肉眼观察键盘
- 用 `GetDCHU_Data_Integer(0x02, …)` 读 DCHU 设备能力包（CPKG），有些机型在能力包里返回 zone 数量

## 六、对软件架构的指导

当前 `DchuKeyboardDevice` 的"3 zone"接口在本机是误解。建议：

1. **删除"伪 zone"**：`SetZone(1/2/3, color)` 在当前代码里走的是序列槽位写，并不是分区，留着会让上层逻辑（如未来真接入 zone 的机型）更乱。
2. **改用单一 `SetColor(RgbColor)` 直走 Local7=0** 的 9-bit 静态色路径。
3. **用 Local7=4 实现 `PowerOn/PowerOff`**，替代用黑色模拟关灯。
4. **可选：暴露硬件灯效**（mode 3/4/6/7..11），把 CPU 渲染交给 EC，省功耗。
5. **能力探测**：调一次 `GetDCHU_Data_Integer(0x02, …)` 读 CPKG，根据返回值决定运行时是否走多 zone 路径（其他机型升级用）。

## 七、为什么"做分区/单键"在 P955ET1 上不可能

DSDT 是真相来源。`SCMD` 的所有 LED 写入路径都直接写**单一 EC 寄存器组**（FCMD/FDAT/FBUF/FBF1-3），没有"zone index"参数，没有"key matrix coordinate"参数。EC 固件本身决定整块面板的颜色——硬件不存在按位寻址。

这条结论**不会**被任何"挖更多命令"或"hook 官方 CC"推翻，因为 DSDT 是 BIOS 给出的接口契约本身。要"分区/单键"必须更换硬件（如 X170 / NH5x 等带 per-key 控制的 Clevo 机型）。

## 八、实施记录（2026-06-12）

基于上述反向工程结论，对硬件抽象层执行了一次微创重构。提交记录可由 `git log --oneline -- ColorfulLedKeyboard.Core/DchuKeyboardDevice.cs` 查询。

### 改动范围

| 项目 | 改动 |
|------|------|
| `ColorfulLedKeyboard.Core/DchuKeyboardDevice.cs` | 删除 `SetZone`/`SetAllZones`/`Zones`，新增 `SetColor(RgbColor)` 与 `Pack9BitColor` |
| `ColorfulLedKeyboard.Service/Worker.cs` | 5 处 `SetAllZones(...)` → `SetColor(...)`（机械替换） |
| `ColorfulLedKeyboard.Tests/DchuKeyboardDeviceTests.cs` | 新增 7 个 `Pack9BitColor` 单元测试 |
| `ColorfulLedKeyboard.ZoneTest/` | 整项目删除（验证目标已被证伪） |
| `ColorfulLedKeyboard.slnx` | 移除 ZoneTest 引用 |
| `README.md` | 删除"分区测试工具"描述、致谢段补充 SetZone 误用说明 |

### Pack9BitColor 推导

DSDT 对 SCMD 0x67 Local7=0 的颜色还原算法（见第二节）：

```
Local0 = (ARGS & 0x07)            // R 高 3 bit @ bit 0..2
       | ((ARGS >> 1) & 0x38)     // G 高 3 bit @ bit 3..5
       | ((ARGS >> 2) & 0x1C0)    // B 高 3 bit @ bit 6..8
```

正向编码（从 8-bit RGB 到 9-bit 紧致）：

```csharp
internal static int Pack9BitColor(RgbColor color)
{
    var r3 = (color.R >> 5) & 0x07;
    var g3 = (color.G >> 5) & 0x07;
    var b3 = (color.B >> 5) & 0x07;
    return r3 | (g3 << 3) | (b3 << 6);
}
```

每通道仅保留最高 3 bit，是硬件量化（512 色），不是软件简化。

### 显式跳过的非目标（YAGNI）

| 候选改动 | 跳过理由 |
|---------|---------|
| Local7=0xD 硬件亮度档（10 档） | 软件 100 档亮度已稳定，10 档量化损失精度无收益 |
| Local7=4 硬件开关键 | 现 Off 状态机用 `RgbColor.Black` 模拟，工作正常；引入额外状态会让 Worker 复杂化 |
| Local7=1/2/3/7..0xB/0xE/0xF 硬件灯效 | 与 IdleDim/Schedule/AppProfile/TypingPulse/NotificationFlash/Music 全套软件调度冲突 |
| `GetDCHU_Data_Integer(0x02)` CPKG 能力探测 | 本机已知单分区，留给将来支持多分区机型时再做 |
| 真分区/单键 | 硬件不支持（DSDT 写单一 EC 寄存器组，无 zone index/key matrix 参数） |

### 用户感知验证

改动后用户行为应与改动前完全一致：所有灯效、配置文件、亮度滑条、应用场景、计划、空闲降亮、敲字脉冲、通知闪烁、音乐模式全部不变。**唯一可能可见的差异**是颜色精度从"软件 24-bit + 硬件 mode 5 处理"变为"显式 9-bit 量化"，肉眼基本无差异。

Release 前手动验证步骤（在装有 `InsydeDCHU.dll` 的目标机器上）：

1. 切到 Static 灯效，依次设为红/绿/蓝/白/黄/青/品红，肉眼确认硬件响应正确
2. 切到 Off，确认键盘黑屏
3. 切到 Rainbow 跑 30 秒，确认色相循环正常
