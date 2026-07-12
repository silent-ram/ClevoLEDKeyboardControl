using ColorfulLedKeyboard.Core;
using System.Diagnostics;
using System.Net.Http.Headers;
using System.Reflection;
using System.Text.Json;

namespace ColorfulLedKeyboard.Tray;

public sealed class UpdateChecker
{
    private readonly IUpdatePackageVerifier _packageVerifier;
    public const string ReleasesUrl = "https://github.com/silent-ram/ClevoLEDKeyboardControl/releases";
    private const string LatestReleaseUrl = "https://github.com/silent-ram/ClevoLEDKeyboardControl/releases/latest";
    private static readonly JsonSerializerOptions JsonOptions = new() { PropertyNameCaseInsensitive = true };

    public Version CurrentVersion { get; } = Assembly.GetExecutingAssembly().GetName().Version ?? new Version(0, 0, 0);
    public bool CanInstallAutomatically => _packageVerifier.CanInstallAutomatically;

    public UpdateChecker(IUpdatePackageVerifier? packageVerifier = null) =>
        _packageVerifier = packageVerifier ?? new ManualReleaseVerifier();

    public async Task<UpdateCheckResult> CheckAsync(bool force, UpdateCheckInterval interval, CancellationToken cancellationToken = default)
    {
        if (!force && !ShouldRunAutomaticCheck(interval))
        {
            return UpdateCheckResult.Skipped(CurrentVersion);
        }

        using var client = new HttpClient { Timeout = TimeSpan.FromSeconds(10) };
        client.DefaultRequestHeaders.UserAgent.Add(new ProductInfoHeaderValue("ClevoLEDKeyboardControl", CurrentVersion.ToString(3)));
        client.DefaultRequestHeaders.Accept.Add(new MediaTypeWithQualityHeaderValue("text/html"));

        // releases/latest 通过普通网页 302 跳转到 /releases/tag/vX.Y.Z，
        // 不消耗 GitHub REST API 的匿名限额，避免多个用户共享公网 IP 时出现 403。
        using var response = await client.GetAsync(LatestReleaseUrl, HttpCompletionOption.ResponseHeadersRead, cancellationToken);
        response.EnsureSuccessStatusCode();
        var releaseUrl = response.RequestMessage?.RequestUri?.ToString() ?? LatestReleaseUrl;
        var tagName = TryGetTagFromReleaseUrl(releaseUrl);
        var latestVersion = ParseVersion(tagName);
        if (latestVersion == new Version(0, 0, 0))
            throw new InvalidOperationException("无法从 GitHub 最新发布地址识别版本号。");
        var state = LoadState() ?? new UpdateCheckState();
        state.LastCheckedUtc = DateTimeOffset.UtcNow;
        state.LastAvailableVersion = latestVersion.CompareTo(CurrentVersion) > 0 ? latestVersion.ToString(3) : null;
        state.LastReleaseUrl = latestVersion.CompareTo(CurrentVersion) > 0 ? releaseUrl : null;
        SaveState(state);

        return latestVersion.CompareTo(CurrentVersion) > 0
            ? UpdateCheckResult.Available(CurrentVersion, latestVersion, releaseUrl)
            : UpdateCheckResult.UpToDate(CurrentVersion, latestVersion);
    }

    public static void OpenReleases()
    {
        OpenUrl(ReleasesUrl);
    }

    public static void OpenUrl(string url)
    {
        Process.Start(new ProcessStartInfo(url) { UseShellExecute = true });
    }

    private static Version ParseVersion(string? tagName)
    {
        var value = (tagName ?? string.Empty).Trim();
        if (value.StartsWith('v') || value.StartsWith('V'))
        {
            value = value[1..];
        }

        return Version.TryParse(value, out var version)
            ? version
            : new Version(0, 0, 0);
    }

    private static bool ShouldRunAutomaticCheck(UpdateCheckInterval interval)
    {
        if (interval == UpdateCheckInterval.Never)
        {
            return false;
        }

        var state = LoadState();
        if (state?.LastCheckedUtc is null)
        {
            return true;
        }

        return DateTimeOffset.UtcNow - state.LastCheckedUtc.Value >= ToTimeSpan(interval);
    }

