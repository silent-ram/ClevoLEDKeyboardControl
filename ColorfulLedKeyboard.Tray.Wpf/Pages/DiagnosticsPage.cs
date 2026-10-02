using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using ColorfulLedKeyboard.Core;
using ServiceProcess = System.ServiceProcess;

namespace ColorfulLedKeyboard.Tray.Wpf.Pages;

/// <summary>诊断与恢复页：8 个只读诊断行 + 配置恢复（WinForms BuildDiagnosticsPage 移植件）。</summary>
public sealed class DiagnosticsPage : UserControl
{
    private readonly Dictionary<string, System.Windows.Controls.TextBox> _fields = new();
    private readonly Button _refresh = MakeButton("刷新诊断信息", 130);
    private readonly Button _restoreBackup = MakeButton("恢复最近备份", 130);

    public event EventHandler? Changed;

    public DiagnosticsPage()
    {
        _refresh.Click += (_, _) => CollectAll();
        _restoreBackup.Click += (_, _) => RestoreLastGood();

        var stack = new StackPanel { Margin = new Thickness(18, 18, 18, 28), MaxWidth = 832, HorizontalAlignment = HorizontalAlignment.Center };
        stack.Children.Add(MakeCard("服务与硬件",
            Row("服务状态", MakeDiagnosticBox("服务状态")),
            Row("驱动 DLL", MakeDiagnosticBox("驱动 DLL")),
            PlainRow(_refresh)));
        stack.Children.Add(MakeCard("自动化与播放器",
            Row("当前前台应用", MakeDiagnosticBox("当前前台应用")),
            Row("命中应用场景", MakeDiagnosticBox("命中应用场景")),
            Row("播放器监视", MakeDiagnosticBox("播放器监视"))));
        stack.Children.Add(MakeCard("更新与配置恢复",
            Row("更新检查", MakeDiagnosticBox("更新检查")),
            Row("配置恢复", MakeDiagnosticBox("配置恢复")),
            Row("配置目录", MakeDiagnosticBox("配置目录"))));
        stack.Children.Add(MakeCard("配置恢复", PlainRow(_restoreBackup)));

        Content = new ScrollViewer
        {
            Content = stack,
            VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
            HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled
        };
        Loaded += (_, _) => CollectAll();
    }

    private System.Windows.Controls.TextBox MakeDiagnosticBox(string key)
    {
        var box = new System.Windows.Controls.TextBox
        {
            Style = (Style)Application.Current.Resources["UiTextBox"],
            IsReadOnly = true,
            MinWidth = 500,
            Background = (Brush)Application.Current.Resources["Brush.Window"]
        };
        _fields[key] = box;
        return box;
    }

    public void CollectAll()
    {
        var settings = new SettingsStore().Load();
        var foreground = ForegroundAppState.Load();
        var automation = AutomationStatus.Load();
        var audioState = AudioApplicationsState.Load();
        var mediaState = MediaPlaybackState.Load();
        var recovery = SettingsRecoveryState.Load();

        Set("服务状态", GetServiceStatusText());
        Set("驱动 DLL", GetDriverStatusText());
        Set("当前前台应用", foreground is null
            ? "尚未记录"
            : $"{foreground.ProcessName}（{Math.Max(0, (int)(DateTimeOffset.UtcNow - foreground.UpdatedUtc).TotalSeconds)} 秒前更新）");
        Set("命中应用场景", !settings.Automation.Enabled ? "场景自动化未启用"
            : automation is null || string.IsNullOrWhiteSpace(automation.ActiveRuleName)
                ? $"基础设置{(string.IsNullOrWhiteSpace(automation?.InvalidReason) ? "" : $"；{automation!.InvalidReason}")}"
                : $"{automation!.ActiveRuleName} → {automation.TargetDescription}{(automation.IdleOverrideActive ? "；空闲覆盖" : "")}");
        Set("播放器监视", BuildMonitorStatus(audioState, mediaState));
        Set("更新检查", GetUpdateStatusText(settings.Update.CheckInterval));
        Set("配置恢复", recovery is null
            ? "未发生配置恢复"
            : $"{recovery.Result}，{FormatAge(DateTimeOffset.UtcNow - recovery.UpdatedUtc)}前");
        Set("配置目录", AppPaths.ProgramDataDirectory);
    }

    private static string BuildMonitorStatus(AudioApplicationsState? audio, MediaPlaybackState? media)
    {
        var errors = new[] { audio?.LastError, media?.LastError }
            .Where(value => !string.IsNullOrWhiteSpace(value)).ToList();
        if (errors.Count > 0) return string.Join("；", errors);
        if (audio is null || DateTimeOffset.UtcNow - audio.UpdatedUtc > TimeSpan.FromSeconds(10))
            return "托盘未运行或音频检测状态过期";
        var ipc = ServiceIpc.IsAvailable() ? "安全管道已连接" : "安全管道不可用，设置只读";
        return $"{ipc}；音频程序 {audio.Applications.Count} 个；媒体会话 {media?.Sessions.Count ?? 0} 个";
    }

    private static string FormatAge(TimeSpan age)
    {
        if (age.TotalMinutes < 1) return $"{age.Seconds} 秒";
        if (age.TotalHours < 1) return $"{(int)age.TotalMinutes} 分钟";
        if (age.TotalDays < 1) return $"{(int)age.TotalHours} 小时";
        return $"{(int)age.TotalDays} 天";
    }

