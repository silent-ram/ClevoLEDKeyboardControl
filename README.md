# ClevoLEDKeyboardControl

ClevoLEDKeyboardControl 是基于 [xuha233/ClevoRGBControl](https://github.com/xuha233/ClevoRGBControl) 继续维护的 Clevo / 蓝天兼容机型键盘 RGB 控制工具。

本仓库在原项目基础上做了系统性的工程化重构和功能增强：重构键盘灯效输出逻辑，完善安装器和驱动 DLL 发现流程，扩展托盘设置界面，并重点优化音乐模式、耳机音频源切换、自适应鼓点检测、应用场景配置和诊断信息。

项目仍采用 Windows 服务 + 托盘程序的架构：服务负责实际控制键盘灯，托盘程序负责设置、状态显示和用户交互。

程序通过厂商 Control Center 附带的 `InsydeDCHU.dll` 调用底层接口控制键盘灯；该 DLL 的查找和授权边界见下方“驱动 DLL”章节。

## 相比上游的主要改动

本仓库不是简单改名版本，而是在 fork 基础上继续做了系统性的重构、稳定性修复和功能扩展：

- **协议与输出逻辑修正**：根据实际协议分析，修正单分区机型的灯效输出逻辑，避免把多色序列槽位误认为多分区控制。
- **音乐模式增强**：支持扬声器、有线/蓝牙耳机等默认音频设备切换；提供节拍脉冲、多色列表、自适应鼓点检测，以及灵敏度、节拍阈值、噪声门和频段参考等参数。
- **自适应鼓点检测优化**：重构音频分析流程，改善暂停后恢复、耳机下弱节拍识别和静音状态下的稳定性，并通过 PCM buffer 复用减少长期运行时的 GC 抖动。
- **托盘设置页扩展**：增加音乐预设、应用场景配置、空闲降亮、时间计划、通知闪烁、更新检查和诊断信息。
- **安装和升级体验优化**：安装器会自动查找 `InsydeDCHU.dll`，并处理旧版服务、注册表项和升级路径。
- **测试和维护性改进**：补充核心设置、音频源状态、音乐模式和 DCHU 数据结构测试；音乐模式新增基于合成 PCM 的回归测试，降低后续算法调整风险。

## 功能

### 灯效控制

- 固定颜色、RGB 循环、单色呼吸、循环呼吸、关闭灯效。
- 自定义多色序列，支持停留时长、过渡时间和呼吸效果。
- 敲字闪烁：输入时短暂提高键盘亮度。
- Windows 通知闪烁：收到系统通知时短促闪烁键盘。

### 音乐模式

- 支持根据系统音频电平驱动灯光，并可选择是否跟随系统主音量。
- 支持节拍脉冲、多色列表和自适应鼓点检测。
- 支持灵敏度、节拍阈值、噪声门、响应速度、衰减速度、低频/高频参考等参数。
- 支持扬声器、有线耳机、蓝牙耳机等默认音频设备切换。
- 内置“通用”音乐预设，并支持最多 8 个自定义音乐预设。

### 自动化与诊断

- 支持空闲降亮、时间计划。
- 支持按前台应用自动切换灯效配置。
- 支持检查更新。
- 设置页提供服务状态、驱动 DLL、当前音频源、前台应用、应用场景命中情况和更新状态。

## 驱动 DLL

`InsydeDCHU.dll` 是 Clevo / 蓝天厂商 Control Center 附带的私有驱动 DLL，**本仓库不会重新分发该文件**。

如果发布者本地打包时提供了 DLL，安装包可能包含内置 payload；否则安装器会从用户机器上的 Control Center 中查找并复制。通常只要电脑安装过厂商的 Control Center / Control Center 3.0，安装器就能自动找到它，用户不需要手动处理。

安装器会按顺序搜索：

- 安装包内置 payload。
- 安装器所在目录。
- 旧版 `ClevoRGBControl` / `ColorfulLedKeyboard` 安装目录。
- `C:\Program Files (x86)\ControlCenter`
- `C:\Program Files\ControlCenter`
- `C:\Program Files (x86)\Control Center`
- `C:\Program Files\Control Center`
- `ControlCenter3` / `Control Center 3.0` 等常见目录。

如果仍然找不到 DLL，安装可以完成，但服务无法控制键盘灯。此时安装厂商 Control Center 后重新运行安装器选择修复即可。

## 安装

1. 从 Releases 下载 `ClevoLEDKeyboardControlSetup.exe`。
2. 以管理员身份运行安装器。
3. 安装器会安装服务、启动托盘程序，并尝试处理 `InsydeDCHU.dll`。

服务名称：

```text
ClevoLEDKeyboardControlService
```

默认安装目录：

```text
C:\Program Files\ClevoLEDKeyboardControl
```

配置目录：

```text
C:\ProgramData\ClevoLEDKeyboardControl
```

> 从旧版 `ClevoRGBControl` / `ColorfulLedKeyboard` 升级时，安装器会自动停用并清理同名旧服务与注册表项，不需要手动卸载。

## 卸载

可以在 Windows 设置 > 应用中卸载，或再次运行安装器选择卸载。

命令行卸载：

```powershell
ClevoLEDKeyboardControlSetup.exe /uninstall
```

## 构建

环境要求：

- Windows 10/11 x64
- .NET 8 SDK 或更新版本

构建和测试：

```powershell
dotnet build .\ColorfulLedKeyboard.slnx -c Release
dotnet test .\ColorfulLedKeyboard.slnx -c Release
```

生成安装器：

```powershell
.\scripts\publish.ps1 -Configuration Release
```

如果本机 .NET SDK 不支持 `.slnx`，请直接使用 `scripts\publish.ps1 -Configuration Release` 构建发布包。

发布脚本会自动从本机 `assets\driver`、`CLEVO_DRIVER_DLL` 环境变量和常见 Control Center 目录中查找 `InsydeDCHU.dll`。找到就打包进安装器；找不到也会正常生成安装器，安装时再从用户机器的 Control Center 中自动复制。

安装器输出：

```text
publish\ClevoLEDKeyboardControlSetup.exe
```

## Fork 说明与致谢

本项目自 2026-06-10 起 fork 自 [xuha233/ClevoRGBControl](https://github.com/xuha233/ClevoRGBControl)（GPL-3.0）。原项目实现了 Windows 服务、托盘程序、安装器和基础键盘控制框架，本仓库在此基础上继续维护、重构并扩展功能。

原项目最初参考了 [moshuiD/Colorful-Keyborad-Led-Color-Setting](https://github.com/moshuiD/Colorful-Keyborad-Led-Color-Setting)，确认了通过 `InsydeDCHU.dll` 和 `SetDCHU_Data` 控制键盘灯的方式。

本仓库根据 P955ET1 等单分区机型的实际行为重新分析了 DCHU 协议：上游的 `SetZone(1/2/3, color)` 在这类机型上实际对应 SCMD 0x67 多色序列槽位写入，而不是真正的多分区控制。相关分析见 [`docs/reverse-engineering/dchu-protocol-findings.md`](docs/reverse-engineering/dchu-protocol-findings.md)。

原始版权归原作者所有；本仓库作为衍生作品继续遵循 GPL-3.0 协议开源。

详细修改记录见 [NOTICE](NOTICE) 与 [CHANGES.md](CHANGES.md)。

## 许可证

GPL-3.0，见 [LICENSE](LICENSE)。本项目作为衍生作品整体以 GPL-3.0 继续开源，第三方厂商驱动 `InsydeDCHU.dll` 不在本仓库授权范围内，请自行从厂商获取。
