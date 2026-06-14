# 音乐模式耳机支持（v2.1）设计文档

**日期**：2026-06-14
**目标版本**：v2.1.0
**分支**：`feat/music-headphones`

## 背景与问题

ClevoLEDKeyboardControl v2.0 的音乐模式只能跟随启动时的扬声器输出。一旦用户切换到耳机（有线 / 蓝牙），键盘灯不再有任何反应，必须重启服务才能恢复，这是阻断式 bug。

根因（已确认）：

- `AudioBandLevelMeter` 在构造时通过无参 `WasapiLoopbackCapture()` 抓"启动那一刻"的默认渲染设备，并未订阅 `IMMNotificationClient.OnDefaultDeviceChanged`。切到耳机后，旧 capture 抛错或 stop，会触发 5 秒退避重试，但重试时仍然走 NAudio 的内部默认设备解析——某些路径下解析到的还是同一个失效端点，从而长期"哑"。
- `SystemAudioLevelMeter` 同样问题。
- 蓝牙耳机在 Windows 中存在两个独立 render 端点：A2DP（音乐）和 HFP/Hands-Free（通话）。Windows 会在通话状态切换时自动切换默认设备。当前代码没有任何识别机制。

用户的实测设备列表（iQOO TWS Air3）：

| Friendly Name | InstanceId 关键字 | 含义 |
|---|---|---|
| 耳机 (iQOO TWS Air3) | `{0.0.0.0}` render | A2DP 音乐端点 |
| 耳机 (iQOO TWS Air3 Hands-Free) | `{0.0.0.0}` render | HFP 通话端点 |
| 扬声器 (Realtek(R) Audio) | `{0.0.0.0}` render | 板载扬声器 |

## 范围（v2.1）

### 包含

1. 监听 `IMMNotificationClient`，默认渲染设备变化时主动重建 loopback。
2. 识别 HFP / 通话端点，灯压到底色（`Music.Colors[0] × BaseBrightness/100`），不开 capture（避免激活 SCO 链路）。
3. 默认设备切换的过渡期（旧 capture 停止、新 capture 启动中）灯保持底色。
4. fallback 机制：1.5 秒未收到有效样本 → 状态降级到 `Unavailable`，灯压底色。
5. 托盘 tooltip 与设置窗音乐页 Label 显示当前音频源状态。
6. Music → Lighting 模式切换时主动停止 capture，释放 loopback 资源。

### 不包含

- 设置页"手动指定音频源"下拉框（v2.2 候选）。
- 多设备混音 / 同时抓多个端点。
- WASAPI 独占模式抢占场景的处理。
- 应用切到音乐模式（`AppProfileSettings.TargetEffect` 不允许 Music）。

## 方案选型

调研阶段对比过三种方案：

- **A. 在现有 meter 类里就地加监听**：改动最少，但两个 meter 类各自重复一份 NotificationClient + 状态机，`AudioBandLevelMeter` 已经 380 行，进一步臃肿。
- **B. 抽 `AudioSourceProvider` 抽象**（采用）：状态机集中在一个类，meter 只消费 `MMDevice` + `Status`，反而瘦身。可单元测试。未来扩展（手动指定设备、应用切音乐模式）只需改 Provider。
- **C. 用 `System.Threading.Channels` 重写音频管线**：超出 v2.1 范围，工程量翻倍且无对应价值，放弃。

## 架构

