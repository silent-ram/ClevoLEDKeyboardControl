# 音乐模式耳机支持（v2.1）实施计划

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** 让音乐模式跟随系统默认渲染设备切换（有线耳机 / A2DP 蓝牙），HFP 通话端点自动压底色，配套托盘 / 设置窗状态显示。

**Architecture:** 新增 `AudioSourceProvider` 集中管理音频源状态机（`Active` / `Hfp` / `Switching` / `Unavailable`），订阅 `IMMNotificationClient.OnDefaultDeviceChanged` 主动重建 loopback。两个 meter 类瘦身，从 Provider 拿 `MMDevice`。Worker 订阅 `SourceChanged` 写状态文件，Tray 用 `FileSystemWatcher` 读取并更新 NotifyIcon.Text 与设置窗音乐页 Label。

**Tech Stack:** .NET 8、NAudio 2.2.1（`MMDeviceEnumerator`、`IMMNotificationClient`、`WasapiLoopbackCapture(MMDevice)`）、xUnit、Windows Forms。

**Spec：** `docs/superpowers/specs/2026-06-14-music-mode-headphones-design.md`

**Branch：** `feat/music-headphones`（已基于 main 拉出，working tree clean）

---

## 文件结构概览

| 文件 | 职责 | 行为类型 |
|---|---|---|
| `ColorfulLedKeyboard.Core/AudioSourceStatus.cs` | 公共枚举 `AudioSourceStatus` | 新增 |
| `ColorfulLedKeyboard.Core/AudioSourceStatusInfo.cs` | 跨进程序列化 DTO | 新增 |
| `ColorfulLedKeyboard.Core/AudioSourceStatusFile.cs` | 状态文件原子读写 | 新增 |
| `ColorfulLedKeyboard.Core/AppPaths.cs` | 加 `AudioSourceStatusFileName` 常量 | 修改 |
| `ColorfulLedKeyboard.Service/IAudioDeviceProbe.cs` | 测试隔离接口 + `DeviceSnapshot` record | 新增 |
| `ColorfulLedKeyboard.Service/MMDeviceProbe.cs` | NAudio 实现 | 新增 |
| `ColorfulLedKeyboard.Service/AudioSourceProvider.cs` | 状态机 + IMMNotificationClient + fallback timer | 新增 |
| `ColorfulLedKeyboard.Service/AudioBandLevelMeter.cs` | 改造：消费 Provider，移除 5 秒退避 | 修改 |
| `ColorfulLedKeyboard.Service/SystemAudioLevelMeter.cs` | 改造：消费 Provider，移除 5 秒退避 | 修改 |
| `ColorfulLedKeyboard.Service/Worker.cs` | 订阅 SourceChanged 写状态文件，主循环按状态分支 | 修改 |
| `ColorfulLedKeyboard.Tray/AudioSourceStatusWatcher.cs` | Tray 端 FileSystemWatcher + debounce | 新增 |
| `ColorfulLedKeyboard.Tray/TrayApplicationContext.cs` | 持有 watcher，更新 NotifyIcon.Text | 修改 |
| `ColorfulLedKeyboard.Tray/SettingsForm.cs` | 音乐页顶部加 Label，公开 `UpdateAudioSourceLabel` | 修改 |
| `ColorfulLedKeyboard.Tests/AudioSourceStatusFileTests.cs` | StatusFile 5 个测试 | 新增 |
| `ColorfulLedKeyboard.Tests/AudioSourceProviderTests.cs` | Provider 13 个测试 | 新增 |
| `ColorfulLedKeyboard.Core/ColorfulLedKeyboard.Core.csproj` | 加 `InternalsVisibleTo("ColorfulLedKeyboard.Service")` 不需要；Service 加 `InternalsVisibleTo("ColorfulLedKeyboard.Tests")` | 修改 |

**编译运行命令（Windows，git bash）：**

- `dotnet build` 路径：项目根目录直接 `dotnet build ColorfulLedKeyboard.Service/ColorfulLedKeyboard.Service.csproj`
- 测试：`dotnet test ColorfulLedKeyboard.Tests/ColorfulLedKeyboard.Tests.csproj --nologo --verbosity minimal`
- 已有 51 个旧测试需保持通过；新增 18 个测试。

---

## Task 1：核心枚举 AudioSourceStatus

**Files:**
- Create: `ColorfulLedKeyboard.Core/AudioSourceStatus.cs`

- [ ] **Step 1：创建枚举文件**

```csharp
namespace ColorfulLedKeyboard.Core;

/// <summary>当前音频源的状态。决定灯效是按节拍跳还是压到底色。</summary>
public enum AudioSourceStatus
{
    /// <summary>设备正常、采样率合法、有有效样本流。</summary>
    Active,

    /// <summary>HFP/通话端点（采样率 ≤ 16000 Hz 且单声道）。loopback 不开启。</summary>
    Hfp,

    /// <summary>默认设备刚切换、capture 重建中（≤2 秒过渡窗口）。</summary>
    Switching,

    /// <summary>无可用 render 设备 / 已开 capture 但 1.5 秒无有效样本。</summary>
    Unavailable,
}
```

- [ ] **Step 2：编译验证**

Run: `dotnet build ColorfulLedKeyboard.Core/ColorfulLedKeyboard.Core.csproj --nologo --verbosity minimal`
Expected: `Build succeeded.`，0 errors。

- [ ] **Step 3：commit**

```bash
git add ColorfulLedKeyboard.Core/AudioSourceStatus.cs
git commit -m "feat(core): add AudioSourceStatus enum"
```

---

## Task 2：跨进程 DTO AudioSourceStatusInfo

**Files:**
- Create: `ColorfulLedKeyboard.Core/AudioSourceStatusInfo.cs`

- [ ] **Step 1：创建 DTO**

```csharp
namespace ColorfulLedKeyboard.Core;

/// <summary>跨进程序列化用 DTO。Service 写入，Tray 读取。
/// JSON 中 Status 序列化为字符串（不是数字），未来加枚举值不破坏兼容性。</summary>
public sealed class AudioSourceStatusInfo
{
    public AudioSourceStatus Status { get; set; } = AudioSourceStatus.Unavailable;
    public string DeviceFriendlyName { get; set; } = "";
    public string DeviceId { get; set; } = "";
    public DateTimeOffset UpdatedAt { get; set; }
}
```

- [ ] **Step 2：编译**

Run: `dotnet build ColorfulLedKeyboard.Core/ColorfulLedKeyboard.Core.csproj --nologo --verbosity minimal`
Expected: 0 errors。

- [ ] **Step 3：commit**

```bash
git add ColorfulLedKeyboard.Core/AudioSourceStatusInfo.cs
git commit -m "feat(core): add AudioSourceStatusInfo DTO"
```

---

## Task 3：AppPaths 加状态文件路径常量

**Files:**
- Modify: `ColorfulLedKeyboard.Core/AppPaths.cs`

- [ ] **Step 1：在常量列表加 `AudioSourceStatusFileName`**

把这一行：

```csharp
    public const string DriverComponentStateFileName = "driver-component.json";
```

改成：

```csharp
    public const string DriverComponentStateFileName = "driver-component.json";
    public const string AudioSourceStatusFileName = "audio-source-status.json";
```

- [ ] **Step 2：在静态属性区加 `AudioSourceStatusPath`**

把这一行：

```csharp
    public static string DriverComponentStatePath => Path.Combine(ProgramDataDirectory, DriverComponentStateFileName);
```

改成：

```csharp
    public static string DriverComponentStatePath => Path.Combine(ProgramDataDirectory, DriverComponentStateFileName);

    public static string AudioSourceStatusPath => Path.Combine(ProgramDataDirectory, AudioSourceStatusFileName);
```

- [ ] **Step 3：编译**

Run: `dotnet build ColorfulLedKeyboard.Core/ColorfulLedKeyboard.Core.csproj --nologo --verbosity minimal`
Expected: 0 errors。

- [ ] **Step 4：commit**

```bash
git add ColorfulLedKeyboard.Core/AppPaths.cs
git commit -m "feat(core): add audio source status file path"
```

---

## Task 4：AudioSourceStatusFile（原子读写）+ 5 个单元测试（TDD）

**Files:**
- Create: `ColorfulLedKeyboard.Core/AudioSourceStatusFile.cs`
- Create: `ColorfulLedKeyboard.Tests/AudioSourceStatusFileTests.cs`

- [ ] **Step 1：先写失败测试**

创建 `ColorfulLedKeyboard.Tests/AudioSourceStatusFileTests.cs`：

