using ColorfulLedKeyboard.Installer;
using System.Text;

namespace ColorfulLedKeyboard.Tests;

public sealed class InstallerSettingsIpcTests
{
    [Fact]
    public void SuccessfulServiceReplyExtractsSettingsPayload()
    {
        var reply = Encoding.UTF8.GetBytes(
            "{\"Success\":true,\"Error\":\"\",\"Payload\":{\"Brightness\":70,\"Automation\":{\"Version\":2}}}");

        var success = ColorfulLedKeyboard.Installer.Program.TryExtractSettingsPayload(reply, out var settings);

        Assert.True(success);
        Assert.Contains("\"Brightness\":70", Encoding.UTF8.GetString(settings), StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("{\"Success\":false,\"Payload\":false}")]
    [InlineData("{\"Success\":true,\"Payload\":false}")]
    [InlineData("not-json")]
    public void InvalidServiceReplyDoesNotProduceSnapshot(string reply)
    {
        Assert.False(ColorfulLedKeyboard.Installer.Program.TryExtractSettingsPayload(
            Encoding.UTF8.GetBytes(reply), out var settings));
        Assert.Empty(settings);
    }
}