```
┌─────────────────────────────────────────────────────────────────┐
│ Worker (Service)                                                 │
│   ├─ 订阅 AudioSourceProvider.SourceChanged                       │
│   ├─ 把 Status 写入 ProgramData/audio-source-status.json         │
│   │  （Tray 的 FileSystemWatcher 读取 → 更新 tooltip / 设置页）    │
│   └─ RunMusicAsync 循环里读 Provider.Status，决定灯效行为         │
│       ├─ Active        → 走原 Goertzel + Beat 管线                │
│       ├─ Hfp           → 灯压到 BaseBrightness × Music.Colors[0]  │
│       ├─ Switching     → 同上（过渡 200-500ms）                   │
│       └─ Unavailable   → 同上                                    │
└─────────────────────────────────────────────────────────────────┘
                          │ 持有
                          ▼
┌─────────────────────────────────────────────────────────────────┐
│ AudioSourceProvider  (NEW)                                       │
│   ├─ 内部持有 IMMNotificationClient（默认设备变化通知）            │
│   ├─ CurrentDevice: MMDevice                                    │
│   ├─ Status: AudioSourceStatus { Active | Hfp | Switching       │
│   │                              | Unavailable }                │
│   ├─ SourceChanged 事件（Status / DeviceFriendlyName 变化时触发） │
│   ├─ HFP 判断：MixFormat.SampleRate ≤ 16000 && Channels == 1     │
│   ├─ Fallback：1.5s 无 ReportSamples → Unavailable               │
│   └─ ReportSamples()：meter 调用以汇报"刚才有样本"                │
└─────────────────────────────────────────────────────────────────┘
                ▲                                ▲
                │ 消费                           │ 消费
┌───────────────┴──────────────┐  ┌──────────────┴──────────────┐
│ AudioBandLevelMeter           │  │ SystemAudioLevelMeter        │
│   - 不再自己管设备             │  │   - 不再自己管设备            │
│   - WasapiLoopbackCapture     │  │   - 从 Provider 拿 MMDevice  │
│     用 Provider.CurrentDevice │  │   - GetPeakLevel 返回前调    │
│   - OnDataAvailable 调用      │  │     ReportSamples            │
│     ReportSamples             │  │                              │
└──────────────────────────────┘  └─────────────────────────────┘
                                                ▲
                                                │ 跨进程读取
┌─────────────────────────────────────────────────────────────────┐
│ Tray (UI)                                                        │
│   ├─ FileSystemWatcher 读 audio-source-status.json               │
│   ├─ NotifyIcon.Text = "ClevoLEDKeyboardControl\n音乐：[设备名]"   │
│   └─ SettingsForm 音乐页顶部 Label 同步显示                       │
└─────────────────────────────────────────────────────────────────┘
```

## 改动文件清单

| 文件 | 改动 | 大致行数 |
|---|---|---|
| `ColorfulLedKeyboard.Service/AudioSourceProvider.cs` | 新增 | ~180 |
| `ColorfulLedKeyboard.Core/AudioSourceStatus.cs` | 新增（枚举） | ~10 |
| `ColorfulLedKeyboard.Core/AudioSourceStatusInfo.cs` | 新增（DTO） | ~25 |
| `ColorfulLedKeyboard.Core/AudioSourceStatusFile.cs` | 新增（IO 工具） | ~50 |
| `ColorfulLedKeyboard.Service/IAudioDeviceProbe.cs` | 新增（测试隔离接口） | ~20 |
| `ColorfulLedKeyboard.Service/MMDeviceProbe.cs` | 新增（生产实现） | ~40 |
| `ColorfulLedKeyboard.Service/AudioBandLevelMeter.cs` | 修改 | -30 ~ -50 |
| `ColorfulLedKeyboard.Service/SystemAudioLevelMeter.cs` | 修改 | -20 |
| `ColorfulLedKeyboard.Service/Worker.cs` | 修改 | +60 |
| `ColorfulLedKeyboard.Core/AppPaths.cs` | 修改（路径常量） | +3 |
| `ColorfulLedKeyboard.Tray/TrayApplicationContext.cs` | 修改 | +50 |
| `ColorfulLedKeyboard.Tray/SettingsForm.cs` | 修改 | +40 |
| `ColorfulLedKeyboard.Tests/AudioSourceProviderTests.cs` | 新增 | ~150 |
| `ColorfulLedKeyboard.Tests/AudioSourceStatusFileTests.cs` | 新增 | ~50 |

净增加约 430 行（含 ~200 行测试）。

## 组件接口

### `AudioSourceStatus`（Core 枚举）

