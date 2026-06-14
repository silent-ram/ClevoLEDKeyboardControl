namespace ColorfulLedKeyboard.Core;

public static class AppPaths
{
    public const string ServiceName = "ClevoLEDKeyboardControlService";
    public const string DisplayName = "ClevoLEDKeyboardControl Service";
    public const string ProgramDataFolderName = "ClevoLEDKeyboardControl";
    public const string SettingsFileName = "settings.json";
    public const string UpdateStateFileName = "update-check.json";
    public const string ForegroundAppStateFileName = "foreground-app.json";
    public const string TypingPulseStateFileName = "typing-pulse.json";
    public const string NotificationFlashStateFileName = "notification-flash.json";
    public const string SpotifyAlbumColorStateFileName = "spotify-album-color.json";
    public const string DriverComponentStateFileName = "driver-component.json";
    public const string AudioSourceStatusFileName = "audio-source-status.json";

    public static string ProgramDataDirectory =>
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData), ProgramDataFolderName);

    public static string SettingsPath => Path.Combine(ProgramDataDirectory, SettingsFileName);

    public static string UpdateStatePath => Path.Combine(ProgramDataDirectory, UpdateStateFileName);

    public static string ForegroundAppStatePath => Path.Combine(ProgramDataDirectory, ForegroundAppStateFileName);

    public static string TypingPulseStatePath => Path.Combine(ProgramDataDirectory, TypingPulseStateFileName);

    public static string NotificationFlashStatePath => Path.Combine(ProgramDataDirectory, NotificationFlashStateFileName);

    public static string SpotifyAlbumColorStatePath => Path.Combine(ProgramDataDirectory, SpotifyAlbumColorStateFileName);

    public static string DriverComponentStatePath => Path.Combine(ProgramDataDirectory, DriverComponentStateFileName);

    public static string AudioSourceStatusPath => Path.Combine(ProgramDataDirectory, AudioSourceStatusFileName);
}
