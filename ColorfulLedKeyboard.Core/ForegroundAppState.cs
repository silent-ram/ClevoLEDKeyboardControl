using System.Text.Json;

namespace ColorfulLedKeyboard.Core;

public sealed class ForegroundAppState
{
    public string ProcessName { get; set; } = "";

    public DateTimeOffset UpdatedUtc { get; set; } = DateTimeOffset.UtcNow;

    public static ForegroundAppState? Load()
    {
        try
        {
            if (!File.Exists(AppPaths.ForegroundAppStatePath))
            {
                return null;
            }

            var json = File.ReadAllText(AppPaths.ForegroundAppStatePath);
            return JsonSerializer.Deserialize<ForegroundAppState>(json);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or JsonException)
        {
            return null;
        }
    }

    public static void Save(string processName)
    {
        try
        {
            Directory.CreateDirectory(AppPaths.ProgramDataDirectory);
            var state = new ForegroundAppState
            {
                ProcessName = AppProfileRule.NormalizeProcessName(processName),
                UpdatedUtc = DateTimeOffset.UtcNow
            };
            File.WriteAllText(AppPaths.ForegroundAppStatePath, JsonSerializer.Serialize(state));
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
        }
    }
}
