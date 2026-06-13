# ClevoLEDKeyboardControl

ClevoLEDKeyboardControl 是一个面向 Clevo 兼容机型的键盘 RGB 灯效控制程序，采用 Windows 服务 + 托盘程序运行。

> **Fork notice**：本项目基于 [xuha233/ClevoRGBControl](https://github.com/xuha233/ClevoRGBControl)（GPL-3.0）继续开发并改名。原始版权归原作者所有；自 2026-06-10 起在本仓库继续维护与修改，遵循 GPL-3.0 协议开源。

程序通过 `InsydeDCHU.dll` 调用厂商接口控制键盘灯。安装器会自动从安装包、安装器旁边、旧版安装目录和常见 Control Center 目录中查找该 DLL（来自 Clevo / 蓝天厂商提供的 Control Center 软件）。

## 功能

- Windows 服务后台控制键盘灯效。
- 托盘菜单快速切换灯效、亮度和预设。
- 支持固定颜色、RGB 循环、单色呼吸、循环呼吸、音乐模式、关闭灯效。
- RGB 循环使用停留时长控制；单色呼吸和循环呼吸使用呼吸周期控制。
- 音乐模式支持节拍脉冲、多色列表和自适应鼓点检测。
- 音乐模式内置“通用”预设，并支持最多 8 个命名自定义预设。
- 支持敲字闪烁：输入时短暂提高键盘亮度。
- 支持 Windows 通知闪烁：收到系统通知时短促闪烁键盘。
- 支持空闲降亮和时间计划。
- 支持应用场景配置：按当前前台应用覆盖固定颜色、单色呼吸或音乐模式。
- 支持检查更新，可设置每天、每周、每月或从不检查。
- 设置页提供诊断信息，包括服务状态、驱动 DLL、前台应用、命中的应用场景和更新检查状态。

## 驱动 DLL

`InsydeDCHU.dll` 是 Clevo / 蓝天厂商提供的私有驱动，**不随本仓库分发**。通常只要电脑安装过厂商的 Control Center / Control Center 3.0，安装器就会自动找到并复制它，用户不需要手动处理。

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

发布脚本会自动从本机 `assets\driver`、`CLEVO_DRIVER_DLL` 环境变量和常见 Control Center 目录中查找 `InsydeDCHU.dll`。找到就打包进安装器；找不到也会正常生成安装器，安装时再从用户机器的 Control Center 中自动复制。

安装器输出：

```text
publish\ClevoLEDKeyboardControlSetup.exe
```

## 致谢

- 本项目 fork 自 [xuha233/ClevoRGBControl](https://github.com/xuha233/ClevoRGBControl)，原作者实现了 Windows 服务 + 托盘 + 安装器 + 诊断页等完整框架。
- 原项目最初参考了 [moshuiD/Colorful-Keyborad-Led-Color-Setting](https://github.com/moshuiD/Colorful-Keyborad-Led-Color-Setting)，确认了通过 `InsydeDCHU.dll` 和 `SetDCHU_Data` 控制键盘灯的方式。
- 注：上游项目中的 `SetZone(1/2/3, color)` 接口在 P955ET1 这类单分区机型上实际是 SCMD 0x67 多色序列槽位写而非真分区，本项目已据反向工程结果重写为单一面板接口。完整协议分析见 [`docs/reverse-engineering/dchu-protocol-findings.md`](docs/reverse-engineering/dchu-protocol-findings.md)。

详细修改记录见 [NOTICE](NOTICE) 与 [CHANGES.md](CHANGES.md)。

## 许可证

GPL-3.0，见 [LICENSE](LICENSE)。本项目作为衍生作品整体以 GPL-3.0 继续开源，第三方厂商驱动 `InsydeDCHU.dll` 不在本仓库授权范围内，请自行从厂商获取。
