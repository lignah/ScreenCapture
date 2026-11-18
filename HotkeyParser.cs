namespace ScreenCapture;

internal static class HotkeyParser
{
    public const string Default = "Ctrl+Shift+A";

    /// <summary>Choices in the Settings dropdown. F12 is left out: Windows reserves it for debuggers.</summary>
    public static readonly string[] Presets =
    {
        "Ctrl+Shift+A",
        "F1", "F2", "F3", "F4", "F5", "F6", "F7", "F8", "F9", "F10", "F11",
        "Ctrl+Alt+A", "Ctrl+Shift+S", "Ctrl+Shift+X", "Alt+Shift+A", "Ctrl+Shift+F1", "PrintScreen",
    };

    /// <summary>Canonical spelling, e.g. "ctrl+shift+a" -> "Ctrl+Shift+A", "f5" -> "F5". Unparsable text is returned trimmed.</summary>
    public static string Normalize(string? text)
    {
        if (!TryParse(text, out uint mods, out Keys key)) return text?.Trim() ?? "";
        var parts = new List<string>();
        if ((mods & Native.MOD_CONTROL) != 0) parts.Add("Ctrl");
        if ((mods & Native.MOD_ALT) != 0) parts.Add("Alt");
        if ((mods & Native.MOD_SHIFT) != 0) parts.Add("Shift");
        if ((mods & Native.MOD_WIN) != 0) parts.Add("Win");
        parts.Add(key is >= Keys.D0 and <= Keys.D9 ? ((char)('0' + (key - Keys.D0))).ToString() : key.ToString());
        return string.Join("+", parts);
    }

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