```csharp
public enum AudioSourceStatus
{
    Active,        // 设备正常，有有效样本
    Hfp,           // HFP/通话端点，capture 不开
    Switching,     // 默认设备刚切换、capture 重建中
    Unavailable,   // 无可用设备 / 1.5s 无样本
}
```

从灯效行为看 `Hfp/Switching/Unavailable` 等价（都走兜底底色），区分它们是为了 UI 文案精确。

### `AudioSourceStatusInfo`（Core DTO，跨进程序列化）

```csharp
public sealed class AudioSourceStatusInfo
{
    public AudioSourceStatus Status { get; set; } = AudioSourceStatus.Unavailable;
    public string DeviceFriendlyName { get; set; } = "";
    public string DeviceId { get; set; } = "";
    public DateTimeOffset UpdatedAt { get; set; }
}
```

序列化时 `Status` 写为字符串而不是数字（用 `JsonStringEnumConverter`），避免未来加枚举值破坏兼容性。

### `AudioSourceStatusFile`（Core IO 工具）

```csharp
public static class AudioSourceStatusFile
{
    public static string Path => System.IO.Path.Combine(AppPaths.ProgramDataRoot, "audio-source-status.json");
    public static void Write(AudioSourceStatusInfo info);  // 原子写：.tmp + rename
    public static AudioSourceStatusInfo? Read();           // 损坏返回 null
}
```

### `IAudioDeviceProbe`（Service internal 接口，测试隔离用）

```csharp
internal interface IAudioDeviceProbe
{
    DeviceSnapshot? GetDefaultRenderDevice();
    DeviceSnapshot? GetDevice(string id);
}

internal sealed record DeviceSnapshot(string Id, string FriendlyName, int SampleRate, int Channels);
```

生产实现 `MMDeviceProbe` 包 `MMDeviceEnumerator`；测试用 `FakeAudioDeviceProbe`。

### `AudioSourceProvider`（Service）

```csharp
public sealed class AudioSourceProvider : IDisposable
{
    public AudioSourceProvider(ILogger<AudioSourceProvider>? logger = null);
    internal AudioSourceProvider(IAudioDeviceProbe probe, ILogger<AudioSourceProvider>? logger = null);

    public MMDevice? CurrentDevice { get; }
    public AudioSourceStatus Status { get; }
    public string DeviceFriendlyName { get; }

    public event EventHandler<AudioSourceChangedEventArgs>? SourceChanged;

    public void ReportSamples();
    public void RefreshNow();

    // 测试入口（internal）
    internal void TestOnly_SimulateDefaultDeviceChanged(string newDeviceId);
    internal void TestOnly_AdvanceFallbackClock(System.TimeSpan delta);

    public void Dispose();
}

public sealed class AudioSourceChangedEventArgs : EventArgs
{
    public AudioSourceStatus Status { get; init; }
    public string DeviceFriendlyName { get; init; } = "";
    public string DeviceId { get; init; } = "";
}
```

线程要求：

- `SourceChanged` 可能在 NAudio COM 回调线程或 fallback timer 线程触发，订阅者自行保证线程安全。
- `Status` / `CurrentDevice` / `DeviceFriendlyName` 用 `lock(_stateLock)` 保护。
- `ReportSamples` 用 `Interlocked.Exchange` 写时间戳，无锁。
- 所有 NotificationClient 回调和 fallback timer tick 内部包 try/catch，吞下异常 + 记日志，避免冒到 NAudio COM 层导致后续通知失效。

### meter 类改动

`AudioBandLevelMeter`：

- 构造接收 `AudioSourceProvider`，订阅 `SourceChanged`（→ `ResetCapture`）。
- `EnsureCapture()` 改为：`Provider.Status != Active` 直接返回（不开 capture）；用 `Provider.CurrentDevice` 显式构造 `WasapiLoopbackCapture(device)`。
- `OnDataAvailable` 调用 `Provider.ReportSamples()`。
- 移除 `_lastError` / `_nextRetry` / 5 秒退避——交给 Provider 状态机。
- 新增 `PauseCapture()`：Worker 退出 Music 模式时调用，停 capture 释放 loopback。

