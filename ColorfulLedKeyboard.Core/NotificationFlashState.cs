using System.Text.Json;

namespace ColorfulLedKeyboard.Core;

public sealed class NotificationFlashState
{
    public DateTimeOffset TriggeredUtc { get; set; } = DateTimeOffset.UtcNow;

    public static NotificationFlashState? Load()
    {
        try
        {
            if (!File.Exists(AppPaths.NotificationFlashStatePath))
            {
                return null;
            }

            return JsonSerializer.Deserialize<NotificationFlashState>(File.ReadAllText(AppPaths.NotificationFlashStatePath));
        }
        catch
        {
            return null;
        }
    }

    public static void Save()
    {
        var state = new NotificationFlashState();
        if (Environment.UserInteractive) { ServiceIpc.TrySend("NotificationFlash", state); return; }
        Directory.CreateDirectory(AppPaths.ProgramDataDirectory);
        File.WriteAllText(AppPaths.NotificationFlashStatePath, JsonSerializer.Serialize(state));
    }
}