    private void Set(string key, string value)
    {
        if (_fields.TryGetValue(key, out var box)) box.Text = value;
    }

    private void RestoreLastGood()
    {
        var result = new SettingsStore().RestoreLastGood();
        if (result.Success)
        {
            Changed?.Invoke(this, EventArgs.Empty);
        }
        System.Windows.MessageBox.Show(
            result.Success ? "已恢复到最近的良好配置。" : $"恢复失败：{result.Message}",
            "ClevoLEDKeyboardControl",
            MessageBoxButton.OK,
            result.Success ? MessageBoxImage.Information : MessageBoxImage.Warning);
        CollectAll();
    }

    private static string GetServiceStatusText()
    {
        try
        {
            using var controller = new ServiceProcess.ServiceController(AppPaths.ServiceName);
            return controller.Status switch
            {
                ServiceProcess.ServiceControllerStatus.Running => "运行中",
                ServiceProcess.ServiceControllerStatus.Stopped => "已停止",
                ServiceProcess.ServiceControllerStatus.Paused => "已暂停",
                ServiceProcess.ServiceControllerStatus.StartPending => "正在启动",
                ServiceProcess.ServiceControllerStatus.StopPending => "正在停止",
                _ => controller.Status.ToString()
            };
        }
        catch (InvalidOperationException)
        {
            return "未安装";
        }
        catch (Exception ex) when (ex is System.ComponentModel.Win32Exception or UnauthorizedAccessException)
        {
            return $"无法读取：{ex.Message}";
        }
    }

    private static string GetDriverStatusText()
    {
        var state = LoadDriverComponentState();
        if (state is not null)
        {
            if (string.Equals(state.Status, "Installed", StringComparison.OrdinalIgnoreCase) &&
                !string.IsNullOrWhiteSpace(state.InstalledPath) &&
                File.Exists(state.InstalledPath))
            {
                return $"已安装（来源：{state.Source ?? "未知"}）：{state.InstalledPath}";
            }

            if (string.Equals(state.Status, "Missing", StringComparison.OrdinalIgnoreCase))
            {
                return "未找到（安装器最近检查未命中）";
            }
        }

        var serviceDll = Path.Combine(AppContext.BaseDirectory, "InsydeDCHU.dll");
        var installedServiceDll = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles),
            "ClevoLEDKeyboardControl",
            "Service",
            "InsydeDCHU.dll");
        var controlCenterDll = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86),
            "ControlCenter",
            "InsydeDCHU.dll");

        if (File.Exists(serviceDll)) return $"已安装（来源：托盘目录）：{serviceDll}";
        if (File.Exists(installedServiceDll)) return $"已安装（来源：服务目录）：{installedServiceDll}";
        if (File.Exists(controlCenterDll)) return "OEM Control Center 中存在，未复制到服务目录";
        return "未找到";
    }

    private static DriverComponentState? LoadDriverComponentState()
    {
        try
        {
            if (!File.Exists(AppPaths.DriverComponentStatePath)) return null;
            var json = File.ReadAllText(AppPaths.DriverComponentStatePath);
            return System.Text.Json.JsonSerializer.Deserialize<DriverComponentState>(json);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or System.Text.Json.JsonException)
        {
            return null;
        }
    }

    private static string GetUpdateStatusText(UpdateCheckInterval interval)
    {
        var lastChecked = UpdateChecker.LoadLastCheckedUtc();
        var intervalText = interval switch
        {
            UpdateCheckInterval.Never => "从不",
            UpdateCheckInterval.Weekly => "每周",
            UpdateCheckInterval.Monthly => "每月",
            _ => "每天"
        };
        return lastChecked is null
            ? $"检查频率：{intervalText}，尚未检查"
            : $"检查频率：{intervalText}，上次：{lastChecked.Value.ToLocalTime():yyyy-MM-dd HH:mm}";
    }

    private sealed record DriverComponentState(string? Status, string? Source, string? InstalledPath);

    private static Border MakeCard(string title, params UIElement[] children)
    {
        var stack = new StackPanel();
        stack.Children.Add(new TextBlock
        {
            Text = title,
            FontSize = 13,
            FontWeight = FontWeights.Bold,
            Foreground = (Brush)Application.Current.Resources["Brush.Text"],
            Margin = new Thickness(0, 0, 0, 8)
        });
        foreach (var child in children) stack.Children.Add(child);
        return new Border { Style = (Style)Application.Current.Resources["UiCard"], Child = stack };
    }

    private static UIElement Row(string label, FrameworkElement control)
    {
        var grid = new Grid { MinHeight = 40, Width = UiMetrics.ContentWidth };
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(130) });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        grid.Children.Add(new TextBlock { Text = label, VerticalAlignment = VerticalAlignment.Center, Foreground = (Brush)Application.Current.Resources["Brush.Text"] });
        control.VerticalAlignment = VerticalAlignment.Center;
        Grid.SetColumn(control, 1);
        grid.Children.Add(control);
        return grid;
    }

    private static UIElement PlainRow(FrameworkElement control)
    {
        var grid = new Grid { MinHeight = 40 };
        control.VerticalAlignment = VerticalAlignment.Center;
        control.HorizontalAlignment = HorizontalAlignment.Left;
        grid.Children.Add(control);
        return grid;
    }

    private static Button MakeButton(string text, double minWidth = 112) => new()
    {
        Content = text,
        Style = (Style)Application.Current.Resources["UiButton"],
        MinWidth = minWidth
    };
}