`SystemAudioLevelMeter`：

- 类似改造：构造接收 Provider，`EnsureDevice` 从 Provider 拿。
- `GetPeakLevel` 在峰值 > 0 时调一次 `ReportSamples`。
- 移除自己的退避机制。
- 新增 `PauseDevice()`。

### Worker 改动

```csharp
private readonly AudioSourceProvider _audioSource;

public Worker(...)
{
    _audioSource = new AudioSourceProvider(loggerFactory.CreateLogger<AudioSourceProvider>());
    _audioBandLevelMeter = new AudioBandLevelMeter(_audioSource);
    _audioLevelMeter = new SystemAudioLevelMeter(_audioSource);
    _audioSource.SourceChanged += OnAudioSourceChanged;
}

private void OnAudioSourceChanged(object? sender, AudioSourceChangedEventArgs e)
{
    AudioSourceStatusFile.Write(new AudioSourceStatusInfo {
        Status = e.Status,
        DeviceFriendlyName = e.DeviceFriendlyName,
        DeviceId = e.DeviceId,
        UpdatedAt = DateTimeOffset.UtcNow,
    });
}

private async Task RunMusicAsync(...)
{
    _audioSource.RefreshNow();
    try
    {
        while (...)
        {
            var status = _audioSource.Status;
            RgbColor color;
            if (status == AudioSourceStatus.Active)
            {
                // 原 Goertzel + Beat 管线
            }
            else
            {
                var fallbackColor = musicColors[0];
                var brightness = music.BaseBrightness / 100f;
                color = ApplyNotificationFlash(fallbackColor.Scale(brightness), settings);
            }
            if (color != lastColor) _device.SetColor(color);
            await Task.Delay(music.IntervalMs, ct);
        }
    }
    finally
    {
        _audioBandLevelMeter.PauseCapture();
        _audioLevelMeter.PauseDevice();
    }
}
```

`StopAsync`：

```csharp
_audioSource.SourceChanged -= OnAudioSourceChanged;
_audioBandLevelMeter.Dispose();
_audioLevelMeter.Dispose();
_audioSource.Dispose();
```

### Tray 改动

`TrayApplicationContext`：

- 新增 `_audioStatusWatcher: FileSystemWatcher`，监听 `audio-source-status.json`。
- 文件变更时（带 50ms debounce）→ 读 → 更新 NotifyIcon.Text、推送给 SettingsForm。
- NotifyIcon.Text 文案：

| Status | 文案 |
|---|---|
| Active | `ClevoLEDKeyboardControl\n音乐：{DeviceFriendlyName}` |
| Hfp | `ClevoLEDKeyboardControl\n音乐：通话中，已暂停` |
| Switching | `ClevoLEDKeyboardControl\n音乐：切换音频源中…` |
| Unavailable | `ClevoLEDKeyboardControl\n音乐：无可用音频源` |
| null | `ClevoLEDKeyboardControl\n音乐：等待状态…` |

- 非音乐模式下 NotifyIcon.Text 维持 `ClevoLEDKeyboardControl`，不显示音乐相关。
- 上限截断：超过 120 字符截断 + "…"，避免触发 Win32Exception。

`SettingsForm`：

- 音乐页 `BuildMusicPage()` 顶部加灰色 Label `_audioSourceLabel`。
- 公开方法 `UpdateAudioSourceLabel(AudioSourceStatusInfo? info)`，由 `TrayApplicationContext` 在状态变化时推送。
- 文案：

| Status | 文案 |
|---|---|
| Active | `当前音频源：{DeviceFriendlyName}` |
| Hfp | `当前音频源：{DeviceFriendlyName}（通话中，已暂停）` |
| Switching | `当前音频源：切换中…` |
| Unavailable | `当前音频源：不可用` |
| null | `当前音频源：检测中…` |

## 关键时序

### 切到 A2DP 蓝牙耳机

