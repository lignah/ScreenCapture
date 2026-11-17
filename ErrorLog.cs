namespace ScreenCapture;

internal static class ErrorLog
{
    public static string Dir => System.IO.Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "ScreenCapture");

    public static string Path => System.IO.Path.Combine(Dir, "error.log");

    private const long MaxBytes = 1024 * 1024;

    public static void Write(Exception? ex)
    {
        if (ex is null) return;
        try
        {
            Directory.CreateDirectory(Dir);
            // Rotate at ~1 MB: keep one previous file (error.old.log).
            var fi = new FileInfo(Path);
            if (fi.Exists && fi.Length > MaxBytes)
                File.Move(Path, System.IO.Path.Combine(Dir, "error.old.log"), true);
            File.AppendAllText(Path, $"[{DateTime.Now:yyyy-MM-dd HH:mm:ss}] {ex}{Environment.NewLine}{Environment.NewLine}");
        }
        catch
        {
            // Logging must never throw.
        }
    }
}
