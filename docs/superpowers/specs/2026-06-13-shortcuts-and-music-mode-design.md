# 快捷方式 + 音乐模式互斥重构 — 设计

> 日期：2026-06-13
> 适用项目：ClevoLEDKeyboardControl
> 上下文：用户两个独立但密切相关的需求，决定打包成一次重构（spec/plan/实施都合并）

## 背景与上下文

### 需求 1：快捷方式入口缺失

当前安装器（`ColorfulLedKeyboard.Installer/Program.cs`）**完全不创建任何 `.lnk` 快捷方式**。它只做：

- 注册表 `HKCU\...\Run\ClevoLEDKeyboardControl` 写入开机自启
- 注册表 `HKLM\...\Uninstall\ClevoLEDKeyboardControl` 写入控制面板卸载入口
- `sc.exe create/start` 注册并启动 Windows 服务

**问题：** 用户从托盘菜单点"退出托盘"后，要重新启动托盘程序就**找不到任何入口**。Tray 程序装在 `C:\Program Files\ClevoLEDKeyboardControl\Tray\ColorfulLedKeyboard.Tray.exe`，普通用户不会去翻这个目录。重启电脑会通过 Run 注册表项自动拉起，但用户可能不想等到下次重启。

### 需求 2：音乐模式与灯效互斥

当前 `EffectType` 枚举把 `Music = 5` 和 `Static/Rainbow/Breathing/Sequence/Pulse/Heartbeat/Off` 平级混在同一个枚举里：

```csharp
public enum EffectType
{
    Static = 0,    Rainbow = 1,    Breathing = 2,    Sequence = 3,
    Off = 4,       Music = 5,      Pulse = 6,        Heartbeat = 7
}
```

这是一个**维度混淆**——「使用音乐响应」是一个**模式开关**（用与不用），而「Static/Rainbow/Breathing/...」是**灯效选择**（具体显示哪种动画）。两者本应是不同维度，混在一个枚举里：

- 设置窗"常规"页 ComboBox 列出 8 项，"音乐模式"夹在灯效中间
- 但点中"音乐模式"后所有常规页参数（颜色、周期、序列、最低亮度等）全部隐藏，强制跳到"音乐"页配置——本质上是已经承认"音乐是另一种模式"
- 托盘菜单的"效果 ▶"子菜单也把"音乐模式"列为效果之一，但行为上又特殊（亮度滑条置灰、关掉单击勾选状态切换逻辑）

用户反馈："音乐模式不再显示在常规中，这两个变成互斥关系"。

### 决策汇总（与用户达成的 7 个决策点）

| # | 决策 | 选择 |
|---|------|------|
| Q1.1 | 快捷方式行为 | 启动托盘 + 自动打开设置窗（用 `--settings` 参数）|
| Q1.2 | 创建位置 | 开始菜单（全机）+ 公共桌面 |
| Q1.3 | 卸载快捷方式 | **不加**，只放主程序 |
| Q1.4 | 技术方案 | WScript.Shell COM |
| Q2.1 | 互斥 UI | 常规页顶部「模式」单选 + 托盘菜单"音乐模式"提级 |
| Q2.2 | 枚举处理 | **彻底移除** `EffectType.Music`，新增 `OperatingMode { Lighting, Music }` |
| Q2.3 | Off 归属 | 保留为 `EffectType.Off`，不参与模式互斥 |
| Q2.4 | 迁移策略 | 软件内 `Normalize()` 自动迁移，settings.json 不替换；老 `Effect.Type=Music` → `OperatingMode=Music + Effect.Type=Static + Effect.Music 不动`；同时设置 `SavedEffects.LastUsedLightingEffect=Static` |

## 设计目标

1. **修复用户体验断点**：让用户退出托盘后能从开始菜单/桌面快捷方式重新启动
2. **澄清概念**：音乐模式与灯效是不同维度，UI 和数据模型都要反映这个事实
3. **零数据丢失**：升级后用户的所有自定义配置（预设、规则、参数、当前状态）完整保留并自动迁移
4. **代码意图清晰**：`OperatingMode` 顶层字段一目了然地表达"现在跑哪条路径"，Worker 分支基于它而非 `Effect.Type`

## 非目标（YAGNI 显式跳过）

- **WiX/Inno/NSIS 重写安装器**：只在现有 WinForms 安装器里加 ~50 行 COM 代码，不引入新工具链
- **快捷方式自定义图标管理**：直接复用 Tray.exe 自带图标
- **保留 `EffectType.Music` 作过渡**：彻底移除，不留半截枚举值（迁移逻辑一次性完成）
- **应用场景与自动化的功能扩展**（需求 3、4）：本次只做需求 1+2，需求 3+4 等用户后续讨论
- **Linux/macOS 兼容**：本项目只 Windows
- **快捷方式国际化**：固定中文标签 `ClevoLEDKeyboardControl`

## 设计

### 一、快捷方式（需求 1）

#### 1.1 创建位置

```
开始菜单（全机）：
  %ALLUSERSPROFILE%\Microsoft\Windows\Start Menu\Programs\ClevoLEDKeyboardControl.lnk
  即 C:\ProgramData\Microsoft\Windows\Start Menu\Programs\ClevoLEDKeyboardControl.lnk

公共桌面（全机）：
  %PUBLIC%\Desktop\ClevoLEDKeyboardControl.lnk
  即 C:\Users\Public\Desktop\ClevoLEDKeyboardControl.lnk
```