```
T+0ms     用户切换默认设备
T+~80ms   COM 回调 → OnDefaultDeviceChanged
          ├─ Status=Switching, 触发 SourceChanged
          │   ├─→ 两个 meter ResetCapture
          │   └─→ Worker 写状态文件 → tooltip="切换中…"
          ├─ InspectDevice(): SampleRate=48000, Ch=2 → 不是 HFP
          └─ Status=Active, 再触发 SourceChanged
              └─→ Worker 写状态文件 → tooltip="音乐：iQOO TWS Air3"
T+~125ms  Worker 主循环：Status=Active → 调 meter.GetLevel
          └─ EnsureCapture: 用 Provider.CurrentDevice 重建 WasapiLoopbackCapture
T+~400ms  第一帧 PCM → ReportSamples → 灯按节拍跳

总过渡：~200-500ms。期间灯保持底色。
```

### 切到 HFP 通话端点

```
T+~100ms  OnDefaultDeviceChanged
          ├─ Status=Switching → 事件
          ├─ InspectDevice(): SampleRate=16000, Ch=1 → IsHfp=true
          └─ Status=Hfp → 事件
              └─→ tooltip="通话中，已暂停"
T+~125ms  Worker: Status=Hfp → 跳过 meter，灯=Music.Colors[0] × Base/100
          *** capture 永远不开 ***  ← 不激活 SCO 链路
```

### fallback：1.5 秒无样本

```
正常：每帧 PCM → ReportSamples → _lastSampleTimestamp 更新

fallback timer (500ms 周期)：
  elapsed = now - _lastSampleTimestamp
  if Status==Active && elapsed > 1.5s:
      Status=Unavailable, 触发事件
  if Status==Unavailable && elapsed < 1.5s:
      Status=Active, 触发事件
```

为什么 1.5s：低于 0.5s 会误报 capture 启动延迟；高于 2s 拔耳机后用户体感太慢。NAudio 在静音播放时仍调 `OnDataAvailable`（用静音填充），所以"完全无回调"才判 Unavailable。

### Music → Lighting 切换

```
Worker._settingsChanged=true → RunMusicAsync 退出
  → finally: meter.PauseCapture / PauseDevice （释放 loopback）
  → Provider 仍在线监听（NotificationClient 不重建，开销大）
  → 切回 Music：进入 RefreshNow + meter 懒重建
```

## 错误处理

| # | 场景 | 处理 |
|---|---|---|
| E1 | 启动时无 render 设备 | Status=Unavailable，fallback timer 每 500ms 重试，不抛 |
| E2 | RegisterEndpointNotificationCallback 失败 | warn 日志，Provider 退化为静态实现（启动时解析一次） |
| E3 | InspectDevice MixFormat 抛异常 | catch → Status=Switching；fallback timer 兜底 |
| E4 | WasapiLoopbackCapture 构造失败 | catch → ResetCapture；不复刻 5 秒退避（Provider 状态机决定节奏） |
| E5 | RecordingStopped 携带 Exception | ResetCapture；OnDefaultDeviceChanged 几乎同时会到 |
| E6 | MasterPeakValue 抛异常 | catch → Provider.RefreshNow，本帧返回 0 |
| E7 | 状态文件写入失败 | catch + 日志，不影响 Worker 主循环 |
| E8 | 状态文件读取损坏 | catch → 返回 null，Tray 显示"等待状态…" |
| E9 | NotificationClient 回调抛异常 | 全部包 try/catch，避免后续通知失效 |
| E10 | NotifyIcon.Text 超 127 字符 | 自截到 120 + "…" |
| E11 | 系统休眠唤醒 | NAudio 自动触发 OnDefaultDeviceChanged + fallback 兜底，不专门订阅 |
| E12 | 用户禁用所有 render 设备 | OnDeviceStateChanged → 重新解析 → Unavailable |

不处理（明确放弃）：

- WASAPI 独占模式抢占（极少见，用户能感知）。
- A2DP 报告假采样率（违反协议；fallback 兜底）。

资源释放：`AudioSourceProvider.Dispose` 顺序：fallback timer → UnregisterEndpointNotificationCallback → 释放 CurrentDevice → 释放 enumerator → 清空事件订阅。所有 Dispose 用 `_disposed` 防重入。