```csharp
using System.Text.Json;
using ColorfulLedKeyboard.Core;

namespace ColorfulLedKeyboard.Tests;

public class AudioSourceStatusFileTests : IDisposable
{
    private readonly string _tempPath;

    public AudioSourceStatusFileTests()
    {
        // 测试用绝对临时路径，避免污染 ProgramData
        _tempPath = Path.Combine(Path.GetTempPath(), $"audio-status-test-{Guid.NewGuid():N}.json");
    }

    public void Dispose()
    {
        if (File.Exists(_tempPath)) File.Delete(_tempPath);
        var tmp = _tempPath + ".tmp";
        if (File.Exists(tmp)) File.Delete(tmp);
    }

    [Fact]
    public void WriteThenRead_RoundTripsAllFields()
    {
        var info = new AudioSourceStatusInfo
        {
            Status = AudioSourceStatus.Active,
            DeviceFriendlyName = "iQOO TWS Air3",
            DeviceId = "{0.0.0.00000000}.{abcd}",
            UpdatedAt = DateTimeOffset.Parse("2026-06-14T10:00:00Z"),
        };
        AudioSourceStatusFile.WriteTo(_tempPath, info);

        var loaded = AudioSourceStatusFile.ReadFrom(_tempPath);

        Assert.NotNull(loaded);
        Assert.Equal(AudioSourceStatus.Active, loaded!.Status);
        Assert.Equal("iQOO TWS Air3", loaded.DeviceFriendlyName);
        Assert.Equal("{0.0.0.00000000}.{abcd}", loaded.DeviceId);
        Assert.Equal(info.UpdatedAt, loaded.UpdatedAt);
    }

    [Fact]
    public void Read_NonExistentFile_ReturnsNull()
    {
        var loaded = AudioSourceStatusFile.ReadFrom(_tempPath);
        Assert.Null(loaded);
    }

    [Fact]
    public void Read_CorruptJson_ReturnsNull()
    {
        File.WriteAllText(_tempPath, "{this is not valid json");
        var loaded = AudioSourceStatusFile.ReadFrom(_tempPath);
        Assert.Null(loaded);
    }

    [Fact]
    public void Write_SerializesStatusAsString()
    {
        var info = new AudioSourceStatusInfo { Status = AudioSourceStatus.Hfp };
        AudioSourceStatusFile.WriteTo(_tempPath, info);

        var raw = File.ReadAllText(_tempPath);
        Assert.Contains("\"Hfp\"", raw);
        Assert.DoesNotContain("\"Status\":1", raw);
    }

    [Fact]
    public void ConcurrentWriteRead_NeverYieldsHalfWrittenState()
    {
        // 并发 50 轮：写 / 读交替，读到非 null 时必须能反序列化成功。
        var writer = Task.Run(() =>
        {
            for (var i = 0; i < 50; i++)
            {
                AudioSourceStatusFile.WriteTo(_tempPath, new AudioSourceStatusInfo
                {
                    Status = AudioSourceStatus.Active,
                    DeviceFriendlyName = $"Device-{i}",
                });
            }
        });

        var failures = 0;
        var reader = Task.Run(() =>
        {
            for (var i = 0; i < 200; i++)
            {
                var info = AudioSourceStatusFile.ReadFrom(_tempPath);
                if (info != null && !info.DeviceFriendlyName.StartsWith("Device-"))
                {
                    Interlocked.Increment(ref failures);
                }
            }
        });

        Task.WaitAll(writer, reader);
        Assert.Equal(0, failures);
    }
}
```

- [ ] **Step 2：跑测试，确认失败（类还不存在）**

Run: `dotnet test ColorfulLedKeyboard.Tests/ColorfulLedKeyboard.Tests.csproj --nologo --verbosity minimal`
Expected: 编译错误，提示 `AudioSourceStatusFile` 找不到。

- [ ] **Step 3：实现 AudioSourceStatusFile**

创建 `ColorfulLedKeyboard.Core/AudioSourceStatusFile.cs`：

```csharp
using System.Text.Json;
using System.Text.Json.Serialization;

namespace ColorfulLedKeyboard.Core;

/// <summary>状态文件原子读写。Service 写、Tray 读。
/// 写入流程：写 .tmp → File.Move(overwrite=true)，避免读到半写状态。</summary>
public static class AudioSourceStatusFile
{
    private static readonly JsonSerializerOptions Options = new()
    {
        WriteIndented = false,
        Converters = { new JsonStringEnumConverter() },
    };

    public static string Path => AppPaths.AudioSourceStatusPath;

    public static void Write(AudioSourceStatusInfo info) => WriteTo(Path, info);

    public static AudioSourceStatusInfo? Read() => ReadFrom(Path);

    public static void WriteTo(string path, AudioSourceStatusInfo info)
    {
        var directory = System.IO.Path.GetDirectoryName(path);
        if (!string.IsNullOrEmpty(directory))
        {
            try { Directory.CreateDirectory(directory); }
            catch (IOException) { return; }
            catch (UnauthorizedAccessException) { return; }
        }

        var tmp = path + ".tmp";
        try
        {
            var json = JsonSerializer.Serialize(info, Options);
            File.WriteAllText(tmp, json);
            File.Move(tmp, path, overwrite: true);
        }
        catch (IOException)
        {
            TryDelete(tmp);
        }
        catch (UnauthorizedAccessException)
        {
            TryDelete(tmp);
        }
    }

    public static AudioSourceStatusInfo? ReadFrom(string path)
    {
        try
        {
            if (!File.Exists(path)) return null;
            var json = File.ReadAllText(path);
            return JsonSerializer.Deserialize<AudioSourceStatusInfo>(json, Options);
        }
        catch (IOException) { return null; }
        catch (JsonException) { return null; }
        catch (UnauthorizedAccessException) { return null; }
    }

    private static void TryDelete(string path)
    {
        try { if (File.Exists(path)) File.Delete(path); }
        catch { /* swallow */ }
    }
}
```

- [ ] **Step 4：跑测试，确认通过**

Run: `dotnet test ColorfulLedKeyboard.Tests/ColorfulLedKeyboard.Tests.csproj --nologo --verbosity minimal`
Expected: 51（旧）+ 5（新）= 56 个测试通过。

- [ ] **Step 5：commit**

```bash
git add ColorfulLedKeyboard.Core/AudioSourceStatusFile.cs ColorfulLedKeyboard.Tests/AudioSourceStatusFileTests.cs
git commit -m "feat(core): add AudioSourceStatusFile with atomic write + tests"
```

---

## Task 5：测试隔离接口 IAudioDeviceProbe + DeviceSnapshot

**Files:**
- Create: `ColorfulLedKeyboard.Service/IAudioDeviceProbe.cs`

- [ ] **Step 1：创建接口与 record**

```csharp
namespace ColorfulLedKeyboard.Service;

/// <summary>把"如何枚举默认 render 设备"抽出来，方便单测脱离 NAudio。
/// 生产实现 MMDeviceProbe 包 NAudio MMDeviceEnumerator；测试用 FakeAudioDeviceProbe。</summary>
internal interface IAudioDeviceProbe
{
    /// <summary>当前默认 render 设备的快照。null = 无可用设备。</summary>
    DeviceSnapshot? GetDefaultRenderDevice();

    /// <summary>按设备 id 拿快照。null = 设备已消失或无效。</summary>
    DeviceSnapshot? GetDevice(string id);
}

/// <summary>设备快照：仅包含 Provider 状态机所需字段。
/// SampleRate ≤ 16000 且 Channels == 1 → 判 HFP。</summary>
internal sealed record DeviceSnapshot(string Id, string FriendlyName, int SampleRate, int Channels);
```

- [ ] **Step 2：让 Tests 工程能访问 internal**

修改 `ColorfulLedKeyboard.Service/ColorfulLedKeyboard.Service.csproj`，在 `</PropertyGroup>` 之后、`<ItemGroup>` 之前插入：

```xml
  <ItemGroup>
    <InternalsVisibleTo Include="ColorfulLedKeyboard.Tests" />
  </ItemGroup>
```

最终 csproj 节点顺序：

```xml
<Project Sdk="Microsoft.NET.Sdk.Worker">
  <PropertyGroup>
    ...
  </PropertyGroup>
  <ItemGroup>
    <InternalsVisibleTo Include="ColorfulLedKeyboard.Tests" />
  </ItemGroup>
  <ItemGroup>
    <PackageReference Include="Microsoft.Extensions.Hosting" Version="8.0.0" />
    ...
  </ItemGroup>
  <ItemGroup>
    <ProjectReference Include="..\ColorfulLedKeyboard.Core\ColorfulLedKeyboard.Core.csproj" />
  </ItemGroup>
</Project>
```

- [ ] **Step 3：让 Tests 工程引用 Service**

修改 `ColorfulLedKeyboard.Tests/ColorfulLedKeyboard.Tests.csproj`，在已有的 `ProjectReference` 块里加一行（**注意：Tests 当前 TargetFramework 是 net8.0，Service 是 net8.0-windows，需要把 Tests 改成 net8.0-windows 才能引用，否则编译失败**）：

把这段：

```xml
  <PropertyGroup>
    <TargetFramework>net8.0</TargetFramework>
    <ImplicitUsings>enable</ImplicitUsings>
    <Nullable>enable</Nullable>
    <IsPackable>false</IsPackable>
  </PropertyGroup>
```

改成：

```xml
  <PropertyGroup>
    <TargetFramework>net8.0-windows</TargetFramework>
    <ImplicitUsings>enable</ImplicitUsings>
    <Nullable>enable</Nullable>
    <IsPackable>false</IsPackable>
  </PropertyGroup>
```

把这段：

```xml
  <ItemGroup>
    <ProjectReference Include="..\ColorfulLedKeyboard.Core\ColorfulLedKeyboard.Core.csproj" />
  </ItemGroup>
```

改成：

```xml
  <ItemGroup>
    <ProjectReference Include="..\ColorfulLedKeyboard.Core\ColorfulLedKeyboard.Core.csproj" />
    <ProjectReference Include="..\ColorfulLedKeyboard.Service\ColorfulLedKeyboard.Service.csproj" />
  </ItemGroup>
```

- [ ] **Step 4：编译验证**

Run: `dotnet build ColorfulLedKeyboard.Tests/ColorfulLedKeyboard.Tests.csproj --nologo --verbosity minimal`
Expected: 0 errors。

