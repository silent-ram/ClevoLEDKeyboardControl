using System.IO.Pipes;
using System.Text;
using System.Text.Json;

namespace ColorfulLedKeyboard.Core;

public static class ServiceIpc
{
    public const string PipeName = "ClevoLEDKeyboardControl.v2";
    public const int ProtocolVersion = 1;
    public const int MaximumMessageBytes = 1024 * 1024;

    public static bool TryRequest<TRequest, TResponse>(string kind, TRequest payload, out TResponse? response, int timeoutMs = 750)
    {
        response = default;
        try
        {
            using var pipe = new NamedPipeClientStream(".", PipeName, PipeDirection.InOut, PipeOptions.None);
            pipe.Connect(timeoutMs);
            var request = JsonSerializer.SerializeToUtf8Bytes(new IpcEnvelope<TRequest>(ProtocolVersion, kind, payload));
            if (request.Length > MaximumMessageBytes) return false;
            using var writer = new BinaryWriter(pipe, Encoding.UTF8, leaveOpen: true);
            using var reader = new BinaryReader(pipe, Encoding.UTF8, leaveOpen: true);
            writer.Write(request.Length);
            writer.Write(request);
            writer.Flush();
            var length = reader.ReadInt32();
            if (length <= 0 || length > MaximumMessageBytes) return false;
            var bytes = reader.ReadBytes(length);
            return TryParseReply(bytes, out response);
        }
        catch (Exception ex) when (ex is IOException or TimeoutException or UnauthorizedAccessException or JsonException)
        {
            return false;
        }
    }

    internal static bool TryParseReply<TResponse>(ReadOnlyMemory<byte> bytes, out TResponse? response)
    {
        response = default;
        try
        {
            using var document = JsonDocument.Parse(bytes);
            var root = document.RootElement;
            if (!root.TryGetProperty("Success", out var success) || success.ValueKind != JsonValueKind.True)
                return false;
            if (!root.TryGetProperty("Payload", out var payload)) return false;
            response = payload.Deserialize<TResponse>();
            return response is not null || default(TResponse) is null;
        }
        catch (JsonException)
        {
            return false;
        }
    }

    public static bool TrySend<T>(string kind, T payload, int timeoutMs = 750) =>
        TryRequest<T, bool>(kind, payload, out _, timeoutMs);

    public static bool TryRequestWithRetry<TRequest, TResponse>(string kind, TRequest payload,
        out TResponse? response, int attempts = 3, int timeoutMs = 750, int delayMs = 150)
    {
        response = default;
        for (var attempt = 0; attempt < Math.Max(1, attempts); attempt++)
        {
            if (TryRequest(kind, payload, out response, timeoutMs)) return true;
            if (attempt + 1 < attempts) Thread.Sleep(Math.Max(0, delayMs));
        }
        return false;
    }

    public static bool TrySendWithRetry<T>(string kind, T payload, int attempts = 3,
        int timeoutMs = 750, int delayMs = 150) =>
        TryRequestWithRetry<T, bool>(kind, payload, out _, attempts, timeoutMs, delayMs);

    public static bool IsAvailable(int timeoutMs = 250) => TrySend("Ping", true, timeoutMs);
}

public sealed record IpcEnvelope<T>(int Version, string Kind, T Payload);
public sealed record IpcReply<T>(bool Success, string Error, T? Payload);
