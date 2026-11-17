using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.Runtime.InteropServices;
using Windows.Media.Ocr;
using WinLanguage = Windows.Globalization.Language;
using WinBitmapPixelFormat = Windows.Graphics.Imaging.BitmapPixelFormat;
using WinBitmapAlphaMode = Windows.Graphics.Imaging.BitmapAlphaMode;
using WinSoftwareBitmap = Windows.Graphics.Imaging.SoftwareBitmap;

namespace ScreenCapture;

internal sealed record OcrOutcome(IReadOnlyList<OcrLine> Lines, string LanguageTag, int Scale, string? Note = null)
{
    public int LineCount => Lines.Count;
    public string Text => OcrTextLayout.Raw(Lines);
    public string MergedText => OcrTextLayout.Merge(Lines);
    public string Format(bool merge) => merge ? MergedText : Text;
}

/// <summary>Offline text recognition through the built-in Windows.Media.Ocr engine.</summary>
internal static class OcrService
{
    /// <summary>Tags of OCR languages installed on this PC, e.g. "ko", "en-US".</summary>
    public static IReadOnlyList<string> AvailableLanguages =>
        OcrEngine.AvailableRecognizerLanguages.Select(l => l.LanguageTag).ToList();

    public static bool HasKorean => AvailableLanguages.Any(IsKoreanTag);

    private static bool IsKoreanTag(string tag) => tag.StartsWith("ko", StringComparison.OrdinalIgnoreCase);

    private static bool IsEnglishTag(string tag) => tag.StartsWith("en", StringComparison.OrdinalIgnoreCase);

    public static string MissingKoreanHelp =>
        "Korean OCR is not installed. Install it from Settings > Time & language > Language & region > " +
        "Korean > Language options (Optical character recognition), or in an admin PowerShell run:\n" +
        "Add-WindowsCapability -Online -Name \"Language.OCR~~~ko-KR~0.0.1.0\"";

    private static OcrEngine? TryEngine(string tag)
    {
        try
        {
            var lang = new WinLanguage(tag);
            return OcrEngine.IsLanguageSupported(lang) ? OcrEngine.TryCreateFromLanguage(lang) : null;
        }
        catch
        {
            return null;
        }
    }

    /// <summary>Korean first (it also reads Latin text), then the user-profile engine, then English.</summary>
    private static List<OcrEngine> EnginesInPreferenceOrder()
    {
        var list = new List<OcrEngine>();
        void Add(OcrEngine? e)
        {
            if (e != null && list.All(x => x.RecognizerLanguage.LanguageTag != e.RecognizerLanguage.LanguageTag))
                list.Add(e);
        }

        foreach (var tag in AvailableLanguages.Where(IsKoreanTag)) Add(TryEngine(tag));
        try { Add(OcrEngine.TryCreateFromUserProfileLanguages()); } catch { }
        foreach (var tag in AvailableLanguages.Where(IsEnglishTag)) Add(TryEngine(tag));
        foreach (var tag in AvailableLanguages) Add(TryEngine(tag));
        return list;
    }

    public static bool IsWindowsEngine(string? engine) =>
        string.Equals(engine, "windows", StringComparison.OrdinalIgnoreCase);

    /// <summary>
    /// PaddleOCR (default) or Windows OCR. Paddle loads its models only for this call and frees them afterwards;
    /// if its models are missing or it fails, Windows OCR is used instead.
    /// </summary>
    public static async Task<OcrOutcome> RecognizeAsync(Bitmap source, string? engine = "paddle", int? forceScale = null)
    {
        string? note = null;
        if (!IsWindowsEngine(engine))
        {
            if (PaddleOcr.IsAvailable)
            {
                try
                {
                    using var copy = new Bitmap(source);
                    var lines = await Task.Run(() => PaddleOcr.RecognizeAndRelease(copy));
                    return new OcrOutcome(lines, "PaddleOCR ko", 1);
                }
                catch (Exception ex)
                {
                    ErrorLog.Write(ex);
                    note = "PaddleOCR failed, used Windows OCR";
                }
            }
            else note = "PaddleOCR models missing, used Windows OCR";
        }
        var outcome = await RecognizeWindowsAsync(source, forceScale);
        return note == null ? outcome : outcome with { Note = note };
    }

