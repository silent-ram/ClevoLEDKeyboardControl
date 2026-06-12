# 快捷方式 + 音乐模式互斥重构 — 实施计划

> 日期：2026-06-13
> 关联设计：[`docs/superpowers/specs/2026-06-13-shortcuts-and-music-mode-design.md`](../specs/2026-06-13-shortcuts-and-music-mode-design.md)
> 适用项目：ClevoLEDKeyboardControl

**For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** 修复用户体验断点（添加桌面/开始菜单快捷方式）+ 把音乐模式从 EffectType 中分离为顶层 OperatingMode（彻底 + 数据零丢失）。

**Architecture:** 三层改动——Core 新增 `OperatingMode` 枚举与迁移逻辑、Service Worker 改用 `OperatingMode` 派发、Tray UI 在常规页加模式单选并把音乐子菜单提级、Installer 用 WScript.Shell COM 创建/清理快捷方式。

**Tech Stack:** .NET 8 / C# / WinForms / WScript.Shell COM / System.Text.Json / xUnit

---

## 阶段 A：Core 数据模型与迁移

### Task 1: 新增 OperatingMode 枚举

**Files:**
- Create: `ColorfulLedKeyboard.Core/OperatingMode.cs`

- [ ] **Step 1: Write the file**

```csharp
namespace ColorfulLedKeyboard.Core;

/// <summary>
/// 顶层运行模式：表示用户当前希望键盘走灯效路径还是音乐响应路径。
/// 与 <see cref="EffectType"/> 是不同维度——前者是"模式"，后者是"灯效"。
/// 当 <see cref="Music"/> 时 Worker 走 RunMusicAsync；当 <see cref="Lighting"/> 时走 RunEffectAsync。
/// </summary>
public enum OperatingMode
{
    /// <summary>灯效模式（默认）：键盘按 EffectType 显示静态色/呼吸/RGB 循环等动画。</summary>
    Lighting = 0,

    /// <summary>音乐模式：键盘根据系统音频电平动态变化，参数由 EffectSettings.Music 配置。</summary>
    Music = 1
}
```

- [ ] **Step 2: Build to verify**

Run: `dotnet build ColorfulLedKeyboard.Core/ColorfulLedKeyboard.Core.csproj -c Release`
Expected: 构建通过，0 错误。

- [ ] **Step 3: Commit**

```bash
git add ColorfulLedKeyboard.Core/OperatingMode.cs
git commit -m "feat(core): introduce OperatingMode enum (Lighting/Music)"
```

---

### Task 2: 移除 EffectType.Music 和 KeyboardMode.Music

**Files:**
- Modify: `ColorfulLedKeyboard.Core/EffectType.cs`
- Modify: `ColorfulLedKeyboard.Core/KeyboardMode.cs`

- [ ] **Step 1: 修改 EffectType.cs**

```csharp
namespace ColorfulLedKeyboard.Core;

public enum EffectType
{
    Static = 0,
    Rainbow = 1,
    Breathing = 2,
    Sequence = 3,
    Off = 4,
    // 注意：值 5（原 Music）已移除，但保留 Pulse=6/Heartbeat=7 的整数值不变，
    // 以维持序列化兼容性。详见 docs/superpowers/specs/2026-06-13-...md
    Pulse = 6,
    Heartbeat = 7
}
```

- [ ] **Step 2: 修改 KeyboardMode.cs**

读取 `KeyboardMode.cs` 文件，移除 `Music` 项。同样保留其他枚举值的整数值不变。

- [ ] **Step 3: Build——预期会大量报错**

Run: `dotnet build -c Release`
Expected: 多处编译错误，引用 `EffectType.Music` / `KeyboardMode.Music` 的位置都需要修。下面的 Task 3-7 逐一修复。**这一步先不提交。**

---

### Task 3: 修复 KeyboardSettings 中的 Music 引用 + 加 OperatingMode 字段 + 加 LastUsedLightingEffect

**Files:**
- Modify: `ColorfulLedKeyboard.Core/KeyboardSettings.cs`

- [ ] **Step 1: 加字段**

在 `KeyboardSettings` 类中，`Brightness` 字段之后加：

```csharp
public OperatingMode OperatingMode { get; set; } = OperatingMode.Lighting;
```

在 `EffectMemorySettings` 类中，所有 `LightingEffectSettings` 子记忆字段之后加：

```csharp
/// <summary>
/// 最后一次用户切到的灯效类型（不含 Off）。
/// 当用户从音乐模式切回灯效模式时，UI 会从此字段恢复 Effect.Type，让"切回"体验连贯。
/// 老版本升级时缺失此字段，默认为 Static。
/// </summary>
public EffectType LastUsedLightingEffect { get; set; } = EffectType.Static;
```

- [ ] **Step 2: 更新 Normalize 方法移除 Music 分支**

把 `Normalize(bool migrateLegacyMode)` 中所有引用 `EffectType.Music` / `KeyboardMode.Music` 的地方改造：