- [ ] **Step 5：跑现有测试确认没破坏**

Run: `dotnet test ColorfulLedKeyboard.Tests/ColorfulLedKeyboard.Tests.csproj --nologo --verbosity minimal`
Expected: 56 passed。

- [ ] **Step 6：commit**

```bash
git add ColorfulLedKeyboard.Service/IAudioDeviceProbe.cs \
        ColorfulLedKeyboard.Service/ColorfulLedKeyboard.Service.csproj \
        ColorfulLedKeyboard.Tests/ColorfulLedKeyboard.Tests.csproj
git commit -m "feat(service): add IAudioDeviceProbe + DeviceSnapshot for testability"
```

---

## Task 6：AudioSourceProvider 13 个单元测试（TDD：先写测试）

**Files:**
- Create: `ColorfulLedKeyboard.Tests/AudioSourceProviderTests.cs`

设计要点（在写测试时已固化）：
- Provider 构造接收 `IAudioDeviceProbe`；测试用 `FakeAudioDeviceProbe`。
- Provider 不直接订阅真实 `IMMNotificationClient`（生产路径在 MMDeviceProbe 里），而是通过 `internal` 方法 `TestOnly_SimulateDefaultDeviceChanged(string newDeviceId)` 模拟回调。
- fallback timer 由 `internal` 方法 `TestOnly_AdvanceFallbackClock(TimeSpan)` 推动，不靠真实时钟。
- Provider 内部对外暴露的状态方法：`Status`、`DeviceFriendlyName`、`SourceChanged` 事件、`ReportSamples()`、`RefreshNow()`、`Dispose()`。
- 测试不检查 `CurrentDevice`（NAudio MMDevice 类型），只检查 `Status` / `DeviceFriendlyName`。

- [ ] **Step 1：写测试文件（仅测试，不实现）**

```csharp
using ColorfulLedKeyboard.Core;
using ColorfulLedKeyboard.Service;

namespace ColorfulLedKeyboard.Tests;

public class AudioSourceProviderTests
{
    private sealed class FakeAudioDeviceProbe : IAudioDeviceProbe
    {
        private readonly Dictionary<string, DeviceSnapshot> _devices = new();
        public string? DefaultDeviceId { get; set; }
        public Func<string, DeviceSnapshot?>? InspectThrows { get; set; }

        public void Add(DeviceSnapshot snapshot) => _devices[snapshot.Id] = snapshot;

        public DeviceSnapshot? GetDefaultRenderDevice()
        {
            if (DefaultDeviceId is null) return null;
            return GetDevice(DefaultDeviceId);
        }

        public DeviceSnapshot? GetDevice(string id)
        {
            if (InspectThrows is { } thrower) return thrower(id);
            return _devices.TryGetValue(id, out var snapshot) ? snapshot : null;
        }
    }

    private static DeviceSnapshot Speaker(string id = "spk") =>
        new(id, "扬声器 (Realtek)", 48000, 2);

    private static DeviceSnapshot A2dp(string id = "bt-a2dp") =>
        new(id, "iQOO TWS Air3", 48000, 2);

    private static DeviceSnapshot Hfp(string id = "bt-hfp") =>
        new(id, "iQOO TWS Air3 Hands-Free", 16000, 1);

    [Fact]
    public void Initial_WithSpeaker_StatusIsActive()
    {
        var probe = new FakeAudioDeviceProbe();
        probe.Add(Speaker());
        probe.DefaultDeviceId = "spk";

        using var sut = new AudioSourceProvider(probe);

        Assert.Equal(AudioSourceStatus.Active, sut.Status);
        Assert.Equal("扬声器 (Realtek)", sut.DeviceFriendlyName);
    }

    [Fact]
    public void Initial_WithHfpDevice_StatusIsHfp()
    {
        var probe = new FakeAudioDeviceProbe();
        probe.Add(Hfp());
        probe.DefaultDeviceId = "bt-hfp";

        using var sut = new AudioSourceProvider(probe);

        Assert.Equal(AudioSourceStatus.Hfp, sut.Status);
    }

    [Fact]
    public void Initial_NoDevice_StatusIsUnavailable()
    {
        var probe = new FakeAudioDeviceProbe();
        using var sut = new AudioSourceProvider(probe);

        Assert.Equal(AudioSourceStatus.Unavailable, sut.Status);
    }

    [Fact]
    public void DefaultDeviceChanged_ToA2dp_FiresSwitchingThenActive()
    {
        var probe = new FakeAudioDeviceProbe();
        probe.Add(Speaker());
        probe.DefaultDeviceId = "spk";
        using var sut = new AudioSourceProvider(probe);

        var events = new List<AudioSourceStatus>();
        sut.SourceChanged += (_, e) => events.Add(e.Status);

        probe.Add(A2dp());
        probe.DefaultDeviceId = "bt-a2dp";
        sut.TestOnly_SimulateDefaultDeviceChanged("bt-a2dp");

        Assert.Equal(new[] { AudioSourceStatus.Switching, AudioSourceStatus.Active }, events);
        Assert.Equal(AudioSourceStatus.Active, sut.Status);
        Assert.Equal("iQOO TWS Air3", sut.DeviceFriendlyName);
    }

    [Fact]
    public void DefaultDeviceChanged_ToHfp_FiresSwitchingThenHfp()
    {
        var probe = new FakeAudioDeviceProbe();
        probe.Add(Speaker());
        probe.DefaultDeviceId = "spk";
        using var sut = new AudioSourceProvider(probe);

        var events = new List<AudioSourceStatus>();
        sut.SourceChanged += (_, e) => events.Add(e.Status);

        probe.Add(Hfp());
        probe.DefaultDeviceId = "bt-hfp";
        sut.TestOnly_SimulateDefaultDeviceChanged("bt-hfp");

        Assert.Equal(new[] { AudioSourceStatus.Switching, AudioSourceStatus.Hfp }, events);
    }

    [Fact]
    public void ReportSamples_WithinFallbackWindow_DoesNotFireUnavailable()
    {
        var probe = new FakeAudioDeviceProbe();
        probe.Add(Speaker());
        probe.DefaultDeviceId = "spk";
        using var sut = new AudioSourceProvider(probe);

        var events = new List<AudioSourceStatus>();
        sut.SourceChanged += (_, e) => events.Add(e.Status);

        sut.ReportSamples();
        sut.TestOnly_AdvanceFallbackClock(TimeSpan.FromMilliseconds(1000));

        Assert.Empty(events);
        Assert.Equal(AudioSourceStatus.Active, sut.Status);
    }

    [Fact]
    public void NoSamplesFor1500ms_FromActive_TransitionsToUnavailable()
    {
        var probe = new FakeAudioDeviceProbe();
        probe.Add(Speaker());
        probe.DefaultDeviceId = "spk";
        using var sut = new AudioSourceProvider(probe);

        var events = new List<AudioSourceStatus>();
        sut.SourceChanged += (_, e) => events.Add(e.Status);

        sut.ReportSamples();
        sut.TestOnly_AdvanceFallbackClock(TimeSpan.FromMilliseconds(1600));

        Assert.Contains(AudioSourceStatus.Unavailable, events);
        Assert.Equal(AudioSourceStatus.Unavailable, sut.Status);
    }

    [Fact]
    public void ReportSamples_AfterUnavailable_TransitionsBackToActive()
    {
        var probe = new FakeAudioDeviceProbe();
        probe.Add(Speaker());
        probe.DefaultDeviceId = "spk";
        using var sut = new AudioSourceProvider(probe);

        sut.ReportSamples();
        sut.TestOnly_AdvanceFallbackClock(TimeSpan.FromMilliseconds(1600));
        Assert.Equal(AudioSourceStatus.Unavailable, sut.Status);

        var events = new List<AudioSourceStatus>();
        sut.SourceChanged += (_, e) => events.Add(e.Status);

        sut.ReportSamples();
        sut.TestOnly_AdvanceFallbackClock(TimeSpan.FromMilliseconds(100));

        Assert.Contains(AudioSourceStatus.Active, events);
        Assert.Equal(AudioSourceStatus.Active, sut.Status);
    }

    [Fact]
    public void InspectDevice_Throws_StatusFallsBackToSwitching()
    {
        var probe = new FakeAudioDeviceProbe();
        probe.Add(Speaker());
        probe.DefaultDeviceId = "spk";
        using var sut = new AudioSourceProvider(probe);

        // 模拟下一次 GetDevice 抛 COM 异常
        probe.InspectThrows = _ => throw new InvalidOperationException("simulated COM");
        probe.DefaultDeviceId = "broken";
        sut.TestOnly_SimulateDefaultDeviceChanged("broken");

        Assert.Equal(AudioSourceStatus.Switching, sut.Status);
    }

    [Fact]
    public void NotificationCallback_Throws_DoesNotPropagate()
    {
        var probe = new FakeAudioDeviceProbe();
        probe.Add(Speaker());
        probe.DefaultDeviceId = "spk";
        using var sut = new AudioSourceProvider(probe);

        sut.SourceChanged += (_, _) => throw new InvalidOperationException("subscriber bug");

        // 事件订阅者抛异常不应让回调本身崩溃
        probe.Add(A2dp());
        probe.DefaultDeviceId = "bt-a2dp";
        var ex = Record.Exception(() => sut.TestOnly_SimulateDefaultDeviceChanged("bt-a2dp"));

        Assert.Null(ex);
    }

    [Fact]
    public void Dispose_Twice_DoesNotThrow()
    {
        var probe = new FakeAudioDeviceProbe();
        probe.Add(Speaker());
        probe.DefaultDeviceId = "spk";
        var sut = new AudioSourceProvider(probe);

        sut.Dispose();
        var ex = Record.Exception(() => sut.Dispose());

        Assert.Null(ex);
    }

    [Fact]
    public void ReportSamples_AfterDispose_DoesNotThrow()
    {
        var probe = new FakeAudioDeviceProbe();
        probe.Add(Speaker());
        probe.DefaultDeviceId = "spk";
        var sut = new AudioSourceProvider(probe);
        sut.Dispose();

        var ex = Record.Exception(() => sut.ReportSamples());
        Assert.Null(ex);
    }

    [Fact]
    public void OneSubscriberThrows_OtherSubscribersStillReceive()
    {
        var probe = new FakeAudioDeviceProbe();
        probe.Add(Speaker());
        probe.DefaultDeviceId = "spk";
        using var sut = new AudioSourceProvider(probe);

        var second = new List<AudioSourceStatus>();
        sut.SourceChanged += (_, _) => throw new InvalidOperationException("first sub bug");
        sut.SourceChanged += (_, e) => second.Add(e.Status);

        probe.Add(A2dp());
        probe.DefaultDeviceId = "bt-a2dp";
        sut.TestOnly_SimulateDefaultDeviceChanged("bt-a2dp");

        Assert.Contains(AudioSourceStatus.Switching, second);
        Assert.Contains(AudioSourceStatus.Active, second);
    }
}
```

