using System.Text.Json;
using ColorfulLedKeyboard.Core;

namespace ColorfulLedKeyboard.Tests;

public class AudioSourceStatusFileTests : IDisposable
{
    private readonly string _tempPath;

    public AudioSourceStatusFileTests()
    {
        _tempPath = Path.Combine(Path.GetTempPath(), $"audio-status-test-{Guid.NewGuid():N}.json");
    }

    public void Dispose()
    {
        if (File.Exists(_tempPath)) File.Delete(_tempPath);
        var tmp = _tempPath + ".tmp";
        if (File.Exists(tmp)) File.Delete(tmp);
    }

    [Fact]
    public void WriteThenRead_RoundTripsAllFields()
    {
        var info = new AudioSourceStatusInfo
        {
            Status = AudioSourceStatus.Active,
            DeviceFriendlyName = "iQOO TWS Air3",
            DeviceId = "{0.0.0.00000000}.{abcd}",
            UpdatedAt = DateTimeOffset.Parse("2026-06-14T10:00:00Z"),
        };
        AudioSourceStatusFile.WriteTo(_tempPath, info);

        var loaded = AudioSourceStatusFile.ReadFrom(_tempPath);

        Assert.NotNull(loaded);
        Assert.Equal(AudioSourceStatus.Active, loaded!.Status);
        Assert.Equal("iQOO TWS Air3", loaded.DeviceFriendlyName);
        Assert.Equal("{0.0.0.00000000}.{abcd}", loaded.DeviceId);
        Assert.Equal(info.UpdatedAt, loaded.UpdatedAt);
    }

    [Fact]
    public void Read_NonExistentFile_ReturnsNull()
    {
        var loaded = AudioSourceStatusFile.ReadFrom(_tempPath);
        Assert.Null(loaded);
    }

    [Fact]
    public void Read_CorruptJson_ReturnsNull()
    {
        File.WriteAllText(_tempPath, "{this is not valid json");
        var loaded = AudioSourceStatusFile.ReadFrom(_tempPath);
        Assert.Null(loaded);
    }

    [Fact]
    public void Write_SerializesStatusAsString()
    {
        var info = new AudioSourceStatusInfo { Status = AudioSourceStatus.Hfp };
        AudioSourceStatusFile.WriteTo(_tempPath, info);

        var raw = File.ReadAllText(_tempPath);
        Assert.Contains("\"Hfp\"", raw);
        Assert.DoesNotContain("\"Status\":1", raw);
    }

    [Fact]
    public void ConcurrentWriteRead_NeverYieldsHalfWrittenState()
    {
        var writer = Task.Run(() =>
        {
            for (var i = 0; i < 50; i++)
            {
                AudioSourceStatusFile.WriteTo(_tempPath, new AudioSourceStatusInfo
                {
                    Status = AudioSourceStatus.Active,
                    DeviceFriendlyName = $"Device-{i}",
                });
            }
        });

        var failures = 0;
        var reader = Task.Run(() =>
        {
            for (var i = 0; i < 200; i++)
            {
                var info = AudioSourceStatusFile.ReadFrom(_tempPath);
                if (info != null && !info.DeviceFriendlyName.StartsWith("Device-"))
                {
                    Interlocked.Increment(ref failures);
                }
            }
        });

        Task.WaitAll(writer, reader);
        Assert.Equal(0, failures);
    }
}
