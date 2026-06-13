# DCHU 硬件抽象层微创重构 — 设计

> 日期：2026-06-12
> 适用项目：ClevoLEDKeyboardControl
> 关联反向工程文档：[`docs/reverse-engineering/dchu-protocol-findings.md`](../../reverse-engineering/dchu-protocol-findings.md)

## 背景

`ColorfulLedKeyboard.Core.DchuKeyboardDevice` 当前暴露 `SetZone(int zone, RgbColor)` / `SetAllZones(RgbColor)` 两个接口，给上层"3 分区"的错觉。完整的 DSDT/DLL 反向工程（详见 reverse-engineering/dchu-protocol-findings.md）证实：

1. 当前 `SetZone(1/2/3, color)` 实际走的是 **SCMD 0x67 的 Local7=0xF（多色序列槽位写）**，命令字节 `0xF0/0xF1/0xF2` 拆出来是 `Local7=0xF, Local4=0/1/2`，并不是分区 index。
2. P955ET1（本机）以及任何遵循该 DSDT 形态的机型，`SCMD 0x67` 的所有 LED 写入路径都直接写**单一 EC 寄存器组**（FCMD/FDAT/FBUF/FBF1-3），DSDT 不接受 zone index 或 key matrix 参数。**单分区硬件无法通过软件实现真分区/单键。**
3. 当前对颜色的编码（`(B << 16) | (R << 8) | G` 24-bit packed）和硬件实际接受的 9-bit 三通道紧致编码不一致。运行起来恰好"看起来能工作"是因为 EC 在 mode 3/4/5 序列槽位写入时把传进来的字节当颜色用了；语义上是错的。
4. P955ET1 真正的"静态色"路径是 `SCMD 0x67` Local7=0：`bit 11..0` 装 9-bit 三通道紧致颜色。这是硬件原生的颜色精度。

ZoneTest 的实测现象（"全程红色，没绿没蓝，结束就全关了"）与上述协议解释完全一致：三次"分区"调用是在向多色序列连续槽位写颜色，然后把面板切到 mode 3/4/5；单分区面板上看到的是最后一次/序列开始的颜色应用到全键盘。

## 设计目标

让 `DchuKeyboardDevice` 的接口形态准确反映本机硬件能力，**让代码不再误导**。具体地：

- 删除虚假的 zone 接口（`SetZone(int, RgbColor)`、`Zones` 数组）
- 把"全键盘上色"改为单一接口 `SetColor(RgbColor)`
- 内部走 `SCMD 0x67` Local7=0 的 9-bit 紧致颜色路径，编码方式由反向工程结果给出
- 不动任何上层功能（灯效、亮度、音乐、应用场景、计划、空闲降亮、敲字、通知、预设、托盘、设置、安装器全部不变）
- 清理 ZoneTest 项目和相关 README 描述
- 在反向工程文档里留下清晰的"为什么这么改"，未来贡献者能查到上下文

## 非目标（YAGNI）

明确**不**做：

- **硬件亮度档**（Local7=0xD）：保留软件 100 档亮度。原因：不破坏现有用户体验，避免 100→10 档量化损失。
- **硬件开关键**（Local7=4）：保留用 `RgbColor.Black` 模拟的 Off 行为。原因：当前 `EffectType.Off` 状态机已经稳定工作，引入额外状态会让 Worker.cs 的 Off 分支复杂化，收益小（仅省一些写 EC 的开销）。
- **硬件灯效卸载**（Local7=1/2/3/7..0xB/0xE/0xF）：保留软件渲染。原因：硬件灯效会与 IdleDim、Schedule、AppProfile、TypingPulse、NotificationFlash、Music 全套上层调度冲突，引入"两套灯效引擎"协调成本远高于收益。
- **能力探测（CPKG / `GetDCHU_Data_Integer(0x02)`）**：本次不加。本机已经知道是单分区，能力包探测留给将来支持多分区机型时一起做。
- **真分区或单键**：硬件不支持，无法做。文档里把这一点写清楚。

## 设计

### 接口

`DchuKeyboardDevice` 改成：