    private static async Task<OcrOutcome> RecognizeWindowsAsync(Bitmap source, int? forceScale)
    {
        var engines = EnginesInPreferenceOrder();
        if (engines.Count == 0)
            throw new InvalidOperationException("No Windows OCR language is installed on this PC.");

        int scale = forceScale is > 0 ? forceScale.Value : ChooseScale(source.Width, source.Height);
        using var prepared = Prepare(source, scale);
        using var sb = ToSoftwareBitmap(prepared);

        var first = engines[0];
        var result = await first.RecognizeAsync(sb);
        var outcome = new OcrOutcome(ToLines(result), first.RecognizerLanguage.LanguageTag, scale);

        // A Korean engine on purely Latin text can drop spaces; if nothing Hangul came out, prefer English.
        if (IsKoreanTag(outcome.LanguageTag) && !ContainsHangul(outcome.Text))
        {
            var en = engines.FirstOrDefault(e => IsEnglishTag(e.RecognizerLanguage.LanguageTag));
            if (en != null)
            {
                var r2 = await en.RecognizeAsync(sb);
                var alt = new OcrOutcome(ToLines(r2), en.RecognizerLanguage.LanguageTag, scale);
                if (alt.Text.Trim().Length >= outcome.Text.Trim().Length * 0.8) outcome = alt;
            }
        }
        return outcome;
    }

    private static bool ContainsHangul(string s) =>
        s.Any(c => c is >= '\uAC00' and <= '\uD7A3' or >= '\u1100' and <= '\u11FF' or >= '\u3130' and <= '\u318F');

    private static int ChooseScale(int w, int h)
    {
        int scale = h < 120 || w < 120 ? 3 : h < 600 || w < 600 ? 2 : 1;
        uint max = OcrEngine.MaxImageDimension;
        while (scale > 1 && (w * scale > max || h * scale > max)) scale--;
        return scale;
    }

    private static Bitmap Prepare(Bitmap src, int scale)
    {
        uint max = OcrEngine.MaxImageDimension;
        double fit = Math.Min(1.0, Math.Min(max / (double)(src.Width * scale), max / (double)(src.Height * scale)));
        int w = Math.Max(1, (int)(src.Width * scale * fit));
        int h = Math.Max(1, (int)(src.Height * scale * fit));
        var bmp = new Bitmap(w, h, PixelFormat.Format32bppArgb);
        using var g = Graphics.FromImage(bmp);
        g.Clear(Color.White);
        g.InterpolationMode = InterpolationMode.HighQualityBicubic;
        g.PixelOffsetMode = PixelOffsetMode.HighQuality;
        using var ia = new ImageAttributes();
        ia.SetWrapMode(WrapMode.TileFlipXY);
        g.DrawImage(src, new Rectangle(0, 0, w, h), 0, 0, src.Width, src.Height, GraphicsUnit.Pixel, ia);
        return bmp;
    }

    private static WinSoftwareBitmap ToSoftwareBitmap(Bitmap bmp)
    {
        var rect = new Rectangle(0, 0, bmp.Width, bmp.Height);
        var data = bmp.LockBits(rect, ImageLockMode.ReadOnly, PixelFormat.Format32bppArgb);
        try
        {
            int stride = bmp.Width * 4;
            var bytes = new byte[stride * bmp.Height];
            for (int y = 0; y < bmp.Height; y++)
                Marshal.Copy(data.Scan0 + y * data.Stride, bytes, y * stride, stride);
            var buffer = Windows.Security.Cryptography.CryptographicBuffer.CreateFromByteArray(bytes);
            return WinSoftwareBitmap.CreateCopyFromBuffer(buffer, WinBitmapPixelFormat.Bgra8, bmp.Width, bmp.Height,
                WinBitmapAlphaMode.Premultiplied);
        }
        finally
        {
            bmp.UnlockBits(data);
        }
    }

    private static List<OcrLine> ToLines(OcrResult result)
    {
        var lines = new List<OcrLine>();
        foreach (var line in result.Lines)
        {
            if (line.Words.Count == 0) continue;
            double l = double.MaxValue, t = double.MaxValue, r = double.MinValue, b = double.MinValue;
            foreach (var w in line.Words)
            {
                var br = w.BoundingRect;
                l = Math.Min(l, br.X);
                t = Math.Min(t, br.Y);
                r = Math.Max(r, br.X + br.Width);
                b = Math.Max(b, br.Y + br.Height);
            }
            lines.Add(new OcrLine(line.Text, RectangleF.FromLTRB((float)l, (float)t, (float)r, (float)b),
                (float)line.Words[0].BoundingRect.Width));
        }
        return lines;
    }
}
