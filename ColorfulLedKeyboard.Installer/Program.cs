using System.Diagnostics;
using System.IO.Compression;
using System.Reflection;
using System.Security.Principal;
using System.Text;
using Microsoft.Win32;

namespace ColorfulLedKeyboard.Installer;

internal static class Program
{
    private const string AppName = "ClevoRGBControl";
    private const string ServiceName = "ClevoRGBControlService";
    private const string LegacyServiceName = "ColorfulLedKeyboardService";
    private const string DisplayName = "ClevoRGBControl Service";
    private const string InstallFolderName = "ClevoRGBControl";
    private const string PayloadResourceName = "payload.zip";
    private const string UninstallKeyPath = @"Software\Microsoft\Windows\CurrentVersion\Uninstall\ClevoRGBControl";
    private const string LegacyUninstallKeyPath = @"Software\Microsoft\Windows\CurrentVersion\Uninstall\ColorfulLedKeyboard";
    private const string DriverDllName = "InsydeDCHU.dll";

    private static readonly string InstallDirectory = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles),
        InstallFolderName);

    private static readonly string ServiceDirectory = Path.Combine(InstallDirectory, "Service");
    private static readonly string TrayDirectory = Path.Combine(InstallDirectory, "Tray");
    private static readonly string ExperimentalDirectory = Path.Combine(InstallDirectory, "Experimental");
    private static readonly string ServiceExe = Path.Combine(ServiceDirectory, "ColorfulLedKeyboard.Service.exe");
    private static readonly string TrayExe = Path.Combine(TrayDirectory, "ColorfulLedKeyboard.Tray.exe");
    private static readonly string ControlCenterDriverPath = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86),
        "ControlCenter",
        DriverDllName);
    private static readonly string ProgramDataDirectory = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData),
        "ClevoRGBControl");

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

            if (args.Any(arg => string.Equals(arg, "/uninstall", StringComparison.OrdinalIgnoreCase)))
            {
                Uninstall();
                Show($"{AppName} has been removed.");
                return 0;
            }

            if (IsInstalled())
            {
                var choice = MessageBox.Show(
                    $"{AppName} is already installed.\n\nYes: repair or update\nNo: uninstall\nCancel: do nothing",
                    AppName,
                    MessageBoxButtons.YesNoCancel,
                    MessageBoxIcon.Question);

                if (choice == DialogResult.Cancel)
                {
                    return 0;
                }

                if (choice == DialogResult.No)
                {
                    Uninstall();
                    Show($"{AppName} has been removed.");
                    return 0;
                }
            }

            Install();
            Show(BuildInstallSuccessMessage());
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
        Directory.CreateDirectory(InstallDirectory);
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
        StartTray();
    }

    private static bool TryInstallDriverDll()
    {
        var serviceDestination = Path.Combine(ServiceDirectory, DriverDllName);
        if (File.Exists(ControlCenterDriverPath))
        {
            Directory.CreateDirectory(ServiceDirectory);
            File.Copy(ControlCenterDriverPath, serviceDestination, overwrite: true);
            CopyDriverToExperimental(serviceDestination);
            return true;
        }

        if (File.Exists(serviceDestination))
        {
            CopyDriverToExperimental(serviceDestination);
            return true;
        }

        return false;
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
        if (File.Exists(destination))
        {
            return $"{AppName} has been installed.\n\n{DriverDllName} was installed automatically.\n\nThe tray app will start automatically for the current user.";
        }

        return $"{AppName} has been installed.\n\n{DriverDllName} was not found in:\n{Path.GetDirectoryName(ControlCenterDriverPath)}\n\nPlease copy {DriverDllName} to:\n{ServiceDirectory}\n\nThe tray app will start automatically for the current user.";
    }

    private static void Uninstall()
    {
        StopAndDeleteServiceIfPresent(ServiceName);
        StopAndDeleteServiceIfPresent(LegacyServiceName);
        RemoveTrayStartup();
        UnregisterUninstaller();
        KillTray();

        if (Directory.Exists(InstallDirectory))
        {
            TryDeleteInstallDirectory();
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
        var scriptPath = Path.Combine(Path.GetTempPath(), $"ClevoRGBControl-uninstall-{Guid.NewGuid():N}.cmd");
        File.WriteAllText(
            scriptPath,
            $"""
            @echo off
            timeout /t 3 /nobreak > nul
            rmdir /s /q "{InstallDirectory}"
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
        var setupPath = Path.Combine(InstallDirectory, "ClevoRGBControlSetup.exe");
        File.Copy(Environment.ProcessPath!, setupPath, overwrite: true);

        using var key = Registry.LocalMachine.CreateSubKey(UninstallKeyPath, writable: true)
            ?? throw new InvalidOperationException("Failed to create uninstall registry key.");

        key.SetValue("DisplayName", AppName);
        key.SetValue("DisplayVersion", Assembly.GetExecutingAssembly().GetName().Version?.ToString(3) ?? "1.0.1");
        key.SetValue("Publisher", "ClevoRGBControl");
        key.SetValue("InstallLocation", InstallDirectory);
        key.SetValue("DisplayIcon", setupPath);
        key.SetValue("UninstallString", $"\"{setupPath}\" /uninstall");
        key.SetValue("QuietUninstallString", $"\"{setupPath}\" /uninstall");
        key.SetValue("NoModify", 1, RegistryValueKind.DWord);
        key.SetValue("NoRepair", 1, RegistryValueKind.DWord);
        key.SetValue("EstimatedSize", GetDirectorySizeInKb(InstallDirectory), RegistryValueKind.DWord);
    }

    private static void UnregisterUninstaller()
    {
        Registry.LocalMachine.DeleteSubKeyTree(UninstallKeyPath, throwOnMissingSubKey: false);
        Registry.LocalMachine.DeleteSubKeyTree(LegacyUninstallKeyPath, throwOnMissingSubKey: false);
    }

    private static void RemoveTrayStartup()
    {
        using var key = Registry.CurrentUser.OpenSubKey(@"Software\Microsoft\Windows\CurrentVersion\Run", writable: true);
        key?.DeleteValue(AppName, throwOnMissingValue: false);
    }

    private static void StartTray()
    {
        if (File.Exists(TrayExe))
        {
            Process.Start(new ProcessStartInfo(TrayExe) { UseShellExecute = true });
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
        return Directory.Exists(InstallDirectory) ||
            Registry.LocalMachine.OpenSubKey(UninstallKeyPath) is not null ||
            Run("sc.exe", $"query {ServiceName}", allowFailure: true).ExitCode == 0 ||
            Run("sc.exe", $"query {LegacyServiceName}", allowFailure: true).ExitCode == 0;
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

    private sealed record CommandResult(int ExitCode, string Output, string Error);
}
