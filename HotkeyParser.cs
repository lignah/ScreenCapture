namespace ScreenCapture;

internal static class HotkeyParser
{
    public const string Default = "Ctrl+Shift+A";

    public static readonly string[] Presets =
    {
        "Ctrl+Shift+A", "Ctrl+Alt+A", "Ctrl+Shift+S", "Ctrl+Shift+X", "Alt+Shift+A", "Ctrl+Shift+F1", "PrintScreen",
    };

    /// <summary>Parses text like "Ctrl+Shift+A" into RegisterHotKey modifiers and a virtual key.</summary>
    public static bool TryParse(string? text, out uint modifiers, out Keys key)
    {
        modifiers = 0;
        key = Keys.None;
        if (string.IsNullOrWhiteSpace(text)) return false;
        var parts = text.Split('+', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries);
        if (parts.Length == 0) return false;

        for (int i = 0; i < parts.Length - 1; i++)
        {
            switch (parts[i].ToLowerInvariant())
            {
                case "ctrl" or "control": modifiers |= Native.MOD_CONTROL; break;
                case "alt": modifiers |= Native.MOD_ALT; break;
                case "shift": modifiers |= Native.MOD_SHIFT; break;
                case "win" or "windows": modifiers |= Native.MOD_WIN; break;
                default: return false;
            }
        }

        var k = parts[^1];
        if (k.Length == 1 && char.IsAsciiLetter(k[0])) key = Keys.A + (char.ToUpperInvariant(k[0]) - 'A');
        else if (k.Length == 1 && char.IsAsciiDigit(k[0])) key = Keys.D0 + (k[0] - '0');
        else if (!Enum.TryParse(k, true, out key)) return false;

        return key is not (Keys.None or Keys.ControlKey or Keys.ShiftKey or Keys.Menu or Keys.LWin or Keys.RWin
            or Keys.Control or Keys.Shift or Keys.Alt);
    }
}
