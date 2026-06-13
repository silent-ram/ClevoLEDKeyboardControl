using System.Diagnostics;
using System.IO.Compression;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Security.Principal;
using System.Text;
using System.Text.Json;
using Microsoft.Win32;

namespace ColorfulLedKeyboard.Installer;

internal static class Program
{
    private const string AppName = "ClevoLEDKeyboardControl";
    private const string ServiceName = "ClevoLEDKeyboardControlService";
    private const string LegacyServiceName = "ColorfulLedKeyboardService";
    private const string LegacyServiceNameClevoRgb = "ClevoRGBControlService";
    private const string DisplayName = "ClevoLEDKeyboardControl Service";
    private const string InstallFolderName = "ClevoLEDKeyboardControl";
    private const string PayloadResourceName = "payload.zip";
    private const string UninstallKeyPath = @"Software\Microsoft\Windows\CurrentVersion\Uninstall\ClevoLEDKeyboardControl";
    private const string LegacyUninstallKeyPath = @"Software\Microsoft\Windows\CurrentVersion\Uninstall\ColorfulLedKeyboard";
    private const string LegacyUninstallKeyPathClevoRgb = @"Software\Microsoft\Windows\CurrentVersion\Uninstall\ClevoRGBControl";
    private const string DriverDllName = "InsydeDCHU.dll";