```csharp
public sealed class DchuKeyboardDevice
{
    [DllImport("InsydeDCHU.dll")]
    private static extern int SetDCHU_Data(int command, byte[] buffer, int length);

    /// <summary>
    /// 把整块键盘面板设为指定颜色。
    /// 使用 SCMD 0x67 Local7=0 的 9-bit 三通道紧致编码（硬件原生 512 色量化）。
    /// </summary>
    public void SetColor(RgbColor color)
    {
        var packed = Pack9BitColor(color);          // 0..0xFFF
        var args = packed & 0xFFF;                  // bit 11..0
        var payload = BitConverter.GetBytes(args);  // 4 字节小端
        SetDCHU_Data(103, payload, 4);
    }

    /// <summary>
    /// 9-bit 三通道紧致颜色：每通道取最高 3 bit。
    /// 还原算法见 docs/reverse-engineering/dchu-protocol-findings.md「Local7=0 的颜色编码」。
    /// </summary>
    internal static int Pack9BitColor(RgbColor color)
    {
        var r3 = color.R >> 5;          // 8-bit -> 3-bit
        var g3 = color.G >> 5;
        var b3 = color.B >> 5;
        return (r3 & 0x07)              // bit 0..2
             | ((g3 & 0x07) << 3)        // bit 3..5
             | ((b3 & 0x07) << 6);       // bit 6..8
    }
}
```

`SetAllZones`、`SetZone`、`Zones` 全部删除。

### Worker 替换

`ColorfulLedKeyboard.Service\Worker.cs` 共 5 处调用，全部 `_device.SetAllZones(x)` → `_device.SetColor(x)`：

| 行 | 上下文 |
|----|------|
| 72 | `TryTurnOffKeyboard()` 里的关灯（黑色） |
| 108 | `RunEffectAsync` 主推送 |
| 171 | `RunMusicAsync` 主推送 |
| 338 | `FlashStartupAsync` 启动闪烁白色 |
| 340 | `FlashStartupAsync` 启动闪烁黑色 |

机械替换，无逻辑变化。

### ZoneTest 项目处理

`ColorfulLedKeyboard.ZoneTest` 整个项目删除：

1. 项目目录 `ColorfulLedKeyboard.ZoneTest/` 删除
2. `ColorfulLedKeyboard.slnx` 移除该项目引用
3. 跟该项目相关的发布脚本/CI 引用（如有）一并清理

理由：该项目唯一作用是验证 SetZone(1/2/3) 的分区效果，反向工程已经证实它测的是序列槽位写而非分区，留着只会误导。

### README 清理

`README.md` 第 23 行附近：

> 保留实验性分区控制测试工具。当前测试机不支持分区，分区灯效未进入主线功能。

删除。同时在「致谢」段落补一句说明：上游 `moshuiD/Colorful-Keyborad-Led-Color-Setting` 的 SetZone 实现实际是 SCMD 0x67 的多色序列槽位写，而非真分区接口；详见 `docs/reverse-engineering/dchu-protocol-findings.md`。

### 文档更新

`docs/reverse-engineering/dchu-protocol-findings.md` 已经写完整，本次只在文末加一节「实施记录」：

- 本次迁移的范围（接口、Worker、ZoneTest、README）
- `Pack9BitColor` 的完整推导链
- 哪些非目标被显式跳过（硬件亮度档/开关键/灯效卸载/CPKG），以及理由（YAGNI + 调度冲突）

### 测试

`ColorfulLedKeyboard.Tests` 当前没有 DchuKeyboardDevice 的测试。新增：