- [ ] **Step 2：跑测试，确认编译失败（Provider 还不存在）**

Run: `dotnet test ColorfulLedKeyboard.Tests/ColorfulLedKeyboard.Tests.csproj --nologo --verbosity minimal`
Expected: 编译错误，找不到 `AudioSourceProvider` / `AudioSourceChangedEventArgs`。

- [ ] **Step 3：commit 测试（先于实现，TDD red）**

```bash
git add ColorfulLedKeyboard.Tests/AudioSourceProviderTests.cs
git commit -m "test(service): add 13 AudioSourceProvider unit tests (red)"
```

---

## Task 7：AudioSourceProvider 实现（TDD：让测试通过）

**Files:**
- Create: `ColorfulLedKeyboard.Service/AudioSourceProvider.cs`

- [ ] **Step 1：实现 Provider**

```csharp
using ColorfulLedKeyboard.Core;
using Microsoft.Extensions.Logging;
using NAudio.CoreAudioApi;
using NAudio.CoreAudioApi.Interfaces;

namespace ColorfulLedKeyboard.Service;

/// <summary>音频源状态机 + 设备切换通知 + 1.5 秒无样本 fallback。
/// 两个 meter 类共享一个 Provider，从这里拿 MMDevice。</summary>
public sealed class AudioSourceProvider : IDisposable
{
    private static readonly TimeSpan FallbackThreshold = TimeSpan.FromMilliseconds(1500);

    private readonly object _stateLock = new();
    private readonly IAudioDeviceProbe _probe;
    private readonly ILogger<AudioSourceProvider>? _logger;
    private readonly System.Threading.Timer? _fallbackTimer;
    private readonly bool _ownsProbe;

    private AudioSourceStatus _status;
    private string _deviceFriendlyName = "";
    private string _deviceId = "";
    private long _lastSampleTicks;
    private TimeSpan _testClockOffset = TimeSpan.Zero;
    private bool _disposed;

    /// <summary>生产构造：内部创建 MMDeviceProbe。</summary>
    public AudioSourceProvider(ILogger<AudioSourceProvider>? logger = null)
        : this(new MMDeviceProbe(), logger, ownsProbe: true)
    {
    }

    /// <summary>测试构造：注入 probe，不启用真实 fallback timer。</summary>
    internal AudioSourceProvider(IAudioDeviceProbe probe, ILogger<AudioSourceProvider>? logger = null)
        : this(probe, logger, ownsProbe: false)
    {
    }

    private AudioSourceProvider(IAudioDeviceProbe probe, ILogger<AudioSourceProvider>? logger, bool ownsProbe)
    {
        _probe = probe;
        _logger = logger;
        _ownsProbe = ownsProbe;
        _lastSampleTicks = 0;

        // 真实部署时启用周期 timer；测试构造（ownsProbe=false）依赖 TestOnly_AdvanceFallbackClock 推动
        if (ownsProbe)
        {
            _fallbackTimer = new System.Threading.Timer(_ => CheckFallback(), null,
                TimeSpan.FromMilliseconds(500), TimeSpan.FromMilliseconds(500));
        }

        InitialResolve();
    }

    public AudioSourceStatus Status
    {
        get { lock (_stateLock) return _status; }
    }

    public string DeviceFriendlyName
    {
        get { lock (_stateLock) return _deviceFriendlyName; }
    }

    public string DeviceId
    {
        get { lock (_stateLock) return _deviceId; }
    }

    public event EventHandler<AudioSourceChangedEventArgs>? SourceChanged;

    /// <summary>meter 收到一帧 PCM 后调用。无锁高频路径。</summary>
    public void ReportSamples()
    {
        if (_disposed) return;
        var now = DateTime.UtcNow.Ticks;
        System.Threading.Interlocked.Exchange(ref _lastSampleTicks, now);
    }

    /// <summary>Worker 进入 Music 模式那一刻调一次，强刷状态文件。</summary>
    public void RefreshNow()
    {
        if (_disposed) return;
        ResolveAndPublish(transitional: false);
    }

    /// <summary>测试入口：模拟 IMMNotificationClient.OnDefaultDeviceChanged。</summary>
    internal void TestOnly_SimulateDefaultDeviceChanged(string newDeviceId)
    {
        OnDefaultDeviceChangedInternal(newDeviceId);
    }

    /// <summary>测试入口：推进 fallback 时钟。</summary>
    internal void TestOnly_AdvanceFallbackClock(TimeSpan delta)
    {
        _testClockOffset += delta;
        CheckFallback();
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;

        _fallbackTimer?.Dispose();

        if (_ownsProbe && _probe is IDisposable disposable)
        {
            try { disposable.Dispose(); }
            catch { /* swallow */ }
        }

        SourceChanged = null;
    }

    private void InitialResolve()
    {
        try
        {
            var snapshot = _probe.GetDefaultRenderDevice();
            ApplySnapshot(snapshot, fireEvent: false);
        }
        catch (Exception ex)
        {
            _logger?.LogWarning(ex, "AudioSourceProvider initial resolve failed");
            lock (_stateLock)
            {
                _status = AudioSourceStatus.Unavailable;
                _deviceFriendlyName = "";
                _deviceId = "";
            }
        }
    }

    /// <summary>NotificationClient 真实回调进来时也走这条路径（生产路径在 MMDeviceProbe 里 wire 到这个方法）。</summary>
    internal void OnDefaultDeviceChangedInternal(string newDeviceId)
    {
        if (_disposed) return;

        // 第一次：进入 Switching
        PublishStatus(AudioSourceStatus.Switching, _deviceFriendlyName, _deviceId);

        // 第二次：根据 InspectDevice 结果决定 Active / Hfp / Switching（保守）
        ResolveAndPublish(transitional: true);
    }

    private void ResolveAndPublish(bool transitional)
    {
        DeviceSnapshot? snapshot;
        try
        {
            snapshot = _probe.GetDefaultRenderDevice();
        }
        catch (Exception ex)
        {
            _logger?.LogWarning(ex, "AudioSourceProvider resolve threw, keeping Switching");
            // 保守：当前回调过程中保持 Switching，让 fallback 兜底
            PublishStatus(AudioSourceStatus.Switching, "", "");
            return;
        }

        ApplySnapshot(snapshot, fireEvent: true);
    }

    private void ApplySnapshot(DeviceSnapshot? snapshot, bool fireEvent)
    {
        AudioSourceStatus newStatus;
        string newName;
        string newId;

        if (snapshot is null)
        {
            newStatus = AudioSourceStatus.Unavailable;
            newName = "";
            newId = "";
        }
        else if (IsHfp(snapshot))
        {
            newStatus = AudioSourceStatus.Hfp;
            newName = snapshot.FriendlyName;
            newId = snapshot.Id;
        }
        else
        {
            newStatus = AudioSourceStatus.Active;
            newName = snapshot.FriendlyName;
            newId = snapshot.Id;
        }

        bool changed;
        lock (_stateLock)
        {
            changed = _status != newStatus || _deviceFriendlyName != newName || _deviceId != newId;
            _status = newStatus;
            _deviceFriendlyName = newName;
            _deviceId = newId;
        }

        if (changed && fireEvent)
        {
            RaiseSourceChanged(newStatus, newName, newId);
        }
    }

    private void PublishStatus(AudioSourceStatus status, string name, string id)
    {
        bool changed;
        lock (_stateLock)
        {
            changed = _status != status || _deviceFriendlyName != name || _deviceId != id;
            _status = status;
            _deviceFriendlyName = name;
            _deviceId = id;
        }
        if (changed)
        {
            RaiseSourceChanged(status, name, id);
        }
    }

    private void RaiseSourceChanged(AudioSourceStatus status, string name, string id)
    {
        var handlers = SourceChanged;
        if (handlers is null) return;

        var args = new AudioSourceChangedEventArgs { Status = status, DeviceFriendlyName = name, DeviceId = id };
        foreach (var handler in handlers.GetInvocationList())
        {
            try
            {
                ((EventHandler<AudioSourceChangedEventArgs>)handler).Invoke(this, args);
            }
            catch (Exception ex)
            {
                _logger?.LogWarning(ex, "AudioSourceProvider subscriber threw");
            }
        }
    }

    private static bool IsHfp(DeviceSnapshot snapshot) =>
        snapshot.SampleRate > 0 && snapshot.SampleRate <= 16000 && snapshot.Channels == 1;

    private void CheckFallback()
    {
        if (_disposed) return;
        try
        {
            var lastTicks = System.Threading.Interlocked.Read(ref _lastSampleTicks);
            DateTime nowUtc;
            if (_ownsProbe)
            {
                nowUtc = DateTime.UtcNow;
            }
            else
            {
                nowUtc = new DateTime(lastTicks, DateTimeKind.Utc) + _testClockOffset;
            }
            var elapsed = lastTicks == 0
                ? TimeSpan.Zero
                : nowUtc - new DateTime(lastTicks, DateTimeKind.Utc);

            AudioSourceStatus current;
            string name, id;
            lock (_stateLock)
            {
                current = _status;
                name = _deviceFriendlyName;
                id = _deviceId;
            }

            // Active 状态下，超过 1.5s 没样本 → Unavailable
            if (current == AudioSourceStatus.Active && lastTicks != 0 && elapsed > FallbackThreshold)
            {
                PublishStatus(AudioSourceStatus.Unavailable, name, id);
                return;
            }

            // Unavailable 状态下，最近 1.5s 内有样本 → 切回 Active（设备名保持）
            if (current == AudioSourceStatus.Unavailable && lastTicks != 0 && elapsed <= FallbackThreshold)
            {
                PublishStatus(AudioSourceStatus.Active, name, id);
                return;
            }
        }
        catch (Exception ex)
        {
            _logger?.LogWarning(ex, "AudioSourceProvider fallback check threw");
        }
    }

    /// <summary>Provider 暴露给 meter 的辅助：当前 NAudio MMDevice。
    /// 测试构造（FakeAudioDeviceProbe）下永远返回 null。
    /// 生产构造（MMDeviceProbe）下从 probe 内部缓存拿。</summary>
    public MMDevice? CurrentDevice
    {
        get
        {
            if (_probe is MMDeviceProbe production) return production.GetCurrentMMDevice();
            return null;
        }
    }
}

public sealed class AudioSourceChangedEventArgs : EventArgs
{
    public AudioSourceStatus Status { get; init; }
    public string DeviceFriendlyName { get; init; } = "";
    public string DeviceId { get; init; } = "";
}
```

