using ColorfulLedKeyboard.Core;
using System.Diagnostics;
using System.IO.Pipes;
using System.Runtime.InteropServices;
using System.Security.AccessControl;
using System.Security.Principal;
using System.Text;
using System.Text.Json;

namespace ColorfulLedKeyboard.Service;

public sealed class ServiceIpcServer : IDisposable
{
    private readonly ILogger<ServiceIpcServer> _logger;
    private readonly CancellationTokenSource _stop = new();
    private Task? _loop;

    public ServiceIpcServer(ILogger<ServiceIpcServer> logger) => _logger = logger;

    public void Start() => _loop ??= Task.Run(() => AcceptLoopAsync(_stop.Token));

    private async Task AcceptLoopAsync(CancellationToken cancellationToken)
    {
        while (!cancellationToken.IsCancellationRequested)
        {
            try
            {
                using var pipe = CreatePipe();
                await pipe.WaitForConnectionAsync(cancellationToken);
                await HandleAsync(pipe, cancellationToken);
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested) { }
            catch (Exception ex) { _logger.LogWarning(ex, "IPC request failed"); }
        }
    }

    private static NamedPipeServerStream CreatePipe()
    {
        var security = new PipeSecurity();
        security.SetOwner(new SecurityIdentifier(WellKnownSidType.LocalSystemSid, null));
        security.AddAccessRule(new PipeAccessRule(
            new SecurityIdentifier(WellKnownSidType.BuiltinUsersSid, null),
            PipeAccessRights.ReadWrite | PipeAccessRights.CreateNewInstance, AccessControlType.Allow));
        security.AddAccessRule(new PipeAccessRule(
            new SecurityIdentifier(WellKnownSidType.LocalSystemSid, null),
            PipeAccessRights.FullControl, AccessControlType.Allow));
        return NamedPipeServerStreamAcl.Create(ServiceIpc.PipeName, PipeDirection.InOut, 8,
            PipeTransmissionMode.Byte, PipeOptions.Asynchronous, 4096, 4096, security);
    }

    private async Task HandleAsync(NamedPipeServerStream pipe, CancellationToken cancellationToken)
    {
        if (!IsInteractiveLocalClient(pipe))
        {
            await ReplyAsync(pipe, false, "Unauthorized client session", false, cancellationToken);
            return;
        }
        using var reader = new BinaryReader(pipe, Encoding.UTF8, leaveOpen: true);
        var length = reader.ReadInt32();
        if (length <= 0 || length > ServiceIpc.MaximumMessageBytes)
        {
            await ReplyAsync(pipe, false, "Invalid message length", false, cancellationToken);
            return;
        }
        var bytes = reader.ReadBytes(length);
        using var document = JsonDocument.Parse(bytes);
        var root = document.RootElement;
        if (!root.TryGetProperty("Version", out var version) || version.GetInt32() != ServiceIpc.ProtocolVersion ||
            !root.TryGetProperty("Kind", out var kindElement) || !root.TryGetProperty("Payload", out var payload))
        {
            await ReplyAsync(pipe, false, "Invalid envelope", false, cancellationToken);
            return;
        }
        var kind = kindElement.GetString() ?? "";
        switch (kind)
        {
            case "Ping": await ReplyAsync(pipe, true, "", true, cancellationToken); break;
            case "GetSettings":
                await ReplyAsync(pipe, true, "", new SettingsStore().LoadLocal(), cancellationToken);
                break;
            case "SaveSettings":
                var settings = payload.Deserialize<KeyboardSettings>();
                if (settings is null) await ReplyAsync(pipe, false, "Invalid settings", false, cancellationToken);
                else
                {
                    new SettingsStore().SaveLocal(settings);
                    await ReplyAsync(pipe, true, "", true, cancellationToken);
                }
                break;
            case "ForegroundState": SaveJson(AppPaths.ForegroundAppStatePath, payload); await ReplyAsync(pipe, true, "", true, cancellationToken); break;
            case "TypingPulse": SaveJson(AppPaths.TypingPulseStatePath, payload); await ReplyAsync(pipe, true, "", true, cancellationToken); break;
            case "NotificationFlash": SaveJson(AppPaths.NotificationFlashStatePath, payload); await ReplyAsync(pipe, true, "", true, cancellationToken); break;
            case "AudioApplications": SaveJson(AppPaths.AudioApplicationsStatePath, payload); await ReplyAsync(pipe, true, "", true, cancellationToken); break;
            case "MediaPlayback": SaveJson(AppPaths.MediaPlaybackStatePath, payload); await ReplyAsync(pipe, true, "", true, cancellationToken); break;
            default: await ReplyAsync(pipe, false, "Unsupported message kind", false, cancellationToken); break;
        }
    }

    private static void SaveJson(string destination, JsonElement payload)
    {
        Directory.CreateDirectory(AppPaths.ProgramDataDirectory);
        var temp = destination + ".ipc.tmp";
        File.WriteAllText(temp, payload.GetRawText());
        if (File.Exists(destination)) File.Replace(temp, destination, null);
        else File.Move(temp, destination);
    }

    private static async Task ReplyAsync<T>(Stream stream, bool success, string error, T payload, CancellationToken token)
    {
        var bytes = JsonSerializer.SerializeToUtf8Bytes(new IpcReply<T>(success, error, payload));
        var prefix = BitConverter.GetBytes(bytes.Length);
        await stream.WriteAsync(prefix, token);
        await stream.WriteAsync(bytes, token);
        await stream.FlushAsync(token);
    }

    private static bool IsInteractiveLocalClient(NamedPipeServerStream pipe)
    {
        if (!GetNamedPipeClientProcessId(pipe.SafePipeHandle.DangerousGetHandle(), out var pid) || pid == 0) return false;
        try
        {
            using var process = Process.GetProcessById((int)pid);
            var activeSession = WTSGetActiveConsoleSessionId();
            return process.SessionId > 0 && activeSession != uint.MaxValue && process.SessionId == (int)activeSession;
        }
        catch { return false; }
    }

    public void Dispose()
    {
        _stop.Cancel();
        try { _loop?.Wait(TimeSpan.FromSeconds(2)); } catch { }
        _stop.Dispose();
    }

    [DllImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool GetNamedPipeClientProcessId(IntPtr pipe, out uint clientProcessId);

    [DllImport("kernel32.dll")]
    private static extern uint WTSGetActiveConsoleSessionId();
}
