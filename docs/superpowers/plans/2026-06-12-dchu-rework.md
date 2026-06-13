# DCHU 硬件抽象层微创重构 实现计划

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** 把 `DchuKeyboardDevice` 的伪 zone API 替换为单一面板接口 `SetColor(RgbColor)`，使用 SCMD 0x67 Local7=0 的 9-bit 三通道紧致编码（硬件原生），同时清理 ZoneTest 项目和 README 中关于"分区"的误导性描述。

**Architecture:** 单文件硬件抽象层（`ColorfulLedKeyboard.Core/DchuKeyboardDevice.cs`）暴露 `SetColor` 与内部 `Pack9BitColor`；调用方 `Worker.cs` 的 5 处 `SetAllZones` 机械替换为 `SetColor`；上层灯效引擎、配置、调度、音乐等一字不动；`ColorfulLedKeyboard.ZoneTest` 项目整体删除。

**Tech Stack:** .NET 8 / C#（Windows 服务 + xUnit 测试），P/Invoke 调用 `InsydeDCHU.dll`，PowerShell（Windows 系统，不使用 Linux 命令）。

**关联文档：**
- 设计：[`docs/superpowers/specs/2026-06-12-dchu-rework-design.md`](../specs/2026-06-12-dchu-rework-design.md)
- 反向工程：[`docs/reverse-engineering/dchu-protocol-findings.md`](../../reverse-engineering/dchu-protocol-findings.md)

**重要约束：**
- 必须在 Windows 环境运行命令，使用 `pwsh` / `powershell` 或 git bash 兼容语法
- `dotnet test` 需要在 `.slnx` 解决方案根目录运行
- 测试不依赖真实硬件（`InsydeDCHU.dll` 不可用环境也要能跑）

---

## 任务概览

| # | 任务 | 涉及文件 |
|---|------|---------|
| 1 | 暴露 Core internals 给 Tests | `ColorfulLedKeyboard.Core/ColorfulLedKeyboard.Core.csproj` |
| 2 | 写 Pack9BitColor 失败测试 | `ColorfulLedKeyboard.Tests/DchuKeyboardDeviceTests.cs`（新建） |
| 3 | 实现 Pack9BitColor + SetColor，删除伪 zone API | `ColorfulLedKeyboard.Core/DchuKeyboardDevice.cs` |
| 4 | 验证 Pack9BitColor 测试全部通过 | 测试运行 |
| 5 | 替换 Worker.cs 的 5 处 SetAllZones → SetColor | `ColorfulLedKeyboard.Service/Worker.cs` |
| 6 | 全量构建 + 测试通过 | 解决方案 |
| 7 | 删除 ZoneTest 项目 + slnx 引用 | `ColorfulLedKeyboard.slnx`、`ColorfulLedKeyboard.ZoneTest/` |
| 8 | 清理 README.md 中"分区测试工具"描述、补致谢说明 | `README.md` |
| 9 | 反向工程文档追加「实施记录」节 | `docs/reverse-engineering/dchu-protocol-findings.md` |
| 10 | 最终构建 + 测试 + 提交 | 解决方案 |

---

## 文件结构

| 路径 | 责任 | 改动 |
|------|------|------|
| `ColorfulLedKeyboard.Core/DchuKeyboardDevice.cs` | DCHU 单一面板接口 | 重写 |
| `ColorfulLedKeyboard.Core/ColorfulLedKeyboard.Core.csproj` | Core 项目配置 | 加 `InternalsVisibleTo` |
| `ColorfulLedKeyboard.Tests/DchuKeyboardDeviceTests.cs` | 9-bit 颜色编码单元测试 | 新建 |
| `ColorfulLedKeyboard.Service/Worker.cs` | 后台服务循环 | 5 行替换 |
| `ColorfulLedKeyboard.slnx` | 解决方案 | 移除 ZoneTest 项目 |
| `ColorfulLedKeyboard.ZoneTest/` | 伪分区测试工具 | 整目录删除 |
| `README.md` | 用户说明 | 删除分区描述 + 补致谢 |
| `docs/reverse-engineering/dchu-protocol-findings.md` | 协议反向工程记录 | 追加「实施记录」节 |

---

### Task 1: 暴露 Core internals 给测试项目

