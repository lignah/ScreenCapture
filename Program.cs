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
        int bi = Array.IndexOf(args, "--ocr-bench");
        if (bi >= 0) return RunOcrBench(args, bi);
        int ct = Array.IndexOf(args, "--ocr-copy-test");
        if (ct >= 0) return RunOcrCopyTest(args, ct);
        int o = Array.IndexOf(args, "--ocr-test");
        if (o >= 0) return RunOcrTest(args, o);
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

    /// <summary>--ocr-test &lt;image&gt; [--out file]: prints languages + recognized text to stdout and a UTF-8 file.</summary>
    private static int RunOcrTest(string[] args, int index)
    {
        string image = index + 1 < args.Length ? Path.GetFullPath(args[index + 1]) : "";
        int oi = Array.IndexOf(args, "--out");
        string outPath = oi >= 0 && oi + 1 < args.Length ? Path.GetFullPath(args[oi + 1]) : image + ".ocr.txt";
        var sb = new System.Text.StringBuilder();
        int code = 0;
        try
        {
            sb.AppendLine("available: " + string.Join(", ", OcrService.AvailableLanguages));
            if (!OcrService.HasKorean) sb.AppendLine("note: " + OcrService.MissingKoreanHelp);
            using var bmp = new Bitmap(image);
            int si = Array.IndexOf(args, "--scale");
            int? scale = si >= 0 && si + 1 < args.Length && int.TryParse(args[si + 1], out var sv) ? sv : null;
            int ei = Array.IndexOf(args, "--engine");
            string engine = ei >= 0 && ei + 1 < args.Length ? args[ei + 1] : "paddle";
            var sw = System.Diagnostics.Stopwatch.StartNew();
            var outcome = Task.Run(() => OcrService.RecognizeAsync(bmp, engine, scale)).GetAwaiter().GetResult();
            sb.AppendLine($"elapsed: {sw.ElapsedMilliseconds} ms" + (outcome.Note != null ? $"  note: {outcome.Note}" : ""));
            if (!OcrService.IsWindowsEngine(engine)) sb.AppendLine("paddle: " + PaddleOcr.LastTimings);
            sb.AppendLine($"engine: {outcome.LanguageTag}  scale: {outcome.Scale}x  lines: {outcome.LineCount}  image: {bmp.Width}x{bmp.Height}");
            if (args.Contains("--boxes"))
                foreach (var l in outcome.Lines)
                    sb.AppendLine($"[L={l.Box.Left:0} R={l.Box.Right:0} T={l.Box.Top:0} B={l.Box.Bottom:0} cw={l.CharWidth:0.0} w0={l.FirstWordWidth:0}] {l.Text}");
            sb.AppendLine("----- 원본 줄바꿈 (raw) -----");
            sb.AppendLine(outcome.Text);
            sb.AppendLine("----- 문단으로 합치기 (merged) -----");
            sb.AppendLine(outcome.MergedText);
        }
        catch (Exception ex)
        {
            sb.AppendLine("error: " + ex);
            code = 1;
        }
        var text = sb.ToString();
        try { File.WriteAllText(outPath, text, new System.Text.UTF8Encoding(false)); } catch { }
        try
        {
            using var stdout = new StreamWriter(Console.OpenStandardOutput(), new System.Text.UTF8Encoding(false));
            stdout.Write(text);
        }
        catch { }
        return code;
    }

    private static class ClipNative
    {
        [System.Runtime.InteropServices.DllImport("user32.dll", SetLastError = true)]
        public static extern bool OpenClipboard(IntPtr owner);
        [System.Runtime.InteropServices.DllImport("user32.dll", SetLastError = true)]
        public static extern bool CloseClipboard();
    }

    private static string Arg(string[] args, string name, string fallback)
    {
        int i = Array.IndexOf(args, name);
        return i >= 0 && i + 1 < args.Length ? args[i + 1] : fallback;
    }

    /// <summary>
    /// --ocr-copy-test &lt;image&gt; [--engine paddle|windows] [--merge on|off] [--autocopy on|off] [--lock-ms N]:
    /// runs the real OCR result window (async OCR, auto-copy on the UI thread), then reads the clipboard back and
    /// compares it to the window text. Then checks the retry path while another thread holds the clipboard open.
    /// The previous clipboard text is restored afterwards. Output: stdout + &lt;image&gt;.copytest.txt.
    /// </summary>
    private static int RunOcrCopyTest(string[] args, int index)
    {
        string image = Path.GetFullPath(args[index + 1]);
        var settings = new AppSettings
        {
            OcrEngine = Arg(args, "--engine", "paddle"),
            OcrMergeParagraphs = Arg(args, "--merge", "on") != "off",
            OcrAutoCopy = Arg(args, "--autocopy", "on") != "off",
        };
        int lockMs = int.TryParse(Arg(args, "--lock-ms", "400"), out var lm) ? lm : 400;
        var sb = new System.Text.StringBuilder();
        int code = 0;
        string? previous = Clipboard.ContainsText() ? Clipboard.GetText() : null;
        const string sentinel = "__ocr_copy_test_sentinel__";
        try
        {
            Clipboard.SetText(sentinel);
            sb.AppendLine($"engine={settings.OcrEngine} merge={settings.OcrMergeParagraphs} autocopy={settings.OcrAutoCopy} thread={Thread.CurrentThread.GetApartmentState()}");
            string shown = "";
            bool copied = false, done = false;
            var bmp = new Bitmap(image);
            var form = new OcrResultForm(bmp, new Point(100, 100), settings);
            form.Completed += (text, c) =>
            {
                shown = text;
                copied = c;
                done = true;
                sb.AppendLine($"completed on UI thread: {!form.InvokeRequired}, apartment {Thread.CurrentThread.GetApartmentState()}");
                form.BeginInvoke(form.Close);
            };
            var timeout = new System.Windows.Forms.Timer { Interval = 60000 };
            timeout.Tick += (_, _) => { timeout.Stop(); form.Close(); };
            timeout.Start();
            Application.Run(form);
            timeout.Dispose();
            string clip = Clipboard.ContainsText() ? Clipboard.GetText() : "";
            bool expectCopy = settings.OcrAutoCopy && shown.Length > 0;
            bool ok = done && (expectCopy ? copied && clip == shown : clip == sentinel);
            sb.AppendLine($"completed={done} copied={copied} clipboardLines={clip.Split('\n').Length} windowLines={shown.Split('\n').Length}");
            sb.AppendLine("auto-copy check: " + (ok ? "PASS" : "FAIL") + (expectCopy ? " (clipboard == window text)" : " (clipboard untouched)"));
            if (!ok) code = 1;
            sb.AppendLine("----- clipboard -----");
            sb.AppendLine(clip);

            // Retry path: another thread holds the clipboard open for lockMs.
            using var opened = new ManualResetEventSlim();
            var holder = new Thread(() =>
            {
                bool got = ClipNative.OpenClipboard(IntPtr.Zero);
                opened.Set();
                Thread.Sleep(lockMs);
                if (got) ClipNative.CloseClipboard();
            });
            holder.Start();
            opened.Wait();
            var sw = System.Diagnostics.Stopwatch.StartNew();
            var err = ClipboardText.TrySet(shown.Length > 0 ? shown : "lock-test");
            sw.Stop();
            holder.Join();
            bool lockOk = err == null && sw.ElapsedMilliseconds >= lockMs - 60;
            sb.AppendLine($"locked-clipboard retry ({lockMs} ms lock): {(lockOk ? "PASS" : "FAIL")} after {sw.ElapsedMilliseconds} ms" + (err != null ? " error " + err : ""));
            if (!lockOk) code = 1;
        }
        catch (Exception ex)
        {
            sb.AppendLine("error: " + ex);
            code = 1;
        }
        finally
        {
            if (previous != null) ClipboardText.TrySet(previous);
            else try { Clipboard.Clear(); } catch { }
        }
        var outText = sb.ToString();
        try { File.WriteAllText(image + ".copytest.txt", outText, new System.Text.UTF8Encoding(false)); } catch { }
        try
        {
            using var stdout = new StreamWriter(Console.OpenStandardOutput(), new System.Text.UTF8Encoding(false));
            stdout.Write(outText);
        }
        catch { }
        return code;
    }

    /// <summary>--ocr-bench &lt;image&gt; [--engine paddle|windows] [--runs N]: latency + memory before/peak/after.</summary>
    private static int RunOcrBench(string[] args, int index)
    {
        string image = Path.GetFullPath(args[index + 1]);
        int ei = Array.IndexOf(args, "--engine");
        string engine = ei >= 0 && ei + 1 < args.Length ? args[ei + 1] : "paddle";
        int ri = Array.IndexOf(args, "--runs");
        int runs = ri >= 0 && ri + 1 < args.Length && int.TryParse(args[ri + 1], out var rv) ? rv : 3;
        var sb = new System.Text.StringBuilder();
        var proc = System.Diagnostics.Process.GetCurrentProcess();
        string Mem()
        {
            proc.Refresh();
            return $"WS {proc.WorkingSet64 / 1048576.0:0.0} MB, private {proc.PrivateMemorySize64 / 1048576.0:0.0} MB";
        }

        using var bmp = new Bitmap(image);
        PaddleOcr.ReleaseMemory();
        Thread.Sleep(500);
        sb.AppendLine($"engine={engine} image={bmp.Width}x{bmp.Height} paddleModels={PaddleOcr.IsAvailable}");
        sb.AppendLine("before:  " + Mem());
        for (int r = 1; r <= runs; r++)
        {
            long peakWs = 0, peakPriv = 0;
            bool stop = false;
            var sampler = new Thread(() =>
            {
                using var p = System.Diagnostics.Process.GetCurrentProcess();
                while (!Volatile.Read(ref stop))
                {
                    p.Refresh();
                    peakWs = Math.Max(peakWs, p.WorkingSet64);
                    peakPriv = Math.Max(peakPriv, p.PrivateMemorySize64);
                    Thread.Sleep(5);
                }
            }) { IsBackground = true };
            sampler.Start();
            var sw = System.Diagnostics.Stopwatch.StartNew();
            var outcome = Task.Run(() => OcrService.RecognizeAsync(bmp, engine)).GetAwaiter().GetResult();
            long ms = sw.ElapsedMilliseconds;
            Volatile.Write(ref stop, true);
            sampler.Join();
            if (OcrService.IsWindowsEngine(engine)) PaddleOcr.ReleaseMemory();
            Thread.Sleep(1000);
            sb.AppendLine($"run {r}: {ms} ms, lines {outcome.LineCount} ({outcome.LanguageTag}{(outcome.Note != null ? ", " + outcome.Note : "")}) | " +
                          $"peak WS {peakWs / 1048576.0:0.0} MB, private {peakPriv / 1048576.0:0.0} MB | after: {Mem()}");
        }
        var text = sb.ToString();
        try { File.WriteAllText(image + ".bench.txt", text); } catch { }
        try
        {
            using var stdout = new StreamWriter(Console.OpenStandardOutput(), new System.Text.UTF8Encoding(false));
            stdout.Write(text);
        }
        catch { }
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
