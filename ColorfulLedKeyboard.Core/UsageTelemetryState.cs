using System.Text.Json;

namespace ColorfulLedKeyboard.Core;

/// <summary>
/// 本机匿名上报状态。这里只保存随机安装 ID 和去重信息，不包含设备或用户身份信息。
/// </summary>
public sealed class UsageTelemetryState
{
    private static readonly object Sync = new();

    public string InstallId { get; set; } = "";

    public bool InstallSent { get; set; }

    public string LastHeartbeatDate { get; set; } = "";

    public string LastVersionSent { get; set; } = "";

    public UsageTelemetryState Normalize()
    {
        InstallId = Guid.TryParse(InstallId, out var id)
            ? id.ToString("D")
            : "";
        LastHeartbeatDate = NormalizeDate(LastHeartbeatDate);
        LastVersionSent = (LastVersionSent ?? "").Trim();
        return this;
    }

    public void EnsureInstallId()
    {
        if (!Guid.TryParse(InstallId, out var id))
        {
            InstallId = Guid.NewGuid().ToString("D");
        }
        else
        {
            InstallId = id.ToString("D");
        }
    }

    public static UsageTelemetryState Load(string? path = null)
    {
        path ??= AppPaths.UsageTelemetryStatePath;
        lock (Sync)
        {
            try
            {
                if (!File.Exists(path)) return new UsageTelemetryState();
                return (JsonSerializer.Deserialize<UsageTelemetryState>(File.ReadAllText(path))
                    ?? new UsageTelemetryState()).Normalize();
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or JsonException)
            {
                return new UsageTelemetryState();
            }
        }
    }

    public void Save(string? path = null)
    {
        path ??= AppPaths.UsageTelemetryStatePath;
        lock (Sync)
        {
            var normalized = Normalize();
            var directory = Path.GetDirectoryName(path);
            if (string.IsNullOrWhiteSpace(directory)) return;

            try
            {
                Directory.CreateDirectory(directory);
                var temporary = $"{path}.{Environment.ProcessId}.{Guid.NewGuid():N}.tmp";
                File.WriteAllText(temporary, JsonSerializer.Serialize(normalized, new JsonSerializerOptions { WriteIndented = true }));
                File.Move(temporary, path, overwrite: true);
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                // 统计状态写入失败不应影响灯效或设置。
            }
        }
    }

    private static string NormalizeDate(string? value)
    {
        value = (value ?? "").Trim();
        return DateOnly.TryParseExact(value, "yyyy-MM-dd", out var date)
            ? date.ToString("yyyy-MM-dd")
            : "";
    }
}
