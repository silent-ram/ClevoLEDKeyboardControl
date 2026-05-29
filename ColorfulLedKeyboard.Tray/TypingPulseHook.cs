using ColorfulLedKeyboard.Core;
using System.Diagnostics;
using System.Runtime.InteropServices;

namespace ColorfulLedKeyboard.Tray;

internal sealed class TypingPulseHook : IDisposable
{
    private const int WH_KEYBOARD_LL = 13;
    private const int WM_KEYDOWN = 0x0100;
    private const int WM_SYSKEYDOWN = 0x0104;
    private readonly LowLevelKeyboardProc _proc;
    private IntPtr _hook;
    private DateTimeOffset _lastSaved = DateTimeOffset.MinValue;

    public TypingPulseHook()
    {
        _proc = HookCallback;
    }

    public void SetEnabled(bool enabled)
    {
        if (enabled && _hook == IntPtr.Zero)
        {
            _hook = SetHook(_proc);
        }
        else if (!enabled && _hook != IntPtr.Zero)
        {
            UnhookWindowsHookEx(_hook);
            _hook = IntPtr.Zero;
        }
    }

    public void Dispose()
    {
        SetEnabled(false);
    }

    private IntPtr HookCallback(int nCode, IntPtr wParam, IntPtr lParam)
    {
        if (nCode >= 0 && (wParam == WM_KEYDOWN || wParam == WM_SYSKEYDOWN))
        {
            var now = DateTimeOffset.UtcNow;
            if (now - _lastSaved >= TimeSpan.FromMilliseconds(60))
            {
                TypingPulseState.Save(now);
                _lastSaved = now;
            }
        }

        return CallNextHookEx(_hook, nCode, wParam, lParam);
    }

    private static IntPtr SetHook(LowLevelKeyboardProc proc)
    {
        using var currentProcess = Process.GetCurrentProcess();
        using var currentModule = currentProcess.MainModule;
        var moduleHandle = currentModule is null ? IntPtr.Zero : GetModuleHandle(currentModule.ModuleName);
        return SetWindowsHookEx(WH_KEYBOARD_LL, proc, moduleHandle, 0);
    }

    private delegate IntPtr LowLevelKeyboardProc(int nCode, IntPtr wParam, IntPtr lParam);

    [DllImport("user32.dll", SetLastError = true)]
    private static extern IntPtr SetWindowsHookEx(int idHook, LowLevelKeyboardProc lpfn, IntPtr hMod, uint dwThreadId);

    [DllImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool UnhookWindowsHookEx(IntPtr hhk);

    [DllImport("user32.dll")]
    private static extern IntPtr CallNextHookEx(IntPtr hhk, int nCode, IntPtr wParam, IntPtr lParam);

    [DllImport("kernel32.dll", CharSet = CharSet.Auto, SetLastError = true)]
    private static extern IntPtr GetModuleHandle(string? lpModuleName);
}
