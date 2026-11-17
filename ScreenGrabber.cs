using System.Drawing.Imaging;
using System.Text;

namespace ScreenCapture;

internal static class ScreenGrabber
{
    /// <summary>Virtual desktop bounds in physical pixels (process is per-monitor DPI aware).</summary>
    public static Rectangle VirtualBounds => new(
        Native.GetSystemMetrics(Native.SM_XVIRTUALSCREEN),
        Native.GetSystemMetrics(Native.SM_YVIRTUALSCREEN),
        Native.GetSystemMetrics(Native.SM_CXVIRTUALSCREEN),
        Native.GetSystemMetrics(Native.SM_CYVIRTUALSCREEN));

    public static Bitmap CaptureVirtualScreen(out Rectangle bounds)
    {
        bounds = VirtualBounds;
        var bmp = new Bitmap(bounds.Width, bounds.Height, PixelFormat.Format32bppPArgb);
        using var g = Graphics.FromImage(bmp);
        g.CopyFromScreen(bounds.Left, bounds.Top, 0, 0, bounds.Size,
            CopyPixelOperation.SourceCopy);
        return bmp;
    }

    /// <summary>Visible top-level window rectangles (screen coords), front to back.</summary>
    public static List<Rectangle> GetWindowRects()
    {
        var list = new List<Rectangle>();
        var sb = new StringBuilder(256);
        Native.EnumWindows((h, _) =>
        {
            if (!Native.IsWindowVisible(h) || Native.IsIconic(h)) return true;
            if (Native.DwmGetWindowAttribute(h, Native.DWMWA_CLOAKED, out int cloaked, sizeof(int)) == 0 && cloaked != 0)
                return true;

            long ex = Native.GetWindowLongPtr(h, Native.GWL_EXSTYLE).ToInt64();
            if ((ex & Native.WS_EX_TRANSPARENT) != 0) return true;

            sb.Clear();
            Native.GetClassName(h, sb, sb.Capacity);
            var cls = sb.ToString();
            if (cls is "Progman" or "WorkerW") return true;

            sb.Clear();
            Native.GetWindowText(h, sb, sb.Capacity);
            if (sb.Length == 0 && (ex & Native.WS_EX_TOOLWINDOW) != 0) return true;

            Rectangle r;
            if (Native.DwmGetWindowAttribute(h, Native.DWMWA_EXTENDED_FRAME_BOUNDS, out Native.RECT rc,
                    System.Runtime.InteropServices.Marshal.SizeOf<Native.RECT>()) == 0)
                r = rc.ToRectangle();
            else if (Native.GetWindowRect(h, out rc))
                r = rc.ToRectangle();
            else
                return true;

            if (r.Width >= 4 && r.Height >= 4) list.Add(r);
            return true;
        }, IntPtr.Zero);
        return list;
    }

    public static float DpiScaleAt(Point screenPoint)
    {
        var mon = Native.MonitorFromPoint(new Native.POINT { X = screenPoint.X, Y = screenPoint.Y }, 2);
        return Native.GetDpiForMonitor(mon, 0, out uint dx, out _) == 0 ? dx / 96f : 1f;
    }
}
