using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Interop;
using System.Windows.Media;

namespace AltSpacy;

public enum BackdropKind
{
    None = 1,
    Mica = 2,
    Acrylic = 3,
}

/// <summary>Windows 11 system backdrops (acrylic/mica), rounded corners and dark title bars via DWM.</summary>
public static class Backdrop
{
    private const int DwmwaUseImmersiveDarkMode = 20, DwmwaWindowCornerPreference = 33, DwmwaSystemBackdropType = 38;
    private const int CornerRound = 2;

    /// <summary>Returns false when the backdrop isn't available (older Windows, RDP, transparency effects off).</summary>
    public static bool Apply(Window window, BackdropKind kind, bool dark)
    {
        var hwnd = new WindowInteropHelper(window).EnsureHandle();
        if (HwndSource.FromHwnd(hwnd)?.CompositionTarget is { } target) target.BackgroundColor = Colors.Transparent;

        SetDarkMode(window, dark);
        var corner = CornerRound;
        DwmSetWindowAttribute(hwnd, DwmwaWindowCornerPreference, ref corner, sizeof(int));
        var margins = new Margins { Left = -1, Right = -1, Top = -1, Bottom = -1 };
        DwmExtendFrameIntoClientArea(hwnd, ref margins);
        var type = (int)kind;
        return DwmSetWindowAttribute(hwnd, DwmwaSystemBackdropType, ref type, sizeof(int)) == 0 && TransparencyEnabled();
    }

    /// <summary>Removes the system menu so DWM doesn't draw a close button on the extended glass frame.</summary>
    public static void HideCaptionButtons(Window window)
    {
        var hwnd = new WindowInteropHelper(window).EnsureHandle();
        SetWindowLongPtr(hwnd, GwlStyle, new IntPtr(GetWindowLongPtr(hwnd, GwlStyle).ToInt64() & ~WsSysMenu));
    }

    private const int GwlStyle = -16;
    private const long WsSysMenu = 0x00080000;

    [DllImport("user32.dll", EntryPoint = "GetWindowLongPtrW")]
    private static extern IntPtr GetWindowLongPtr(IntPtr hwnd, int index);

    [DllImport("user32.dll", EntryPoint = "SetWindowLongPtrW")]
    private static extern IntPtr SetWindowLongPtr(IntPtr hwnd, int index, IntPtr value);

    public static void SetDarkMode(Window window, bool dark)
    {
        var hwnd = new WindowInteropHelper(window).EnsureHandle();
        var value = dark ? 1 : 0;
        DwmSetWindowAttribute(hwnd, DwmwaUseImmersiveDarkMode, ref value, sizeof(int));
    }

    private static bool TransparencyEnabled()
    {
        using var key = Microsoft.Win32.Registry.CurrentUser.OpenSubKey(@"Software\Microsoft\Windows\CurrentVersion\Themes\Personalize");
        return key?.GetValue("EnableTransparency") is not int value || value != 0;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct Margins
    {
        public int Left, Right, Top, Bottom;
    }

    [DllImport("dwmapi.dll")]
    private static extern int DwmSetWindowAttribute(IntPtr hwnd, int attribute, ref int value, int size);

    [DllImport("dwmapi.dll")]
    private static extern int DwmExtendFrameIntoClientArea(IntPtr hwnd, ref Margins margins);
}
