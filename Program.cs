namespace ScreenCapture;

internal static class Program
{
    [STAThread]
    private static int Main(string[] args)
    {
        Application.SetHighDpiMode(HighDpiMode.PerMonitorV2);
        Application.EnableVisualStyles();
        Application.SetCompatibleTextRenderingDefault(false);

        // Hidden test hooks.
        int i = Array.IndexOf(args, "--fullscreen-test");
        if (i >= 0) return RunTest(args, i, overlay: false);
        i = Array.IndexOf(args, "--overlay-selftest");
        if (i >= 0) return RunTest(args, i, overlay: true);

        using var mutex = new Mutex(true, @"Local\ScreenCapture-SingleInstance-7F3A1C2E", out bool createdNew);
        if (!createdNew) return 0;

        Application.SetUnhandledExceptionMode(UnhandledExceptionMode.CatchException);
        Application.ThreadException += (_, e) =>
        {
            ErrorLog.Write(e.Exception);
            MessageBox.Show($"Unexpected error: {e.Exception.Message}\n\nDetails: {ErrorLog.Path}",
                "Screen Capture", MessageBoxButtons.OK, MessageBoxIcon.Error);
        };
        AppDomain.CurrentDomain.UnhandledException += (_, e) => ErrorLog.Write(e.ExceptionObject as Exception);

        Application.Run(new TrayContext(startCapture: args.Contains("--capture")));
        GC.KeepAlive(mutex);
        return 0;
    }

    private static int RunTest(string[] args, int index, bool overlay)
    {
        string path = index + 1 < args.Length
            ? args[index + 1]
            : Path.Combine(Path.GetTempPath(), overlay ? "screencapture-overlay-test.png" : "screencapture-fullscreen-test.png");
        try
        {
            path = Path.GetFullPath(path);
            var windows = overlay ? ScreenGrabber.GetWindowRects() : new List<Rectangle>();
            var bmp = ScreenGrabber.CaptureVirtualScreen(out var vb);
            int w = bmp.Width, h = bmp.Height;
            if (!overlay)
            {
                using (bmp) ImageOutput.Save(bmp, path);
            }
            else
            {
                using var form = new OverlayForm(bmp, vb, windows, new AppSettings());
                using var result = form.SelfTestRender();
                w = result.Width;
                h = result.Height;
                ImageOutput.Save(result, path);
            }
            var monitors = string.Join("; ", Screen.AllScreens.Select(s =>
                $"{s.DeviceName} {s.Bounds} dpiScale={ScreenGrabber.DpiScaleAt(s.Bounds.Location)}"));
            File.WriteAllText(path + ".info.txt",
                $"ok image={w}x{h} virtual={vb} windows={windows.Count}{Environment.NewLine}monitors: {monitors}{Environment.NewLine}");
            return 0;
        }
        catch (Exception ex)
        {
            try { File.WriteAllText(path + ".error.txt", ex.ToString()); } catch { }
            return 1;
        }
    }
}