```csharp
// 原：
if (migrateLegacyMode && Effect.Type == EffectType.Rainbow && Mode != KeyboardMode.Rainbow)
{
    Effect.Type = Mode switch
    {
        KeyboardMode.Static => EffectType.Static,
        KeyboardMode.Breathing => EffectType.Breathing,
        KeyboardMode.Sequence => EffectType.Sequence,
        KeyboardMode.Off => EffectType.Off,
        KeyboardMode.Music => EffectType.Music,    // ← 删
        KeyboardMode.Pulse => EffectType.Pulse,
        KeyboardMode.Heartbeat => EffectType.Heartbeat,
        _ => Effect.Type
    };
}
```

把 `KeyboardMode.Music` 的分支改成迁移到 OperatingMode：

```csharp
if (migrateLegacyMode && Effect.Type == EffectType.Rainbow && Mode != KeyboardMode.Rainbow)
{
    Effect.Type = Mode switch
    {
        KeyboardMode.Static => EffectType.Static,
        KeyboardMode.Breathing => EffectType.Breathing,
        KeyboardMode.Sequence => EffectType.Sequence,
        KeyboardMode.Off => EffectType.Off,
        KeyboardMode.Pulse => EffectType.Pulse,
        KeyboardMode.Heartbeat => EffectType.Heartbeat,
        _ => Effect.Type
    };
}

// ★ 新增：检测旧 KeyboardMode.Music 的迁移
// 注意：KeyboardMode.Music 已移除，无法直接 == 比较；
// 但旧 JSON 反序列化的 Mode 字段如果是无效值（5）会落到 Mode 默认 Rainbow，
// 因此实际迁移由 SettingsStore.Load 处理（见 Task 6）。
// 此处只处理"OperatingMode 还没设过、但 Effect.Type 是无效值"的兜底路径。

// 验证 Effect.Type 是有效枚举值
if (!Enum.IsDefined(Effect.Type))
{
    // 落到了已删除的 Music（值 5）：把它当 Static，且推断为音乐模式
    Effect.Type = EffectType.Static;
    OperatingMode = OperatingMode.Music;
}
```

把 `Mode = Effect.Type switch` 中的 `EffectType.Music => KeyboardMode.Music` 整行删除：

```csharp
Mode = Effect.Type switch
{
    EffectType.Static => KeyboardMode.Static,
    EffectType.Rainbow => KeyboardMode.Rainbow,
    EffectType.Breathing => KeyboardMode.Breathing,
    EffectType.Sequence => KeyboardMode.Sequence,
    EffectType.Off => KeyboardMode.Off,
    EffectType.Pulse => KeyboardMode.Pulse,
    EffectType.Heartbeat => KeyboardMode.Heartbeat,
    _ => Mode
};
```

把 `Enum.IsDefined(Mode)` 校验保留——校验失败时由于 KeyboardMode.Music 已删，老的 Mode=5 会触发该 if，被重置为 `KeyboardMode.Rainbow`，这是预期行为（之后 OperatingMode 已经在前面被设为 Music）。

- [ ] **Step 3: 更新 CloneForRuntime 复制新字段**

```csharp
public KeyboardSettings CloneForRuntime()
{
    return new KeyboardSettings
    {
        Enabled = Enabled,
        OperatingMode = OperatingMode,  // ← 新增
        Mode = Mode,
        // ... 其余字段不变 ...
        SavedEffects = new EffectMemorySettings
        {
            Static = CloneEffect(SavedEffects.Static),
            Rainbow = CloneEffect(SavedEffects.Rainbow),
            Breathing = CloneEffect(SavedEffects.Breathing),
            Sequence = CloneEffect(SavedEffects.Sequence),
            Pulse = CloneEffect(SavedEffects.Pulse),
            Heartbeat = CloneEffect(SavedEffects.Heartbeat),
            LastUsedLightingEffect = SavedEffects.LastUsedLightingEffect  // ← 新增
        },
        // ...
    }.Normalize();
}
```

- [ ] **Step 4: Build——可能仍有错**

Run: `dotnet build ColorfulLedKeyboard.Core/ColorfulLedKeyboard.Core.csproj -c Release`
Expected: 减少错误。如还有 EffectType.Music 引用待 Task 4。

---

### Task 4: 修复 AppProfileSettings.cs 中的 Music 引用

**Files:**
- Modify: `ColorfulLedKeyboard.Core/AppProfileSettings.cs`

- [ ] **Step 1: 修改 Normalize**

```csharp
// 原 行 45：
if (TargetEffect is not EffectType.Static and not EffectType.Breathing and not EffectType.Music)
{
    TargetEffect = EffectType.Static;
}

// 改后：
// AppProfile 现在只支持灯效模式下的 Static / Breathing 切换，不再支持切到音乐模式
// 用户希望"前台某进程时切到音乐"需要在未来的 AppProfile 改进中重新设计
if (TargetEffect is not EffectType.Static and not EffectType.Breathing)
{
    TargetEffect = EffectType.Static;
}
```