    private static TimeSpan ToTimeSpan(UpdateCheckInterval interval) => interval switch
    {
        UpdateCheckInterval.Weekly => TimeSpan.FromDays(7),
        UpdateCheckInterval.Monthly => TimeSpan.FromDays(30),
        _ => TimeSpan.FromDays(1)
    };

    public static DateTimeOffset? LoadLastCheckedUtc()
    {
        return LoadState()?.LastCheckedUtc;
    }

    private static string TryGetTagFromReleaseUrl(string url)
    {
        if (!Uri.TryCreate(url, UriKind.Absolute, out var uri)) return "";
        var segments = uri.AbsolutePath.Split('/', StringSplitOptions.RemoveEmptyEntries);
        var tagIndex = Array.FindIndex(segments, segment => string.Equals(segment, "tag", StringComparison.OrdinalIgnoreCase));
        return tagIndex >= 0 && tagIndex + 1 < segments.Length
            ? Uri.UnescapeDataString(segments[tagIndex + 1])
            : "";
    }

    public UpdateCheckResult? LoadKnownAvailable()
    {
        var state = LoadState();
        return Version.TryParse(state?.LastAvailableVersion, out var version) && version.CompareTo(CurrentVersion) > 0
            ? UpdateCheckResult.Available(CurrentVersion, version, state?.LastReleaseUrl ?? ReleasesUrl)
            : null;
    }

    public static bool ShouldPrompt(Version version)
    {
        var state = LoadState();
        return !string.Equals(state?.LastPromptedVersion, version.ToString(3), StringComparison.OrdinalIgnoreCase);
    }

    public static void MarkPrompted(Version version)
    {
        var state = LoadState() ?? new UpdateCheckState();
        state.LastPromptedVersion = version.ToString(3);
        SaveState(state);
    }

    private static UpdateCheckState? LoadState()
    {
        try
        {
            if (!File.Exists(AppPaths.UpdateStatePath))
            {
                var legacyPath = Path.Combine(AppPaths.ProgramDataDirectory, AppPaths.UpdateStateFileName);
                if (!File.Exists(legacyPath)) return null;
                Directory.CreateDirectory(AppPaths.UserDataDirectory);
                File.Copy(legacyPath, AppPaths.UpdateStatePath, overwrite: false);
            }

            var json = File.ReadAllText(AppPaths.UpdateStatePath);
            return JsonSerializer.Deserialize<UpdateCheckState>(json, JsonOptions);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or JsonException)
        {
            return null;
        }
    }

    private static void SaveState(UpdateCheckState state)
    {
        try
        {
            Directory.CreateDirectory(AppPaths.UserDataDirectory);
            File.WriteAllText(AppPaths.UpdateStatePath, JsonSerializer.Serialize(state, JsonOptions));
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
        }
    }

    private sealed class UpdateCheckState
    {
        public DateTimeOffset? LastCheckedUtc { get; set; }
        public string? LastAvailableVersion { get; set; }
        public string? LastReleaseUrl { get; set; }
        public string? LastPromptedVersion { get; set; }
    }
}

public interface IUpdatePackageVerifier
{
    bool CanInstallAutomatically { get; }
    ValueTask<bool> VerifyAsync(Stream package, string? expectedDigest, CancellationToken cancellationToken = default);
}

public sealed class ManualReleaseVerifier : IUpdatePackageVerifier
{
    public bool CanInstallAutomatically => false;
    public ValueTask<bool> VerifyAsync(Stream package, string? expectedDigest, CancellationToken cancellationToken = default) =>
        ValueTask.FromResult(false);
}

public sealed record UpdateCheckResult(
    UpdateCheckStatus Status,
    Version CurrentVersion,
    Version? LatestVersion,
    string? ReleaseUrl)
{
    public static UpdateCheckResult Skipped(Version currentVersion) =>
        new(UpdateCheckStatus.Skipped, currentVersion, null, null);

    public static UpdateCheckResult UpToDate(Version currentVersion, Version latestVersion) =>
        new(UpdateCheckStatus.UpToDate, currentVersion, latestVersion, null);

    public static UpdateCheckResult Available(Version currentVersion, Version latestVersion, string releaseUrl) =>
        new(UpdateCheckStatus.Available, currentVersion, latestVersion, releaseUrl);
}

public enum UpdateCheckStatus
{
    Skipped,
    UpToDate,
    Available
}
