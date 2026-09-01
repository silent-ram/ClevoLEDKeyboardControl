namespace ColorfulLedKeyboard.Tray;

using ColorfulLedKeyboard.Core;

static class Program
{
    private const string SingleInstanceMutexName = "Local\\ClevoLEDKeyboardControl.Tray";
    private const string OpenSettingsEventName = "Local\\ClevoLEDKeyboardControl.OpenSettings";
    private static Mutex? _singleInstanceMutex;
    private static EventWaitHandle? _openSettingsEvent;

    [STAThread]
    static void Main(string[] args)
    {
        _openSettingsEvent = new EventWaitHandle(false, EventResetMode.AutoReset, OpenSettingsEventName);
        _singleInstanceMutex = new Mutex(initiallyOwned: true, SingleInstanceMutexName, out var createdNew);
        if (!createdNew)
        {
            _openSettingsEvent.Set();
            _openSettingsEvent.Dispose();
            _singleInstanceMutex.Dispose();
            return;
        }

        Application.ApplicationExit += (_, _) =>
        {
            _openSettingsEvent.Dispose();
            _singleInstanceMutex.Dispose();
        };

        ApplicationConfiguration.Initialize();
        ThemeManager.Initialize(UiStateStore.Shared.Load().Theme);
        var openSettingsOnStartup = args.Any(arg =>
            string.Equals(arg, "--settings", StringComparison.OrdinalIgnoreCase) ||
            string.Equals(arg, "/settings", StringComparison.OrdinalIgnoreCase));

        Application.Run(new TrayApplicationContext(new SettingsStore(), openSettingsOnStartup, _openSettingsEvent));
    }    
}
