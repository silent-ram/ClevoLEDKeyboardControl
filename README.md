# ClevoRGBControl

ClevoRGBControl 是一个面向 Clevo 兼容机型的键盘 RGB 灯效控制程序，采用 Windows 服务 + 托盘程序运行。

程序通过 `InsydeDCHU.dll` 调用厂商接口控制键盘灯。安装器会优先从 `C:\Program Files (x86)\ControlCenter` 自动复制该 DLL；如果复制失败，会使用安装包内置的备用 DLL。

## 功能

- Windows 服务后台控制键盘灯效。
- 托盘菜单快速切换灯效、亮度、速度和预设。
- 支持固定颜色、RGB 循环、单色呼吸、色彩序列、音乐模式、关闭灯效。
- 音乐模式支持按电平变色和仅亮度脉冲。
- 音乐模式内置流行、摇滚、经典、电子预设，并支持最多 8 个自定义预设。
- 支持敲字闪烁：输入时短暂提高键盘亮度。
- 支持空闲降亮和时间计划。
- 支持应用场景配置：按当前前台应用覆盖固定颜色或单色呼吸。
- 支持检查更新，可设置每天、每周、每月或从不检查。
- 设置页提供诊断信息，包括服务状态、驱动 DLL、前台应用、命中的应用场景和更新检查状态。
- 保留实验性分区控制测试工具。当前测试机不支持分区，分区灯效未进入主线功能。

## 安装

1. 从 Releases 下载 `ClevoRGBControlSetup.exe`。
2. 以管理员身份运行安装器。
3. 安装器会安装服务、启动托盘程序，并处理 `InsydeDCHU.dll`。

服务名称：

```text
ClevoRGBControlService
```

默认安装目录：

```text
C:\Program Files\ClevoRGBControl
```

配置目录：

```text
C:\ProgramData\ClevoRGBControl
```

## 卸载

可以在 Windows 设置 > 应用中卸载，或再次运行安装器选择卸载。

命令行卸载：

```powershell
ClevoRGBControlSetup.exe /uninstall
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

安装器输出：

```text
publish\ClevoRGBControlSetup.exe
```

## 致谢

本项目最初参考了 [moshuiD/Colorful-Keyborad-Led-Color-Setting](https://github.com/moshuiD/Colorful-Keyborad-Led-Color-Setting)。

原项目确认了通过 `InsydeDCHU.dll` 和 `SetDCHU_Data` 控制键盘灯的方式。ClevoRGBControl 在此基础上扩展为 Windows 服务、托盘程序、安装器、诊断页和更多灯效功能。

## 许可证

GPL-3.0，见 [LICENSE](LICENSE)。
