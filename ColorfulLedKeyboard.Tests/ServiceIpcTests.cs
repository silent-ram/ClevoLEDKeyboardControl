using ColorfulLedKeyboard.Core;
using System.Text;

namespace ColorfulLedKeyboard.Tests;

public sealed class ServiceIpcTests
{
    [Fact]
    public void UnsupportedOldServiceReplyDoesNotDeserializeMismatchedPayload()
    {
        var json = "{\"Success\":false,\"Error\":\"Unsupported message kind\",\"Payload\":false}";

        var success = ServiceIpc.TryParseReply<AutomationStatus>(Encoding.UTF8.GetBytes(json), out var response);

        Assert.False(success);
        Assert.Null(response);
    }

    [Fact]
    public void SuccessfulReplyDeserializesPayloadAfterCheckingStatus()
    {
        var json = "{\"Success\":true,\"Error\":\"\",\"Payload\":{\"ActiveRuleName\":\"Music\"}}";

        var success = ServiceIpc.TryParseReply<AutomationStatus>(Encoding.UTF8.GetBytes(json), out var response);

        Assert.True(success);
        Assert.Equal("Music", response?.ActiveRuleName);
    }

    [Theory]
    [InlineData("not-json")]
    [InlineData("{\"Success\":true}")]
    [InlineData("{\"Success\":true,\"Payload\":false}")]
    public void InvalidSuccessfulReplyReturnsFalse(string json)
    {
        Assert.False(ServiceIpc.TryParseReply<AutomationStatus>(Encoding.UTF8.GetBytes(json), out _));
    }
}