状态文件原子写：先写 `.tmp` 再 `File.Move(overwrite: true)`。Tray 端 FileSystemWatcher 50ms debounce（Windows 一次写文件触发多次 Changed 事件）。

## 测试策略

### 单元测试

`AudioSourceProviderTests`（13 用例，~150 行）：

1. 启动后初始 Status=Active（默认是扬声器，48k 立体声）
2. 启动后初始 Status=Hfp（默认是 16k 单声道）
3. 启动后初始 Status=Unavailable（解析返回 null）
4. 默认设备切到 A2DP：触发 Switching → Active 两次事件
5. 默认设备切到 HFP：触发 Switching → Hfp 两次事件
6. ReportSamples 后 1.5s 内 fallback 不触发
7. 1.5s 无 ReportSamples + Status=Active → Unavailable + 事件
8. Unavailable 期间 ReportSamples → Active + 事件
9. InspectDevice 抛异常时 Status 保守置 Switching
10. NotificationClient 回调抛异常被吞下
11. Dispose 后再次 Dispose 不抛
12. Dispose 后 ReportSamples 不抛
13. SourceChanged 订阅者抛异常不影响其他订阅者

`AudioSourceStatusFileTests`（5 用例，~50 行）：

1. Write→Read 往返字段正确
2. Read 不存在文件 → null
3. Read 损坏 JSON → null
4. 50 次循环并发 Write+Read 不读到半写状态
5. Status 序列化为字符串（兼容性保护）

不写测试的部分：

- Worker 主循环状态分支（mock 太多，手工验证更可靠）
- Tray tooltip 文案（UI 字符串，维护成本 > 价值）
- FileSystemWatcher 触发（Windows API）
- meter 内部 P/Invoke（本次重构是减法）

### 手工验证清单

设备切换：

- [ ] 启动时默认是扬声器，灯按系统音频跳
- [ ] 切到有线耳机，2 秒内灯跟着跳
- [ ] 拔出有线耳机，2 秒内灯切回扬声器
- [ ] 切到 A2DP 蓝牙耳机，2 秒内灯跟着跳
- [ ] 蓝牙耳机断开，2 秒内灯切回扬声器
- [ ] 蓝牙耳机微信通话接听，灯压底色，tooltip 显示"通话中，已暂停"
- [ ] 通话结束，灯自动恢复

状态显示：

- [ ] 托盘 tooltip 在 Active 时显示设备名（含中文）
- [ ] 托盘 tooltip 在 Hfp/Unavailable 时显示对应文案
- [ ] 设置窗音乐页顶部 Label 同步显示
- [ ] 切到灯效模式，tooltip 不再显示音乐相关文字

模式切换：

- [ ] 音乐 → 灯效：capture 释放（任务管理器服务进程不再有 audio 句柄）
- [ ] 灯效 → 音乐：进入瞬间灯先压底色，~500ms 内开始正常跳

异常恢复：

- [ ] 系统设置禁用扬声器，灯切到 Unavailable 底色
- [ ] 重新启用，灯自动恢复
- [ ] 系统休眠唤醒，灯效自动恢复（不需要重启服务）

测试运行：`dotnet test ColorfulLedKeyboard.Tests/ColorfulLedKeyboard.Tests.csproj`，应通过 51（旧）+ 13 + 5 = 69 个。

## 已知限制（写到 Release Notes 注意事项）

- 蓝牙耳机固有延迟 100-300ms，灯效会比声音慢半拍（物理限制无解）。
- 通话中（HFP 端点）灯效暂停（音质太差不适合做频谱可视化）。
- 极少数应用使用 WASAPI 独占模式输出时，loopback 抓不到流，灯会进入 Unavailable 状态。

## 分支与发布

- 工作分支：`feat/music-headphones`，从 `main` 拉出。
- 完成后合并 main，tag `v2.1.0`，删除分支，发 GitHub Release。
- Release Notes 按"新增 / 优化 / 修复 / 安装 / 注意事项"分组。