注意：`MMDeviceProbe` 在下一个 Task 实现。当前 Task 的代码中引用它，所以 Step 1 完成后编译会暂时失败 → 跑测试只看 Provider 的 13 个测试是否能通过。**实际编译要等 Task 8 完成后才会全绿。**

为了让本 Task 单独验证，先在 `MMDeviceProbe.cs` 暂时放一个空骨架（Task 8 再补完）：

创建 `ColorfulLedKeyboard.Service/MMDeviceProbe.cs`（最小骨架）：

```csharp
using NAudio.CoreAudioApi;

namespace ColorfulLedKeyboard.Service;

internal sealed class MMDeviceProbe : IAudioDeviceProbe, IDisposable
{
    public DeviceSnapshot? GetDefaultRenderDevice() => null;
    public DeviceSnapshot? GetDevice(string id) => null;
    public MMDevice? GetCurrentMMDevice() => null;
    public void Dispose() { }
}
```

- [ ] **Step 2：跑测试，确认 13 个 Provider 测试通过**

Run: `dotnet test ColorfulLedKeyboard.Tests/ColorfulLedKeyboard.Tests.csproj --filter "FullyQualifiedName~AudioSourceProviderTests" --nologo --verbosity minimal`
Expected: 13 passed。

- [ ] **Step 3：跑全量测试**

Run: `dotnet test ColorfulLedKeyboard.Tests/ColorfulLedKeyboard.Tests.csproj --nologo --verbosity minimal`
Expected: 51 + 5 + 13 = 69 passed。

- [ ] **Step 4：commit**

```bash
git add ColorfulLedKeyboard.Service/AudioSourceProvider.cs ColorfulLedKeyboard.Service/MMDeviceProbe.cs
git commit -m "feat(service): implement AudioSourceProvider state machine (green)"
```

---

## Task 8：MMDeviceProbe 真实实现 + 接入 Provider

**Files:**
- Modify: `ColorfulLedKeyboard.Service/MMDeviceProbe.cs`
- Modify: `ColorfulLedKeyboard.Service/AudioSourceProvider.cs`

- [ ] **Step 1：替换 MMDeviceProbe 完整实现**

把 `ColorfulLedKeyboard.Service/MMDeviceProbe.cs` 整个文件内容替换为：

```csharp
using NAudio.CoreAudioApi;
using NAudio.CoreAudioApi.Interfaces;

namespace ColorfulLedKeyboard.Service;

/// <summary>NAudio 实现的 IAudioDeviceProbe，封装 MMDeviceEnumerator 与
/// IMMNotificationClient 通知。Provider 通过 SetCallback 注册回调，所有 NAudio 异常被吞下。</summary>
internal sealed class MMDeviceProbe : IAudioDeviceProbe, IMMNotificationClient, IDisposable
{
    private readonly object _lock = new();
    private readonly MMDeviceEnumerator _enumerator = new();
    private MMDevice? _currentDevice;
    private Action<string>? _onDefaultDeviceChanged;
    private bool _registered;
    private bool _disposed;

    public MMDeviceProbe()
    {
        try
        {
            _enumerator.RegisterEndpointNotificationCallback(this);
            _registered = true;
        }
        catch
        {
            _registered = false;
        }
    }

    public void SetCallback(Action<string> onDefaultDeviceChanged)
    {
        _onDefaultDeviceChanged = onDefaultDeviceChanged;
    }

    public DeviceSnapshot? GetDefaultRenderDevice()
    {
        if (_disposed) return null;
        try
        {
            var device = _enumerator.GetDefaultAudioEndpoint(DataFlow.Render, Role.Multimedia);
            return CacheAndSnapshot(device);
        }
        catch
        {
            return null;
        }
    }

    public DeviceSnapshot? GetDevice(string id)
    {
        if (_disposed || string.IsNullOrEmpty(id)) return null;
        try
        {
            var device = _enumerator.GetDevice(id);
            return CacheAndSnapshot(device);
        }
        catch
        {
            return null;
        }
    }

    public MMDevice? GetCurrentMMDevice()
    {
        lock (_lock) return _currentDevice;
    }

    private DeviceSnapshot? CacheAndSnapshot(MMDevice device)
    {
        if (device is null) return null;

        int sampleRate = 0;
        int channels = 0;
        try
        {
            var mix = device.AudioClient.MixFormat;
            sampleRate = mix.SampleRate;
            channels = mix.Channels;
        }
        catch
        {
            // 拿不到 MixFormat 的设备，按非 HFP 处理
        }

        var snapshot = new DeviceSnapshot(device.ID, device.FriendlyName, sampleRate, channels);

        lock (_lock)
        {
            if (_currentDevice is { } previous && !ReferenceEquals(previous, device))
            {
                try { previous.Dispose(); } catch { }
            }
            _currentDevice = device;
        }

        return snapshot;
    }

    public void OnDefaultDeviceChanged(DataFlow flow, Role role, string defaultDeviceId)
    {
        if (_disposed) return;
        if (flow != DataFlow.Render) return;
        if (role != Role.Multimedia) return;

        try
        {
            _onDefaultDeviceChanged?.Invoke(defaultDeviceId);
        }
        catch
        {
            // 不让异常冒到 NAudio COM 层
        }
    }

    public void OnDeviceAdded(string deviceId) { }
    public void OnDeviceRemoved(string deviceId) { }
    public void OnDeviceStateChanged(string deviceId, DeviceState newState) { }
    public void OnPropertyValueChanged(string pwstrDeviceId, PropertyKey key) { }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;

        if (_registered)
        {
            try { _enumerator.UnregisterEndpointNotificationCallback(this); } catch { }
        }

        lock (_lock)
        {
            try { _currentDevice?.Dispose(); } catch { }
            _currentDevice = null;
        }

        try { _enumerator.Dispose(); } catch { }
    }
}
```

- [ ] **Step 2：在 Provider 构造里 wire MMDeviceProbe 回调**

打开 `ColorfulLedKeyboard.Service/AudioSourceProvider.cs`，找到这段：

```csharp
    private AudioSourceProvider(IAudioDeviceProbe probe, ILogger<AudioSourceProvider>? logger, bool ownsProbe)
    {
        _probe = probe;
        _logger = logger;
        _ownsProbe = ownsProbe;
        _lastSampleTicks = 0;

        // 真实部署时启用周期 timer；测试构造（ownsProbe=false）依赖 TestOnly_AdvanceFallbackClock 推动
        if (ownsProbe)
        {
            _fallbackTimer = new System.Threading.Timer(_ => CheckFallback(), null,
                TimeSpan.FromMilliseconds(500), TimeSpan.FromMilliseconds(500));
        }

        InitialResolve();
    }
```

替换为：