**注意**：开始菜单**直接放 .lnk 在 Programs 根目录**，不创建子文件夹（Q1.3 决定不加卸载快捷方式 → 不需要文件夹来分组）。

#### 1.2 快捷方式属性

| 属性 | 值 |
|------|----|
| TargetPath | `C:\Program Files\ClevoLEDKeyboardControl\Tray\ColorfulLedKeyboard.Tray.exe` |
| Arguments | `--settings`（启动后自动打开设置窗，Q1.1）|
| WorkingDirectory | `C:\Program Files\ClevoLEDKeyboardControl\Tray\` |
| IconLocation | `<TargetPath>,0`（exe 自带图标）|
| Description | `ClevoLEDKeyboardControl - 键盘 RGB 灯控制` |
| WindowStyle | `1`（normal）|

#### 1.3 技术实现：WScript.Shell COM

.NET 不需要引入第三方依赖，通过 `Type.GetTypeFromProgID("WScript.Shell")` + `dynamic` 调 COM：

```csharp
private static void CreateShortcut(string lnkPath, string targetPath, string arguments,
    string workingDirectory, string description)
{
    var shellType = Type.GetTypeFromProgID("WScript.Shell")
        ?? throw new InvalidOperationException("WScript.Shell COM is not available.");
    dynamic shell = Activator.CreateInstance(shellType)!;
    try
    {
        dynamic shortcut = shell.CreateShortcut(lnkPath);
        try
        {
            shortcut.TargetPath = targetPath;
            shortcut.Arguments = arguments;
            shortcut.WorkingDirectory = workingDirectory;
            shortcut.Description = description;
            shortcut.IconLocation = $"{targetPath},0";
            shortcut.WindowStyle = 1;
            shortcut.Save();
        }
        finally { Marshal.FinalReleaseComObject(shortcut); }
    }
    finally { Marshal.FinalReleaseComObject(shell); }
}
```

需要 `using System.Runtime.InteropServices;`，已有。

**为什么不用 IShellLink P/Invoke**：WScript.Shell 是 Windows 自 Win98 起内置的 COM 组件，跨所有版本稳定，代码量约为 P/Invoke 方案的 1/10。

#### 1.4 安装时机

在 `Install()` 方法的最后一步（`RegisterUninstaller()` 之后）调用 `CreateUserShortcuts()`：

```csharp
private static void Install()
{
    // ... 现有逻辑 ...
    AddTrayStartup();
    RegisterUninstaller();
    CreateUserShortcuts();  // ← 新增
}
```

#### 1.5 卸载时清理

`Uninstall()` 中追加 `RemoveUserShortcuts()`：

```csharp
private static void Uninstall(bool keepSettings)
{
    StopAndDeleteServiceIfPresent(ServiceName);
    StopAndDeleteServiceIfPresent(LegacyServiceName);
    StopAndDeleteServiceIfPresent(LegacyServiceNameClevoRgb);
    RemoveTrayStartup();
    RemoveUserShortcuts();  // ← 新增
    UnregisterUninstaller();
    KillTray();
    // ... 其余逻辑 ...
}
```

`RemoveUserShortcuts()` 只是 `File.Delete(lnkPath)` 包了 try/catch（IO/Unauthorized/FileNotFound 都吞掉，因为卸载流程不能被快捷方式删除失败阻塞）。

#### 1.6 路径常量

```csharp
private static readonly string StartMenuShortcutPath = Path.Combine(
    Environment.GetFolderPath(Environment.SpecialFolder.CommonStartMenu),
    "Programs",
    "ClevoLEDKeyboardControl.lnk");

private static readonly string DesktopShortcutPath = Path.Combine(
    Environment.GetFolderPath(Environment.SpecialFolder.CommonDesktopDirectory),
    "ClevoLEDKeyboardControl.lnk");
```

`SpecialFolder.CommonStartMenu` = `C:\ProgramData\Microsoft\Windows\Start Menu`
`SpecialFolder.CommonDesktopDirectory` = `C:\Users\Public\Desktop`

两者都需要管理员权限写入；安装器已经走 `requireAdministrator`，正好符合。

---

### 二、音乐模式与灯效互斥（需求 2）

#### 2.1 数据模型变更

**新增枚举** `ColorfulLedKeyboard.Core/OperatingMode.cs`：

```csharp
namespace ColorfulLedKeyboard.Core;

public enum OperatingMode
{
    Lighting = 0,  // 默认值（未指定时）
    Music = 1
}
```

**移除** `EffectType.Music`（值 5）：

```csharp
// 删前
public enum EffectType
{
    Static = 0, Rainbow = 1, Breathing = 2, Sequence = 3,
    Off = 4, Music = 5, Pulse = 6, Heartbeat = 7
}