注：旧 JSON 反序列化时如果遇到无效枚举值会被 catch 抛——`SettingsStore.Load` 中已对此预处理（Task 6）。

- [ ] **Step 2: Build**

Run: `dotnet build ColorfulLedKeyboard.Core/ColorfulLedKeyboard.Core.csproj -c Release`

---

### Task 5: 创建迁移 + OperatingMode 单元测试

**Files:**
- Create: `ColorfulLedKeyboard.Tests/KeyboardSettingsMigrationTests.cs`

- [ ] **Step 1: 写测试**

```csharp
using ColorfulLedKeyboard.Core;

namespace ColorfulLedKeyboard.Tests;

public sealed class KeyboardSettingsMigrationTests
{
    [Fact]
    public void Normalize_DefaultOperatingMode_IsLighting()
    {
        var settings = new KeyboardSettings();
        settings.Normalize();
        Assert.Equal(OperatingMode.Lighting, settings.OperatingMode);
    }

    [Fact]
    public void Normalize_InvalidEffectTypeValueFive_RestoresStaticAndSetsMusicMode()
    {
        // 模拟旧版 JSON 反序列化后 Effect.Type 落到无效值 5（原 EffectType.Music）
        var settings = new KeyboardSettings
        {
            Effect = new LightingEffectSettings { Type = (EffectType)5 }
        };
        settings.Normalize(migrateLegacyMode: true);

        Assert.Equal(OperatingMode.Music, settings.OperatingMode);
        Assert.Equal(EffectType.Static, settings.Effect.Type);
    }

    [Fact]
    public void Normalize_PreservesMusicSubsettingsDuringMigration()
    {
        var settings = new KeyboardSettings
        {
            Effect = new LightingEffectSettings
            {
                Type = (EffectType)5,
                Music = new MusicSettings
                {
                    PresetName = "我的预设",
                    BaseBrightness = 30,
                    PeakBrightness = 90,
                    CustomPresets =
                    [
                        new MusicPreset { Name = "自定义A", BaseBrightness = 20, PeakBrightness = 80 }
                    ]
                }
            }
        };
        settings.Normalize(migrateLegacyMode: true);

        Assert.Equal("我的预设", settings.Effect.Music.PresetName);
        Assert.Equal(30, settings.Effect.Music.BaseBrightness);
        Assert.Equal(90, settings.Effect.Music.PeakBrightness);
        Assert.Single(settings.Effect.Music.CustomPresets);
        Assert.Equal("自定义A", settings.Effect.Music.CustomPresets[0].Name);
    }

    [Fact]
    public void Normalize_DefaultLastUsedLightingEffectIsStatic()
    {
        var settings = new KeyboardSettings();
        settings.Normalize();
        Assert.Equal(EffectType.Static, settings.SavedEffects.LastUsedLightingEffect);
    }

    [Fact]
    public void Normalize_AppProfileTargetEffectMusicLegacyValue_DowngradedToStatic()
    {
        var settings = new KeyboardSettings
        {
            AppProfiles = new AppProfileSettings
            {
                Rules =
                [
                    new AppProfileRule
                    {
                        Name = "测试",
                        ProcessName = "test.exe",
                        TargetEffect = (EffectType)5  // 老版的 Music
                    }
                ]
            }
        };
        settings.Normalize(migrateLegacyMode: true);

        Assert.Equal(EffectType.Static, settings.AppProfiles.Rules[0].TargetEffect);
    }
}
```

- [ ] **Step 2: Run tests**

Run: `dotnet test ColorfulLedKeyboard.Tests/ColorfulLedKeyboard.Tests.csproj -c Release`
Expected: 5 个新测试通过；现有的 6 个 BuildSequenceSlotArgs 测试也通过；总计 ≥ 11 通过。

- [ ] **Step 3: Commit**

```bash
git add ColorfulLedKeyboard.Core ColorfulLedKeyboard.Tests
git commit -m "refactor(core): remove EffectType.Music, add OperatingMode + migration"
```

---

### Task 6: SettingsStore 旧 JSON 预扫描与字符串迁移

**Files:**
- Modify: `ColorfulLedKeyboard.Core/SettingsStore.cs`

- [ ] **Step 1: Read SettingsStore.cs**

Run: `cat ColorfulLedKeyboard.Core/SettingsStore.cs`
确认现有 `Load()` 方法的具体位置和异常处理逻辑。

- [ ] **Step 2: 在 Load() 内反序列化前加预扫描**

