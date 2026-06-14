using System.Text.Json;
using System.Text.Json.Serialization;

namespace ColorfulLedKeyboard.Core;

/// <summary>状态文件原子读写。Service 写、Tray 读。
/// 写入流程：写 .tmp → File.Move(overwrite=true)，避免读到半写状态。</summary>
public static class AudioSourceStatusFile
{
    private static readonly JsonSerializerOptions Options = new()
    {
        WriteIndented = false,
        Converters = { new JsonStringEnumConverter() },
    };

    public static string Path => AppPaths.AudioSourceStatusPath;

    public static void Write(AudioSourceStatusInfo info) => WriteTo(Path, info);

    public static AudioSourceStatusInfo? Read() => ReadFrom(Path);

    public static void WriteTo(string path, AudioSourceStatusInfo info)
    {
        var directory = System.IO.Path.GetDirectoryName(path);
        if (!string.IsNullOrEmpty(directory))
        {
            try { Directory.CreateDirectory(directory); }
            catch (IOException) { return; }
            catch (UnauthorizedAccessException) { return; }
        }

        var tmp = path + ".tmp";
        try
        {
            var json = JsonSerializer.Serialize(info, Options);
            File.WriteAllText(tmp, json);
            File.Move(tmp, path, overwrite: true);
        }
        catch (IOException)
        {
            TryDelete(tmp);
        }
        catch (UnauthorizedAccessException)
        {
            TryDelete(tmp);
        }
    }

    public static AudioSourceStatusInfo? ReadFrom(string path)
    {
        try
        {
            if (!File.Exists(path)) return null;
            var json = File.ReadAllText(path);
            return JsonSerializer.Deserialize<AudioSourceStatusInfo>(json, Options);
        }
        catch (IOException) { return null; }
        catch (JsonException) { return null; }
        catch (UnauthorizedAccessException) { return null; }
    }

    private static void TryDelete(string path)
    {
        try { if (File.Exists(path)) File.Delete(path); }
        catch { /* swallow */ }
    }
}