**Files:**
- Modify: `ColorfulLedKeyboard.Core/ColorfulLedKeyboard.Core.csproj`

`Pack9BitColor` 计划标记为 `internal`，需要让 `ColorfulLedKeyboard.Tests` 程序集可见。

- [ ] **Step 1: 替换 csproj 内容**

打开 `D:\ClevoRGBControl\ColorfulLedKeyboard.Core\ColorfulLedKeyboard.Core.csproj`，把全文替换为：

```xml
<Project Sdk="Microsoft.NET.Sdk">

  <PropertyGroup>
    <TargetFramework>net8.0</TargetFramework>
    <ImplicitUsings>enable</ImplicitUsings>
    <Nullable>enable</Nullable>
  </PropertyGroup>

  <ItemGroup>
    <InternalsVisibleTo Include="ColorfulLedKeyboard.Tests" />
  </ItemGroup>

</Project>
```

- [ ] **Step 2: 构建验证 csproj 仍可解析**

运行（在仓库根 `D:\ClevoRGBControl`）：

```powershell
dotnet build .\ColorfulLedKeyboard.Core\ColorfulLedKeyboard.Core.csproj -c Debug
```

Expected: `Build succeeded`，`0 Warning(s)`，`0 Error(s)`。

- [ ] **Step 3: 提交**

```powershell
git add ColorfulLedKeyboard.Core/ColorfulLedKeyboard.Core.csproj
git commit -m "chore(core): expose internals to test assembly for upcoming DCHU rework"
```

---

### Task 2: 写 Pack9BitColor 的失败测试

**Files:**
- Create: `ColorfulLedKeyboard.Tests/DchuKeyboardDeviceTests.cs`

7 个测试覆盖：黑、白、纯红/绿/蓝（验证位字段顺序）、低位量化、混合通道高 3 bit。

- [ ] **Step 1: 新建测试文件**

创建 `D:\ClevoRGBControl\ColorfulLedKeyboard.Tests\DchuKeyboardDeviceTests.cs`，内容：

```csharp
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
```

- [ ] **Step 2: 运行测试验证它们失败**

```powershell
dotnet test .\ColorfulLedKeyboard.slnx -c Debug --filter "FullyQualifiedName~DchuKeyboardDeviceTests"
```

Expected: 编译失败（`'DchuKeyboardDevice' does not contain a definition for 'Pack9BitColor'`）。这是预期 — 还没实现。

- [ ] **Step 3: 暂不提交**

测试代码停留在工作树，等 Task 3 实现后一起验证再提交。

---

### Task 3: 实现 Pack9BitColor + SetColor，删除伪 zone API

**Files:**
- Modify: `ColorfulLedKeyboard.Core/DchuKeyboardDevice.cs`（全文重写）

按设计文档实现 9-bit 紧致颜色编码 + 单一 `SetColor` 接口。

- [ ] **Step 1: 重写 DchuKeyboardDevice.cs 全文**

把 `D:\ClevoRGBControl\ColorfulLedKeyboard.Core\DchuKeyboardDevice.cs` 全文替换为：

```csharp
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
        var packed = Pack9BitColor(color);
        var args = packed & 0xFFF; // bit 11..0
        var payload = BitConverter.GetBytes(args);
        SetDCHU_Data(SetDchuStaticColorCommand, payload, 4);
    }

    /// <summary>
    /// 9-bit 三通道紧致颜色编码，每通道取最高 3 bit。
    /// 位字段排布：bit 0..2 = R 高 3 bit，bit 3..5 = G 高 3 bit，bit 6..8 = B 高 3 bit。
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
```

变更点：
- 删除 `Zones` 数组、`SetZone(int, RgbColor)`、`SetAllZones(RgbColor)`
- 新增 `SetColor(RgbColor)` 走 SCMD 0x67 Local7=0
- 新增 `Pack9BitColor(RgbColor)` 内部静态方法（`internal` 让测试可见）
- 增加 `SetDchuStaticColorCommand = 103` 常量替代魔术数字

注意：暂时不要构建整个解决方案 — `Worker.cs` 还在调 `SetAllZones`，会编不过。下一步先验证 Core 项目本身能编译。

- [ ] **Step 2: 验证 Core 项目能单独编译**