```csharp
    private AudioSourceProvider(IAudioDeviceProbe probe, ILogger<AudioSourceProvider>? logger, bool ownsProbe)
    {
        _probe = probe;
        _logger = logger;
        _ownsProbe = ownsProbe;
        _lastSampleTicks = 0;

        // 真实部署：把 NAudio 回调 wire 到 OnDefaultDeviceChangedInternal
        if (ownsProbe && probe is MMDeviceProbe mmProbe)
        {
            mmProbe.SetCallback(OnDefaultDeviceChangedInternal);
        }

        // 真实部署时启用周期 timer；测试构造依赖 TestOnly_AdvanceFallbackClock 推动
        if (ownsProbe)
        {
            _fallbackTimer = new System.Threading.Timer(_ => CheckFallback(), null,
                TimeSpan.FromMilliseconds(500), TimeSpan.FromMilliseconds(500));
        }

        InitialResolve();
    }
```

- [ ] **Step 3：编译 + 跑测试**

Run: `dotnet build ColorfulLedKeyboard.Service/ColorfulLedKeyboard.Service.csproj --nologo --verbosity minimal`
Expected: 0 errors。

Run: `dotnet test ColorfulLedKeyboard.Tests/ColorfulLedKeyboard.Tests.csproj --nologo --verbosity minimal`
Expected: 69 passed。

- [ ] **Step 4：commit**

```bash
git add ColorfulLedKeyboard.Service/MMDeviceProbe.cs ColorfulLedKeyboard.Service/AudioSourceProvider.cs
git commit -m "feat(service): wire MMDeviceProbe with IMMNotificationClient"
```

---

## Task 9：AudioBandLevelMeter 改造

**Files:**
- Modify: `ColorfulLedKeyboard.Service/AudioBandLevelMeter.cs`

目标：构造接收 Provider；EnsureCapture 校验 Status + 用 Provider.CurrentDevice；OnDataAvailable 调 ReportSamples；新增 PauseCapture；移除 5 秒退避。

- [ ] **Step 1：在文件顶部加 `_source` 字段，替换构造与 EnsureCapture**

打开 `ColorfulLedKeyboard.Service/AudioBandLevelMeter.cs`。

把这段：

```csharp
    private readonly object _sync = new();
    private readonly BandState[] _bandStates = AdaptiveBands.Select(_ => new BandState()).ToArray();
    private WasapiLoopbackCapture? _capture;
    private float[] _samples = [];
    private int _sampleRate = 48000;
    private DateTimeOffset _nextRetry = DateTimeOffset.MinValue;
```

改成：

```csharp
    private readonly object _sync = new();
    private readonly BandState[] _bandStates = AdaptiveBands.Select(_ => new BandState()).ToArray();
    private readonly AudioSourceProvider _source;
    private WasapiLoopbackCapture? _capture;
    private float[] _samples = [];
    private int _sampleRate = 48000;
```

注意：删除了 `_nextRetry` 字段。

- [ ] **Step 2：把 GetLevel 之前加构造与事件处理**

把 `public float GetLevel` 这一行之前（紧邻 `_gateOpen` 字段定义之后）插入：

```csharp
    public AudioBandLevelMeter(AudioSourceProvider source)
    {
        _source = source;
        _source.SourceChanged += OnSourceChanged;
    }

    private void OnSourceChanged(object? sender, AudioSourceChangedEventArgs e)
    {
        // 任何状态变化都先停 capture，下一帧懒重建（仅 Active 时才会真正重建）
        ResetCapture();
    }

    public void PauseCapture()
    {
        ResetCapture();
    }
```

- [ ] **Step 3：替换 EnsureCapture 使用 Provider.CurrentDevice**

把这段：

```csharp
    private void EnsureCapture()
    {
        if (_capture is not null || DateTimeOffset.UtcNow < _nextRetry)
        {
            return;
        }

        try
        {
            _capture = new WasapiLoopbackCapture();
            _sampleRate = _capture.WaveFormat.SampleRate;
            _capture.DataAvailable += OnDataAvailable;
            _capture.RecordingStopped += (_, _) => ResetCapture();
            _capture.StartRecording();
        }
        catch
        {
            ResetCapture();
            _nextRetry = DateTimeOffset.UtcNow.AddSeconds(5);
        }
    }
```

替换为：

```csharp
    private void EnsureCapture()
    {
        if (_capture is not null) return;
        if (_source.Status != AudioSourceStatus.Active) return;

        var device = _source.CurrentDevice;
        if (device is null) return;

        try
        {
            _capture = new WasapiLoopbackCapture(device);
            _sampleRate = _capture.WaveFormat.SampleRate;
            _capture.DataAvailable += OnDataAvailable;
            _capture.RecordingStopped += (_, _) => ResetCapture();
            _capture.StartRecording();
        }
        catch
        {
            ResetCapture();
        }
    }
```

- [ ] **Step 4：在 OnDataAvailable 顶部加 `_source.ReportSamples();`**

把这段：

```csharp
    private void OnDataAvailable(object? sender, WaveInEventArgs args)
    {
        if (sender is not WasapiLoopbackCapture capture)
        {
            return;
        }

        var format = capture.WaveFormat;
```

改成：

```csharp
    private void OnDataAvailable(object? sender, WaveInEventArgs args)
    {
        if (sender is not WasapiLoopbackCapture capture)
        {
            return;
        }

        _source.ReportSamples();

        var format = capture.WaveFormat;
```

- [ ] **Step 5：Dispose 取消订阅**

把这段：

```csharp
    public void Dispose()
    {
        ResetCapture();
    }
```

改成：

```csharp
    public void Dispose()
    {
        _source.SourceChanged -= OnSourceChanged;
        ResetCapture();
    }
```

- [ ] **Step 6：编译（暂不跑全量测试，因 Worker 还没改）**

Run: `dotnet build ColorfulLedKeyboard.Service/ColorfulLedKeyboard.Service.csproj --nologo --verbosity minimal`
Expected: Worker.cs 报 `does not contain a constructor that takes 0 arguments` —— 是预期，下个 Task 改 Worker 后会解决。

- [ ] **Step 7：commit**

```bash
git add ColorfulLedKeyboard.Service/AudioBandLevelMeter.cs
git commit -m "refactor(service): AudioBandLevelMeter consumes AudioSourceProvider"
```

---

## Task 10：SystemAudioLevelMeter 改造

**Files:**
- Modify: `ColorfulLedKeyboard.Service/SystemAudioLevelMeter.cs`

- [ ] **Step 1：把整个文件替换为**

```csharp
using ColorfulLedKeyboard.Core;
using NAudio.CoreAudioApi;

namespace ColorfulLedKeyboard.Service;

internal sealed class SystemAudioLevelMeter : IDisposable
{
    private readonly AudioSourceProvider _source;

    public SystemAudioLevelMeter(AudioSourceProvider source)
    {
        _source = source;
    }

    public float GetPeakLevel()
    {
        if (_source.Status != AudioSourceStatus.Active) return 0f;
        var device = _source.CurrentDevice;
        if (device is null) return 0f;

        try
        {
            var peak = device.AudioMeterInformation.MasterPeakValue;
            if (peak > 0f) _source.ReportSamples();
            return peak;
        }
        catch
        {
            _source.RefreshNow();
            return 0f;
        }
    }

    public float GetMasterVolumeScalar()
    {
        if (_source.Status != AudioSourceStatus.Active) return 1f;
        var device = _source.CurrentDevice;
        if (device is null) return 1f;

        try
        {
            if (device.AudioEndpointVolume.Mute) return 0f;
            return device.AudioEndpointVolume.MasterVolumeLevelScalar;
        }
        catch
        {
            _source.RefreshNow();
            return 1f;
        }
    }

    /// <summary>退出 Music 模式时调用。当前 device 由 Provider 持有，这里仅占位以匹配 spec 接口。</summary>
    public void PauseDevice() { }

    public void Dispose()
    {
        // device 生命周期由 Provider 管理
    }
}
```

- [ ] **Step 2：编译**

Run: `dotnet build ColorfulLedKeyboard.Service/ColorfulLedKeyboard.Service.csproj --nologo --verbosity minimal`
Expected: Worker.cs 仍然报错（构造签名变了），其他文件 0 errors。

- [ ] **Step 3：commit**

```bash
git add ColorfulLedKeyboard.Service/SystemAudioLevelMeter.cs
git commit -m "refactor(service): SystemAudioLevelMeter consumes AudioSourceProvider"
```

---

## Task 11：Worker 改造

**Files:**
- Modify: `ColorfulLedKeyboard.Service/Worker.cs`

- [ ] **Step 1：改字段与构造**

把这段：

```csharp
public class Worker : BackgroundService
{
    private readonly SettingsStore _settingsStore = new();
    private readonly DchuKeyboardDevice _device = new();
    private readonly SystemAudioLevelMeter _audioLevelMeter = new();
    private readonly AudioBandLevelMeter _audioBandLevelMeter = new();
    private readonly ILogger<Worker> _logger;
    private FileSystemWatcher? _watcher;
    private volatile bool _settingsChanged = true;

    public Worker(ILogger<Worker> logger)
    {
        _logger = logger;
    }
```

替换为：

```csharp
public class Worker : BackgroundService
{
    private readonly SettingsStore _settingsStore = new();
    private readonly DchuKeyboardDevice _device = new();
    private readonly AudioSourceProvider _audioSource;
    private readonly SystemAudioLevelMeter _audioLevelMeter;
    private readonly AudioBandLevelMeter _audioBandLevelMeter;
    private readonly ILogger<Worker> _logger;
    private FileSystemWatcher? _watcher;
    private volatile bool _settingsChanged = true;

    public Worker(ILogger<Worker> logger, ILoggerFactory loggerFactory)
    {
        _logger = logger;
        _audioSource = new AudioSourceProvider(loggerFactory.CreateLogger<AudioSourceProvider>());
        _audioLevelMeter = new SystemAudioLevelMeter(_audioSource);
        _audioBandLevelMeter = new AudioBandLevelMeter(_audioSource);
        _audioSource.SourceChanged += OnAudioSourceChanged;
    }
```