// 删后
public enum EffectType
{
    Static = 0, Rainbow = 1, Breathing = 2, Sequence = 3,
    Off = 4, Pulse = 6, Heartbeat = 7
}
```

**保留 `EffectType.Pulse = 6` 的整数值**：不重新编号 Pulse=5、Heartbeat=6，因为旧 settings.json 里如果存了 `Pulse` 字符串名称是按枚举名匹配的（`JsonStringEnumConverter`），值会被正确反序列化；保留原数值还能让旧版本在不慎读到新 JSON 时显式失败而非误读为另一种灯效。**枚举数值的稳定性是兼容性契约。**

`KeyboardMode` 枚举里也存在 `Music`，按相同方式移除（顺带保留枚举名稳定）。

**`KeyboardSettings` 新增字段**：

```csharp
public sealed class KeyboardSettings
{
    public bool Enabled { get; set; } = true;
    public OperatingMode OperatingMode { get; set; } = OperatingMode.Lighting;  // ← 新增
    // ... 其余字段不变 ...
}
```

**`EffectMemorySettings` 新增字段**：

```csharp
public sealed class EffectMemorySettings
{
    // ... 现有的 Static/Rainbow/Breathing/Sequence/Pulse/Heartbeat 子记忆 ...
    public EffectType LastUsedLightingEffect { get; set; } = EffectType.Static;  // ← 新增
}
```

#### 2.2 迁移逻辑（`KeyboardSettings.Normalize()`）

迁移触发：检测到 `Effect.Type` 在反序列化后是无效枚举值（即旧的 5），或检测到 `Mode` 字段是已删除的 `KeyboardMode.Music`：

```csharp
public KeyboardSettings Normalize(bool migrateLegacyMode = false)
{
    Brightness = Math.Clamp(Brightness, 0, 100);
    // ...

    Effect ??= new LightingEffectSettings();

    // ★ 迁移：旧版本 Effect.Type == Music（枚举值 5）
    // System.Text.Json 配 JsonStringEnumConverter：旧 "Music" 字符串反序列化时
    // 因找不到匹配名抛 JsonException，由 SettingsStore.Load 走 catch 重置为默认；
    // 但用户的"我在用音乐模式"语义会丢。所以策略：在 SettingsStore.Load 之前
    // 先用一次 JsonDocument 探测原始 JSON 中的 Effect.Type 字符串，命中 "Music" 则做迁移记号
    // → 详细机制见 §2.3，此处 Normalize 只处理已迁移过的对象（OperatingMode 已设好）

    // 如果 Mode（旧字段）== Music 而 OperatingMode 还是默认 Lighting
    // 说明 SettingsStore.Load 用预迁移流程把 Effect.Type 改成了 Static 但忘了同步 OperatingMode
    // 此处补刀（防御性）
    if (migrateLegacyMode && Mode == KeyboardMode.Music && OperatingMode == OperatingMode.Lighting)
    {
        OperatingMode = OperatingMode.Music;
        if (Effect.Type == EffectType.Static && SavedEffects?.LastUsedLightingEffect == EffectType.Static)
        {
            // 老用户首次升级：没有"上次玩什么灯效"信息，留 Static 默认
        }
    }

    // 验证 Effect.Type 是有效枚举值（防御性 —— Enum.IsDefined 跳过移除的 5）
    if (!Enum.IsDefined(Effect.Type) || (int)Effect.Type == 5)
    {
        // 落到了已删除的 Music：把它当 Static
        Effect.Type = EffectType.Static;
        // 由于这条路径只可能是错位反序列化，安全起见也设 OperatingMode=Music
        OperatingMode = OperatingMode.Music;
    }

    Effect.Normalize();
    SavedEffects ??= new EffectMemorySettings();
    SavedEffects.Normalize();
    // ... 其余 Normalize 调用不变 ...

    // KeyboardMode（旧字段，现在仅用作显示派生）从 Effect.Type 推导，去掉 Music 分支
    Mode = Effect.Type switch
    {
        EffectType.Static => KeyboardMode.Static,
        EffectType.Rainbow => KeyboardMode.Rainbow,
        EffectType.Breathing => KeyboardMode.Breathing,
        EffectType.Sequence => KeyboardMode.Sequence,
        EffectType.Off => KeyboardMode.Off,
        EffectType.Pulse => KeyboardMode.Pulse,
        EffectType.Heartbeat => KeyboardMode.Heartbeat,
        _ => KeyboardMode.Static  // 兜底（不再有 Music 分支）
    };

    StaticColor = Effect.Color;
    RainbowStep = Effect.Step;
    RefreshIntervalMs = Effect.IntervalMs;
    return this;
}
```

`KeyboardMode` 同样移除 `Music` 值；该枚举本身已经只为旧 JSON 兼容存在，移除一个值不影响新逻辑。

#### 2.3 旧 JSON 反序列化的具体处理

**问题**：当前 `SettingsStore.Load()` 用 `System.Text.Json` + `JsonStringEnumConverter`。如果旧 JSON 里 `Effect.Type` 是 `"Music"`，反序列化时找不到匹配的枚举名会抛 `JsonException`，被 `Load()` 的 catch 兜成"重置为默认"——这会导致**整个 settings.json 被覆盖为默认值**（用户所有自定义配置丢失）。

**解决**：在 `SettingsStore.Load()` 反序列化**之前**做一次低成本字符串预扫描：用 `JsonDocument.Parse(json)` 检查 `Effect.Type` 字段值是否为 `"Music"`，如果是，就在 JSON 字符串里把 `"Music"` 替换为 `"Static"` 后再反序列化，并设置一个 `_pendingMusicMigration` 标志位（或者直接在反序列化后立即把 `OperatingMode` 设为 `Music`）。

**实现位置**：`ColorfulLedKeyboard.Core/SettingsStore.cs`（用户消息没贴出此文件全文，但 Worker 和 Tray 都在调它，结构应该简单）。

**伪代码**：

```csharp
public KeyboardSettings Load()
{
    if (!File.Exists(SettingsPath)) { /* 写默认并返回 */ }

    string json;
    try { json = File.ReadAllText(SettingsPath); }
    catch (IOException) { return new KeyboardSettings().Normalize(migrateLegacyMode: true); }

    bool legacyMusicDetected = DetectLegacyMusicMode(json);
    if (legacyMusicDetected)
    {
        json = SanitizeLegacyMusic(json);  // 替换 "Type":"Music" → "Type":"Static"
    }

    KeyboardSettings settings;
    try
    {
        settings = JsonSerializer.Deserialize<KeyboardSettings>(json, JsonOptions)
            ?? new KeyboardSettings();
    }
    catch (JsonException) { return new KeyboardSettings().Normalize(migrateLegacyMode: true); }

    if (legacyMusicDetected)
    {
        settings.OperatingMode = OperatingMode.Music;
    }
    return settings.Normalize(migrateLegacyMode: true);
}