```csharp
public KeyboardSettings Load()
{
    if (!File.Exists(SettingsPath))
    {
        var defaults = new KeyboardSettings().Normalize(migrateLegacyMode: true);
        Save(defaults);
        return defaults;
    }

    string json;
    try
    {
        json = File.ReadAllText(SettingsPath);
    }
    catch (IOException)
    {
        return new KeyboardSettings().Normalize(migrateLegacyMode: true);
    }
    catch (UnauthorizedAccessException)
    {
        return new KeyboardSettings().Normalize(migrateLegacyMode: true);
    }

    // ★ 预扫描：检测旧版 EffectType.Music 字符串（已从枚举中移除）
    bool legacyMusicDetected = DetectLegacyMusicMode(json);
    if (legacyMusicDetected)
    {
        json = SanitizeLegacyMusicStrings(json);
    }

    KeyboardSettings settings;
    try
    {
        settings = JsonSerializer.Deserialize<KeyboardSettings>(json, JsonOptions)
            ?? new KeyboardSettings();
    }
    catch (JsonException)
    {
        return new KeyboardSettings().Normalize(migrateLegacyMode: true);
    }

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
        if (!doc.RootElement.TryGetProperty("Effect", out var effect)) return false;
        if (!effect.TryGetProperty("Type", out var type)) return false;
        return type.ValueKind == JsonValueKind.String &&
            string.Equals(type.GetString(), "Music", StringComparison.OrdinalIgnoreCase);
    }
    catch (JsonException)
    {
        return false;
    }
}

private static string SanitizeLegacyMusicStrings(string json)
{
    // 把 JSON 中所有 "Type": "Music" 和 "TargetEffect": "Music" 替换为对应字段的 Static
    // 这两个字段是 EffectType 枚举的反序列化目标——之外没有其他地方持有该枚举
    return System.Text.RegularExpressions.Regex.Replace(
        json,
        @"""(Type|TargetEffect)""\s*:\s*""Music""",
        @"""$1"":""Static""",
        System.Text.RegularExpressions.RegexOptions.IgnoreCase);
}
```

如果 `using` 没有，加：

```csharp
using System.Text.RegularExpressions;
```

- [ ] **Step 3: Build + Test**

Run:
```bash
dotnet build -c Release
dotnet test ColorfulLedKeyboard.Tests/ColorfulLedKeyboard.Tests.csproj -c Release
```
Expected: 全部通过。

- [ ] **Step 4: 新增 SettingsStore 集成测试**

**File:** `ColorfulLedKeyboard.Tests/SettingsStoreMigrationTests.cs`

```csharp
using System.Text.Json;
using ColorfulLedKeyboard.Core;

namespace ColorfulLedKeyboard.Tests;

public sealed class SettingsStoreMigrationTests : IDisposable
{
    private readonly string _tempSettingsPath;
    private readonly SettingsStore _store;

    public SettingsStoreMigrationTests()
    {
        _tempSettingsPath = Path.Combine(Path.GetTempPath(), $"settings-migration-{Guid.NewGuid():N}.json");
        _store = new SettingsStore(_tempSettingsPath);
    }

    public void Dispose()
    {
        if (File.Exists(_tempSettingsPath)) File.Delete(_tempSettingsPath);
    }

    [Fact]
    public void Load_LegacyMusicJson_MigratesToOperatingModeMusicAndPreservesPresets()
    {
        var legacyJson = """
        {
          "Enabled": true,
          "Brightness": 75,
          "Mode": "Music",
          "Effect": {
            "Type": "Music",
            "Color": "#FF0000",
            "Music": {
              "PresetName": "通用",
              "BaseBrightness": 20,
              "PeakBrightness": 95,
              "CustomPresets": [
                { "Name": "我的预设1", "BaseBrightness": 10, "PeakBrightness": 80 },
                { "Name": "我的预设2", "BaseBrightness": 30, "PeakBrightness": 100 }
              ]
            }
          }
        }
        """;
        File.WriteAllText(_tempSettingsPath, legacyJson);

        var loaded = _store.Load();

        Assert.Equal(OperatingMode.Music, loaded.OperatingMode);
        Assert.NotEqual((EffectType)5, loaded.Effect.Type);
        Assert.Equal("通用", loaded.Effect.Music.PresetName);
        Assert.Equal(20, loaded.Effect.Music.BaseBrightness);
        Assert.Equal(95, loaded.Effect.Music.PeakBrightness);
        Assert.Equal(2, loaded.Effect.Music.CustomPresets.Count);
        Assert.Equal("我的预设1", loaded.Effect.Music.CustomPresets[0].Name);
    }

    [Fact]
    public void Load_LegacyAppProfileMusicTarget_DowngradedToStatic()
    {
        var legacyJson = """
        {
          "Enabled": true,
          "AppProfiles": {
            "Enabled": true,
            "Rules": [
              { "Name": "音乐播放器", "ProcessName": "spotify", "TargetEffect": "Music" }
            ]
          }
        }
        """;
        File.WriteAllText(_tempSettingsPath, legacyJson);

        var loaded = _store.Load();

        Assert.Single(loaded.AppProfiles.Rules);
        Assert.Equal(EffectType.Static, loaded.AppProfiles.Rules[0].TargetEffect);
    }

    [Fact]
    public void Load_NewFormatJson_DoesNotTriggerMigration()
    {
        var newJson = """
        {
          "Enabled": true,
          "OperatingMode": "Lighting",
          "Effect": { "Type": "Rainbow", "Color": "#FF0000" }
        }
        """;
        File.WriteAllText(_tempSettingsPath, newJson);

        var loaded = _store.Load();

        Assert.Equal(OperatingMode.Lighting, loaded.OperatingMode);
        Assert.Equal(EffectType.Rainbow, loaded.Effect.Type);
    }
}
```

