using ColorfulLedKeyboard.Core;
using System.Diagnostics;
using System.Net;
using System.Net.Http.Headers;
using System.Reflection;
using System.Text.Json;
using System.Xml;
using System.Xml.Linq;

namespace ColorfulLedKeyboard.Tray;

public sealed class UpdateChecker
{
    private readonly IUpdatePackageVerifier _packageVerifier;
    private readonly Func<HttpMessageHandler> _httpHandlerFactory;
    private readonly TimeSpan _requestTimeout;
    private readonly Func<TimeSpan, CancellationToken, Task> _delayAsync;
    private readonly Version _currentVersion;
    public const string ReleasesUrl = "https://github.com/silent-ram/ClevoLEDKeyboardControl/releases";
    private const string LatestReleaseUrl = "https://github.com/silent-ram/ClevoLEDKeyboardControl/releases/latest";
    private const string ReleasesFeedUrl = "https://github.com/silent-ram/ClevoLEDKeyboardControl/releases.atom";
    private static readonly JsonSerializerOptions JsonOptions = new() { PropertyNameCaseInsensitive = true };

    public Version CurrentVersion => _currentVersion;
    public bool CanInstallAutomatically => _packageVerifier.CanInstallAutomatically;

    public UpdateChecker(IUpdatePackageVerifier? packageVerifier = null)
        : this(
            packageVerifier ?? new ManualReleaseVerifier(),
            () => new SocketsHttpHandler
            {
                AllowAutoRedirect = false,
                AutomaticDecompression = DecompressionMethods.All,
                ConnectTimeout = TimeSpan.FromSeconds(10)
            },
            Assembly.GetExecutingAssembly().GetName().Version ?? new Version(0, 0, 0),
            TimeSpan.FromSeconds(15),
            Task.Delay)
    {
    }

    internal UpdateChecker(
        IUpdatePackageVerifier packageVerifier,
        Func<HttpMessageHandler> httpHandlerFactory,
        Version currentVersion,
        TimeSpan requestTimeout,
        Func<TimeSpan, CancellationToken, Task>? delayAsync = null)
    {
        _packageVerifier = packageVerifier;
        _httpHandlerFactory = httpHandlerFactory;
        _currentVersion = currentVersion;
        _requestTimeout = requestTimeout;
        _delayAsync = delayAsync ?? Task.Delay;
    }

    public async Task<UpdateCheckResult> CheckAsync(bool force, UpdateCheckInterval interval, CancellationToken cancellationToken = default)
    {
        if (!force && !ShouldRunAutomaticCheck(interval))
        {
            return UpdateCheckResult.Skipped(CurrentVersion);
        }

        var release = await ResolveLatestReleaseAsync(cancellationToken);
        var releaseUrl = release.ReleaseUrl;
        var latestVersion = release.Version;
        var state = LoadState() ?? new UpdateCheckState();
        state.LastCheckedUtc = DateTimeOffset.UtcNow;
        state.LastAvailableVersion = latestVersion.CompareTo(CurrentVersion) > 0 ? latestVersion.ToString(3) : null;
        state.LastReleaseUrl = latestVersion.CompareTo(CurrentVersion) > 0 ? releaseUrl : null;
        SaveState(state);

        return latestVersion.CompareTo(CurrentVersion) > 0
            ? UpdateCheckResult.Available(CurrentVersion, latestVersion, releaseUrl)
            : UpdateCheckResult.UpToDate(CurrentVersion, latestVersion);
    }

    internal async Task<LatestReleaseInfo> ResolveLatestReleaseAsync(CancellationToken cancellationToken = default)
    {
        Exception? redirectFailure = null;
        for (var attempt = 0; attempt < 2; attempt++)
        {
            try
            {
                return await ResolveFromRedirectAsync(cancellationToken);
            }
            catch (Exception ex) when (IsRetryable(ex, cancellationToken))
            {
                redirectFailure = ex;
                if (attempt == 0)
                {
                    await _delayAsync(TimeSpan.FromMilliseconds(350), cancellationToken);
                }
            }
            catch (Exception ex) when (ex is HttpRequestException or InvalidOperationException)
            {
                redirectFailure = ex;
                break;
            }
        }

        try
        {
            return await ResolveFromFeedAsync(cancellationToken);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception feedFailure) when (feedFailure is HttpRequestException or TaskCanceledException or InvalidOperationException or XmlException)
        {
            throw UpdateCheckException.FromFailures(redirectFailure, feedFailure);
        }
    }

