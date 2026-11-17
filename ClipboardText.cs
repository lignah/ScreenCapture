using System.Runtime.InteropServices;

namespace ScreenCapture;

/// <summary>Sets clipboard text on an STA thread, retrying while another process holds the clipboard open.</summary>
internal static class ClipboardText
{
    public const int Attempts = 20;
    public const int DelayMs = 50;

    /// <summary>Returns null on success, otherwise the error message.</summary>
    public static string? TrySet(string text)
    {
        if (Thread.CurrentThread.GetApartmentState() != ApartmentState.STA)
        {
            string? result = null;
            var t = new Thread(() => result = TrySet(text));
            t.SetApartmentState(ApartmentState.STA);
            t.Start();
            t.Join();
            return result;
        }
        Exception? last = null;
        for (int i = 0; i < Attempts; i++)
        {
            try
            {
                Clipboard.SetDataObject(text, true, 1, 0);
                return null;
            }
            catch (ExternalException ex)
            {
                last = ex;
                Thread.Sleep(DelayMs);
            }
        }
        if (last != null) ErrorLog.Write(last);
        return last?.Message ?? "clipboard unavailable";
    }
}