注意：`SettingsStore` 已经接受 `string? settingsPath = null` 构造参数（在 Core 项目内），且 Tests 项目已通过 `InternalsVisibleTo` 引用 Core，无需额外改动。

- [ ] **Step 5: Run tests**

Run: `dotnet test ColorfulLedKeyboard.Tests/ColorfulLedKeyboard.Tests.csproj -c Release`
Expected: ≥ 14 个测试通过。

- [ ] **Step 6: Commit**

```bash
git add ColorfulLedKeyboard.Core/SettingsStore.cs ColorfulLedKeyboard.Tests/SettingsStoreMigrationTests.cs
git commit -m "feat(core): pre-scan settings.json for legacy Music EffectType migration"
```

---

## 阶段 B：Service Worker 适配

### Task 7: Worker 改用 OperatingMode 派发

**Files:**
- Modify: `ColorfulLedKeyboard.Service/Worker.cs`

- [ ] **Step 1: 主入口分支**

```csharp
// 原 行 82：
if (settings.Effect.Type == EffectType.Music)
{
    await RunMusicAsync(settings, stoppingToken);
    return;
}

// 改后：
if (settings.OperatingMode == OperatingMode.Music)
{
    await RunMusicAsync(settings, stoppingToken);
    return;
}
```

- [ ] **Step 2: ApplyAppProfiles 移除 Music 路径**

```csharp
// 原 行 394-401：
if (rule.TargetEffect == EffectType.Music)
{
    settings.Effect.Type = EffectType.Music;
    settings.Effect.Color = rule.AutoColorEnabled ? rule.IconColor : rule.ManualColor;
    return;
}
settings.Effect = rule.BuildEffect();

// 改后：
// AppProfile 不再支持 TargetEffect=Music（已从 EffectType 中移除）
// BuildEffect 内部自然不会构造出 Music 类型
settings.Effect = rule.BuildEffect();
```

- [ ] **Step 3: ShouldRebuildRuntimeSettings 比较 OperatingMode**

```csharp
return next.Enabled != current.Enabled ||
    next.OperatingMode != current.OperatingMode ||  // ← 新增
    next.Brightness != current.Brightness ||
    !NotificationFlashEquals(next.NotificationFlash, current.NotificationFlash) ||
    next.Effect.Type != current.Effect.Type ||
    // ... 其余比较不变 ...
```

- [ ] **Step 4: Build**

Run: `dotnet build ColorfulLedKeyboard.Service/ColorfulLedKeyboard.Service.csproj -c Release`
Expected: 通过。

- [ ] **Step 5: Commit**

```bash
git add ColorfulLedKeyboard.Service/Worker.cs
git commit -m "refactor(service): switch Worker dispatch to OperatingMode"
```

---

## 阶段 C：Tray UI 改造

### Task 8: TrayApplicationContext 菜单互斥

**Files:**
- Modify: `ColorfulLedKeyboard.Tray/TrayApplicationContext.cs`

- [ ] **Step 1: BuildEffectMenu 移除音乐子菜单 + 加 Checked**

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
    // 删除：AddMusicPresetMenu(effect);
    return effect;
}
```

- [ ] **Step 2: 重写 AddMusicPresetMenu 为 BuildMusicModeMenu（提级）**

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

- [ ] **Step 3: BuildMenu 中插入新菜单**

```csharp
private ContextMenuStrip BuildMenu()
{
    var menu = new ContextMenuStrip();
    var enabled = new ToolStripMenuItem("启用灯效") { Checked = _settings.Enabled };
    enabled.Click += (_, _) => { /* unchanged */ };

    menu.Items.Add(enabled);
    menu.Items.Add(BuildEffectMenu());
    menu.Items.Add(BuildMusicModeMenu());  // ← 新增，与"效果"同级
    menu.Items.Add(BuildBrightnessMenu());
    menu.Items.Add(new ToolStripSeparator());
    // ... 其余不变 ...
}
```

- [ ] **Step 4: AddEffectPresetMenu 灯效项点击时切回 Lighting 模式**

修改 `softwareDefault.Click` 和 preset.Click 内部 lambda：

```csharp
softwareDefault.Click += (_, _) => ApplyEffect(settings =>
{
    settings.Enabled = true;
    settings.OperatingMode = OperatingMode.Lighting;  // ← 新增
    ApplyEffectToSettings(settings, EffectPresetSettings.CreateSoftwareDefault(effectType));
    settings.SavedEffects.LastUsedLightingEffect = effectType;  // ← 新增
});

