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

    /// <summary>Show a Save As dialog; otherwise save straight to <see cref="SaveFolder"/>.</summary>
    public bool AskWhereToSave { get; set; } = true;

    /// <summary>Also write a PNG to the save folder whenever a capture is copied.</summary>
    public bool SaveOnCopy { get; set; }

    /// <summary>Full-screen capture always copies; this also writes a PNG.</summary>
    public bool FullScreenSavesFile { get; set; } = true;

    /// <summary>OCR result window: join wrapped lines into paragraphs (true) or keep original line breaks.</summary>
    public bool OcrMergeParagraphs { get; set; } = true;

    [JsonIgnore]
    public string EffectiveFolder => string.IsNullOrWhiteSpace(SaveFolder) ? ImageOutput.DefaultFolder : SaveFolder;

    private static string FilePath => Path.Combine(ErrorLog.Dir, "settings.json");

    public static AppSettings Load()
    {
        try
        {
            if (File.Exists(FilePath))
                return JsonSerializer.Deserialize<AppSettings>(File.ReadAllText(FilePath)) ?? new AppSettings();
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
