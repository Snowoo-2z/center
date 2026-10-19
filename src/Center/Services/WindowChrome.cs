using System;
using System.Runtime.InteropServices;
using Avalonia.Controls;

namespace Center.Services;

/// <summary>
/// Paints the Win32 title bar dark so it blends with the #121212 canvas.
/// Silently does nothing on older Windows versions (or non-Windows dev machines).
/// </summary>
internal static class WindowChrome
{
    private const int DwmwaUseImmersiveDarkMode = 20;
    private const int DwmwaUseImmersiveDarkModeBefore20H1 = 19;

    [DllImport("dwmapi.dll", SetLastError = true)]
    private static extern int DwmSetWindowAttribute(IntPtr hwnd, int attribute, ref int value, int size);

    public static void ApplyDarkTitleBar(Window window)
    {
        try
        {
            var handle = window.TryGetPlatformHandle();
            if (handle is null || handle.Handle == IntPtr.Zero)
                return;

            var enabled = 1;
            var result = DwmSetWindowAttribute(
                handle.Handle, DwmwaUseImmersiveDarkMode, ref enabled, sizeof(int));

            if (result != 0)
            {
                DwmSetWindowAttribute(
                    handle.Handle, DwmwaUseImmersiveDarkModeBefore20H1, ref enabled, sizeof(int));
            }
        }
        catch
        {
            // Not fatal: the app just keeps the default title bar.
        }
    }
}