    private async Task<LatestReleaseInfo> ResolveFromRedirectAsync(CancellationToken cancellationToken)
    {
        using var client = CreateHttpClient("text/html");
        using var response = await client.GetAsync(LatestReleaseUrl, HttpCompletionOption.ResponseHeadersRead, cancellationToken);

        var releaseUri = response.Headers.Location;
        if (releaseUri is not null && !releaseUri.IsAbsoluteUri)
        {
            releaseUri = new Uri(new Uri(LatestReleaseUrl), releaseUri);
        }

        if (releaseUri is null && response.IsSuccessStatusCode)
        {
            releaseUri = response.RequestMessage?.RequestUri;
        }

        if (releaseUri is null || ParseVersion(TryGetTagFromReleaseUrl(releaseUri.ToString())) == new Version(0, 0, 0))
        {
            response.EnsureSuccessStatusCode();
            throw new InvalidOperationException("GitHub 最新发布地址未返回有效的版本标签。");
        }

        var version = ParseVersion(TryGetTagFromReleaseUrl(releaseUri.ToString()));
        return new LatestReleaseInfo(version, releaseUri.ToString());
    }

    private async Task<LatestReleaseInfo> ResolveFromFeedAsync(CancellationToken cancellationToken)
    {
        using var client = CreateHttpClient("application/atom+xml");
        using var response = await client.GetAsync(ReleasesFeedUrl, HttpCompletionOption.ResponseHeadersRead, cancellationToken);
        response.EnsureSuccessStatusCode();
        await using var stream = await response.Content.ReadAsStreamAsync(cancellationToken);
        using var reader = XmlReader.Create(stream, new XmlReaderSettings
        {
            Async = true,
            DtdProcessing = DtdProcessing.Prohibit,
            XmlResolver = null
        });
        var document = await XDocument.LoadAsync(reader, LoadOptions.None, cancellationToken);
        XNamespace atom = "http://www.w3.org/2005/Atom";
        var entry = document.Root?.Elements(atom + "entry").FirstOrDefault();
        var releaseUrl = entry?
            .Elements(atom + "link")
            .Select(link => (string?)link.Attribute("href"))
            .FirstOrDefault(url => !string.IsNullOrWhiteSpace(url) && url.Contains("/releases/tag/", StringComparison.OrdinalIgnoreCase));
        var tag = releaseUrl is null
            ? entry?.Element(atom + "title")?.Value
            : TryGetTagFromReleaseUrl(releaseUrl);
        var version = ParseVersion(tag);
        if (version == new Version(0, 0, 0))
        {
            throw new InvalidOperationException("GitHub 发布源未返回有效的版本标签。");
        }

        return new LatestReleaseInfo(version, releaseUrl ?? ReleasesUrl);
    }

    private HttpClient CreateHttpClient(string accept)
    {
        var client = new HttpClient(_httpHandlerFactory(), disposeHandler: true)
        {
            Timeout = _requestTimeout
        };
        client.DefaultRequestHeaders.UserAgent.Add(
            new ProductInfoHeaderValue("ClevoLEDKeyboardControl", CurrentVersion.ToString(3)));
        client.DefaultRequestHeaders.Accept.Add(new MediaTypeWithQualityHeaderValue(accept));
        return client;
    }

    private static bool IsRetryable(Exception exception, CancellationToken cancellationToken) =>
        exception is TaskCanceledException && !cancellationToken.IsCancellationRequested ||
        exception is HttpRequestException { StatusCode: null } ||
        exception is HttpRequestException { StatusCode: >= HttpStatusCode.InternalServerError };

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

internal sealed record LatestReleaseInfo(Version Version, string ReleaseUrl);

public enum UpdateCheckFailureKind
{
    Timeout,
    HttpStatus,
    Network,
    InvalidResponse
}

public sealed class UpdateCheckException : Exception
{
    public UpdateCheckFailureKind Kind { get; }
    public HttpStatusCode? StatusCode { get; }

    private UpdateCheckException(
        string message,
        UpdateCheckFailureKind kind,
        HttpStatusCode? statusCode,
        Exception innerException)
        : base(message, innerException)
    {
        Kind = kind;
        StatusCode = statusCode;
    }

    internal static UpdateCheckException FromFailures(Exception? redirectFailure, Exception feedFailure)
    {
        var failures = new[] { feedFailure, redirectFailure }.Where(exception => exception is not null).Cast<Exception>();
        var timeout = failures.FirstOrDefault(exception => exception is TaskCanceledException);
        if (timeout is not null)
        {
            return new UpdateCheckException("连接 GitHub 超时，请检查网络或代理后重试。", UpdateCheckFailureKind.Timeout, null, timeout);
        }

        var http = failures.OfType<HttpRequestException>().FirstOrDefault(exception => exception.StatusCode.HasValue);
        if (http is not null)
        {
            return new UpdateCheckException(
                $"GitHub 返回状态码 {(int)http.StatusCode!.Value}，请稍后重试。",
                UpdateCheckFailureKind.HttpStatus,
                http.StatusCode,
                http);
        }

        var network = failures.OfType<HttpRequestException>().FirstOrDefault();
        if (network is not null)
        {
            return new UpdateCheckException("无法连接 GitHub，请检查网络、DNS 或代理设置。", UpdateCheckFailureKind.Network, null, network);
        }

        return new UpdateCheckException("无法识别 GitHub 最新版本信息，请稍后重试。", UpdateCheckFailureKind.InvalidResponse, null, feedFailure);
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