// preset 类似
item.Click += (_, _) => ApplyEffect(settings =>
{
    settings.Enabled = true;
    settings.OperatingMode = OperatingMode.Lighting;  // ← 新增
    ApplyEffectToSettings(settings, presetCopy.Effect);
    settings.SavedEffects.LastUsedLightingEffect = presetCopy.Effect.Type;  // ← 新增
});
```

- [ ] **Step 5: 亮度菜单禁用条件改用 OperatingMode**

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

- [ ] **Step 6: 移除 RememberCurrentEffect 中的 Music 分支** 和 RestoreSavedEffect / ApplyEffectToSettings 的 Music switch 分支

```csharp
// RememberCurrentEffect: 删除 case EffectType.Music: 不再有该枚举值
// RestoreSavedEffect: 删除 EffectType.Music => KeyboardMode.Music 行
// ApplyEffectToSettings: 删除 EffectType.Music => KeyboardMode.Music 行
```

- [ ] **Step 7: Build**

Run: `dotnet build ColorfulLedKeyboard.Tray/ColorfulLedKeyboard.Tray.csproj -c Release`
Expected: 通过。

- [ ] **Step 8: Commit**

```bash
git add ColorfulLedKeyboard.Tray/TrayApplicationContext.cs
git commit -m "refactor(tray): split lighting/music modes in tray menu (mutually exclusive)"
```

---

### Task 9: SettingsForm 模式单选 + 跳页

**Files:**
- Modify: `ColorfulLedKeyboard.Tray/SettingsForm.cs`

- [ ] **Step 1: 字段声明**

在类顶部已有的 `_musicAdvanced` 等字段附近加：

```csharp
private readonly RadioButton _modeLighting = new() { Text = "灯效模式", AutoSize = true };
private readonly RadioButton _modeMusic = new() { Text = "音乐模式", AutoSize = true };
private ListBox? _navigation;  // ← 把原来的局部变量提升为字段
```

- [ ] **Step 2: 把 navigation ListBox 提升为字段 _navigation**

在构造函数里找到 `var navigation = new ListBox { ... }` 这一行，改为：

```csharp
_navigation = new ListBox { /* 原参数 */ };
// 后续所有 navigation.Items.Add / navigation.SelectedIndexChanged / navigation.SelectedIndex
// 都改为 _navigation.Items.Add / 等
```

- [ ] **Step 3: BuildGeneralPage 加模式行**

```csharp
private Panel BuildGeneralPage()
{
    var page = CreatePage();

    // ★ 新增：模式单选
    var modeRow = new FlowLayoutPanel
    {
        FlowDirection = FlowDirection.LeftToRight,
        AutoSize = true,
        Margin = new Padding(0, 0, 0, 8),
        Width = ContentWidth
    };
    modeRow.Controls.Add(new Label { Text = "模式：", AutoSize = true, Margin = new Padding(0, 6, 8, 0) });
    modeRow.Controls.Add(_modeLighting);
    modeRow.Controls.Add(_modeMusic);
    _modeLighting.CheckedChanged += (_, _) => OnModeChanged();
    _modeMusic.CheckedChanged += (_, _) => OnModeChanged();
    page.Controls.Add(modeRow);

    // 现有控件初始化
    _effectType.DropDownStyle = ComboBoxStyle.DropDownList;
    _effectType.Items.AddRange(["固定颜色", "RGB 循环", "单色呼吸", "循环呼吸", "脉冲", "心跳", "关闭"]);  // 7 项
    // ... 其余不变 ...
}
```

- [ ] **Step 4: 索引映射重写**

```csharp
private EffectType SelectedEffectType(EffectType fallback) => _effectType.SelectedIndex switch
{
    0 => EffectType.Static,
    1 => EffectType.Rainbow,
    2 => EffectType.Breathing,
    3 => EffectType.Sequence,
    4 => EffectType.Pulse,
    5 => EffectType.Heartbeat,
    6 => EffectType.Off,
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

- [ ] **Step 5: AppProfile TargetEffect 编辑器（_appProfiles）的下拉去掉 Music**

`SettingsForm.cs` 行 2614/2621 附近有 AppProfile 编辑器中 TargetEffect 的索引映射。当前是 3 项：Static/Breathing/Music。改为 2 项：

```csharp
// 找到类似：
private static int TargetEffectToIndex(EffectType effect) => effect switch
{
    EffectType.Static => 0,
    EffectType.Breathing => 1,
    EffectType.Music => 2,  // ← 删
    _ => 0
};

private static EffectType IndexToTargetEffect(int index) => index switch
{
    0 => EffectType.Static,
    1 => EffectType.Breathing,
    2 => EffectType.Music,  // ← 删
    _ => EffectType.Static
};
```

如果有 `Items.AddRange(["Static", "Breathing", "Music"])` 之类的初始化，也去掉 "Music"。

- [ ] **Step 6: LoadSettings 加 OperatingMode**

```csharp
private void LoadSettings()
{
    _loadingSettings = true;
    var settings = _settingsStore.Load();
    try
    {
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
            _ => 1  // 移除 EffectType.Music => 6 的旧分支
        };
        // ... 其余不变 ...
    }
    finally { _loadingSettings = false; }

    // 加载完后更新模式对控件的可用性
    UpdateModeAvailability();
}
```

- [ ] **Step 7: SaveSettings 写 OperatingMode + LastUsedLightingEffect**

```csharp
private void SaveSettings()
{
    try
    {
        var settings = _settingsStore.Load();
        settings.Enabled = true;
        settings.OperatingMode = _modeMusic.Checked ? OperatingMode.Music : OperatingMode.Lighting;

        if (_effectChangedByUser)
        {
            settings.Effect.Type = SelectedEffectType(settings.Effect.Type);
            // 记录最后玩的灯效（不含 Off）
            if (settings.Effect.Type != EffectType.Off)
            {
                settings.SavedEffects ??= new EffectMemorySettings();
                settings.SavedEffects.LastUsedLightingEffect = settings.Effect.Type;
            }
        }
        // ... 其余不变 ...
    }
}
```

- [ ] **Step 8: OnModeChanged 实现**

```csharp
private void OnModeChanged()
{
    if (_loadingSettings) return;
    UpdateModeAvailability();

    if (_modeMusic.Checked)
    {
        // 切到音乐模式：自动跳到"音乐"导航页（索引 1）
        if (_navigation is not null && _navigation.Items.Count > 1)
        {
            _navigation.SelectedIndex = 1;
        }
    }
    else
    {
        // 切回灯效模式：从 LastUsedLightingEffect 恢复
        var settings = _settingsStore.Load();
        var lastEffect = settings.SavedEffects?.LastUsedLightingEffect ?? EffectType.Static;
        _effectType.SelectedIndex = EffectTypeToIndex(lastEffect);
        _effectChangedByUser = true;
    }
}

private void UpdateModeAvailability()
{
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
}
```

- [ ] **Step 9: UpdateBrightnessAvailability / UpdateEffectConfigurationVisibility 移除 Music 分支**

```csharp
private void UpdateBrightnessAvailability()
{
    var effect = SelectedEffectType(EffectType.Rainbow);
    var brightnessEnabled = effect != EffectType.Off;  // 删除 EffectType.Music
    _brightness.Enabled = brightnessEnabled && !_modeMusic.Checked;
    _brightness.Visible = brightnessEnabled && !_modeMusic.Checked;
    _brightness.BackColor = _brightness.Enabled ? SystemColors.Window : SystemColors.Control;
}

private void UpdateEffectConfigurationVisibility()
{
    var effect = SelectedEffectType(EffectType.Rainbow);
    // ... 移除所有 EffectType.Music 分支 ...
    // 移除 _modeHint.Visible = effect is EffectType.Music or EffectType.Off; 的 Music 分支
    _modeHint.Visible = effect == EffectType.Off;
    _modeHint.Text = effect == EffectType.Off ? "关闭模式：键盘灯将关闭。" : "";
}
```

- [ ] **Step 10: Build + Test**

Run:
```bash
dotnet build -c Release
dotnet test -c Release
```
Expected: 全部通过。

- [ ] **Step 11: Commit**

```bash
git add ColorfulLedKeyboard.Tray/SettingsForm.cs
git commit -m "refactor(tray): add Mode radio in General page, split UI for lighting/music"
```

---

## 阶段 D：Installer 快捷方式

### Task 10: 添加 CreateUserShortcuts / RemoveUserShortcuts

**Files:**
- Modify: `ColorfulLedKeyboard.Installer/Program.cs`

- [ ] **Step 1: 加路径常量**

在现有路径常量（`InstallDirectory` 等）附近：

```csharp
private static readonly string StartMenuShortcutPath = Path.Combine(
    Environment.GetFolderPath(Environment.SpecialFolder.CommonStartMenu),
    "Programs",
    "ClevoLEDKeyboardControl.lnk");

private static readonly string DesktopShortcutPath = Path.Combine(
    Environment.GetFolderPath(Environment.SpecialFolder.CommonDesktopDirectory),
    "ClevoLEDKeyboardControl.lnk");
```

- [ ] **Step 2: 加 CreateShortcut 静态方法**

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
        finally
        {
            Marshal.FinalReleaseComObject(shortcut);
        }
    }
    finally
    {
        Marshal.FinalReleaseComObject(shell);
    }
}
```

- [ ] **Step 3: 加 CreateUserShortcuts / RemoveUserShortcuts**

```csharp
private static bool CreateUserShortcuts()
{
    if (!File.Exists(TrayExe))
    {
        return false;
    }

    var workingDir = Path.GetDirectoryName(TrayExe) ?? InstallDirectory;
    var success = true;

    foreach (var lnkPath in new[] { StartMenuShortcutPath, DesktopShortcutPath })
    {
        try
        {
            // 确保目标目录存在
            var dir = Path.GetDirectoryName(lnkPath);
            if (!string.IsNullOrWhiteSpace(dir))
            {
                Directory.CreateDirectory(dir);
            }
            CreateShortcut(
                lnkPath,
                TrayExe,
                "--settings",
                workingDir,
                "ClevoLEDKeyboardControl - 键盘 RGB 灯控制");
        }
        catch (Exception ex) when (ex is COMException or InvalidOperationException or UnauthorizedAccessException or IOException)
        {
            success = false;
        }
    }

    return success;
}

