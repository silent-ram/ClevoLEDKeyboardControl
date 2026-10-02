using ColorfulLedKeyboard.Core;
using System.Diagnostics;
using System.ServiceProcess;
using Microsoft.Win32;

namespace ColorfulLedKeyboard.Tray;

/// <summary>
/// 统一控制软件的开机自启动:托盘注册表 Run 键 + 灯控服务启动类型。
/// 两者必须作为一个整体开关——只禁托盘会导致"软件还是启动了但不完整"
/// (灯控服务是 Windows 服务,独立于启动项,任务管理器管不到它)。
/// 服务启动类型修改需要管理员权限:当前进程已提权时直接生效,否则弹出 UAC。
/// </summary>
internal static class StartupManager
{
    private const string RunKeyPath = @"Software\Microsoft\Windows\CurrentVersion\Run";
    private const string RunValueName = "ClevoLEDKeyboardControl";

    public static string TrayExePath => Environment.ProcessPath ?? System.Windows.Forms.Application.ExecutablePath;

    public static bool IsTrayRegistered()
    {
        using var key = Registry.CurrentUser.OpenSubKey(RunKeyPath);
        var value = key?.GetValue(RunValueName) as string;
        return !string.IsNullOrWhiteSpace(value)
            && value.Contains(TrayExePath, StringComparison.OrdinalIgnoreCase);
    }

    public static bool IsServiceAuto()
    {
        try
        {
            using var controller = new ServiceController(AppPaths.ServiceName);
            return controller.StartType == ServiceStartMode.Automatic;
        }
        catch
        {
            // 服务未安装等场景按"已配置"处理,避免开关状态误导。
            return true;
        }
    }

    public static (bool TrayRegistered, bool ServiceAuto) GetState() => (IsTrayRegistered(), IsServiceAuto());

    /// <summary>自启动状态实际发生变化后触发(设置窗口订阅它以保持显示同步)。</summary>
    public static event EventHandler? StartupChanged;

    /// <summary>整体开关。先执行需要提权的服务配置,成功后再写托盘注册表键;
    /// 服务步骤失败时托盘键保持原值,不产生半生效状态。</summary>
    public static bool TrySetEnabled(bool enabled, out string error)
    {
        error = "";
        try
        {
            if (!TrySetServiceStartupType(enabled, out var serviceError))
            {
                error = serviceError;
                return false;
            }
            SetTrayRegistered(enabled);
            StartupChanged?.Invoke(null, EventArgs.Empty);
            return true;
        }
        catch (Exception ex)
        {
            error = ex.Message;
            return false;
        }
    }

    public static void SetTrayRegistered(bool enabled)
    {
        using var key = Registry.CurrentUser.OpenSubKey(RunKeyPath, writable: true)
            ?? Registry.CurrentUser.CreateSubKey(RunKeyPath, writable: true);
        if (enabled) key.SetValue(RunValueName, $"\"{TrayExePath}\"");
        else key.DeleteValue(RunValueName, throwOnMissingValue: false);
    }

    private static bool TrySetServiceStartupType(bool automatic, out string error)
    {
        error = "";
        try
        {
            using var controller = new ServiceController(AppPaths.ServiceName);
            var target = automatic ? ServiceStartMode.Automatic : ServiceStartMode.Manual;
            if (controller.StartType == target) return true;
        }
        catch
        {
            return true; // 服务未安装等场景无需调整。
        }

        var mode = automatic ? "auto" : "demand";
        try
        {
            RunSc($"config {AppPaths.ServiceName} start= {mode}");
            return true;
        }
        catch (Exception directError)
        {
            // 非管理员进程:弹出 UAC 提权重试,交互与"重启服务"菜单一致。
            try
            {
                RunElevatedPowerShell($"Set-Service -Name {AppPaths.ServiceName} -StartupType {(automatic ? "Automatic" : "Manual")}");
                return true;
            }
            catch (Exception elevatedError)
            {
                error = $"服务启动类型修改失败:直接执行({directError.Message});提权执行({elevatedError.Message})";
                return false;
            }
        }
    }

    private static void RunSc(string arguments)
    {
        using var process = Process.Start(new ProcessStartInfo("sc.exe", arguments)
        {
            CreateNoWindow = true,
            UseShellExecute = false,
            RedirectStandardError = true,
        });
        process!.WaitForExit(10_000);
        if (process.ExitCode != 0)
        {
            throw new InvalidOperationException($"sc.exe 退出码 {process.ExitCode}");
        }
    }

    private static void RunElevatedPowerShell(string command)
    {
        using var process = Process.Start(new ProcessStartInfo("powershell.exe", $"-NoProfile -ExecutionPolicy Bypass -Command \"{command}\"")
        {
            UseShellExecute = true,
            Verb = "runas",
            WindowStyle = ProcessWindowStyle.Hidden,
        });
        process!.WaitForExit(15_000);
        if (process.ExitCode != 0)
        {
            throw new InvalidOperationException($"PowerShell 退出码 {process.ExitCode}");
        }
    }
}
