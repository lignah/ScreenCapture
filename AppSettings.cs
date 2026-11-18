using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.Win32;

namespace ScreenCapture;

internal sealed class AppSettings
{
    private const string RunKey = @"Software\Microsoft\Windows\CurrentVersion\Run";
    private const string RunValue = "ScreenCapture";

    public string SaveFolder { get; set; } = ImageOutput.DefaultFolder;

    /// <summary>Global hotkey for region capture, e.g. "Ctrl+Shift+A".</summary>
    public string Hotkey { get; set; } = HotkeyParser.Default;

    /// <summary>Single key (F1-F11) that copies + saves the whole screen at once; empty = none (default).</summary>
    public string FullscreenHotkey { get; set; } = "";

    /// <summary>Show a Save As dialog; otherwise save straight to <see cref="SaveFolder"/>.</summary>
    public bool AskWhereToSave { get; set; } = true;

    /// <summary>Also write a PNG to the save folder whenever a capture is copied.</summary>
    public bool SaveOnCopy { get; set; }

    /// <summary>Full-screen capture always copies; this also writes a PNG.</summary>

    /// <summary>OCR result window: join wrapped lines into paragraphs (true) or keep original line breaks.</summary>
    public bool OcrMergeParagraphs { get; set; } = true;

    /// <summary>Capture-screen starting stroke thickness: 0 = Thin (default), 1 = Medium, 2 = Thick.</summary>
    public int DefaultThickness { get; set; }

    /// <summary>OCR always copies to the clipboard; this additionally opens the editable result window.</summary>
    public bool OcrShowWindow { get; set; }

    /// <summary>Active PaddleOCR recognition language id (see <see cref="OcrLanguages.Catalog"/>).</summary>
    public string OcrLanguage { get; set; } = OcrLanguages.DefaultId;

    /// <summary>Always "paddle": PaddleOCR is the only engine (Windows OCR is an automatic internal fallback).
    /// Older settings files with "windows" are migrated on load.</summary>
    public string OcrEngine { get; set; } = "paddle";

    [JsonIgnore]
    public string EffectiveFolder => string.IsNullOrWhiteSpace(SaveFolder) ? ImageOutput.DefaultFolder : SaveFolder;

    private static string FilePath => Path.Combine(ErrorLog.Dir, "settings.json");

    public static AppSettings Load()
    {
        try
        {
            if (File.Exists(FilePath))
            {
                var s = JsonSerializer.Deserialize<AppSettings>(File.ReadAllText(FilePath)) ?? new AppSettings();
                s.DefaultThickness = Math.Clamp(s.DefaultThickness, 0, 2);
                if (s.OcrEngine != "paddle")
                {
                    s.OcrEngine = "paddle"; // engine choice was removed; migrate old "windows" setting
                    s.Save();
                }
                return s;
            }
        }
        catch (Exception ex)
        {
            ErrorLog.Write(ex);
        }
        return new AppSettings();
    }

    public void Save()
    {
        try
        {
            Directory.CreateDirectory(ErrorLog.Dir);
            File.WriteAllText(FilePath, JsonSerializer.Serialize(this, new JsonSerializerOptions { WriteIndented = true }));
        }
        catch (Exception ex)
        {
            ErrorLog.Write(ex);
        }
    }

    /// <summary>If start-with-Windows is on but points at another exe path (app moved/updated), point it here.</summary>
    public static void RefreshStartupPath()
    {
        using var key = Registry.CurrentUser.OpenSubKey(RunKey, writable: true);
        if (key?.GetValue(RunValue) is not string current) return;
        string wanted = $"\"{Environment.ProcessPath}\"";
        if (!string.Equals(current, wanted, StringComparison.OrdinalIgnoreCase)) key.SetValue(RunValue, wanted);
    }

    public static bool StartWithWindows
    {
        get
        {
            using var key = Registry.CurrentUser.OpenSubKey(RunKey);
            return key?.GetValue(RunValue) is string;
        }
        set
        {
            using var key = Registry.CurrentUser.CreateSubKey(RunKey);
            if (value) key.SetValue(RunValue, $"\"{Environment.ProcessPath}\"");
            else key.DeleteValue(RunValue, false);
        }
    }
}
