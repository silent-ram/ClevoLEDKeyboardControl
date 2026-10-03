using System.Diagnostics;
using System.Reflection;
using System.Windows;
using System.Windows.Controls;
using ColorfulLedKeyboard.Core;

namespace ColorfulLedKeyboard.Tray.Wpf.Pages;

public partial class AboutPage : UserControl
{
    private const string RepositoryUrl = "https://github.com/silent-ram/ClevoLEDKeyboardControl";
    private const string IssuesUrl = "https://github.com/silent-ram/ClevoLEDKeyboardControl/issues";
    private const string LicenseUrl = RepositoryUrl + "/blob/main/LICENSE";

    public AboutPage()
    {
        InitializeComponent();
        VersionText.Text = $"版本 v{ReadVersion()}";
        RuntimeText.Text = $".NET {Environment.Version}";
        RuntimeValue.Text = $".NET {Environment.Version}（自包含，无需单独安装）";
        InstallPathValue.Text = AppContext.BaseDirectory.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
        InstallPathValue.ToolTip = InstallPathValue.Text;
        var settings = new SettingsStore().Load();
        ImprovementStatus.Text = settings.UserImprovementPlan?.Enabled == true
            ? "参与改进计划：已参与。仅上传匿名设备汇总，可在「软件设置」中更改。"
            : "参与改进计划：未参与。仅上传匿名设备汇总，可在「软件设置」中开启。";
    }

    private static string ReadVersion()
    {
        var informational = Assembly.GetExecutingAssembly()
            .GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion;
        if (!string.IsNullOrWhiteSpace(informational))
        {
            var plus = informational.IndexOf('+');
            return plus > 0 ? informational[..plus] : informational;
        }
        return Assembly.GetExecutingAssembly().GetName().Version?.ToString(3) ?? "0.0.0";
    }

    private void OpenRepository(object sender, RoutedEventArgs e) => OpenUrl(RepositoryUrl);

    private void OpenIssues(object sender, RoutedEventArgs e) => OpenUrl(IssuesUrl);

    private void OpenLicense(object sender, RoutedEventArgs e) => OpenUrl(LicenseUrl);

    private static void OpenUrl(string url) =>
        Process.Start(new ProcessStartInfo(url) { UseShellExecute = true });
}
