using System.Diagnostics;
using System.Reflection;
using System.Windows;
using System.Windows.Controls;

namespace ColorfulLedKeyboard.Tray.Wpf.Pages;

public partial class AboutPage : UserControl
{
    private const string RepositoryUrl = "https://github.com/silent-ram/ClevoLEDKeyboardControl";
    private const string IssuesUrl = "https://github.com/silent-ram/ClevoLEDKeyboardControl/issues";

    public AboutPage()
    {
        InitializeComponent();
        VersionText.Text = $"版本 v{ReadVersion()}";
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

    private static void OpenUrl(string url) =>
        Process.Start(new ProcessStartInfo(url) { UseShellExecute = true });
}
