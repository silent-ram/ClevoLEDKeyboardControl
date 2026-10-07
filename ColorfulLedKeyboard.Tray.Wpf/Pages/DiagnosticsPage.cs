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
    private readonly Button _refresh = MakeButton("diag.refresh", 130);
    private readonly Button _restoreBackup = MakeButton("diag.restore", 130);

    public event EventHandler? Changed;

    public DiagnosticsPage()
    {
        _refresh.Click += (_, _) => CollectAll();
        _restoreBackup.Click += (_, _) => RestoreLastGood();

        var stack = new StackPanel { Margin = new Thickness(18, 18, 18, 28), MaxWidth = 832 };
        stack.Children.Add(MakeCard("diag.card.service",
            Row("diag.row.service", MakeDiagnosticBox("diag.row.service")),
            Row("diag.row.driver", MakeDiagnosticBox("diag.row.driver")),
            PlainRow(_refresh)));
        stack.Children.Add(MakeCard("diag.card.apps",
            Row("diag.row.foreground", MakeDiagnosticBox("diag.row.foreground")),
            Row("diag.row.scene", MakeDiagnosticBox("diag.row.scene")),
            Row("diag.row.monitor", MakeDiagnosticBox("diag.row.monitor"))));
        stack.Children.Add(MakeCard("diag.card.update",
            Row("diag.row.update", MakeDiagnosticBox("diag.row.update")),
            Row("diag.row.recovery", MakeDiagnosticBox("diag.row.recovery")),
            Row("diag.row.dir", MakeDiagnosticBox("diag.row.dir"))));
        stack.Children.Add(MakeCard("diag.card.recovery", PlainRow(_restoreBackup)));

        var scroll = new ScrollViewer
        {
            Content = stack,
            VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
            HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled
        };
        scroll.SetResourceReference(StyleProperty, "DarkScrollViewer");
        Content = scroll;
        Loaded += (_, _) => CollectAll();
    }

    private System.Windows.Controls.TextBox MakeDiagnosticBox(string key)
    {
        var box = new System.Windows.Controls.TextBox
        {
            IsReadOnly = true,
            MinWidth = 500
        };
        box.SetResourceReference(StyleProperty, "UiTextBox");
        box.SetResourceReference(TextBox.BackgroundProperty, "Brush.Window");
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

        Set("diag.row.service", GetServiceStatusText());
        Set("diag.row.driver", GetDriverStatusText());
        Set("diag.row.foreground", foreground is null
            ? AppStrings.Get("diag.foreground.none")
            : AppStrings.Format("diag.foreground.updated", foreground.ProcessName, Math.Max(0, (int)(DateTimeOffset.UtcNow - foreground.UpdatedUtc).TotalSeconds)));
        Set("diag.row.scene", !settings.Automation.Enabled ? AppStrings.Get("diag.scene.disabled")
            : automation is null || string.IsNullOrWhiteSpace(automation.ActiveRuleName)
                ? AppStrings.Format("diag.scene.base", string.IsNullOrWhiteSpace(automation?.InvalidReason) ? "" : $"；{automation!.InvalidReason}")
                : AppStrings.Format("diag.scene.rule", automation!.ActiveRuleName, automation.TargetDescription, automation.IdleOverrideActive ? AppStrings.Get("diag.scene.idleTag") : ""));
        Set("diag.row.monitor", BuildMonitorStatus(audioState, mediaState));
        Set("diag.row.update", GetUpdateStatusText(settings.Update.CheckInterval));
        Set("diag.row.recovery", recovery is null
            ? AppStrings.Get("diag.recovery.none")
            : AppStrings.Format("diag.recovery.done", recovery.Result, FormatAge(DateTimeOffset.UtcNow - recovery.UpdatedUtc)));
        Set("diag.row.dir", AppPaths.ProgramDataDirectory);
    }

    private static string BuildMonitorStatus(AudioApplicationsState? audio, MediaPlaybackState? media)
    {
        var errors = new[] { audio?.LastError, media?.LastError }
            .Where(value => !string.IsNullOrWhiteSpace(value)).ToList();
        if (errors.Count > 0) return string.Join("；", errors);
        if (audio is null || DateTimeOffset.UtcNow - audio.UpdatedUtc > TimeSpan.FromSeconds(10))
            return AppStrings.Get("diag.monitor.stale");
        var ipc = ServiceIpc.IsAvailable() ? AppStrings.Get("diag.monitor.pipeOk") : AppStrings.Get("diag.monitor.pipeDown");
        return AppStrings.Format("diag.monitor.summary", ipc, audio.Applications.Count, media?.Sessions.Count ?? 0);
    }

    private static string FormatAge(TimeSpan age)
    {
        if (age.TotalMinutes < 1) return AppStrings.Format("diag.time.seconds", age.Seconds);
        if (age.TotalHours < 1) return AppStrings.Format("diag.time.minutes", (int)age.TotalMinutes);
        if (age.TotalDays < 1) return AppStrings.Format("diag.time.hours", (int)age.TotalHours);
        return AppStrings.Format("diag.time.days", (int)age.TotalDays);
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
            result.Success ? AppStrings.Get("diag.restore.done") : AppStrings.Format("diag.restore.failed", result.Message),
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
                ServiceProcess.ServiceControllerStatus.Running => AppStrings.Get("diag.svc.running"),
                ServiceProcess.ServiceControllerStatus.Stopped => AppStrings.Get("diag.svc.stopped"),
                ServiceProcess.ServiceControllerStatus.Paused => AppStrings.Get("diag.svc.paused"),
                ServiceProcess.ServiceControllerStatus.StartPending => AppStrings.Get("diag.svc.startPending"),
                ServiceProcess.ServiceControllerStatus.StopPending => AppStrings.Get("diag.svc.stopPending"),
                _ => controller.Status.ToString()
            };
        }
        catch (InvalidOperationException)
        {
            return AppStrings.Get("diag.svc.notInstalled");
        }
        catch (Exception ex) when (ex is System.ComponentModel.Win32Exception or UnauthorizedAccessException)
        {
            return AppStrings.Format("diag.svc.readFail", ex.Message);
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
                return AppStrings.Format("diag.driver.installed", state.Source ?? AppStrings.Get("diag.driver.unknown"), state.InstalledPath);
            }

            if (string.Equals(state.Status, "Missing", StringComparison.OrdinalIgnoreCase))
            {
                return AppStrings.Get("diag.driver.notFound");
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

        if (File.Exists(serviceDll)) return AppStrings.Format("diag.driver.installed", AppStrings.Get("diag.driver.trayDir"), serviceDll);
        if (File.Exists(installedServiceDll)) return AppStrings.Format("diag.driver.installed", AppStrings.Get("diag.driver.serviceDir"), installedServiceDll);
        if (File.Exists(controlCenterDll)) return AppStrings.Get("diag.driver.inControlCenter");
        return AppStrings.Get("diag.driver.notFound2");
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
            UpdateCheckInterval.Never => AppStrings.Get("diag.update.never"),
            UpdateCheckInterval.Weekly => AppStrings.Get("diag.update.weekly"),
            UpdateCheckInterval.Monthly => AppStrings.Get("diag.update.monthly"),
            _ => AppStrings.Get("diag.update.daily")
        };
        return lastChecked is null
            ? AppStrings.Format("diag.update.neverChecked", intervalText)
            : AppStrings.Format("diag.update.last", intervalText, lastChecked.Value.ToLocalTime().ToString("yyyy-MM-dd HH:mm"));
    }

    private sealed record DriverComponentState(string? Status, string? Source, string? InstalledPath);

    private static Border MakeCard(string title, params UIElement[] children)
    {
        var stack = new StackPanel();
        var heading = new TextBlock
        {
            Text = AppStrings.Get(title),
            FontSize = 13,
            FontWeight = FontWeights.Bold,
            Margin = new Thickness(0, 0, 0, 8)
        };
        heading.SetResourceReference(TextBlock.ForegroundProperty, "Brush.Text");
        stack.Children.Add(heading);
        foreach (var child in children) stack.Children.Add(child);
        var card = new Border { Child = stack };
        card.SetResourceReference(StyleProperty, "UiCard");
        return card;
    }

    private static UIElement Row(string label, FrameworkElement control)
    {
        var grid = new Grid { MinHeight = 40, MaxWidth = UiMetrics.ContentWidth };
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(130) });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        var labelBlock = new TextBlock { Text = AppStrings.Get(label), VerticalAlignment = VerticalAlignment.Center };
        labelBlock.SetResourceReference(TextBlock.ForegroundProperty, "Brush.Text");
        grid.Children.Add(labelBlock);
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

    private static Button MakeButton(string key, double minWidth = 112)
    {
        var button = new Button { Content = AppStrings.Get(key), MinWidth = minWidth };
        button.SetResourceReference(StyleProperty, "UiButton");
        return button;
    }
}
