namespace ColorfulLedKeyboard.Core;

public static class AppPaths
{
    public const string ServiceName = "ClevoRGBControlService";
    public const string DisplayName = "ClevoRGBControl Service";
    public const string ProgramDataFolderName = "ClevoRGBControl";
    public const string SettingsFileName = "settings.json";
    public const string UpdateStateFileName = "update-check.json";
    public const string ForegroundAppStateFileName = "foreground-app.json";
    public const string TypingPulseStateFileName = "typing-pulse.json";

    public static string ProgramDataDirectory =>
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData), ProgramDataFolderName);

    public static string SettingsPath => Path.Combine(ProgramDataDirectory, SettingsFileName);

    public static string UpdateStatePath => Path.Combine(ProgramDataDirectory, UpdateStateFileName);

    public static string ForegroundAppStatePath => Path.Combine(ProgramDataDirectory, ForegroundAppStateFileName);

    public static string TypingPulseStatePath => Path.Combine(ProgramDataDirectory, TypingPulseStateFileName);
}
