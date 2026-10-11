using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Interop;

namespace ReforgeX;

static class WindowTheme
{
    public static void Apply(Window window)
    {
        window.SourceInitialized += (_, _) => Paint(window);
    }

    static void Paint(Window window)
    {
        var hwnd = new WindowInteropHelper(window).Handle;
        if (hwnd == IntPtr.Zero)
            return;
        int dark = 1;
        DwmSetWindowAttribute(hwnd, 20, ref dark, 4);
        int caption = 0x00111413;
        int text = 0x00E7F0F3;
        DwmSetWindowAttribute(hwnd, 35, ref caption, 4);
        DwmSetWindowAttribute(hwnd, 36, ref text, 4);
    }

    [DllImport("dwmapi.dll")]
    static extern int DwmSetWindowAttribute(IntPtr hwnd, int attr, ref int value, int size);
}
