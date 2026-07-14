# ClevoLEDKeyboardControl

[![Latest release](https://img.shields.io/github/v/release/silent-ram/ClevoLEDKeyboardControl?display_name=tag)](https://github.com/silent-ram/ClevoLEDKeyboardControl/releases/latest)
[![License](https://img.shields.io/github/license/silent-ram/ClevoLEDKeyboardControl)](LICENSE)
[![Windows](https://img.shields.io/badge/Windows-10%20%2F%2011-0078D4)](#系统要求)

面向 Clevo / 蓝天及兼容机型的 Windows 键盘 RGB 控制工具，支持常用灯效、音乐律动、播放器封面取色、程序场景自动化、事件反馈和托盘快捷控制。

本项目基于 [xuha233/ClevoRGBControl](https://github.com/xuha233/ClevoRGBControl) 持续维护。程序采用“Windows 服务负责驱动键盘、托盘程序负责用户会话采集和交互”的架构，通过厂商 Control Center 附带的 `InsydeDCHU.dll` 调用底层键盘接口。

## 下载与安装

- [下载最新正式版安装包](https://github.com/silent-ram/ClevoLEDKeyboardControl/releases/latest/download/ClevoLEDKeyboardControlSetup.exe)
- [查看全部版本与发布说明](https://github.com/silent-ram/ClevoLEDKeyboardControl/releases)

安装步骤：

1. 下载并以管理员身份运行 `ClevoLEDKeyboardControlSetup.exe`。
2. 安装器会安装并启动键盘服务，同时启动当前用户的托盘程序。
3. 首次启动后，从系统托盘打开“设置”，选择灯效模式或音乐模式。

从旧版 `ClevoRGBControl` / `ColorfulLedKeyboard` 升级时，安装器会处理旧服务和注册表项。自动化和安全权限迁移前会分别保留配置备份。后续版本可直接覆盖安装：安装器会等待旧服务和托盘退出、修复数据目录权限、保留现有配置，并在安全通信可用后启动新托盘。

## 功能概览

### 设置界面与主题

- 提供 Windows 11 简洁风、白色科技风和柔和暖色风三套浅色主题，切换后立即应用到设置窗口、弹窗和托盘菜单。
- 当前状态首页集中显示实际运行模式、命中规则、有声程序、歌曲、事件反馈、服务状态和最终亮度。
- 最终亮度由服务根据灯效亮度、音乐动态范围、场景规则上限和空闲覆盖统一计算。
- 界面主题、窗口位置、尺寸、最后访问页面和高级参数展开状态保存在当前用户 `LocalAppData`。
- 支持 100%–200% Windows 显示缩放，导航、按钮和主要编辑区域会按 DPI 调整。

### 灯效模式

- 固定颜色、RGB 循环、单色呼吸、循环呼吸、脉冲、心跳和关闭灯光。
- 自定义多色序列，可配置停留时间、过渡时间和呼吸效果。
- 软件默认预设和自定义灯效预设使用稳定 ID，重命名不会破坏场景引用。
- 支持全局亮度以及空闲降亮、空闲关灯。

### 音乐模式

- 根据音频电平驱动键盘亮度和颜色，保留原有音乐分析算法与调节参数。
- 支持多色节奏、亮度脉冲、自适应鼓点检测、噪声门、灵敏度、响应与衰减速度、频段范围等设置。
- 支持扬声器、有线耳机、蓝牙耳机等 Windows 默认输出设备切换。
- 内置“通用”音乐预设，并支持最多 8 个自定义音乐预设。
- 音乐页面可以直接绑定正在运行的播放器；PID 仅用于首次确认，播放器重启后会按进程身份自动发现新 PID。
- 播放器绑定独立于场景自动化：关闭自动化后，手动音乐模式仍可使用指定程序音频和封面颜色。

### 歌曲封面颜色

- 通过 Windows 全局媒体会话读取歌曲、歌手和封面，不依赖 QQ 音乐、网易云、Spotify 等播放器的私有接口。
- 可以选择音乐预设颜色、封面主色或 3–5 色封面配色。
- 自动过滤透明、近黑、近白及低饱和像素，并增强过暗颜色在键盘上的可见度。
- 按歌曲缓存封面配色；切歌时保留上一组有效颜色，等待新封面到达后进行约 800ms 的平滑过渡。
- 音乐设置页会显示媒体会话匹配结果、当前歌曲、实际颜色值和配色色条。

封面取色依赖播放器向 Windows 提供媒体会话和缩略图。播放器只提供音频会话时仍可进行按程序音乐律动，但会自动回退音乐预设颜色。

### 场景自动化

自动化分为三类，基础优先级固定为：

```text
有声音乐程序
→ 前台灯效程序
→ 时间计划
→ 用户手动基础模式
→ 空闲降亮/关灯最终覆盖
```

- **音乐程序**：程序持续有声约 300ms 后进入，静音约 2 秒后退出；支持进程树、音乐预设、封面颜色、亮度上限和事件策略。
- **灯效程序**：根据前台程序切换灯效预设；音乐程序生效时不会抢走音乐模式，但可继续修饰亮度和事件反馈策略。
- **时间计划**：支持星期、普通时间段和跨午夜时间段。
- 多个音乐程序同时播放时，前台播放器优先；全部位于后台时按音乐规则列表顺序选择。
- 无规则命中时自动恢复用户手动基础设置。
- 亮度上限取音乐规则、前台灯效规则和空闲覆盖中的最小值。
- 内置场景模拟器可以在不改变灯光的情况下检查时间、前台程序、播放程序和空闲状态的最终匹配结果。
- 规则健康检查会提示预设丢失、进程为空、可能被前置规则覆盖及需要重新绑定的播放器。

### 事件反馈

- 敲字闪烁：输入时短暂提升键盘亮度，普通灯效和音乐模式均可使用。
- Windows 通知闪烁：收到系统通知时叠加闪烁提示。
- 音乐程序和灯效程序可以分别继承全局设置、强制开启或强制关闭事件反馈。
- 空闲关灯为最终覆盖，生效时会抑制包括通知闪烁在内的所有输出。

### 托盘快捷控制

右键托盘图标可以快速完成常用操作：

- 查看当前模式、播放器、歌曲、命中场景和封面状态。
- 启停场景自动化，以及单独启停音乐程序、灯效程序和时间计划规则。
- 切换无场景命中时使用的基础灯效或音乐预设。
- 绑定当前有声程序，切换音乐预设颜色、封面主色或封面配色。
- 开关敲字闪烁和通知闪烁。
- 调整普通模式基础亮度或音乐模式峰值亮度。
- 打开设置、诊断、配置目录，或重启键盘服务。

托盘图标悬停文字也会显示当前播放器、歌曲、场景和可用更新。

### 安全通信与配置恢复

- 托盘通过受 ACL 保护的本地命名管道向服务提交配置和用户会话状态，并校验协议版本、消息大小与活动用户会话。
- `C:\ProgramData\ClevoLEDKeyboardControl` 中的服务配置对普通用户只读，配置修改由服务原子写入。
- 更新检查状态保存在当前用户的 `LocalAppData`，不与系统级服务配置混用。
- 配置损坏时保留故障文件并优先恢复最近有效备份；高级设置支持导入、导出和手动恢复。
- 安全管道不可用时设置界面进入只读状态，不回退为直接修改受保护配置文件；服务恢复后可以重新保存。

### 自动更新提醒

- 默认每天自动检查一次更新，也可在设置中改为每周、每月或关闭。
- 托盘启动后自动检查，长期运行时会定时重新触发检查。
- 每个新版本只主动提示一次；选择“稍后”后，托盘菜单会继续保留下载入口。
- 更新检查通过 GitHub `releases/latest` 网页跳转识别版本，不使用受匿名请求限额影响的 GitHub REST API。

## 推荐使用流程

### 普通灯效

1. 打开设置并选择“灯效模式”。
2. 选择固定颜色、RGB 循环、呼吸、脉冲等效果。
3. 调整颜色、亮度和速度；需要长期复用时保存为自定义预设。

### 绑定音乐播放器和封面

1. 启动播放器并开始播放歌曲。
2. 进入“音乐”页面，点击“绑定正在播放的程序”。
3. 选择带 PID 和实时电平的播放器；主进程与音频子进程分离时会自动跟踪进程树。
4. 选择“封面主色”或“封面配色”。
5. 查看“媒体会话”状态；自动匹配失败时可从当前会话列表中手动选择。
6. 保存并切换到音乐模式。场景自动化可以保持关闭。

### 后台听歌、前台办公

1. 在“音乐程序”中绑定播放器，选择音乐预设和封面颜色来源。
2. 在“灯效程序”中添加 Word、IDE 或其他办公程序，设置亮度上限和事件策略。
3. 音乐播放时以音乐模式为基础；办公规则只叠加亮度、敲字和通知策略。
4. 音乐停止约 2 秒后，自动恢复前台灯效、时间计划或手动基础模式。

## 兼容性与限制

- 灯效程序按前台进程名匹配，不匹配窗口标题；音乐程序会保存进程名、程序路径和媒体会话身份用于重新发现。
- 浏览器按整个浏览器进程树处理，不能保证区分单个标签页。
- 普通音乐响应使用目标程序自己的音频会话电平；启用自适应频段分析时，频段部分仍使用系统混音，可能混入其他程序声音，诊断状态会明确标注。
- 封面颜色要求播放器向 Windows 全局媒体会话提供封面；不提供封面时自动使用音乐预设颜色。
- 托盘程序必须保持运行，才能采集用户会话中的前台程序、每程序音频和媒体封面。Windows 服务位于 Session 0，单独运行服务无法访问这些用户会话数据。
- 当前主要面向使用 `InsydeDCHU.dll` 的 Clevo / 蓝天兼容机型；不同厂商定制型号可能存在协议差异。

## 驱动 DLL

`InsydeDCHU.dll` 是厂商 Control Center 附带的私有驱动组件，不属于本仓库的 GPL-3.0 授权内容。

安装器会按顺序从以下位置查找：

1. 安装包 payload。
2. 安装器所在目录。
3. 旧版安装目录。
4. 常见的 `ControlCenter`、`Control Center`、`ControlCenter3` 目录。

找不到 DLL 时安装仍可完成，但服务无法控制键盘。安装厂商 Control Center 后重新运行安装器并选择修复即可。

## 系统要求

- Windows 10 / Windows 11 x64
- 支持对应机型的厂商 Control Center / `InsydeDCHU.dll`
- 从源码构建需要 .NET 8 SDK 或更新版本

服务名称：`ClevoLEDKeyboardControlService`

默认安装目录：`C:\Program Files\ClevoLEDKeyboardControl`

配置目录：`C:\ProgramData\ClevoLEDKeyboardControl`

当前用户更新状态目录：`%LocalAppData%\ClevoLEDKeyboardControl`

## 卸载

可以在 Windows“设置 → 应用”中卸载，或再次运行安装器选择卸载。

命令行卸载：

```powershell
ClevoLEDKeyboardControlSetup.exe /uninstall
```

## 构建与测试

```powershell
dotnet build .\ColorfulLedKeyboard.sln -c Release
dotnet test .\ColorfulLedKeyboard.sln -c Release
.\scripts\publish.ps1 -Configuration Release
```

仓库中的 `global.json` 固定兼容的 .NET 8 SDK。安装器输出位置：

```text
publish\ClevoLEDKeyboardControlSetup.exe
```

发布脚本会从 `assets\driver`、`CLEVO_DRIVER_DLL` 和常见 Control Center 目录查找 `InsydeDCHU.dll`。找不到时仍会生成安装器，并在用户安装阶段继续搜索。

## Fork 说明与致谢

本项目自 2026-06-10 起 fork 自 [xuha233/ClevoRGBControl](https://github.com/xuha233/ClevoRGBControl)（GPL-3.0）。原项目提供了 Windows 服务、托盘程序、安装器和基础键盘控制框架，本仓库在此基础上继续维护、重构并扩展功能。

原项目最初参考了 [moshuiD/Colorful-Keyborad-Led-Color-Setting](https://github.com/moshuiD/Colorful-Keyborad-Led-Color-Setting)，确认了通过 `InsydeDCHU.dll` 和 `SetDCHU_Data` 控制键盘灯的方式。

本仓库根据 P955ET1 等单分区机型的实际行为重新分析了 DCHU 协议。相关说明见 [`docs/reverse-engineering/dchu-protocol-findings.md`](docs/reverse-engineering/dchu-protocol-findings.md)。

详细修改记录见 [NOTICE](NOTICE) 与 [CHANGES.md](CHANGES.md)。

## 许可证

本项目作为衍生作品继续使用 GPL-3.0，详见 [LICENSE](LICENSE)。第三方厂商驱动 `InsydeDCHU.dll` 不在本仓库授权范围内，请从设备厂商获取。