private static void RemoveUserShortcuts()
{
    foreach (var lnkPath in new[] { StartMenuShortcutPath, DesktopShortcutPath })
    {
        try
        {
            if (File.Exists(lnkPath))
            {
                File.Delete(lnkPath);
            }
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            // 卸载时无法删除快捷方式不阻塞主流程
        }
    }
}
```

需要 `using System.Runtime.InteropServices;`（已有）。

- [ ] **Step 4: 在 Install 末尾调用 CreateUserShortcuts**

```csharp
private static void Install()
{
    // ... 现有所有逻辑（StopAndDeleteServiceIfPresent / ExtractPayload / sc.exe / ... ）...
    AddTrayStartup();
    RegisterUninstaller();
    CreateUserShortcuts();  // ← 新增（失败不抛，仅返回 false）
}
```

- [ ] **Step 5: 在 Uninstall 中调用 RemoveUserShortcuts**

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
    // ... 其余不变 ...
}
```

- [ ] **Step 6: Build**

Run: `dotnet build ColorfulLedKeyboard.Installer/ColorfulLedKeyboard.Installer.csproj -c Release`
Expected: 通过。

- [ ] **Step 7: Commit**

```bash
git add ColorfulLedKeyboard.Installer/Program.cs
git commit -m "feat(installer): create Start Menu and public Desktop shortcuts"
```

