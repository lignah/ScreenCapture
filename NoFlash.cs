using System.Runtime.InteropServices;

namespace ScreenCapture;

/// <summary>
/// Shows a window without the white frame DWM draws before the first WM_PAINT: the window is cloaked
/// (invisible but still rendered) and has show/hide animations disabled; after a synchronous first paint
/// it is uncloaked, so the first visible frame already holds our content.
/// </summary>
internal static class NoFlash
{
    private const int DWMWA_TRANSITIONS_FORCEDISABLED = 3;
    private const int DWMWA_CLOAK = 13;
    public const int WM_ERASEBKGND = 0x0014;

    [DllImport("dwmapi.dll")]
    private static extern int DwmSetWindowAttribute(IntPtr hwnd, int attr, ref int value, int size);

    [DllImport("dwmapi.dll")]
    private static extern int DwmFlush();

    /// <summary>Call from OnHandleCreated (before the window is first shown).</summary>
    public static void Prepare(IntPtr hwnd)
    {
        int on = 1;
        DwmSetWindowAttribute(hwnd, DWMWA_TRANSITIONS_FORCEDISABLED, ref on, sizeof(int));
        DwmSetWindowAttribute(hwnd, DWMWA_CLOAK, ref on, sizeof(int));
    }

    /// <summary>Call from OnShown: paints synchronously, waits for composition, then uncloaks.</summary>
    public static void Reveal(Control c)
    {
        try
        {
            c.Refresh();
            DwmFlush();
        }
        catch (Exception ex)
        {
            ErrorLog.Write(ex);
        }
        finally
        {
            int off = 0;
            DwmSetWindowAttribute(c.Handle, DWMWA_CLOAK, ref off, sizeof(int));
        }
    }
}