```powershell
dotnet build .\ColorfulLedKeyboard.Core\ColorfulLedKeyboard.Core.csproj -c Debug
```

Expected: `Build succeeded`，`0 Error(s)`。

- [ ] **Step 3: 暂不提交**

等 Task 4 测试通过后一起提交核心改动。

---

### Task 4: 验证 Pack9BitColor 测试全部通过

**Files:**
- 测试运行（不改文件）

- [ ] **Step 1: 运行 DchuKeyboardDeviceTests**

```powershell
dotnet test .\ColorfulLedKeyboard.slnx -c Debug --filter "FullyQualifiedName~DchuKeyboardDeviceTests"
```

Expected: `Passed!  - Failed: 0, Passed: 7, Skipped: 0` — 7 个测试全过。

注意：此时 `Worker.cs` 仍引用已删除的 `SetAllZones`，整个解决方案构建会失败 — 但 Tests 项目只引用 Core，可以独立编译运行测试。如果上面命令因为 Service 项目编译失败而带崩，改为：

```powershell
dotnet test .\ColorfulLedKeyboard.Tests\ColorfulLedKeyboard.Tests.csproj -c Debug --filter "FullyQualifiedName~DchuKeyboardDeviceTests"
```

- [ ] **Step 2: 提交核心改动 + 测试**

```powershell
git add ColorfulLedKeyboard.Core/DchuKeyboardDevice.cs ColorfulLedKeyboard.Tests/DchuKeyboardDeviceTests.cs
git commit -m "refactor(core): replace fake zone API with single-surface SetColor (DCHU 9-bit native)"
```

---

### Task 5: 替换 Worker.cs 的 5 处 SetAllZones → SetColor

**Files:**
- Modify: `ColorfulLedKeyboard.Service/Worker.cs:72,108,171,338,340`

机械替换，无逻辑变化。

- [ ] **Step 1: 替换第 72 行（TryTurnOffKeyboard）**

打开 `D:\ClevoRGBControl\ColorfulLedKeyboard.Service\Worker.cs`，找到：

```csharp
            _device.SetAllZones(RgbColor.Black);
        }
        catch (Exception ex) when (ex is DllNotFoundException or EntryPointNotFoundException or SEHException)
        {
            _logger.LogWarning(ex, "Keyboard LEDs could not be turned off.");
```

把这一行的 `SetAllZones` 改为 `SetColor`：

```csharp
            _device.SetColor(RgbColor.Black);
```

- [ ] **Step 2: 替换第 108 行（RunEffectAsync 主推送）**

找到：

```csharp
            if (color != lastColor)
            {
                _device.SetAllZones(color);
                lastColor = color;
            }

            if (settings.Effect.Type is EffectType.Static or EffectType.Off)
```

替换：

```csharp
                _device.SetColor(color);
```

- [ ] **Step 3: 替换第 171 行（RunMusicAsync 主推送）**

找到：

```csharp
            if (color != lastColor)
            {
                _device.SetAllZones(color);
                lastColor = color;
            }

            await Task.Delay(music.IntervalMs, stoppingToken);
        }
    }
```

替换：

```csharp
                _device.SetColor(color);
```

- [ ] **Step 4: 替换第 338-340 行（FlashStartupAsync）**

找到：

```csharp
            for (var i = 0; i < 2; i++)
            {
                _device.SetAllZones(new RgbColor(255, 255, 255));
                await Task.Delay(120, stoppingToken);
                _device.SetAllZones(RgbColor.Black);
                await Task.Delay(120, stoppingToken);
            }
```

替换为：

```csharp
            for (var i = 0; i < 2; i++)
            {
                _device.SetColor(new RgbColor(255, 255, 255));
                await Task.Delay(120, stoppingToken);
                _device.SetColor(RgbColor.Black);
                await Task.Delay(120, stoppingToken);
            }
```

- [ ] **Step 5: grep 确认 Worker.cs 不再有 SetAllZones**

```powershell
Select-String -Path .\ColorfulLedKeyboard.Service\Worker.cs -Pattern "SetAllZones|SetZone\("
```

Expected: 无输出（命令在 Windows PowerShell 下）。如使用 git bash：