---

## 阶段 E：构建与验证

### Task 11: 全量构建并打包

- [ ] **Step 1: 全量 build**

Run: `dotnet build -c Release`
Expected: 5 个项目全部通过，0 错误。

- [ ] **Step 2: 测试**

Run: `dotnet test ColorfulLedKeyboard.Tests/ColorfulLedKeyboard.Tests.csproj -c Release`
Expected: ≥ 14 个测试通过（6 个 BuildSequenceSlotArgs + 5 个 KeyboardSettingsMigration + 3 个 SettingsStoreMigration）。

- [ ] **Step 3: 打包安装器**

Run: `powershell -ExecutionPolicy Bypass -File scripts/publish.ps1`
Expected: `publish/ClevoLEDKeyboardControlSetup.exe` 生成；约 74-80 MB。

- [ ] **Step 4: 不提交（这一阶段无文件改动）**

---

### Task 12: 手动验证清单（用户在 Release 前完成）

参考 `docs/superpowers/specs/2026-06-13-shortcuts-and-music-mode-design.md` §3.3。

**验证清单（用户操作）：**

**快捷方式（需求 1）：**
1. 装新版 setup.exe → 安装完成对话框出现
2. 桌面应当出现 `ClevoLEDKeyboardControl` 图标
3. 开始菜单（Win 键）输入 `clevo` → 列表出现 `ClevoLEDKeyboardControl`
4. 双击桌面图标 → 托盘出现 + 设置窗自动打开
5. 右键托盘 → 退出托盘 → 托盘消失
6. 桌面双击快捷方式 → 托盘 + 设置窗重新出现

**音乐模式互斥（需求 2）：**
7. 设置窗常规页顶部应有"模式："+ 两个单选按钮
8. 选「灯效模式」+ 「RGB 循环」+ 应用 → 键盘 RGB 跑动
9. 选「音乐模式」 → 自动跳"音乐"页 + 常规页其他控件灰掉
10. "音乐"页选预设并应用 → 键盘按音乐响应
11. 切回常规页选「灯效模式」 → 自动恢复到上次的 RGB 循环
12. 托盘右键菜单 → 看到"效果"和"音乐模式"同级 + 当前模式打勾
13. 点托盘"音乐模式 ▶ 通用" → 切到音乐
14. 点托盘"效果 ▶ 心跳 ▶ 软件默认" → 切到灯效；OperatingMode 切回 Lighting

**升级回归（需求 2 数据迁移）：**
15. 装新版（覆盖安装） → 设置窗"音乐模式"自动选中（旧 settings.json 中是 Music）
16. 自定义音乐预设全部保留
17. settings.json 文件（在 `C:\ProgramData\ClevoLEDKeyboardControl\settings.json`）查看：含 `"OperatingMode": "Music"`，不再有 `"Type": "Music"`
18. 卸载（保留设置） → 桌面 / 开始菜单 .lnk 消失，settings.json 保留

---

## 拒收标准

- 任何手动验证项失败 → 修对应阶段
- 测试任意一项失败 → 修对应代码
- 升级后任何用户保存的预设/规则丢失 → 修迁移逻辑
- 设置窗中切到音乐模式后常规页控件未禁用 → 修 OnModeChanged
- 桌面或开始菜单没有快捷方式 → 修安装器
- 旧版本读新 settings.json 时崩溃 → 检查 OperatingMode 字段对未知字段是否容错（应当容错）
