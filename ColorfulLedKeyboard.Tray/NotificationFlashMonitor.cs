using ColorfulLedKeyboard.Core;
using Windows.UI.Notifications.Management;

namespace ColorfulLedKeyboard.Tray;

internal sealed class NotificationFlashMonitor : IDisposable
{
    private readonly SettingsStore _settingsStore;
    private readonly System.Windows.Forms.Timer _timer = new() { Interval = 3000 };
    private readonly HashSet<uint> _seen = [];
    private DateTimeOffset _lastFlash = DateTimeOffset.MinValue;
    private bool _permissionRequested;
    private bool _initialized;

    public NotificationFlashMonitor(SettingsStore settingsStore)
    {
        _settingsStore = settingsStore;
        _timer.Tick += async (_, _) => await PollAsync();
    }

    public void SetEnabled(bool enabled)
    {
        if (enabled)
        {
            _timer.Start();
            _ = PollAsync();
            return;
        }

        _timer.Stop();
        _seen.Clear();
        _initialized = false;
    }

    public void Dispose()
    {
        _timer.Stop();
        _timer.Dispose();
    }

    private async Task PollAsync()
    {
        var settings = _settingsStore.Load();
        var flash = settings.NotificationFlash.Normalize();
        var requiredByRule = settings.Automation.MusicApplications.Any(rule => rule.NotificationPolicy == EventPolicy.Enabled) ||
            settings.Automation.LightingApplications.Any(rule => rule.NotificationPolicy == EventPolicy.Enabled);
        if (!flash.Enabled && !requiredByRule)
        {
            return;
        }

        try
        {
            var listener = UserNotificationListener.Current;
            var access = listener.GetAccessStatus();
            if (access == UserNotificationListenerAccessStatus.Unspecified && !_permissionRequested)
            {
                _permissionRequested = true;
                access = await listener.RequestAccessAsync();
            }

            if (access != UserNotificationListenerAccessStatus.Allowed)
            {
                return;
            }

            var notifications = await listener.GetNotificationsAsync(Windows.UI.Notifications.NotificationKinds.Toast);
            if (!_initialized)
            {
                foreach (var item in notifications)
                {
                    _seen.Add(item.Id);
                }

                _initialized = true;
                return;
            }

            var newest = notifications
                .Where(item => item.CreationTime > DateTimeOffset.UtcNow.AddMinutes(-2))
                .OrderByDescending(item => item.CreationTime)
                .FirstOrDefault(item => !_seen.Contains(item.Id));

            foreach (var item in notifications)
            {
                _seen.Add(item.Id);
            }

            if (newest is null || DateTimeOffset.UtcNow - _lastFlash < TimeSpan.FromSeconds(flash.CooldownSeconds))
            {
                return;
            }

            NotificationFlashState.Save();
            _lastFlash = DateTimeOffset.UtcNow;
        }
        catch
        {
        }
    }
}
