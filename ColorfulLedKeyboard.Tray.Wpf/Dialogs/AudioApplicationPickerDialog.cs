using System.Diagnostics;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using ColorfulLedKeyboard.Core;

namespace ColorfulLedKeyboard.Tray.Wpf.Dialogs;

/// <summary>绑定正在运行的音乐程序（WinForms AudioApplicationPickerForm 的 WPF 移植件）。</summary>
public sealed class AudioApplicationPickerDialog : Window
{
    private readonly List<AudioApplicationStatus> _items;
    private readonly ListView _list = new() { MinHeight = 260 };

    public AudioApplicationStatus? Selected { get; private set; }

    public AudioApplicationPickerDialog(bool includeVisibleProcesses = false)
    {
        Title = "绑定正在运行的音乐程序";
        WindowStartupLocation = WindowStartupLocation.CenterOwner;
        Width = 720;
        Height = 440;
        Background = (Brush)Application.Current.Resources["Brush.Window"];
        FontFamily = (FontFamily)Application.Current.Resources["Font.Body"];
        FontSize = 12;
        Foreground = (Brush)Application.Current.Resources["Brush.Text"];
        SourceInitialized += (_, _) => WpfThemeManager.ApplyTitleBarMode(this);

        _items = AudioApplicationsState.Load()?.Applications.ToList()
            ?? AutomationStatus.Load()?.AudioApplications.ToList()
            ?? [];
        if (includeVisibleProcesses)
        {
            foreach (var process in Process.GetProcesses())
            using (process)
            {
                try
                {
                    if (process.MainWindowHandle == IntPtr.Zero) continue;
                    var name = AppProfileRule.NormalizeProcessName(process.ProcessName);
                    var path = process.MainModule?.FileName ?? "";
                    var existing = _items.FirstOrDefault(item =>
                        string.Equals(item.ProcessName, name, StringComparison.OrdinalIgnoreCase) &&
                        (string.IsNullOrWhiteSpace(item.ExecutablePath) ||
                         string.Equals(item.ExecutablePath, path, StringComparison.OrdinalIgnoreCase)));
                    if (existing is not null)
                    {
                        if (!existing.ProcessIds.Contains(process.Id)) existing.ProcessIds.Add(process.Id);
                        continue;
                    }
                    _items.Add(new AudioApplicationStatus
                    {
                        ProcessName = name,
                        ExecutablePath = path,
                        ProcessIds = [process.Id]
                    });
                }
                catch
                {
                }
            }
        }

        _items = _items.OrderByDescending(item => item.IsPlaying)
            .ThenByDescending(item => item.PeakLevel)
            .ThenBy(item => item.ProcessName, StringComparer.OrdinalIgnoreCase)
            .ToList();

        var gridView = new GridView();
        gridView.Columns.Add(MakeColumn("程序", "ProcessName", 180));
        gridView.Columns.Add(MakeColumn("PID", "ProcessIdsText", 180));
        gridView.Columns.Add(MakeColumn("电平", "PeakLevelText", 100));
        gridView.Columns.Add(MakeColumn("状态", "StatusText", 100));
        _list.View = gridView;
        _list.ItemsSource = _items.Select(item => new RowVm(item)).ToList();
        ThemedListView.Apply(_list);
        System.Windows.Controls.ScrollViewer.SetHorizontalScrollBarVisibility(_list, ScrollBarVisibility.Disabled);
        _list.MouseDoubleClick += (_, _) => Accept();

        var bind = new Button
        {
            Content = "绑定",
            Style = (Style)Application.Current.Resources["UiButtonPrimary"],
            MinWidth = 120
        };
        bind.Click += (_, _) => Accept();

        var root = new DockPanel { Margin = new Thickness(14) };
        DockPanel.SetDock(bind, Dock.Bottom);
        root.Children.Add(bind);
        bind.Margin = new Thickness(0, 12, 0, 0);
        bind.HorizontalAlignment = HorizontalAlignment.Stretch;
        root.Children.Add(_list);
        Content = root;
    }

    private sealed record RowVm(AudioApplicationStatus App)
    {
        public string ProcessName => App.ProcessName;
        public string ProcessIdsText => string.Join(",", App.ProcessIds);
        public string PeakLevelText => $"{App.PeakLevel:P1}";
        public string StatusText => App.IsPlaying ? "正在播放" : App.PeakLevel > 0.001f ? "检测声音中" : "已静音";
        public AudioApplicationStatus Source => App;
    }

    private static GridViewColumn MakeColumn(string header, string property, double width) => new()
    {
        Header = header,
        Width = width,
        DisplayMemberBinding = new System.Windows.Data.Binding(property)
    };

    private void Accept()
    {
        if (_list.SelectedItem is not RowVm row) return;
        Selected = row.Source;
        DialogResult = true;
        Close();
    }
}
