namespace ScreenCapture;

internal sealed record OcrRunResult(OcrOutcome? Outcome, string Text, int Lines, bool Copied, string? Error, long ElapsedMs)
{
    /// <summary>Short one-line preview of the copied text for notifications.</summary>
    public string Preview(int max = 40)
    {
        var flat = string.Join(" ", Text.Split(new[] { '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries)).Trim();
        return flat.Length <= max ? flat : flat[..max] + "…";
    }
}

/// <summary>Window-less OCR: recognize in the background, then copy (merged or raw) on the calling STA UI thread.</summary>
internal static class OcrRunner
{
    /// <summary>Apartment of the thread that wrote the clipboard in the last run (for tests).</summary>
    public static ApartmentState LastCopyApartment { get; private set; } = ApartmentState.Unknown;

    public static async Task<OcrRunResult> RunAsync(Bitmap image, AppSettings settings)
    {
        var sw = System.Diagnostics.Stopwatch.StartNew();
        try
        {
            var outcome = await OcrService.RecognizeAsync(image, "paddle", null, settings.OcrLanguage);
            var text = outcome.Format(settings.OcrMergeParagraphs);
            if (text.Trim().Length == 0)
                return new OcrRunResult(outcome, "", 0, false, null, sw.ElapsedMilliseconds);
            LastCopyApartment = Thread.CurrentThread.GetApartmentState();
            var error = ClipboardText.TrySet(text);
            int lines = text.Split('\n').Length;
            return new OcrRunResult(outcome, text, lines, error == null, error, sw.ElapsedMilliseconds);
        }
        catch (Exception ex)
        {
            ErrorLog.Write(ex);
            return new OcrRunResult(null, "", 0, false, ex.Message, sw.ElapsedMilliseconds);
        }
    }
}
