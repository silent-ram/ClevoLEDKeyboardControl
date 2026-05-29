using ColorfulLedKeyboard.Core;
using System.Diagnostics;
using System.Net.Http.Headers;
using System.Reflection;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace ColorfulLedKeyboard.Tray;

public sealed class UpdateChecker
{
    public const string ReleasesUrl = "https://github.com/xuha233/ClevoRGBControl/releases";
    private const string LatestReleaseApiUrl = "https://api.github.com/repos/xuha233/ClevoRGBControl/releases/latest";
    private static readonly JsonSerializerOptions JsonOptions = new() { PropertyNameCaseInsensitive = true };

    public Version CurrentVersion { get; } = Assembly.GetExecutingAssembly().GetName().Version ?? new Version(0, 0, 0);

    public async Task<UpdateCheckResult> CheckAsync(bool force, UpdateCheckInterval interval, CancellationToken cancellationToken = default)
    {
        if (!force && !ShouldRunAutomaticCheck(interval))
        {
            return UpdateCheckResult.Skipped(CurrentVersion);
        }

        using var client = new HttpClient { Timeout = TimeSpan.FromSeconds(10) };
        client.DefaultRequestHeaders.UserAgent.Add(new ProductInfoHeaderValue("ClevoRGBControl", CurrentVersion.ToString(3)));
        client.DefaultRequestHeaders.Accept.Add(new MediaTypeWithQualityHeaderValue("application/vnd.github+json"));

        using var response = await client.GetAsync(LatestReleaseApiUrl, cancellationToken);
        response.EnsureSuccessStatusCode();

        await using var stream = await response.Content.ReadAsStreamAsync(cancellationToken);
        var release = await JsonSerializer.DeserializeAsync<GitHubRelease>(stream, JsonOptions, cancellationToken)
            ?? throw new InvalidOperationException("GitHub 返回的发布信息为空。");

        var latestVersion = ParseVersion(release.TagName);
        SaveState(new UpdateCheckState { LastCheckedUtc = DateTimeOffset.UtcNow });

        return latestVersion.CompareTo(CurrentVersion) > 0
            ? UpdateCheckResult.Available(CurrentVersion, latestVersion, release.HtmlUrl ?? ReleasesUrl)
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

    private static UpdateCheckState? LoadState()
    {
        try
        {
            if (!File.Exists(AppPaths.UpdateStatePath))
            {
                return null;
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
            Directory.CreateDirectory(AppPaths.ProgramDataDirectory);
            File.WriteAllText(AppPaths.UpdateStatePath, JsonSerializer.Serialize(state, JsonOptions));
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
        }
    }

    private sealed class GitHubRelease
    {
        [JsonPropertyName("tag_name")]
        public string? TagName { get; set; }

        [JsonPropertyName("html_url")]
        public string? HtmlUrl { get; set; }
    }

    private sealed class UpdateCheckState
    {
        public DateTimeOffset? LastCheckedUtc { get; set; }
    }
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