    private static readonly string InstallDirectory = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles),
        InstallFolderName);

    private static readonly string ServiceDirectory = Path.Combine(InstallDirectory, "Service");
    private static readonly string TrayDirectory = Path.Combine(InstallDirectory, "Tray");
    private static readonly string ExperimentalDirectory = Path.Combine(InstallDirectory, "Experimental");
    private static readonly string ServiceExe = Path.Combine(ServiceDirectory, "ColorfulLedKeyboard.Service.exe");
    private static readonly string TrayExe = Path.Combine(TrayDirectory, "ColorfulLedKeyboard.Tray.exe");
    private static readonly string UninstallerExe = Path.Combine(InstallDirectory, "ClevoLEDKeyboardControlUninstall.exe");
    private static readonly string LegacyInstalledSetupExe = Path.Combine(InstallDirectory, "ClevoLEDKeyboardControlSetup.exe");
    private static readonly string ProgramDataDirectory = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData),
        "ClevoLEDKeyboardControl");

    private static readonly string StartMenuShortcutPath = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.CommonStartMenu),
        "Programs",
        "ClevoLEDKeyboardControl.lnk");

    private static readonly string DesktopShortcutPath = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.CommonDesktopDirectory),
        "ClevoLEDKeyboardControl.lnk");

    [STAThread]
    private static int Main(string[] args)
    {
        try
        {
            if (!IsAdministrator())
            {
                Show("Please run this installer as Administrator.");
                return 1;
            }

            if (IsUninstallerProcess() && args.Length == 0)
            {
                var keepSettings = AskKeepSettingsBeforeUninstall();
                if (keepSettings is null)
                {
                    return 0;
                }

                RelaunchUninstallFromTemp(keepSettings.Value);
                return 0;
            }

            if (HasArgument(args, "/uninstall"))
            {
                var keepSettings = HasArgument(args, "/keep-settings");
                if (!HasArgument(args, "/uninstall-from-temp") && IsRunningFromInstallDirectory())
                {
                    RelaunchUninstallFromTemp(keepSettings);
                    return 0;
                }

                Uninstall(keepSettings);
                Show(BuildUninstallSuccessMessage(keepSettings));
                if (HasArgument(args, "/uninstall-from-temp"))
                {
                    ScheduleFileRemoval(Environment.ProcessPath!, "temp-uninstaller");
                }

                return 0;
            }

            if (HasArgument(args, "/repair"))
            {
                Install();
                Show(BuildInstallSuccessMessage());
                StartTray();
                return 0;
            }

            if (IsInstalled())
            {
                var choice = AskInstalledAction();

                if (choice == InstalledAction.Cancel)
                {
                    return 0;
                }

                if (choice == InstalledAction.Uninstall)
                {
                    var keepSettings = AskKeepSettingsBeforeUninstall();
                    if (keepSettings is null)
                    {
                        return 0;
                    }

                    Uninstall(keepSettings.Value);
                    Show(BuildUninstallSuccessMessage(keepSettings.Value));
                    return 0;
                }
            }

            Install();
            Show(BuildInstallSuccessMessage());
            StartTray();
            return 0;
        }
        catch (Exception ex)
        {
            Show($"Operation failed:\n{ex.Message}");
            return 1;
        }
    }

    private static void Install()
    {
        StopAndDeleteServiceIfPresent(ServiceName);
        StopAndDeleteServiceIfPresent(LegacyServiceName);
        StopAndDeleteServiceIfPresent(LegacyServiceNameClevoRgb);
        KillTray();
        Directory.CreateDirectory(InstallDirectory);
        CleanPayloadDirectories();
        RemoveLegacyInstalledSetup();
        ExtractPayload();
        EnsureProgramDataPermissions();
        TryInstallDriverDll();

        if (!File.Exists(ServiceExe))
        {
            throw new FileNotFoundException("Service executable is missing from installer payload.", ServiceExe);
        }

        Run("sc.exe", $"create {ServiceName} binPath= \"{ServiceExe}\" start= auto DisplayName= \"{DisplayName}\"");
        Run("sc.exe", $"description {ServiceName} \"Controls Clevo-compatible keyboard RGB lighting in the background.\"");
        Run("sc.exe", $"start {ServiceName}", allowFailure: true);

        AddTrayStartup();
        RegisterUninstaller();
        CreateUserShortcuts();  // 失败不抛，仅返回 false
    }

    private static bool TryInstallDriverDll()
    {
        var serviceDestination = Path.Combine(ServiceDirectory, DriverDllName);
        var driver = FindDriverDll(serviceDestination);
        if (driver is null)
        {
            SaveDriverComponentState("Missing", null, null, serviceDestination);
            return false;
        }

        Directory.CreateDirectory(ServiceDirectory);
        if (!string.Equals(
            Path.GetFullPath(driver.Path),
            Path.GetFullPath(serviceDestination),
            StringComparison.OrdinalIgnoreCase))
        {
            File.Copy(driver.Path, serviceDestination, overwrite: true);
        }

        CopyDriverToExperimental(serviceDestination);
        SaveDriverComponentState("Installed", driver.Source, driver.Path, serviceDestination);
        return true;
    }

    private static DriverSearchResult? FindDriverDll(string serviceDestination)
    {
        foreach (var path in GetDriverSearchPaths(serviceDestination).DistinctBy(candidate => candidate.Path, StringComparer.OrdinalIgnoreCase))
        {
            if (!string.IsNullOrWhiteSpace(path.Path) && File.Exists(path.Path))
            {
                return path;
            }
        }

        return null;
    }

    private static IEnumerable<DriverSearchResult> GetDriverSearchPaths(string serviceDestination)
    {
        yield return new DriverSearchResult("安装包内置", serviceDestination);

        var setupDirectory = Path.GetDirectoryName(Environment.ProcessPath);
        if (!string.IsNullOrWhiteSpace(setupDirectory))
        {
            yield return new DriverSearchResult("安装器同目录", Path.Combine(setupDirectory, DriverDllName));
        }

        var oldInstallRoot = Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles);
        foreach (var oldFolder in new[] { "ClevoRGBControl", "ColorfulLedKeyboard" })
        {
            yield return new DriverSearchResult("旧版安装目录", Path.Combine(oldInstallRoot, oldFolder, "Service", DriverDllName));
        }

        foreach (var root in new[]
        {
            Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86),
            Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles)
        })
        {
            if (string.IsNullOrWhiteSpace(root))
            {
                continue;
            }

            foreach (var controlCenterFolder in new[] { "ControlCenter", "Control Center", "ControlCenter3", "Control Center 3.0" })
            {
                yield return new DriverSearchResult("OEM Control Center", Path.Combine(root, controlCenterFolder, DriverDllName));
            }
        }
    }

    private static void SaveDriverComponentState(string status, string? source, string? sourcePath, string installedPath)
    {
        try
        {
            Directory.CreateDirectory(ProgramDataDirectory);
            var statePath = Path.Combine(ProgramDataDirectory, "driver-component.json");
            var state = new DriverComponentState(
                Status: status,
                Source: source,
                SourcePath: sourcePath,
                InstalledPath: installedPath,
                UpdatedUtc: DateTimeOffset.UtcNow);
            File.WriteAllText(statePath, JsonSerializer.Serialize(state, JsonOptions));
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
        }
    }

    private static void CopyDriverToExperimental(string sourcePath)
    {
        if (!Directory.Exists(ExperimentalDirectory) || !File.Exists(sourcePath))
        {
            return;
        }

        File.Copy(sourcePath, Path.Combine(ExperimentalDirectory, DriverDllName), overwrite: true);
    }

    private static string BuildInstallSuccessMessage()
    {
        var destination = Path.Combine(ServiceDirectory, DriverDllName);
        var serviceStatus = GetServiceStatusText();
        if (File.Exists(destination))
        {
            return $"{AppName} 已安装。\n\n服务：{serviceStatus}\n厂商灯控组件：已安装\n开机自启动：已启用\n\n设置窗口将自动打开。";
        }

        return $"{AppName} 已安装。\n\n服务：{serviceStatus}\n厂商灯控组件：未找到\n开机自启动：已启用\n\n安装包内没有包含厂商灯控组件，且本机未检测到 OEM Control Center。程序已安装，但暂时无法控制键盘灯。请安装 OEM Control Center 后重新运行安装器进行修复。\n\n设置窗口将自动打开。";
    }

    private static string GetServiceStatusText()
    {
        var query = Run("sc.exe", $"query {ServiceName}", allowFailure: true);
        if (query.ExitCode != 0)
        {
            return "未安装";
        }

        if (query.Output.Contains("RUNNING", StringComparison.OrdinalIgnoreCase))
        {
            return "运行中";
        }

        if (query.Output.Contains("START_PENDING", StringComparison.OrdinalIgnoreCase))
        {
            return "正在启动";
        }

        if (query.Output.Contains("STOPPED", StringComparison.OrdinalIgnoreCase))
        {
            return "已停止";
        }

        return "已安装";
    }

    private static void Uninstall(bool keepSettings)
    {
        StopAndDeleteServiceIfPresent(ServiceName);
        StopAndDeleteServiceIfPresent(LegacyServiceName);
        StopAndDeleteServiceIfPresent(LegacyServiceNameClevoRgb);
        RemoveTrayStartup();
        RemoveUserShortcuts();
        UnregisterUninstaller();
        KillTray();

        if (Directory.Exists(InstallDirectory))
        {
            TryDeleteInstallDirectory();
        }

        if (!keepSettings)
        {
            TryDeleteProgramDataDirectory();
        }
    }

    private static void TryDeleteInstallDirectory()
    {
        try
        {
            Directory.Delete(InstallDirectory, recursive: true);
        }
        catch (IOException)
        {
            ScheduleInstallDirectoryRemoval();
        }
        catch (UnauthorizedAccessException)
        {
            ScheduleInstallDirectoryRemoval();
        }
    }

    private static void ScheduleInstallDirectoryRemoval()
    {
        ScheduleDirectoryRemoval(InstallDirectory, "uninstall");
    }

    private static void TryDeleteProgramDataDirectory()
    {
        if (!Directory.Exists(ProgramDataDirectory))
        {
            return;
        }

        try
        {
            Directory.Delete(ProgramDataDirectory, recursive: true);
        }
        catch (IOException)
        {
            ScheduleDirectoryRemoval(ProgramDataDirectory, "purge-config");
        }
        catch (UnauthorizedAccessException)
        {
            ScheduleDirectoryRemoval(ProgramDataDirectory, "purge-config");
        }
    }

    private static void CleanPayloadDirectories()
    {
        TryDeleteDirectory(ServiceDirectory);
        TryDeleteDirectory(TrayDirectory);
        TryDeleteDirectory(ExperimentalDirectory);
    }

    private static void RemoveLegacyInstalledSetup()
    {
        if (!File.Exists(LegacyInstalledSetupExe))
        {
            return;
        }

        try
        {
            if (string.Equals(
                Path.GetFullPath(LegacyInstalledSetupExe),
                Path.GetFullPath(Environment.ProcessPath ?? string.Empty),
                StringComparison.OrdinalIgnoreCase))
            {
                ScheduleFileRemoval(LegacyInstalledSetupExe, "remove-legacy-setup");
                return;
            }

            File.Delete(LegacyInstalledSetupExe);
        }
        catch (IOException)
        {
            ScheduleFileRemoval(LegacyInstalledSetupExe, "remove-legacy-setup");
        }
        catch (UnauthorizedAccessException)
        {
            ScheduleFileRemoval(LegacyInstalledSetupExe, "remove-legacy-setup");
        }
    }

    private static void TryDeleteDirectory(string directory)
    {
        if (!Directory.Exists(directory))
        {
            return;
        }

        try
        {
            Directory.Delete(directory, recursive: true);
        }
        catch (IOException)
        {
            throw new InvalidOperationException($"Failed to clean old install directory. Close running {AppName} processes and try again: {directory}");
        }
        catch (UnauthorizedAccessException)
        {
            throw new InvalidOperationException($"Failed to clean old install directory. Close running {AppName} processes and try again: {directory}");
        }
    }

    private static void ScheduleDirectoryRemoval(string directory, string operation)
    {
        var scriptPath = Path.Combine(Path.GetTempPath(), $"ClevoLEDKeyboardControl-{operation}-{Guid.NewGuid():N}.cmd");
        File.WriteAllText(
            scriptPath,
            $"""
            @echo off
            for /l %%i in (1,1,15) do (
                rmdir /s /q "{directory}" 2>nul
                if not exist "{directory}" goto done
                timeout /t 1 /nobreak > nul
            )
            :done
            del "%~f0"
            """,
            Encoding.ASCII);

        Process.Start(new ProcessStartInfo("cmd.exe", $"/c \"{scriptPath}\"")
        {
            CreateNoWindow = true,
            UseShellExecute = false,
            WindowStyle = ProcessWindowStyle.Hidden
        });
    }

    private static void ScheduleFileRemoval(string filePath, string operation)
    {
        var scriptPath = Path.Combine(Path.GetTempPath(), $"ClevoLEDKeyboardControl-{operation}-{Guid.NewGuid():N}.cmd");
        File.WriteAllText(
            scriptPath,
            $"""
            @echo off
            for /l %%i in (1,1,15) do (
                del /f /q "{filePath}" 2>nul
                if not exist "{filePath}" goto done
                timeout /t 1 /nobreak > nul
            )
            :done
            del "%~f0"
            """,
            Encoding.ASCII);

        Process.Start(new ProcessStartInfo("cmd.exe", $"/c \"{scriptPath}\"")
        {
            CreateNoWindow = true,
            UseShellExecute = false,
            WindowStyle = ProcessWindowStyle.Hidden
        });
    }

    private static bool IsRunningFromInstallDirectory()
    {
        var processPath = Environment.ProcessPath;
        if (string.IsNullOrWhiteSpace(processPath))
        {
            return false;
        }

        var processDirectory = Path.GetFullPath(Path.GetDirectoryName(processPath) ?? "");
        var installDirectory = Path.GetFullPath(InstallDirectory);
        return string.Equals(processDirectory, installDirectory, StringComparison.OrdinalIgnoreCase);
    }

    private static void RelaunchUninstallFromTemp(bool keepSettings)
    {
        var tempUninstaller = Path.Combine(Path.GetTempPath(), $"ClevoLEDKeyboardControl-uninstall-{Guid.NewGuid():N}.exe");
        File.Copy(Environment.ProcessPath!, tempUninstaller, overwrite: true);

        var arguments = keepSettings
            ? "/uninstall /uninstall-from-temp /keep-settings"
            : "/uninstall /uninstall-from-temp";

        Process.Start(new ProcessStartInfo(tempUninstaller, arguments)
        {
            CreateNoWindow = true,
            UseShellExecute = false,
            WindowStyle = ProcessWindowStyle.Hidden
        });
    }

    private static void EnsureProgramDataPermissions()
    {
        Directory.CreateDirectory(ProgramDataDirectory);
        Run("icacls.exe", $"\"{ProgramDataDirectory}\" /grant *S-1-5-32-545:(OI)(CI)M /T /C");
    }

    private static void ExtractPayload()
    {
        using var stream = Assembly.GetExecutingAssembly().GetManifestResourceStream(PayloadResourceName)
            ?? throw new InvalidOperationException("Installer payload is missing. Run scripts\\publish.ps1 to build the setup executable.");

        using var archive = new ZipArchive(stream, ZipArchiveMode.Read);
        archive.ExtractToDirectory(InstallDirectory, overwriteFiles: true);
    }

    private static void StopAndDeleteServiceIfPresent(string serviceName)
    {
        var query = Run("sc.exe", $"query {serviceName}", allowFailure: true);
        if (query.ExitCode != 0)
        {
            return;
        }

        Run("sc.exe", $"stop {serviceName}", allowFailure: true);
        Thread.Sleep(1500);
        Run("sc.exe", $"delete {serviceName}");
        Thread.Sleep(1000);
    }

    private static void AddTrayStartup()
    {
        using var key = Registry.CurrentUser.OpenSubKey(@"Software\Microsoft\Windows\CurrentVersion\Run", writable: true)
            ?? Registry.CurrentUser.CreateSubKey(@"Software\Microsoft\Windows\CurrentVersion\Run", writable: true);

        key.SetValue(AppName, $"\"{TrayExe}\"");
    }

    private static void RegisterUninstaller()
    {
        File.Copy(Environment.ProcessPath!, UninstallerExe, overwrite: true);

        using var key = Registry.LocalMachine.CreateSubKey(UninstallKeyPath, writable: true)
            ?? throw new InvalidOperationException("Failed to create uninstall registry key.");

        key.SetValue("DisplayName", AppName);
        key.SetValue("DisplayVersion", Assembly.GetExecutingAssembly().GetName().Version?.ToString(3) ?? "1.0.1");
        key.SetValue("Publisher", "ClevoLEDKeyboardControl");
        key.SetValue("InstallLocation", InstallDirectory);
        key.SetValue("DisplayIcon", UninstallerExe);
        key.SetValue("UninstallString", $"\"{UninstallerExe}\"");
        key.SetValue("QuietUninstallString", $"\"{UninstallerExe}\" /uninstall");
        key.SetValue("NoModify", 1, RegistryValueKind.DWord);
        key.SetValue("NoRepair", 1, RegistryValueKind.DWord);
        key.SetValue("EstimatedSize", GetDirectorySizeInKb(InstallDirectory), RegistryValueKind.DWord);
    }

    private static void UnregisterUninstaller()
    {
        Registry.LocalMachine.DeleteSubKeyTree(UninstallKeyPath, throwOnMissingSubKey: false);
        Registry.LocalMachine.DeleteSubKeyTree(LegacyUninstallKeyPath, throwOnMissingSubKey: false);
        Registry.LocalMachine.DeleteSubKeyTree(LegacyUninstallKeyPathClevoRgb, throwOnMissingSubKey: false);
    }

    private static void RemoveTrayStartup()
    {
        using var key = Registry.CurrentUser.OpenSubKey(@"Software\Microsoft\Windows\CurrentVersion\Run", writable: true);
        key?.DeleteValue(AppName, throwOnMissingValue: false);
    }

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

    private static void StartTray()
    {
        if (File.Exists(TrayExe))
        {
            Process.Start(new ProcessStartInfo(TrayExe, "--settings") { UseShellExecute = true });
        }
    }

    private static void KillTray()
    {
        foreach (var process in Process.GetProcessesByName("ColorfulLedKeyboard.Tray"))
        {
            process.Kill(entireProcessTree: true);
            process.WaitForExit(5000);
        }
    }

    private static bool IsInstalled()
    {
        return File.Exists(ServiceExe) ||
            File.Exists(TrayExe) ||
            Registry.LocalMachine.OpenSubKey(UninstallKeyPath) is not null ||
            Run("sc.exe", $"query {ServiceName}", allowFailure: true).ExitCode == 0 ||
            Run("sc.exe", $"query {LegacyServiceName}", allowFailure: true).ExitCode == 0 ||
            Run("sc.exe", $"query {LegacyServiceNameClevoRgb}", allowFailure: true).ExitCode == 0 ||
            Registry.LocalMachine.OpenSubKey(LegacyUninstallKeyPathClevoRgb) is not null;
    }

    private static int GetDirectorySizeInKb(string path)
    {
        if (!Directory.Exists(path))
        {
            return 0;
        }

        var bytes = Directory.EnumerateFiles(path, "*", SearchOption.AllDirectories)
            .Sum(file => new FileInfo(file).Length);
        return (int)Math.Max(1, bytes / 1024);
    }

    private static CommandResult Run(string fileName, string arguments, bool allowFailure = false)
    {
        using var process = new Process
        {
            StartInfo = new ProcessStartInfo(fileName, arguments)
            {
                CreateNoWindow = true,
                UseShellExecute = false,
                RedirectStandardError = true,
                RedirectStandardOutput = true,
                StandardOutputEncoding = Encoding.UTF8,
                StandardErrorEncoding = Encoding.UTF8
            }
        };

        process.Start();
        var output = process.StandardOutput.ReadToEnd();
        var error = process.StandardError.ReadToEnd();
        process.WaitForExit();

        if (process.ExitCode != 0 && !allowFailure)
        {
            throw new InvalidOperationException($"{fileName} {arguments} failed with exit code {process.ExitCode}.\n{output}\n{error}");
        }

        return new CommandResult(process.ExitCode, output, error);
    }

    private static bool IsAdministrator()
    {
        using var identity = WindowsIdentity.GetCurrent();
        var principal = new WindowsPrincipal(identity);
        return principal.IsInRole(WindowsBuiltInRole.Administrator);
    }

    private static void Show(string message)
    {
        MessageBox.Show(message, AppName, MessageBoxButtons.OK, MessageBoxIcon.Information);
    }

    private static bool? AskKeepSettingsBeforeUninstall()
    {
        using var form = new Form
        {
            Text = $"{AppName} 卸载",
            StartPosition = FormStartPosition.CenterScreen,
            FormBorderStyle = FormBorderStyle.FixedDialog,
            MaximizeBox = false,
            MinimizeBox = false,
            ClientSize = new Size(420, 170)
        };

        var message = new Label
        {
            Text = "将卸载程序并默认删除本机配置。",
            Location = new Point(22, 20),
            Size = new Size(370, 26)
        };

        var keepSettings = new CheckBox
        {
            Text = "保留我的设置和配置文件",
            Location = new Point(22, 58),
            Size = new Size(260, 28)
        };

        var uninstall = new Button
        {
            Text = "卸载",
            DialogResult = DialogResult.OK,
            Location = new Point(220, 112),
            Size = new Size(82, 30)
        };

        var cancel = new Button
        {
            Text = "取消",
            DialogResult = DialogResult.Cancel,
            Location = new Point(314, 112),
            Size = new Size(82, 30)
        };

        form.Controls.Add(message);
        form.Controls.Add(keepSettings);
        form.Controls.Add(uninstall);
        form.Controls.Add(cancel);
        form.AcceptButton = uninstall;
        form.CancelButton = cancel;

        return form.ShowDialog() == DialogResult.OK
            ? keepSettings.Checked
            : null;
    }

    private static InstalledAction AskInstalledAction()
    {
        using var form = new Form
        {
            Text = AppName,
            StartPosition = FormStartPosition.CenterScreen,
            FormBorderStyle = FormBorderStyle.FixedDialog,
            MaximizeBox = false,
            MinimizeBox = false,
            ClientSize = new Size(460, 178)
        };

        var message = new Label
        {
            Text = $"{AppName} 已安装。请选择要执行的操作。",
            Location = new Point(22, 22),
            Size = new Size(410, 28)
        };

        var repair = new Button
        {
            Text = "修复或更新",
            DialogResult = DialogResult.Yes,
            Location = new Point(108, 118),
            Size = new Size(104, 32)
        };

        var uninstall = new Button
        {
            Text = "卸载",
            DialogResult = DialogResult.No,
            Location = new Point(224, 118),
            Size = new Size(82, 32)
        };

        var cancel = new Button
        {
            Text = "取消",
            DialogResult = DialogResult.Cancel,
            Location = new Point(318, 118),
            Size = new Size(82, 32)
        };

        form.Controls.Add(message);
        form.Controls.Add(repair);
        form.Controls.Add(uninstall);
        form.Controls.Add(cancel);
        form.AcceptButton = repair;
        form.CancelButton = cancel;

        return form.ShowDialog() switch
        {
            DialogResult.Yes => InstalledAction.RepairOrUpdate,
            DialogResult.No => InstalledAction.Uninstall,
            _ => InstalledAction.Cancel
        };
    }

    private static string BuildUninstallSuccessMessage(bool keepSettings)
    {
        var configStatus = keepSettings ? "已保留" : "已删除或已安排删除";
        return $"{AppName} 已卸载。\n\n配置文件：{configStatus}";
    }

    private static bool HasArgument(string[] args, string value) =>
        args.Any(arg => string.Equals(arg, value, StringComparison.OrdinalIgnoreCase));

    private static bool IsUninstallerProcess() =>
        string.Equals(
            Path.GetFileName(Environment.ProcessPath),
            Path.GetFileName(UninstallerExe),
            StringComparison.OrdinalIgnoreCase);

    private static readonly JsonSerializerOptions JsonOptions = new() { WriteIndented = true };

    private sealed record DriverSearchResult(string Source, string Path);

    private sealed record DriverComponentState(
        string Status,
        string? Source,
        string? SourcePath,
        string InstalledPath,
        DateTimeOffset UpdatedUtc);

    private sealed record CommandResult(int ExitCode, string Output, string Error);

    private enum InstalledAction
    {
        RepairOrUpdate,
        Uninstall,
        Cancel
    }
}
