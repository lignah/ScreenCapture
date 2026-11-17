namespace ScreenCapture;

internal static class ErrorLog
{
    public static string Dir => System.IO.Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "ScreenCapture");

    public static string Path => System.IO.Path.Combine(Dir, "error.log");

    public static void Write(Exception? ex)
    {
        if (ex is null) return;
        try
        {
            Directory.CreateDirectory(Dir);
            File.AppendAllText(Path, $"[{DateTime.Now:yyyy-MM-dd HH:mm:ss}] {ex}{Environment.NewLine}{Environment.NewLine}");
        }
        catch
        {
            // Logging must never throw.
        }
    }
}