- [ ] **Step 2：StopAsync 加 Provider 清理**

把这段：

```csharp
    public override Task StopAsync(CancellationToken cancellationToken)
    {
        _watcher?.Dispose();
        _audioLevelMeter.Dispose();
        _audioBandLevelMeter.Dispose();
        return base.StopAsync(cancellationToken);
    }
```

替换为：

```csharp
    public override Task StopAsync(CancellationToken cancellationToken)
    {
        _watcher?.Dispose();
        _audioSource.SourceChanged -= OnAudioSourceChanged;
        _audioLevelMeter.Dispose();
        _audioBandLevelMeter.Dispose();
        _audioSource.Dispose();
        return base.StopAsync(cancellationToken);
    }
```

- [ ] **Step 3：在文件最后一个 `}` 之前加 OnAudioSourceChanged**

```csharp
    private void OnAudioSourceChanged(object? sender, AudioSourceChangedEventArgs e)
    {
        try
        {
            AudioSourceStatusFile.Write(new AudioSourceStatusInfo
            {
                Status = e.Status,
                DeviceFriendlyName = e.DeviceFriendlyName,
                DeviceId = e.DeviceId,
                UpdatedAt = DateTimeOffset.UtcNow,
            });
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to write audio source status file");
        }
    }
```

- [ ] **Step 4：替换整个 RunMusicAsync**

把整个 `private async Task RunMusicAsync(KeyboardSettings settings, CancellationToken stoppingToken)` 方法（含其方法体内所有内容、终止于该方法的 `}`）替换为：

```csharp
    private async Task RunMusicAsync(KeyboardSettings settings, CancellationToken stoppingToken)
    {
        var music = settings.Effect.Music.Normalize();
        var controller = new MusicPulseController();
        var musicColors = music.Colors.Select(RgbColor.FromHex).ToList();
        var nextRuntimeRefresh = DateTimeOffset.UtcNow.AddSeconds(1);
        RgbColor? lastColor = null;

        // 进入音乐模式立刻刷一次状态文件，避免 Tray 看到陈旧值
        _audioSource.RefreshNow();

        try
        {
            while (!stoppingToken.IsCancellationRequested && !_settingsChanged)
            {
                if (DateTimeOffset.UtcNow >= nextRuntimeRefresh)
                {
                    nextRuntimeRefresh = DateTimeOffset.UtcNow.AddSeconds(1);
                    if (ShouldRebuildRuntimeSettings(settings))
                    {
                        _settingsChanged = true;
                        return;
                    }
                }

                RgbColor color;
                if (_audioSource.Status == AudioSourceStatus.Active)
                {
                    var level = music.EqEnabled
                        ? Math.Max(_audioBandLevelMeter.GetAdaptiveBeatLevel(music), _audioLevelMeter.GetPeakLevel() * 0.12f)
                        : _audioLevelMeter.GetPeakLevel();
                    var systemVolume = _audioLevelMeter.GetMasterVolumeScalar();
                    var frame = controller.Next(music, level, systemVolume, musicColors.Count);
                    var envelope = frame.Envelope;
                    var musicBrightness = music.BaseBrightness +
                        (music.PeakBrightness - music.BaseBrightness) * Math.Pow(envelope, 0.55);
                    var brightness = (int)Math.Clamp(Math.Round(musicBrightness), music.BaseBrightness, music.PeakBrightness);
                    var sourceColor = musicColors[frame.ColorIndex % musicColors.Count];
                    color = ApplyNotificationFlash(sourceColor.Scale(brightness), settings);
                }
                else
                {
                    var fallbackColor = musicColors.Count > 0 ? musicColors[0] : RgbColor.Black;
                    var brightness = Math.Clamp(music.BaseBrightness, 0, 100);
                    color = ApplyNotificationFlash(fallbackColor.Scale(brightness), settings);
                }

                if (color != lastColor)
                {
                    _device.SetColor(color);
                    lastColor = color;
                }

                await Task.Delay(music.IntervalMs, stoppingToken);
            }
        }
        finally
        {
            _audioBandLevelMeter.PauseCapture();
            _audioLevelMeter.PauseDevice();
        }
    }
```

- [ ] **Step 5：编译**

Run: `dotnet build ColorfulLedKeyboard.Service/ColorfulLedKeyboard.Service.csproj --nologo --verbosity minimal`
Expected: 0 errors。

- [ ] **Step 6：跑全量测试**

Run: `dotnet test ColorfulLedKeyboard.Tests/ColorfulLedKeyboard.Tests.csproj --nologo --verbosity minimal`
Expected: 69 passed。

- [ ] **Step 7：commit**

```bash
git add ColorfulLedKeyboard.Service/Worker.cs
git commit -m "refactor(service): Worker subscribes AudioSourceProvider events and writes status file"
```

---

## Task 12：Tray 端 AudioSourceStatusWatcher

**Files:**
- Create: `ColorfulLedKeyboard.Tray/AudioSourceStatusWatcher.cs`

- [ ] **Step 1：创建文件**

```csharp
using ColorfulLedKeyboard.Core;

namespace ColorfulLedKeyboard.Tray;

/// <summary>监听 audio-source-status.json 变化，含 50ms debounce。
/// 主要解决 Windows 一次写文件触发多次 Changed 事件的抖动。</summary>
internal sealed class AudioSourceStatusWatcher : IDisposable
{
    private readonly FileSystemWatcher _watcher;
    private readonly System.Windows.Forms.Timer _debounce;
    private readonly Action<AudioSourceStatusInfo?> _onUpdate;
    private bool _disposed;

    public AudioSourceStatusWatcher(Action<AudioSourceStatusInfo?> onUpdate)
    {
        _onUpdate = onUpdate;

        try { Directory.CreateDirectory(AppPaths.ProgramDataDirectory); }
        catch { /* watcher 自己会重试 */ }

        _watcher = new FileSystemWatcher(AppPaths.ProgramDataDirectory, AppPaths.AudioSourceStatusFileName)
        {
            NotifyFilter = NotifyFilters.LastWrite | NotifyFilters.CreationTime | NotifyFilters.Size,
            EnableRaisingEvents = true,
        };
        _watcher.Changed += (_, _) => ScheduleRefresh();
        _watcher.Created += (_, _) => ScheduleRefresh();

        _debounce = new System.Windows.Forms.Timer { Interval = 50 };
        _debounce.Tick += (_, _) =>
        {
            _debounce.Stop();
            Refresh();
        };
    }

    public void RefreshNow() => Refresh();

    private void ScheduleRefresh()
    {
        if (_disposed) return;
        try { _debounce.Stop(); _debounce.Start(); }
        catch { /* UI 已退出 */ }
    }

    private void Refresh()
    {
        if (_disposed) return;
        AudioSourceStatusInfo? info = null;
        try { info = AudioSourceStatusFile.Read(); } catch { }
        try { _onUpdate(info); } catch { }
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        try { _watcher.Dispose(); } catch { }
        try { _debounce.Stop(); _debounce.Dispose(); } catch { }
    }
}
```

- [ ] **Step 2：编译**

Run: `dotnet build ColorfulLedKeyboard.Tray/ColorfulLedKeyboard.Tray.csproj --nologo --verbosity minimal`
Expected: 0 errors。

- [ ] **Step 3：commit**

```bash
git add ColorfulLedKeyboard.Tray/AudioSourceStatusWatcher.cs
git commit -m "feat(tray): add AudioSourceStatusWatcher with 50ms debounce"
```

---

## Task 13：TrayApplicationContext 接入 watcher + NotifyIcon.Text

**Files:**
- Modify: `ColorfulLedKeyboard.Tray/TrayApplicationContext.cs`

- [ ] **Step 1：在字段区追加**

把这段：

```csharp
    private string? _balloonReleaseUrl;
    private string? _lastForegroundProcess;
    private DateTimeOffset _lastForegroundStateSaved = DateTimeOffset.MinValue;
    private KeyboardSettings _settings;
    private SettingsForm? _settingsForm;
```

替换为：

```csharp
    private string? _balloonReleaseUrl;
    private string? _lastForegroundProcess;
    private DateTimeOffset _lastForegroundStateSaved = DateTimeOffset.MinValue;
    private KeyboardSettings _settings;
    private SettingsForm? _settingsForm;
    private AudioSourceStatusWatcher? _audioStatusWatcher;
    private AudioSourceStatusInfo? _lastAudioStatus;
```

- [ ] **Step 2：构造函数末尾启动 watcher**

把这段：

```csharp
        UpdateForegroundAppState();
        _ = CheckForUpdatesOnStartupAsync();

        if (openSettingsOnStartup)
        {
            OpenSettingsAfterStartup();
        }
```

替换为：

```csharp
        UpdateForegroundAppState();
        _ = CheckForUpdatesOnStartupAsync();

        _audioStatusWatcher = new AudioSourceStatusWatcher(OnAudioStatusChanged);
        _audioStatusWatcher.RefreshNow();

        if (openSettingsOnStartup)
        {
            OpenSettingsAfterStartup();
        }
```