1. **`DchuKeyboardDeviceTests.Pack9BitColor_Black_ReturnsZero`** — 黑色 → 0
2. **`DchuKeyboardDeviceTests.Pack9BitColor_White_ReturnsAllBitsSet`** — 白色 → 0x1FF（9 bit 全 1）
3. **`DchuKeyboardDeviceTests.Pack9BitColor_PureRed_TopThreeBitsInLowField`** — 纯红 (0xFF, 0, 0) → 0x007
4. **`DchuKeyboardDeviceTests.Pack9BitColor_PureGreen_TopThreeBitsInMidField`** — 纯绿 → 0x038
5. **`DchuKeyboardDeviceTests.Pack9BitColor_PureBlue_TopThreeBitsInHighField`** — 纯蓝 → 0x1C0
6. **`DchuKeyboardDeviceTests.Pack9BitColor_QuantizesLowBits`** — (0x1F, 0x1F, 0x1F)（每通道低 5 bit）→ 0（量化掉）
7. **`DchuKeyboardDeviceTests.Pack9BitColor_PreservesTopThreeBitsPerChannel`** — (0xE0, 0xC0, 0xA0)（高 3 bit 分别是 0b111/0b110/0b101）→ 7 | (6<<3) | (5<<6) = 0x177

`Pack9BitColor` 标记为 `internal` 并通过 `[InternalsVisibleTo("ColorfulLedKeyboard.Tests")]`（如果 Core 项目还没加，本次添加）。

不写 `SetColor` 本身的集成测试——它依赖 `InsydeDCHU.dll` 和真实硬件，CI 跑不了。仍然依赖 ZoneTest 那种手动验证的位置由作者本机 Release 前手动跑一次确认即可（即：用 SetColor 设几个不同颜色，肉眼看键盘有没有正确响应）。

### 兼容性 & 用户感知

| 维度 | 改动前 | 改动后 |
|------|--------|--------|
| 用户配置文件格式 | 不变 | 不变 |
| 灯效行为 | 当前所有 7 种灯效 + 音乐 | 完全一致 |
| 颜色精度 | 看上去 24-bit 但 EC 仍按 mode 5 处理 | 显式 9-bit 量化（512 色），实际硬件能显示的颜色集合不变 |
| 亮度滑条 | 0-100 软件 scale | 不变 |
| Off 行为 | SetAllZones(Black) | SetColor(Black)，等价 |
| 应用场景/计划/空闲/敲字/通知/音乐 | 全部走软件渲染 + SetAllZones | 走软件渲染 + SetColor，无任何外部行为变化 |

**用户什么都看不出来**——这是预期。改动的是代码意图清晰度，不是行为。

### 风险

- **极低**：5 处 Worker 替换是机械的，灯效引擎一字不动。
- **唯一可观察风险**：`Pack9BitColor` 的位字段顺序如果错了，硬件会显示完全错误的颜色。**这是 7 个单元测试覆盖的核心**，本机 Release 前再用 SetColor 手动跑红/绿/蓝/白/黄/青/品红 7 个颜色肉眼确认即可。

## 实施步骤草稿（写入实现计划时细化）

1. 在 Core 项目里加 `InternalsVisibleTo("ColorfulLedKeyboard.Tests")`（如果没有）
2. 改写 `DchuKeyboardDevice.cs`：删除 SetZone/SetAllZones/Zones、新增 `Pack9BitColor` 和 `SetColor`
3. `Worker.cs`：5 处 `SetAllZones` → `SetColor`
4. 新增 `ColorfulLedKeyboard.Tests/DchuKeyboardDeviceTests.cs`，7 个测试
5. 删除 `ColorfulLedKeyboard.ZoneTest` 项目目录 + 从 slnx 移除引用
6. 更新 `README.md`：删除分区测试工具描述、补充致谢说明
7. 更新 `docs/reverse-engineering/dchu-protocol-findings.md`：附「实施记录」节
8. `dotnet build` + `dotnet test` 通过
9. 手动验证（作者本机）：跑一个 Static 灯效切红/绿/蓝/白，肉眼确认硬件响应正确
10. 提交：`refactor(core): replace fake zone API with single-surface SetColor (DCHU 9-bit native)`

## 拒收标准

- 任何用户可见行为发生变化（灯效、亮度、音乐、配置兼容性等）→ 重新走 brainstorming
- 7 个 Pack9BitColor 测试中有任何一个失败 → 修编码再发
- 手动验证发现颜色完全乱（不是 9-bit 量化的轻微差异，而是红变蓝之类的位字段错序）→ 立刻 revert
