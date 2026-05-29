using System.Diagnostics;
using System.Drawing.Imaging;
using System.Runtime.InteropServices;

namespace ColorfulLedKeyboard.Tray;

internal static class ProcessIconColor
{
    [DllImport("shell32.dll", CharSet = CharSet.Unicode)]
    private static extern int ExtractIconEx(string fileName, int iconIndex, IntPtr[]? largeIcon, IntPtr[]? smallIcon, int nIcons);

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    private static extern bool DestroyIcon(IntPtr hIcon);

    public static bool TryGetColor(string processName, out string color)
    {
        color = "#FFFFFF";
        try
        {
            var path = FindProcessExecutable(processName);
            if (path is null)
            {
                return false;
            }

            using var icon = ExtractLargestIcon(path);
            if (icon is null)
            {
                return false;
            }

            using var bitmap = icon.ToBitmap();
            color = SampleDominantColor(bitmap);
            return true;
        }
        catch
        {
            return false;
        }
    }

    private static string? FindProcessExecutable(string processName)
    {
        foreach (var process in Process.GetProcessesByName(processName))
        {
            try
            {
                return process.MainModule?.FileName;
            }
            catch
            {
            }
        }

        return null;
    }

    private static Icon? ExtractLargestIcon(string path)
    {
        var large = new IntPtr[1];
        var small = new IntPtr[1];
        var count = ExtractIconEx(path, 0, large, small, 1);
        if (count <= 0)
        {
            return null;
        }

        try
        {
            var handle = large[0] != IntPtr.Zero ? large[0] : small[0];
            return handle == IntPtr.Zero ? null : Icon.FromHandle(handle);
        }
        finally
        {
            if (large[0] != IntPtr.Zero) DestroyIcon(large[0]);
            if (small[0] != IntPtr.Zero) DestroyIcon(small[0]);
        }
    }

    private static string SampleDominantColor(Bitmap bitmap)
    {
        var sampleWidth = Math.Min(bitmap.Width, 32);
        var sampleHeight = Math.Min(bitmap.Height, 32);
        var counts = new Dictionary<int, int>();

        for (var y = 0; y < sampleHeight; y++)
        {
            for (var x = 0; x < sampleWidth; x++)
            {
                var pixel = bitmap.GetPixel(x * bitmap.Width / sampleWidth, y * bitmap.Height / sampleHeight);
                if (pixel.A < 32)
                {
                    continue;
                }

                var key = (pixel.R << 16) | (pixel.G << 8) | pixel.B;
                counts[key] = counts.GetValueOrDefault(key) + 1;
            }
        }

        var color = counts.OrderByDescending(entry => entry.Value).FirstOrDefault().Key;
        return $"#{(color >> 16 & 0xFF):X2}{(color >> 8 & 0xFF):X2}{(color & 0xFF):X2}";
    }
}
