namespace ColorfulLedKeyboard.Core;

public sealed class UpdateSettings
{
    public UpdateCheckInterval CheckInterval { get; set; } = UpdateCheckInterval.Daily;

    public UpdateSettings Normalize()
    {
        if (!Enum.IsDefined(CheckInterval))
        {
            CheckInterval = UpdateCheckInterval.Daily;
        }

        return this;
    }
}

public enum UpdateCheckInterval
{
    Never = 0,
    Daily = 1,
    Weekly = 2,
    Monthly = 3
}
