using System.ComponentModel;
using System.Runtime.InteropServices;
using System.Windows.Input;
using System.Windows.Interop;

namespace AltSpacy;

public readonly record struct HotkeyBinding(uint Modifiers, uint VirtualKey)
{
    public const uint ModAlt = 0x1, ModControl = 0x2, ModShift = 0x4, ModWin = 0x8, ModNoRepeat = 0x4000;

    public static HotkeyBinding Parse(string text)
    {
        uint modifiers = 0;
        Key? key = null;
        foreach (var part in text.Split('+', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries))
        {
            switch (part.ToLowerInvariant())
            {
                case "alt": modifiers |= ModAlt; break;
                case "ctrl" or "control": modifiers |= ModControl; break;
                case "shift": modifiers |= ModShift; break;
                case "win" or "windows": modifiers |= ModWin; break;
                default:
                    var name = part.Length == 1 && char.IsAsciiDigit(part[0]) ? "D" + part : part;
                    if (key is not null || !Enum.TryParse<Key>(name, true, out var parsed) || parsed == Key.None || int.TryParse(name, out _))
                        throw new FormatException($"Invalid hotkey '{text}'.");
                    key = parsed;
                    break;
            }
        }
        if (key is null || modifiers == 0) throw new FormatException($"Invalid hotkey '{text}'. Use e.g. Alt+Space or Ctrl+Shift+K.");
        return new(modifiers, (uint)KeyInterop.VirtualKeyFromKey(key.Value));
    }

    public static string? Format(ModifierKeys modifiers, Key key)
    {
        if (key is Key.None or Key.System or Key.LeftCtrl or Key.RightCtrl or Key.LeftAlt or Key.RightAlt
            or Key.LeftShift or Key.RightShift or Key.LWin or Key.RWin) return null;

        var parts = new List<string>();
        if (modifiers.HasFlag(ModifierKeys.Control)) parts.Add("Ctrl");
        if (modifiers.HasFlag(ModifierKeys.Alt)) parts.Add("Alt");
        if (modifiers.HasFlag(ModifierKeys.Shift)) parts.Add("Shift");
        if (modifiers.HasFlag(ModifierKeys.Windows)) parts.Add("Win");
        if (parts.Count == 0) return null;

        parts.Add(key is >= Key.D0 and <= Key.D9 ? (key - Key.D0).ToString() : key.ToString());
        return string.Join("+", parts);
    }
}

public sealed class GlobalHotkey : IDisposable
{
    private const int WmHotkey = 0x0312;
    private readonly HwndSource _source;
    private readonly int _id;

    public event Action? Pressed;

    public GlobalHotkey(IntPtr hwnd, HotkeyBinding binding, int id = 1)
    {
        _source = HwndSource.FromHwnd(hwnd);
        _id = id;
        if (!RegisterHotKey(hwnd, id, binding.Modifiers | HotkeyBinding.ModNoRepeat, binding.VirtualKey))
            throw new Win32Exception(Marshal.GetLastWin32Error());
        _source.AddHook(WndProc);
    }

    private IntPtr WndProc(IntPtr hwnd, int msg, IntPtr wParam, IntPtr lParam, ref bool handled)
    {
        if (msg == WmHotkey && wParam.ToInt32() == _id)
        {
            Pressed?.Invoke();
            handled = true;
        }
        return IntPtr.Zero;
    }

    public void Dispose()
    {
        _source.RemoveHook(WndProc);
        UnregisterHotKey(_source.Handle, _id);
    }

    [DllImport("user32.dll", SetLastError = true)]
    private static extern bool RegisterHotKey(IntPtr hWnd, int id, uint fsModifiers, uint vk);

    [DllImport("user32.dll", SetLastError = true)]
    private static extern bool UnregisterHotKey(IntPtr hWnd, int id);
}