private static bool DetectLegacyMusicMode(string json)
{
    try
    {
        using var doc = JsonDocument.Parse(json);
        return doc.RootElement.TryGetProperty("Effect", out var effect)
            && effect.TryGetProperty("Type", out var type)
            && type.ValueKind == JsonValueKind.String
            && string.Equals(type.GetString(), "Music", StringComparison.OrdinalIgnoreCase);
    }
    catch (JsonException) { return false; }
}

private static string SanitizeLegacyMusic(string json)
{
    // 简单字符串替换：JSON 里 "Effect": { "Type": "Music"
    // 用正则只替换 Effect 块内的 Type 值，避免误伤 AppProfile 等其他地方的 TargetEffect
    // 但简单起见：当前只有 Effect.Type 和 AppProfile.TargetEffect 两处用 EffectType
    // AppProfile.TargetEffect=Music 在新版 EffectType 里也无效，所以一起替换为 Static 是正确的
    return Regex.Replace(json,
        "\"(Type|TargetEffect)\"\\s*:\\s*\"Music\"",
        "\"$1\":\"Static\"",
        RegexOptions.IgnoreCase);
}
```

迁移时还要把每个 `AppProfileRule.TargetEffect == Music` 改成 `Static`（简单字符串替换覆盖了，因为反序列化后已经是 Static），但**附加副作用**：那条 AppProfile 规则原本是"前台跑这个进程时切到音乐模式"，现在会变成"切到 Static"——这是一个**已知降级**。在 §2.7 显式记录。

#### 2.4 Worker 分支：从 `EffectType.Music` 切换到 `OperatingMode.Music`

`ColorfulLedKeyboard.Service/Worker.cs` 修改 4 处：

```csharp
// 行 82：主入口分支
- if (settings.Effect.Type == EffectType.Music)
+ if (settings.OperatingMode == OperatingMode.Music)
{
    await RunMusicAsync(settings, stoppingToken);
    return;
}

// 行 394-396：ApplyAppProfiles 内部
- if (rule.TargetEffect == EffectType.Music)
- {
-     settings.Effect.Type = EffectType.Music;
-     settings.Effect.Color = rule.AutoColorEnabled ? rule.IconColor : rule.ManualColor;
-     return;
- }
+ // AppProfile 不再支持 TargetEffect=Music（已移除该枚举值）
+ // 如果用户希望前台某进程切到音乐模式，需要在 §需求 3 改进 AppProfile 时设计专门的"模式切换"动作字段
+ // 本次：迁移时已把 TargetEffect=Music 重置为 Static
```

`Worker.cs` 行 187（`ShouldRebuildRuntimeSettings`）也要补 `OperatingMode` 比较，避免改了模式但 Worker 不重建 runtime settings：

```csharp
return next.Enabled != current.Enabled ||
    next.OperatingMode != current.OperatingMode ||  // ← 新增
    next.Brightness != current.Brightness ||
    // ... 其余比较 ...
```

`CloneForRuntime()` 复制 `OperatingMode` 字段。

#### 2.5 设置窗 UI 改造

##### 2.5.1 「常规」页顶部新增「模式」单选

```
┌──────────────────────────────────────────────────────┐
│ [当前状态]                                           │
├──────────────────────────────────────────────────────┤
│ 模式：(•) 灯效模式  ( ) 音乐模式                    │  ← 新增第一行
│                                                       │
│ 当前效果 ▼ [固定颜色 / RGB 循环 / ... / 关闭]        │  ← 7 项（移除"音乐模式"）
│                                                       │
│ 全局亮度 ━━●━━                                       │
│ ... 其余控件 ...                                      │
└──────────────────────────────────────────────────────┘
```

**实现**：

```csharp
private readonly RadioButton _modeLighting = new() { Text = "灯效模式", AutoSize = true, Checked = true };
private readonly RadioButton _modeMusic = new() { Text = "音乐模式", AutoSize = true };

