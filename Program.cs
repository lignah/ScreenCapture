namespace ScreenCapture;

internal static class Program
{
    /// <summary>Product version (csproj &lt;Version&gt;, set from the git tag in release builds), e.g. "1.0.0".</summary>
    public static string AppVersion
    {
        get
        {
            var v = typeof(Program).Assembly.GetName().Version;
            return v == null ? "" : $"{v.Major}.{v.Minor}.{v.Build}";
        }
    }

    [STAThread]
    private static int Main(string[] args)
    {
        Application.SetHighDpiMode(HighDpiMode.PerMonitorV2);
        Application.EnableVisualStyles();
        Application.SetCompatibleTextRenderingDefault(false);

        // Hidden test hooks.
        int bi = Array.IndexOf(args, "--ocr-bench");
        if (bi >= 0) return RunOcrBench(args, bi);
        if (args.Contains("--lang-list") || args.Contains("--lang-add") || args.Contains("--lang-delete")) return RunLangTool(args);
        int it = Array.IndexOf(args, "--icon-test");
        if (it >= 0)
        {
            // Loads the embedded tray icons (normal + OCR busy) at several sizes and saves PNGs to <dir>.
            string dir = Path.GetFullPath(args[it + 1]);
            Directory.CreateDirectory(dir);
            var sb = new System.Text.StringBuilder($"tray size={SystemInformation.SmallIconSize}{Environment.NewLine}");
            foreach (var name in new[] { "app", "app_busy" })
                foreach (int n in new[] { 16, 20, 24, 32, 48, 256 })
                {
                    using var stream = typeof(Program).Assembly.GetManifestResourceStream($"ScreenCapture.{name}.ico");
                    if (stream == null) { sb.AppendLine($"{name}: resource missing"); break; }
                    using var icon = new Icon(stream, n, n);
                    using var bmp = icon.ToBitmap();
                    bmp.Save(Path.Combine(dir, $"{name}_{n}.png"), System.Drawing.Imaging.ImageFormat.Png);
                    sb.AppendLine($"{name} {n}: loaded {icon.Width}x{icon.Height}");
                }
            Emit(sb, null, 0);
            return 0;
        }
        if (args.Contains("--log-test"))
        {
            // Writes one test entry through ErrorLog (exercises the ~1 MB rotation).
            ErrorLog.Write(new Exception("log test"));
            return 0;
        }
        if (args.Contains("--startup-refresh-test"))
        {
            // Runs the launch-time start-with-Windows path refresh and prints the resulting Run value.
            AppSettings.RefreshStartupPath();
            using var key = Microsoft.Win32.Registry.CurrentUser.OpenSubKey(@"Software\Microsoft\Windows\CurrentVersion\Run");
            Emit(new System.Text.StringBuilder($"Run value: {key?.GetValue("ScreenCapture") ?? "(not set)"}{Environment.NewLine}exe: {Environment.ProcessPath}{Environment.NewLine}"), null, 0);
            return 0;
        }
        if (args.Contains("--settings-test"))
        {
            // Prints the effective OCR settings after AppSettings.Load() (verifies engine migration).
            var st = AppSettings.Load();
            Emit(new System.Text.StringBuilder($"OcrEngine={st.OcrEngine} OcrLanguage={st.OcrLanguage} merge={st.OcrMergeParagraphs}{Environment.NewLine}"), null, 0);
            return 0;
        }
        int ct = Array.IndexOf(args, "--ocr-copy-test");
        if (ct >= 0) return RunOcrCopyTest(args, ct);
        int o = Array.IndexOf(args, "--ocr-test");
        if (o >= 0) return RunOcrTest(args, o);
        int i = Array.IndexOf(args, "--fullscreen-test");
        if (i >= 0) return RunTest(args, i, overlay: false);
        i = Array.IndexOf(args, "--magnifier-selftest");
        if (i >= 0) return RunMagnifierTest(args, i);
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
            var outcome = Task.Run(() => OcrService.RecognizeAsync(bmp, engine, scale, Arg(args, "--lang", OcrLanguages.DefaultId))).GetAwaiter().GetResult();
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
    /// --ocr-copy-test &lt;image&gt; [--engine paddle|windows] [--merge on|off] [--lang id] [--lock-ms N]:
    /// runs the window-less tray OCR path (background OCR, clipboard write on the STA UI thread), reads the clipboard
    /// back and compares it with the produced text, then checks the retry path while another thread holds the
    /// clipboard open. The previous clipboard text is restored. Output: stdout + &lt;image&gt;.copytest.txt.
    /// </summary>
    private static int RunOcrCopyTest(string[] args, int index)
    {
        string image = Path.GetFullPath(args[index + 1]);
        var settings = new AppSettings
        {
            OcrEngine = Arg(args, "--engine", "paddle"),
            OcrMergeParagraphs = Arg(args, "--merge", "on") != "off",
            OcrLanguage = Arg(args, "--lang", OcrLanguages.DefaultId),
        };
        int lockMs = int.TryParse(Arg(args, "--lock-ms", "400"), out var lm) ? lm : 400;
        var sb = new System.Text.StringBuilder();
        int code = 0;
        string? previous = Clipboard.ContainsText() ? Clipboard.GetText() : null;
        try
        {
            Clipboard.SetText("__ocr_copy_test_sentinel__");
            sb.AppendLine($"engine={settings.OcrEngine} lang={settings.OcrLanguage} merge={settings.OcrMergeParagraphs}");
            using var bmp = new Bitmap(image);
            OcrRunResult? r = null;
            var appCtx = new ApplicationContext();
            var start = new System.Windows.Forms.Timer { Interval = 1 };
            start.Tick += async (_, _) =>
            {
                start.Stop();
                r = await OcrRunner.RunAsync(bmp, settings);
                appCtx.ExitThread();
            };
            start.Start();
            Application.Run(appCtx);
            start.Dispose();
            string clip = Clipboard.ContainsText() ? Clipboard.GetText() : "";
            bool ok = r != null && r.Copied && clip == r.Text && OcrRunner.LastCopyApartment == ApartmentState.STA;
            sb.AppendLine($"ocr: {r?.ElapsedMs} ms, {r?.Outcome?.LanguageTag}, lines={r?.Lines}, copied={r?.Copied}, copy thread={OcrRunner.LastCopyApartment}" + (r?.Error != null ? ", error " + r.Error : "") + (r?.Outcome?.Note != null ? ", note " + r.Outcome.Note : ""));
            sb.AppendLine("notification: none (silent copy)");
            sb.AppendLine("auto-copy check: " + (ok ? "PASS (clipboard == OCR text)" : "FAIL"));
            if (!ok) code = 1;
            sb.AppendLine("----- clipboard -----");
            sb.AppendLine(clip);

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
            var err = ClipboardText.TrySet("lock-test");
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
        return Emit(sb, image + ".copytest.txt", code);
    }

    /// <summary>--lang-list | --lang-add &lt;id&gt; | --lang-delete &lt;id&gt; (PaddleOCR recognition languages).</summary>
    private static int RunLangTool(string[] args)
    {
        var sb = new System.Text.StringBuilder();
        int code = 0;
        try
        {
            string add = Arg(args, "--lang-add", ""), del = Arg(args, "--lang-delete", "");
            if (add.Length > 0)
            {
                var info = OcrLanguages.Find(add) ?? throw new ArgumentException("unknown language id " + add);
                var sw = System.Diagnostics.Stopwatch.StartNew();
                long last = -1;
                var progress = new SyncProgress<(long Done, long Total)>(p =>
                {
                    long pct = p.Total > 0 ? p.Done * 100 / p.Total : 0;
                    if (pct / 25 != last / 25) { last = pct; sb.AppendLine($"  {pct}% ({p.Done:N0}/{p.Total:N0})"); }
                });
                OcrLanguages.DownloadAsync(info, progress, CancellationToken.None).GetAwaiter().GetResult();
                sb.AppendLine($"added {info.Id} ({info.Name}) in {sw.ElapsedMilliseconds} ms, SHA-256 verified");
            }
            if (del.Length > 0)
            {
                var dir = Path.Combine(OcrLanguages.UserDir, del);
                bool ok = OcrLanguages.Delete(del);
                sb.AppendLine($"delete {del}: {(ok ? "ok" : "not deleted (bundled or not installed)")}; folder exists: {Directory.Exists(dir)}");
                if (!ok) code = 1;
            }
            sb.AppendLine("user model folder: " + OcrLanguages.UserDir);
            sb.AppendLine("installed:");
            foreach (var l in OcrLanguages.Installed())
                sb.AppendLine($"  {l.Info.Id,-10} {l.Info.Name} [{l.Info.Version}]{(l.Bundled ? " (bundled)" : "")} {new FileInfo(l.RecFile).Length:N0} B  {l.RecFile}");
            sb.AppendLine("catalog: " + string.Join(", ", OcrLanguages.Catalog.Select(c => c.Id)));
        }
        catch (Exception ex)
        {
            sb.AppendLine("error: " + ex.Message);
            code = 1;
        }
        return Emit(sb, null, code);
    }

    private sealed class SyncProgress<T>(Action<T> report) : IProgress<T>
    {
        public void Report(T value) => report(value);
    }

    private static int Emit(System.Text.StringBuilder sb, string? file, int code)
    {
        var outText = sb.ToString();
        if (file != null) try { File.WriteAllText(file, outText, new System.Text.UTF8Encoding(false)); } catch { }
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

    /// <summary>--magnifier-selftest &lt;png&gt;: renders the magnifier over the current screen at (200,200) and writes
    /// the crop plus &lt;png&gt;.info.txt with the #RRGGBB shown.</summary>
    private static int RunMagnifierTest(string[] args, int index)
    {
        string path = Path.GetFullPath(args[index + 1]);
        try
        {
            var bmp = ScreenGrabber.CaptureVirtualScreen(out var vb);
            using var form = new OverlayForm(bmp, vb, new List<Rectangle>(), new AppSettings());
            var at = new Point(200, 200);
            using (var crop = form.MagnifierSelfTest(at, out var hex))
            {
                ImageOutput.Save(crop, path);
                var info = new System.Text.StringBuilder();
                bool fmt = System.Text.RegularExpressions.Regex.IsMatch(hex, "^#[0-9A-F]{6}$");
                info.AppendLine($"ok color={hex} format={(fmt ? "PASS" : "FAIL")}");

                // Copy path: Ctrl+C must not copy the color (no selection -> nothing), plain C copies #RRGGBB.
                string? previous = Clipboard.ContainsText() ? Clipboard.GetText() : null;
                const string sentinel = "__magnifier_test_sentinel__";
                try
                {
                    ClipboardText.TrySet(sentinel);
                    form.SimulateKey(Keys.Control | Keys.C);
                    bool ctrlOk = Clipboard.GetText() == sentinel;
                    bool handled = form.SimulateKey(Keys.C);
                    string clip = Clipboard.GetText();
                    bool cOk = handled && clip == hex && form.IsDisposed;
                    info.AppendLine($"ctrl+c leaves clipboard alone: {(ctrlOk ? "PASS" : "FAIL")}");
                    info.AppendLine($"plain C copies color and closes overlay: {(cOk ? "PASS" : "FAIL")} clipboard={clip} overlayClosed={form.IsDisposed}");
                }
                finally
                {
                    if (previous != null) ClipboardText.TrySet(previous);
                    else try { Clipboard.Clear(); } catch { }
                }
                File.WriteAllText(path + ".info.txt", info.ToString());
            }
            return 0;
        }
        catch (Exception ex)
        {
            try { File.WriteAllText(path + ".error.txt", ex.ToString()); } catch { }
            return 1;
        }
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
