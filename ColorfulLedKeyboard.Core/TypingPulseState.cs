using System.Text.Json;

namespace ColorfulLedKeyboard.Core;

public sealed class TypingPulseState
{
    public DateTimeOffset LastKeyUtc { get; set; } = DateTimeOffset.UtcNow;

    public static TypingPulseState? Load()
    {
        try
        {
            if (!File.Exists(AppPaths.TypingPulseStatePath))
            {
                return null;
            }

            var json = File.ReadAllText(AppPaths.TypingPulseStatePath);
            return JsonSerializer.Deserialize<TypingPulseState>(json);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or JsonException)
        {
            return null;
        }
    }

    public static void Save(DateTimeOffset timestamp)
    {
        var state = new TypingPulseState { LastKeyUtc = timestamp };
        if (Environment.UserInteractive) { ServiceIpc.TrySend("TypingPulse", state); return; }
        try
        {
            Directory.CreateDirectory(AppPaths.ProgramDataDirectory);
            File.WriteAllText(AppPaths.TypingPulseStatePath, JsonSerializer.Serialize(state));
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
        }
    }
}