- [ ] **Step 3：在 Dispose 释放 watcher**

把这段：

```csharp
            _typingPulseHook.Dispose();
            _notificationFlashMonitor.Dispose();
        }

        base.Dispose(disposing);
```

替换为：

```csharp
            _typingPulseHook.Dispose();
            _notificationFlashMonitor.Dispose();
            _audioStatusWatcher?.Dispose();
        }

        base.Dispose(disposing);
```

- [ ] **Step 4：在类的最后一个 `}` 之前加 OnAudioStatusChanged 与 UpdateNotifyIconText**

```csharp
    private void OnAudioStatusChanged(AudioSourceStatusInfo? info)
    {
        _lastAudioStatus = info;
        UpdateNotifyIconText();
        if (_settingsForm is { } form && !form.IsDisposed)
        {
            try { form.UpdateAudioSourceLabel(info); }
            catch (ObjectDisposedException) { }
            catch (InvalidOperationException) { }
        }
    }

    private void UpdateNotifyIconText()
    {
        var settings = _settings;
        string text;

        if (settings.OperatingMode != OperatingMode.Music || !settings.Enabled)
        {
            text = "ClevoLEDKeyboardControl";
        }
        else
        {
            var info = _lastAudioStatus;
            text = info?.Status switch
            {
                AudioSourceStatus.Active      => $"ClevoLEDKeyboardControl\n音乐：{info.DeviceFriendlyName}",
                AudioSourceStatus.Hfp         => "ClevoLEDKeyboardControl\n音乐：通话中，已暂停",
                AudioSourceStatus.Switching   => "ClevoLEDKeyboardControl\n音乐：切换音频源中…",
                AudioSourceStatus.Unavailable => "ClevoLEDKeyboardControl\n音乐：无可用音频源",
                _                             => "ClevoLEDKeyboardControl\n音乐：等待状态…",
            };
        }

        if (text.Length > 120) text = text[..120] + "…";
        _notifyIcon.Text = text;
    }
```

- [ ] **Step 5：模式切换后刷新 tooltip**

定位 RefreshMenu 方法：

```bash
grep -n "private.*RefreshMenu\|void RefreshMenu" ColorfulLedKeyboard.Tray/TrayApplicationContext.cs
```

打开该方法，在方法体最后一行加：

```csharp
        UpdateNotifyIconText();
```

- [ ] **Step 6：编译**

Run: `dotnet build ColorfulLedKeyboard.Tray/ColorfulLedKeyboard.Tray.csproj --nologo --verbosity minimal`
Expected: 报 `SettingsForm.UpdateAudioSourceLabel` 不存在 —— 是预期，下个 Task 添加。

- [ ] **Step 7：commit**

```bash
git add ColorfulLedKeyboard.Tray/TrayApplicationContext.cs
git commit -m "feat(tray): wire AudioSourceStatusWatcher to NotifyIcon.Text"
```

---

## Task 14：SettingsForm 音乐页 Label

**Files:**
- Modify: `ColorfulLedKeyboard.Tray/SettingsForm.cs`

- [ ] **Step 1：加 `_audioSourceLabel` 字段**

定位字段：

```bash
grep -n "_musicPreset = new" ColorfulLedKeyboard.Tray/SettingsForm.cs
```

在 `private readonly ComboBox _musicPreset = new();` 这一行的下一行插入：

```csharp
    private readonly Label _audioSourceLabel = new()
    {
        AutoSize = true,
        ForeColor = SystemColors.GrayText,
        Text = "当前音频源：检测中…",
        Margin = new Padding(0, 4, 0, 8),
    };
```

- [ ] **Step 2：在 BuildMusicPage 顶部加 Label**

把这段：

```csharp
        page.Controls.Add(Row("音乐预设", _musicPreset));
```

替换为：

```csharp
        page.Controls.Add(_audioSourceLabel);
        page.Controls.Add(Row("音乐预设", _musicPreset));
```

- [ ] **Step 3：在类末尾加公开方法 UpdateAudioSourceLabel**

在 SettingsForm 最后一个 `}` 之前添加：

```csharp
    public void UpdateAudioSourceLabel(AudioSourceStatusInfo? info)
    {
        if (IsDisposed) return;
        if (InvokeRequired)
        {
            try { BeginInvoke(new Action(() => UpdateAudioSourceLabel(info))); }
            catch (ObjectDisposedException) { }
            catch (InvalidOperationException) { }
            return;
        }

        _audioSourceLabel.Text = info?.Status switch
        {
            AudioSourceStatus.Active      => $"当前音频源：{info.DeviceFriendlyName}",
            AudioSourceStatus.Hfp         => $"当前音频源：{info.DeviceFriendlyName}（通话中，已暂停）",
            AudioSourceStatus.Switching   => "当前音频源：切换中…",
            AudioSourceStatus.Unavailable => "当前音频源：不可用",
            _                             => "当前音频源：检测中…",
        };
    }
```

- [ ] **Step 4：构造函数末尾加初始读取**

定位构造函数：

```bash
grep -n "public SettingsForm(" ColorfulLedKeyboard.Tray/SettingsForm.cs
```

在唯一公开构造函数最后一行 `}` 之前加：

```csharp
        UpdateAudioSourceLabel(AudioSourceStatusFile.Read());
```

- [ ] **Step 5：编译 + 全量测试**

Run: `dotnet build ColorfulLedKeyboard.Tray/ColorfulLedKeyboard.Tray.csproj --nologo --verbosity minimal`
Expected: 0 errors。

Run: `dotnet test ColorfulLedKeyboard.Tests/ColorfulLedKeyboard.Tests.csproj --nologo --verbosity minimal`
Expected: 69 passed。

- [ ] **Step 6：commit**

```bash
git add ColorfulLedKeyboard.Tray/SettingsForm.cs
git commit -m "feat(tray): show current audio source on Music settings page"
```

---

## Task 15：全量构建 + 打包 + 手工验证

**Files:** 无（仅编排 / 验证）

- [ ] **Step 1：全量 build**

Run: `dotnet build ColorfulLedKeyboard.Service/ColorfulLedKeyboard.Service.csproj --nologo --verbosity minimal`
Run: `dotnet build ColorfulLedKeyboard.Tray/ColorfulLedKeyboard.Tray.csproj --nologo --verbosity minimal`
Run: `dotnet build ColorfulLedKeyboard.Tests/ColorfulLedKeyboard.Tests.csproj --nologo --verbosity minimal`
Run: `dotnet build ColorfulLedKeyboard.Installer/ColorfulLedKeyboard.Installer.csproj --nologo --verbosity minimal`
Expected: 全部 0 errors。

- [ ] **Step 2：全量 test**

Run: `dotnet test ColorfulLedKeyboard.Tests/ColorfulLedKeyboard.Tests.csproj --nologo --verbosity minimal`
Expected: 51 + 5 + 13 = 69 passed。

- [ ] **Step 3：打包 setup.exe**

Run: `powershell -ExecutionPolicy Bypass -File scripts/publish.ps1`
Expected: `Published setup executable to D:\ClevoRGBControl\publish\ClevoLEDKeyboardControlSetup.exe`。

- [ ] **Step 4：手工验证清单（用户执行）**

```
设备切换：
[ ] 启动时默认是扬声器，灯按系统音频跳
[ ] 切到有线耳机，2 秒内灯跟着跳
[ ] 拔出有线耳机，2 秒内灯切回扬声器
[ ] 切到 A2DP 蓝牙耳机，2 秒内灯跟着跳
[ ] 蓝牙耳机断开，2 秒内灯切回扬声器
[ ] 蓝牙耳机微信通话接听，灯压底色，tooltip 显示"通话中，已暂停"
[ ] 通话结束，灯自动恢复

状态显示：
[ ] 托盘 tooltip 在 Active 时显示设备名（含中文）
[ ] 托盘 tooltip 在 Hfp/Unavailable 时显示对应文案
[ ] 设置窗音乐页顶部 Label 同步显示
[ ] 切到灯效模式，tooltip 不再显示音乐相关文字

模式切换：
[ ] 音乐 → 灯效：服务进程不再持有 audio session（资源管理器观察）
[ ] 灯效 → 音乐：进入瞬间灯先压底色，~500ms 内开始正常跳

异常恢复：
[ ] 系统设置禁用扬声器，灯切到 Unavailable 底色
[ ] 重新启用，灯自动恢复
[ ] 系统休眠唤醒，灯效自动恢复（不需要重启服务）
```

- [ ] **Step 5：（可选）发布 v2.1.0**

仅在手工验证全部通过后由用户执行：

1. csproj 版本号 2.0.0 → 2.1.0（Tray）
2. `git add ColorfulLedKeyboard.Tray/ColorfulLedKeyboard.Tray.csproj && git commit -m "chore: bump version to 2.1.0"`
3. `git checkout main && git merge --no-ff feat/music-headphones -m "Merge feat/music-headphones: headphones support for music mode"`
4. `git branch -D feat/music-headphones`
5. `git tag -a v2.1.0 -m "Release v2.1.0"`
6. `git push origin main && git push origin v2.1.0 && git push origin --delete feat/music-headphones`
7. 写中文 release notes（按"新增 / 优化 / 修复 / 注意事项"分组），保存到 `publish/release-notes-v2.1.0.md`
8. `gh release create v2.1.0 publish/ClevoLEDKeyboardControlSetup.exe --title "v2.1.0" --notes-file publish/release-notes-v2.1.0.md`