```bash
grep -nE "SetAllZones|SetZone\(" ColorfulLedKeyboard.Service/Worker.cs || echo "OK no matches"
```

Expected: `OK no matches`。

- [ ] **Step 6: 暂不提交**

等 Task 6 整体构建 + 全量测试通过再提交。

---

### Task 6: 全量构建 + 全量测试通过

**Files:**
- 解决方案构建（不改文件）

- [ ] **Step 1: 全量构建解决方案**

```powershell
dotnet build .\ColorfulLedKeyboard.slnx -c Release
```

Expected: 所有项目（Core / Service / Tests / Tray / Installer / ZoneTest — ZoneTest 此时还在）全部 `Build succeeded`，`0 Error(s)`。

注意：ZoneTest 引用了 `SetZone` / `SetAllZones`，**这一步会失败**。这是预期 — 在 Task 7 删除 ZoneTest 项目之前，整体构建会有 ZoneTest 这一个项目编译失败。先确认其他 5 个项目都成功：

```powershell
dotnet build .\ColorfulLedKeyboard.Core\ColorfulLedKeyboard.Core.csproj -c Release
dotnet build .\ColorfulLedKeyboard.Service\ColorfulLedKeyboard.Service.csproj -c Release
dotnet build .\ColorfulLedKeyboard.Tray\ColorfulLedKeyboard.Tray.csproj -c Release
dotnet build .\ColorfulLedKeyboard.Tests\ColorfulLedKeyboard.Tests.csproj -c Release
dotnet build .\ColorfulLedKeyboard.Installer\ColorfulLedKeyboard.Installer.csproj -c Release
```

Expected: 这 5 个项目分别构建成功。

- [ ] **Step 2: 全量测试**

```powershell
dotnet test .\ColorfulLedKeyboard.Tests\ColorfulLedKeyboard.Tests.csproj -c Release
```

Expected: 全部测试通过（含原有的 `CoreSettingsTests` + 新增的 `DchuKeyboardDeviceTests` 7 个）。

- [ ] **Step 3: 提交 Worker 替换**

```powershell
git add ColorfulLedKeyboard.Service/Worker.cs
git commit -m "refactor(service): switch Worker to SetColor (DCHU single-surface API)"
```

---

### Task 7: 删除 ZoneTest 项目 + 从 slnx 移除引用

**Files:**
- Modify: `ColorfulLedKeyboard.slnx`
- Delete: `ColorfulLedKeyboard.ZoneTest/`（整目录）

ZoneTest 项目验证的是"伪 zone"的分区效果，反向工程已经证实它测的是序列槽位写而非真分区，留着会误导。

- [ ] **Step 1: 从 slnx 移除 ZoneTest 引用**

把 `D:\ClevoRGBControl\ColorfulLedKeyboard.slnx` 全文替换为：

```xml
<Solution>
  <Project Path="ColorfulLedKeyboard.Core/ColorfulLedKeyboard.Core.csproj" />
  <Project Path="ColorfulLedKeyboard.Installer/ColorfulLedKeyboard.Installer.csproj" />
  <Project Path="ColorfulLedKeyboard.Service/ColorfulLedKeyboard.Service.csproj" />
  <Project Path="ColorfulLedKeyboard.Tests/ColorfulLedKeyboard.Tests.csproj" />
  <Project Path="ColorfulLedKeyboard.Tray/ColorfulLedKeyboard.Tray.csproj" />
</Solution>
```

（删除 `<Project Path="ColorfulLedKeyboard.ZoneTest/ColorfulLedKeyboard.ZoneTest.csproj" />` 那一行）

- [ ] **Step 2: 删除 ZoneTest 目录**

```powershell
Remove-Item -Recurse -Force .\ColorfulLedKeyboard.ZoneTest
```

如果使用 git bash：

```bash
rm -rf ColorfulLedKeyboard.ZoneTest
```

- [ ] **Step 3: 验证 grep 全仓不再有 ZoneTest 引用（除 git 历史）**

```powershell
Select-String -Path .\* -Pattern "ZoneTest" -Recurse -SimpleMatch | Where-Object { $_.Path -notlike "*\.git\*" -and $_.Path -notlike "*\bin\*" -and $_.Path -notlike "*\obj\*" -and $_.Path -notlike "*docs\*" }
```