private Panel BuildGeneralPage()
{
    var page = CreatePage();

    // 模式行
    var modeRow = new FlowLayoutPanel
    {
        FlowDirection = FlowDirection.LeftToRight,
        AutoSize = true,
        Margin = new Padding(0, 0, 0, 8)
    };
    modeRow.Controls.Add(new Label { Text = "模式：", AutoSize = true, Margin = new Padding(0, 6, 8, 0) });
    modeRow.Controls.Add(_modeLighting);
    modeRow.Controls.Add(_modeMusic);

    _modeLighting.CheckedChanged += (_, _) => OnModeChanged();
    _modeMusic.CheckedChanged += (_, _) => OnModeChanged();

    page.Controls.Add(modeRow);

    // 现有的 _effectType 等控件
    _effectType.Items.AddRange(["固定颜色", "RGB 循环", "单色呼吸", "循环呼吸", "脉冲", "心跳", "关闭"]);
    // ... 其余不变 ...
}
```

`OnModeChanged()` 切换时：
- `Lighting` → 启用所有灯效相关控件，"音乐"导航页可访问但不强制跳
- `Music` → 禁用所有灯效相关控件（除"模式"行外整个常规页禁用），并自动跳到"音乐"导航页

```csharp
private void OnModeChanged()
{
    if (_loadingSettings) return;
    var music = _modeMusic.Checked;
    _effectType.Enabled = !music;
    _brightness.Enabled = !music;
    _effectColor.Enabled = !music;
    _period.Enabled = !music;
    _minimumBrightness.Enabled = !music;
    _hardBlink.Enabled = !music;
    _sequence.Enabled = !music;
    _customColors.Enabled = !music;
    _effectPreset.Enabled = !music;
    _effectPresetName.Enabled = !music;
    _effectSavePreset.Enabled = !music;
    _effectCreatePreset.Enabled = !music;
    _effectDeletePreset.Enabled = !music;

    if (music)
    {
        // 自动跳到"音乐"导航页（导航 ListBox 索引 1）
        _navigation.SelectedIndex = 1;
    }
}
```

##### 2.5.2 `_effectType` ComboBox 移除"音乐模式"项

**注意**：需要在 `SettingsForm` 中把导航 `ListBox` 从局部变量 `navigation` 提升为字段 `_navigation`，供 `OnModeChanged` 调 `_navigation.SelectedIndex = 1` 实现自动跳到音乐页。

```csharp
// 改前
_effectType.Items.AddRange(["固定颜色", "RGB 循环", "单色呼吸", "循环呼吸", "脉冲", "心跳", "音乐模式", "关闭"]);

// 改后
_effectType.Items.AddRange(["固定颜色", "RGB 循环", "单色呼吸", "循环呼吸", "脉冲", "心跳", "关闭"]);
```

`SelectedEffectType` 和 `EffectTypeToIndex` 的索引映射：

```csharp
private EffectType SelectedEffectType(EffectType fallback) => _effectType.SelectedIndex switch
{
    0 => EffectType.Static,
    1 => EffectType.Rainbow,
    2 => EffectType.Breathing,
    3 => EffectType.Sequence,
    4 => EffectType.Pulse,
    5 => EffectType.Heartbeat,
    6 => EffectType.Off,  // ← 原本是 7，现在是 6
    _ => fallback
};

private static int EffectTypeToIndex(EffectType effect) => effect switch
{
    EffectType.Static => 0,
    EffectType.Rainbow => 1,
    EffectType.Breathing => 2,
    EffectType.Sequence => 3,
    EffectType.Pulse => 4,
    EffectType.Heartbeat => 5,
    EffectType.Off => 6,
    _ => 1
};
```

`UpdateBrightnessAvailability` 和 `UpdateEffectConfigurationVisibility` 删除 `EffectType.Music` 相关分支。

`AppProfile` 编辑器（`SettingsForm.cs:2614/2621`）的 `TargetEffect` 选项也要相应清理。该编辑器允许用户为某进程指定目标效果，移除 Music 选项后，列表只保留 Static 和 Breathing（与 §2.6 的 AppProfile 规则一致）。

##### 2.5.3 `LoadSettings` / `SaveSettings`

```csharp
// LoadSettings
_modeLighting.Checked = settings.OperatingMode == OperatingMode.Lighting;
_modeMusic.Checked = settings.OperatingMode == OperatingMode.Music;

_effectType.SelectedIndex = settings.Effect.Type switch
{
    EffectType.Static => 0,
    EffectType.Rainbow => 1,
    EffectType.Breathing => 2,
    EffectType.Sequence => 3,
    EffectType.Pulse => 4,
    EffectType.Heartbeat => 5,
    EffectType.Off => 6,
    _ => 1
};
// 移除 EffectType.Music 分支

// SaveSettings
settings.OperatingMode = _modeMusic.Checked ? OperatingMode.Music : OperatingMode.Lighting;
if (_effectChangedByUser)
{
    settings.Effect.Type = SelectedEffectType(settings.Effect.Type);
    // 用户切换灯效时记录"上次玩的灯效"
    if (settings.Effect.Type != EffectType.Off)
    {
        settings.SavedEffects.LastUsedLightingEffect = settings.Effect.Type;
    }
}
```

**切回灯效模式时的恢复**（Q2.4 选 B）：
- 在 `OnModeChanged` 切到 Lighting 时，如果当前 `Effect.Type` 是 Off，从 `SavedEffects.LastUsedLightingEffect` 恢复 Effect.Type
- 这一步只在 UI 层做即可（`_effectType.SelectedIndex = EffectTypeToIndex(LastUsedLightingEffect)`），保存时随 `_effectChangedByUser` 流走

#### 2.6 托盘菜单 UI 改造

**改前结构**：

```
效果 ▶
├── 固定颜色 ▶
├── RGB 循环 ▶
├── 单色呼吸 ▶
├── 循环呼吸 ▶
├── 脉冲 ▶
├── 心跳 ▶
└── 音乐模式 ▶  ← 在效果里
```

**改后结构**：

```
[效果 ▶ 与 音乐模式 ▶ 同级，互斥单选]