Expected: 无输出（docs 下的反向工程 / 设计文档可能提到 ZoneTest 是历史背景，被过滤排除）。

或 git bash：

```bash
git grep -n "ZoneTest" -- ':!docs/**' ':!**/bin/**' ':!**/obj/**' || echo "OK no remaining references"
```

Expected: `OK no remaining references`。

- [ ] **Step 4: 全量构建确认 slnx 仍正确**

```powershell
dotnet build .\ColorfulLedKeyboard.slnx -c Release
```

Expected: 5 个项目全部 `Build succeeded`，0 Errors。

- [ ] **Step 5: 提交**

```powershell
git add -A
git commit -m "chore: remove ColorfulLedKeyboard.ZoneTest project (was testing fake zones)"
```

---

### Task 8: 清理 README.md 中"分区测试工具"描述、补致谢说明

**Files:**
- Modify: `README.md`（第 23 行 + 致谢段）

- [ ] **Step 1: 删除"实验性分区控制测试工具"那行**

打开 `D:\ClevoRGBControl\README.md`，找到（第 23 行附近）：

```markdown
- 设置页提供诊断信息，包括服务状态、驱动 DLL、前台应用、命中的应用场景和更新检查状态。
- 保留实验性分区控制测试工具。当前测试机不支持分区，分区灯效未进入主线功能。
```

把第二行整行删除，只保留"设置页提供诊断信息..."那一行。

- [ ] **Step 2: 在致谢段补充上游 SetZone 误用说明**

找到致谢段（"## 致谢"附近）：

```markdown
- 原项目最初参考了 [moshuiD/Colorful-Keyborad-Led-Color-Setting](https://github.com/moshuiD/Colorful-Keyborad-Led-Color-Setting)，确认了通过 `InsydeDCHU.dll` 和 `SetDCHU_Data` 控制键盘灯的方式。
```

在它下面**新增一行**说明：

```markdown
- 原项目最初参考了 [moshuiD/Colorful-Keyborad-Led-Color-Setting](https://github.com/moshuiD/Colorful-Keyborad-Led-Color-Setting)，确认了通过 `InsydeDCHU.dll` 和 `SetDCHU_Data` 控制键盘灯的方式。
- 注：上游项目中的 `SetZone(1/2/3, color)` 接口在 P955ET1 这类单分区机型上实际是 SCMD 0x67 多色序列槽位写而非真分区，本项目已据反向工程结果重写为单一面板接口。完整协议分析见 [`docs/reverse-engineering/dchu-protocol-findings.md`](docs/reverse-engineering/dchu-protocol-findings.md)。
```

- [ ] **Step 3: 验证 README 不再提到"分区"作为可用功能**

```powershell
Select-String -Path .\README.md -Pattern "分区"
```

Expected: 唯一保留的提到"分区"的行只有致谢段那一句解释（"实际是...而非真分区，本项目已据反向工程..."）。如果还有别的"分区"出现，回头清理。

- [ ] **Step 4: 提交**

```powershell
git add README.md
git commit -m "docs(readme): drop fake-zone description, clarify upstream SetZone caveat"
```

---

### Task 9: 反向工程文档追加「实施记录」节

**Files:**
- Modify: `docs/reverse-engineering/dchu-protocol-findings.md`（追加节）

- [ ] **Step 1: 在文末追加「八、实施记录（2026-06-12）」**

打开 `D:\ClevoRGBControl\docs\reverse-engineering\dchu-protocol-findings.md`，在文件末尾追加：

```markdown

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
```

- [ ] **Step 2: 提交**

```powershell
git add docs/reverse-engineering/dchu-protocol-findings.md
git commit -m "docs(reverse-engineering): record DCHU rework implementation log"
```

---

### Task 10: 最终构建 + 全量测试 + 总结

**Files:**
- 全量构建（不改文件）

- [ ] **Step 1: 干净构建**

```powershell
dotnet clean .\ColorfulLedKeyboard.slnx -c Release
dotnet build .\ColorfulLedKeyboard.slnx -c Release
```

Expected: 5 个项目（Core / Installer / Service / Tests / Tray）全部 `Build succeeded`，`0 Warning(s)`，`0 Error(s)`。