效果 ▶                        ← Checked 当 OperatingMode=Lighting
├── 固定颜色 ▶
├── RGB 循环 ▶
├── 单色呼吸 ▶
├── 循环呼吸 ▶
├── 脉冲 ▶
└── 心跳 ▶
音乐模式 ▶                    ← Checked 当 OperatingMode=Music
├── 通用
└── [自定义音乐预设...]
```

**实现**（`TrayApplicationContext.BuildMenu`）：

```csharp
// 改前
menu.Items.Add(BuildEffectMenu());  // 内部包含"音乐模式 ▶"子项

// 改后
menu.Items.Add(BuildEffectMenu());     // 只剩 6 个灯效，Checked=OperatingMode==Lighting
menu.Items.Add(BuildMusicModeMenu());  // 提到一级，Checked=OperatingMode==Music
```

**`BuildEffectMenu` 删除最后一行 `AddMusicPresetMenu(effect)`**，并在父项加 `Checked` 状态：

```csharp
private ToolStripMenuItem BuildEffectMenu()
{
    var effect = new ToolStripMenuItem("效果")
    {
        Checked = _settings.OperatingMode == OperatingMode.Lighting
    };
    AddEffectPresetMenu(effect, EffectType.Static, "固定颜色");
    AddEffectPresetMenu(effect, EffectType.Rainbow, "RGB 循环");
    AddEffectPresetMenu(effect, EffectType.Breathing, "单色呼吸");
    AddEffectPresetMenu(effect, EffectType.Sequence, "循环呼吸");
    AddEffectPresetMenu(effect, EffectType.Pulse, "脉冲");
    AddEffectPresetMenu(effect, EffectType.Heartbeat, "心跳");
    return effect;
}
```

**新增 `BuildMusicModeMenu`**（提取自原 `AddMusicPresetMenu` 逻辑）：

```csharp
private ToolStripMenuItem BuildMusicModeMenu()
{
    var music = new ToolStripMenuItem("音乐模式")
    {
        Checked = _settings.OperatingMode == OperatingMode.Music
    };

    foreach (var preset in MusicSettings.BuiltInPresets.Concat(_settings.Effect.Music.CustomPresets))
    {
        var presetCopy = CloneMusicPreset(preset);
        var item = new ToolStripMenuItem(presetCopy.Name)
        {
            Checked = _settings.OperatingMode == OperatingMode.Music &&
                string.Equals(_settings.Effect.Music.PresetName, presetCopy.Name, StringComparison.OrdinalIgnoreCase)
        };
        item.Click += (_, _) => ApplyEffect(settings =>
        {
            settings.Enabled = true;
            settings.OperatingMode = OperatingMode.Music;
            settings.Effect.Music.ApplyPreset(presetCopy);
        });
        music.DropDownItems.Add(item);
    }

    return music;
}
```

**修改"效果 ▶ 任意灯效项 click"**（`AddEffectPresetMenu`）：点击灯效项时，要把 `OperatingMode` 设回 `Lighting`：

```csharp
softwareDefault.Click += (_, _) => ApplyEffect(settings =>
{
    settings.Enabled = true;
    settings.OperatingMode = OperatingMode.Lighting;  // ← 新增：从音乐模式切回灯效模式
    ApplyEffectToSettings(settings, EffectPresetSettings.CreateSoftwareDefault(effectType));
    settings.SavedEffects.LastUsedLightingEffect = effectType;  // ← 新增：记录最后玩的
});
```

**亮度菜单的禁用条件**：从 `Effect.Type == Music` 改成 `OperatingMode == Music`：

```csharp
private ToolStripMenuItem BuildBrightnessMenu()
{
    var brightness = new ToolStripMenuItem($"亮度 ({_settings.Brightness}%)");
    if (_settings.OperatingMode == OperatingMode.Music)  // ← 改
    {
        brightness.Enabled = false;
        brightness.Text = "亮度 (由当前模式控制)";
    }
    // ... 其余不变 ...
}
```

#### 2.7 已知降级（迁移副作用）

| 项 | 旧行为 | 新行为 |
|----|--------|--------|
| AppProfile 规则 `TargetEffect=Music` | 前台进程命中时切到音乐模式 | 迁移后变成 `TargetEffect=Static`（前台命中时切到 Static + 进程图标颜色）|
| 用户必须手动**重新创建**音乐相关 AppProfile 规则 | 在需求 3 改进 AppProfile 时再做"动作=切换模式"的设计 |

这是**用户可接受的折衷**——他选了"彻底移除 EffectType.Music"，AppProfile 没有"模式切换"概念是必然的副作用。文档里写清楚即可。

#### 2.8 不变的部分

| 项 | 状态 |
|----|------|
| `MusicSettings`（响应模式、灵敏度、节拍颜色、亮度范围、自定义预设、Spotify 子配置等所有字段）| 保持原样 |
| 设置窗"音乐"导航页 | 保持原样（用户可以在不勾选"音乐模式"时也进去配置参数）|
| `RunMusicAsync` 主循环 | 保持原样（被分支条件从 `Effect.Type==Music` 改为 `OperatingMode==Music`）|
| `LightingFrameGenerator`、`MusicPulseController` 等所有渲染逻辑 | 不变 |
| settings.json 路径与权限 | 不变 |
| 服务名、注册表自启项、卸载入口 | 不变 |

---

### 三、测试

#### 3.1 单元测试新增

**`KeyboardSettingsTests.cs`**（如不存在则新建）：

1. `Normalize_LegacyMusicMode_MigratesToOperatingModeMusic`
   - 输入：`Effect.Type` 反序列化后落到无效值（模拟 Music 旧值）
   - 期望：`OperatingMode == Music`，`Effect.Type == Static`，`Effect.Music` 未被修改

2. `Normalize_DefaultOperatingMode_IsLighting`
   - 输入：全新 KeyboardSettings 默认值
   - 期望：`OperatingMode == Lighting`

3. `Normalize_LegacyKeyboardModeMusic_MigratesOperatingMode`
   - 输入：`Mode = KeyboardMode.Music`（旧字段）+ `Effect.Type = Static`
   - 期望：`OperatingMode == Music`

**`SettingsStoreTests.cs`**（如不存在则新建）：

4. `Load_LegacyMusicJsonString_PreservesAllMusicParameters`
   - 输入：手工拼一个旧版 JSON 字符串，`"Effect": {"Type": "Music", "Music": {...自定义参数...}}`
   - 期望：`Load` 返回的对象 `OperatingMode == Music`，`Effect.Music` 字段值与输入完全一致（不丢自定义参数）

5. `Load_LegacyMusicAppProfile_DowngradesTargetEffectToStatic`
   - 输入：JSON 含 `"AppProfiles.Rules[0].TargetEffect": "Music"`
   - 期望：`Load` 返回 `Rules[0].TargetEffect == Static`

**`InstallerShortcutTests.cs`**——快捷方式逻辑放在 `Program.cs` 静态方法里测起来不容易（涉及 COM）；本次**不写自动化测试**，靠手动验证（§3.3）。

#### 3.2 现有测试影响

`DchuKeyboardDeviceTests.cs`（已修过的 6 个 BuildSequenceSlotArgs 测试）—— 不受影响。

#### 3.3 手动验证

**快捷方式（需求 1）**：

| 步骤 | 期望 |
|------|------|
| 装新版 setup.exe | 安装完成对话框出现 |
| 打开开始菜单（Win 键），输入 `clevo` | 列表里出现 `ClevoLEDKeyboardControl` |
| 双击桌面 `ClevoLEDKeyboardControl.lnk` | 托盘出现图标 + 设置窗自动打开 |
| 右键托盘 → 退出托盘 | 托盘消失 |
| 桌面双击快捷方式 | 托盘 + 设置窗重新打开 |
| 卸载（保留设置） | 桌面快捷方式消失，开始菜单 .lnk 消失 |

**音乐模式互斥（需求 2）**：

| 步骤 | 期望 |
|------|------|
| 设置窗常规页顶部 | 出现"模式："+ 两个单选按钮 |
| 选「灯效模式」、效果选「RGB 循环」、应用 | 键盘 RGB 跑动 |
| 选「音乐模式」 | 自动跳到"音乐"页；常规页其他控件灰掉 |
| "音乐"页选预设、应用 | 键盘按音乐响应 |
| 切回常规页选「灯效模式」 | 自动恢复到"上次玩的"——RGB 循环 |
| 托盘右键菜单 | 看到"效果 ▶"和"音乐模式 ▶"同级，当前模式打勾 |
| 点托盘"音乐模式 ▶ 通用" | 切到音乐 |
| 点托盘"效果 ▶ 心跳 ▶ 软件默认" | 切到灯效；OperatingMode 切回 Lighting |

**升级回归（需求 2 数据迁移）**：

| 步骤 | 期望 |
|------|------|
| 在旧版（带 `EffectType.Music`）下设到音乐模式，自定义 3 个音乐预设 | settings.json 有 3 个 CustomPresets |
| 安装新版（覆盖）| 安装完成 |
| 打开新版设置窗 | 顶部「模式」单选选中"音乐模式"；"音乐"页所有 3 个自定义预设可见 |
| 切回灯效模式 | 默认落到 Static + 红色（首次升级无 LastUsedLightingEffect 信息）|
| 改设到 RGB 循环、应用、再切到音乐、再切回灯效 | 第二次切回时落到 RGB 循环 |
| settings.json 文件内容（重启服务后）| 包含 `"OperatingMode": "Music"`；不再含 `"Type": "Music"` |

---

## 兼容性 & 用户感知

| 维度 | 改动前 | 改动后 |
|------|--------|--------|
| settings.json 文件本身 | 安装器不动 | 安装器不动（数据保留 100%）|
| settings.json 字段格式 | `Effect.Type` 可能是 `"Music"` | 自动迁移为 `OperatingMode: "Music"` + `Effect.Type: "Static"`，所有 `Effect.Music` 子字段不变 |
| 用户保存的灯效预设、音乐预设、应用场景规则、自动化规则 | 全部保留 | 全部保留 |
| AppProfile 中 `TargetEffect=Music` 的规则 | 切到音乐模式 | 切到 Static + 图标色（已知降级）|
| 设置窗常规页"当前效果"下拉 | 8 项含"音乐模式" | 7 项不含"音乐模式" |
| 托盘菜单 | "效果"→"音乐模式" 嵌入式 | "效果"和"音乐模式" 平级互斥 |
| 桌面 / 开始菜单快捷方式 | 无 | 有 |

**用户感知关键差异**：
- **退出托盘后能从开始菜单/桌面快速找回程序**
- **常规页和音乐模式概念清晰分离**
- **所有数据自动保留**

## 风险

| 风险 | 严重度 | 缓解 |
|------|--------|------|
| 旧 JSON 字符串预扫描 + Regex 替换误伤其他字段 | 中 | Regex 限定为 `"Type":"Music"` 和 `"TargetEffect":"Music"`，只这两个字段在旧 JSON 中可能持有 Music 值；多写测试覆盖 |
| WScript.Shell COM 在某些精简版 Windows 上不可用 | 低 | 用 try/catch 包整个 `CreateUserShortcuts`，失败时降级为不创建快捷方式，安装本身仍成功（在安装结果对话框补一行"快捷方式：未创建"提示）|
| 快捷方式被杀软误报 | 低 | LNK 是 Windows 原生格式，杀软误报概率极低；如出现可发 Issue |
| `EffectType.Music = 5` 移除导致旧版本读新 JSON 报错 | 低 | 旧版本读新 JSON 时遇到 `"OperatingMode": "Music"` 会忽略未知字段；遇到 `"Effect.Type": "Static"` 会正常读取——降级到旧版"看起来是 Static 模式"是一致行为 |
| 用户重装老版本回滚 | 低 | 老版本 `Normalize` 把 Mode 从 Effect.Type 推导，Effect.Type 已经是 Static → Mode 也变 Static；Effect.Music 子字段不丢；用户再切到音乐模式时，老版本会写回 `Effect.Type=Music`，再升级新版又自动迁移 |

## 实施路线图（写入 plan 时细化）

1. **阶段 A：Core 数据模型**
   - 新增 `OperatingMode.cs`
   - 移除 `EffectType.Music`、`KeyboardMode.Music`
   - `KeyboardSettings` 新增 `OperatingMode` 字段、`EffectMemorySettings.LastUsedLightingEffect`
   - `Normalize` 迁移逻辑
   - `SettingsStore.Load` 旧 JSON 预扫描 + Regex 替换
   - 单元测试 5 个

2. **阶段 B：Service Worker 适配**
   - `RunEffectAsync` 入口分支改 `OperatingMode`
   - `ApplyAppProfiles` 移除 Music 特殊路径
   - `ShouldRebuildRuntimeSettings` 比较 `OperatingMode`
   - `CloneForRuntime` 复制新字段

3. **阶段 C：Tray UI 改造**
   - `SettingsForm.BuildGeneralPage` 加"模式"单选行
   - `_effectType` ComboBox 7 项
   - `SelectedEffectType / EffectTypeToIndex` 索引重映射
   - `LoadSettings / SaveSettings` 处理 `OperatingMode`
   - `OnModeChanged` 控件启用/禁用 + 自动跳页
   - 切回灯效时从 `LastUsedLightingEffect` 恢复
   - `TrayApplicationContext.BuildMenu`：`BuildEffectMenu` 删除音乐子菜单，新增 `BuildMusicModeMenu`，亮度禁用条件改用 `OperatingMode`

4. **阶段 D：Installer 快捷方式**
   - `CreateShortcut` 静态方法（WScript.Shell COM）
   - `CreateUserShortcuts` / `RemoveUserShortcuts`
   - `Install` 末尾调用 `CreateUserShortcuts`
   - `Uninstall` 调用 `RemoveUserShortcuts`
   - 路径常量 `StartMenuShortcutPath` / `DesktopShortcutPath`
   - 失败容错（COM 不可用时降级，不阻塞安装）

5. **阶段 E：构建与文档**
   - `dotnet build` 5 个项目通过
   - `dotnet test` 全部通过（旧测试 + 新增 5 个）
   - 更新 README（如果需要）说明新增的"模式"概念
   - `scripts/publish.ps1` 重打包 setup.exe
   - 手动验证清单 §3.3 全部通过

6. **阶段 F：提交规范**
   - 阶段 A → `refactor(core): introduce OperatingMode and migrate legacy Music EffectType`
   - 阶段 B → `refactor(service): switch Worker dispatch to OperatingMode`
   - 阶段 C → `refactor(tray): split lighting/music modes in settings UI and tray menu`
   - 阶段 D → `feat(installer): create Start Menu and public Desktop shortcuts`
   - 阶段 E → `chore: rebuild installer and update tests`

## 拒收标准

- 升级后任何用户已保存的灯效/音乐预设丢失 → 重做迁移逻辑
- AppProfile 规则除 TargetEffect=Music 外的字段被改动 → 修迁移
- 装好新版后开始菜单或公共桌面没有 .lnk → 修安装器
- 卸载后 .lnk 残留 → 修卸载流程
- 切到音乐模式时常规页控件未禁用 → 修 OnModeChanged
- 切回灯效模式后 LastUsedLightingEffect 没有恢复（已存在记录的情况下）→ 修恢复逻辑
- 任何现有手动验证项失败（DCHU 颜色显示、灯效切换、音乐响应、敲字脉冲、通知闪烁、应用场景、自动化、空闲降亮）→ 重做对应阶段