- [ ] **Step 2: 全量测试**

```powershell
dotnet test .\ColorfulLedKeyboard.slnx -c Release
```

Expected: 所有测试通过（原 `CoreSettingsTests` 全部 + 新增 `DchuKeyboardDeviceTests` 7 个）。

- [ ] **Step 3: 检查工作树干净**

```powershell
git status
```

Expected: `nothing to commit, working tree clean`。如还有未提交改动，对照前面任务回头补提交。

- [ ] **Step 4: 列出本次实现的提交**

```powershell
git log --oneline 47457a8..HEAD
```

Expected: 看到大约 6 条提交，按顺序：
1. `docs(specs): add DCHU hardware abstraction rework design`（已存在）
2. `chore(core): expose internals to test assembly for upcoming DCHU rework`
3. `refactor(core): replace fake zone API with single-surface SetColor (DCHU 9-bit native)`
4. `refactor(service): switch Worker to SetColor (DCHU single-surface API)`
5. `chore: remove ColorfulLedKeyboard.ZoneTest project (was testing fake zones)`
6. `docs(readme): drop fake-zone description, clarify upstream SetZone caveat`
7. `docs(reverse-engineering): record DCHU rework implementation log`

- [ ] **Step 5: 手动验证清单（作者本机 Release 前）**

⚠️ 自动化测试覆盖不到真实硬件 — 这一步必须由作者在装有 `InsydeDCHU.dll` 的目标机器上手动跑一次：

1. 把构建好的 Service exe 部署上机，启动服务
2. 通过托盘把灯效切到 Static
3. 把颜色依次设为：红 (255,0,0) / 绿 (0,255,0) / 蓝 (0,0,255) / 白 (255,255,255) / 黄 (255,255,0) / 青 (0,255,255) / 品红 (255,0,255)
4. 每次切换观察键盘 — 颜色应该正确响应（9-bit 量化的轻微差异可接受，但红/绿/蓝绝对不能错位）
5. 切到 Off，键盘应黑屏
6. 切到 Rainbow，跑 30 秒确认色相循环正常

如果第 4 步发现颜色错位（例如设红色但显示蓝色），说明 `Pack9BitColor` 位字段顺序错了，立即 revert 第 3 条提交，回头查 DSDT 的 SCMD 0x67 Local7=0 实际编码并修正测试值。

---

## 自审

### 规格覆盖

| Spec 要求 | 任务 |
|----------|------|
| 删除虚假的 zone 接口（SetZone/Zones/SetAllZones） | Task 3 |
| 单一接口 SetColor(RgbColor) 走 Local7=0 9-bit | Task 3 |
| 不动任何上层功能 | Task 5（机械替换） |
| 清理 ZoneTest 项目 | Task 7 |
| 清理 README 误导描述 + 补致谢 | Task 8 |
| 反向工程文档「实施记录」节 | Task 9 |
| 7 个 Pack9BitColor 单元测试 | Task 2 |
| `InternalsVisibleTo` Tests | Task 1 |
| 手动颜色验证 | Task 10 Step 5 |
| 提交粒度 | 每个 Task 末尾分别提交 |

非目标（硬件亮度档/开关键/灯效卸载/CPKG/真分区）显式跳过 — 在 Task 9 实施记录里说明。

### 类型一致性

- `DchuKeyboardDevice.SetColor(RgbColor)` 在 Task 3 定义，Task 5 调用 — ✓
- `DchuKeyboardDevice.Pack9BitColor(RgbColor) → int` 在 Task 3 定义，Task 2 测试调用 — ✓
- 测试方法名前后一致：`Pack9BitColor_Black_ReturnsZero`/`_White_ReturnsAllNineBitsSet`/`_PureRed_PutsTopThreeBitsInLowField` 等 — ✓
- `RgbColor` 构造 `new RgbColor(R, G, B)` — 在 Worker.cs 已存在用法（`new RgbColor(255, 255, 255)`），Task 2 沿用 — ✓

### 占位符扫描

- 无 TBD/TODO/"实现细节" — ✓
- 每个代码步都有完整代码块 — ✓
- 命令都给出 expected 输出 — ✓
- Worker.cs 的 5 处替换都给出了周围的上下文，便于精确定位 — ✓
