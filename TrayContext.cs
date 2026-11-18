using System.Diagnostics;

namespace ScreenCapture;

internal sealed class TrayContext : ApplicationContext
{
    private const int HotkeyId = 0xA11;
    private const int FullHotkeyId = 0xA12;

    private static Icon? _appIcon;

    private readonly AppSettings _settings = AppSettings.Load();
    private readonly NotifyIcon _tray;
    private readonly HotkeyWindow _hotkeyWindow = new();
    private bool _hotkeyOk;
    private string _hotkeyText = HotkeyParser.Default;
    private bool _fullOk;
    private string _fullText = "";
    private OverlayForm? _overlay;
    private SettingsForm? _settingsForm;
    private string? _lastSavedPath;

    public static Icon AppIcon
    {
        get
        {
            if (_appIcon != null) return _appIcon;
            try
            {
                using var stream = typeof(TrayContext).Assembly.GetManifestResourceStream("ScreenCapture.app.ico");
                _appIcon = stream != null ? new Icon(stream, SystemInformation.SmallIconSize) : SystemIcons.Application;
            }
            catch
            {
                _appIcon = SystemIcons.Application;
            }
            return _appIcon;
        }
    }

    public TrayContext(bool startCapture)
    {
        var menu = new ContextMenuStrip();
        var showWindow = new ToolStripMenuItem("결과 창 표시") { CheckOnClick = true };
        showWindow.CheckedChanged += (_, _) => { _settings.OcrShowWindow = showWindow.Checked; _settings.Save(); };
        var merge = new ToolStripMenuItem("문단으로 합치기") { CheckOnClick = true };
        merge.CheckedChanged += (_, _) => { _settings.OcrMergeParagraphs = merge.Checked; _settings.Save(); };
        var languages = new ToolStripMenuItem("OCR 언어");
        languages.DropDownItems.Add("…"); // filled on open
        var ocrSettings = new ToolStripMenuItem("OCR Settings");
        ocrSettings.DropDownItems.AddRange(new ToolStripItem[] { merge, showWindow, new ToolStripSeparator(), languages });
        menu.Items.Add(ocrSettings);
        var thickness = new ToolStripMenuItem("Default Thickness");
        var thickItems = new[] { "Thin", "Medium", "Thick" }.Select((name, i) =>
            new ToolStripMenuItem(name, null, (_, _) => { _settings.DefaultThickness = i; _settings.Save(); })).ToArray();
        thickness.DropDownItems.AddRange(thickItems);
        menu.Items.Add(thickness);
        // Settings can also change in the Settings dialog or the OCR window; refresh check marks on open.
        menu.Opening += (_, _) =>
        {
            showWindow.Checked = _settings.OcrShowWindow;
            for (int i = 0; i < thickItems.Length; i++) thickItems[i].Checked = _settings.DefaultThickness == i;
            merge.Checked = _settings.OcrMergeParagraphs;
            FillLanguageMenu(languages);
        };
        menu.Items.Add("Open screenshots folder", null, (_, _) => OpenFolder());
        menu.Items.Add("Settings...", null, (_, _) => ShowSettings());
        menu.Items.Add(new ToolStripSeparator());
        menu.Items.Add("Exit", null, (_, _) => ExitApp());

        _tray = new NotifyIcon
        {
            Icon = AppIcon,
            ContextMenuStrip = menu,
            Text = "Screen Capture",
            Visible = true,
        };
        _tray.MouseClick += (_, e) =>
        {
            if (e.Button == MouseButtons.Left) StartRegionCapture();
        };
        _tray.BalloonTipClicked += (_, _) => OpenLastSaved();

        _hotkeyWindow.HotkeyPressed += id =>
        {
            if (id == HotkeyId) StartRegionCapture();
            else if (id == FullHotkeyId) FullScreenCapture();
        };
        RegisterHotkey();
        try { AppSettings.RefreshStartupPath(); } catch (Exception ex) { ErrorLog.Write(ex); }

        if (startCapture) RunLater(100, StartRegionCapture);
    }

    private string HotkeyStatus
    {
        get
        {
            var s = _hotkeyOk
                ? $"Current hotkey {_hotkeyText} is active (region capture)."
                : $"Current hotkey {_hotkeyText} could not be registered (in use by another app?). Pick another one above.";
            if (_fullText.Length > 0)
                s += Environment.NewLine + (_fullOk
                    ? $"Full screen hotkey {_fullText} is active."
                    : $"Full screen hotkey {_fullText} could not be registered (in use by another app?).");
            return s;
        }
    }

    private void RegisterHotkey()
    {
        if (_hotkeyOk) Native.UnregisterHotKey(_hotkeyWindow.Handle, HotkeyId);
        _hotkeyOk = false;
        _hotkeyText = string.IsNullOrWhiteSpace(_settings.Hotkey) ? HotkeyParser.Default : _settings.Hotkey.Trim();
        if (HotkeyParser.TryParse(_hotkeyText, out uint mods, out Keys key))
            _hotkeyOk = Native.RegisterHotKey(_hotkeyWindow.Handle, HotkeyId, mods | Native.MOD_NOREPEAT, (uint)key);

        if (_hotkeyOk)
        {
            _tray.Text = Truncate($"Screen Capture {Program.AppVersion} ({_hotkeyText})");
        }
        else
        {
            _tray.Text = Truncate($"Screen Capture {Program.AppVersion} (hotkey unavailable)");
            _tray.ShowBalloonTip(6000, "Screen Capture",
                $"{_hotkeyText} is already used by another app. Choose another hotkey in Settings, or click the tray icon to capture.",
                ToolTipIcon.Warning);
        }
        RegisterFullscreenHotkey();
    }

    /// <summary>Optional single-key (F1-F11) full-screen hotkey; nothing is registered when it is empty.</summary>
    private void RegisterFullscreenHotkey()
    {
        if (_fullOk) Native.UnregisterHotKey(_hotkeyWindow.Handle, FullHotkeyId);
        _fullOk = false;
        _fullText = (_settings.FullscreenHotkey ?? "").Trim();
        if (_fullText.Length == 0 || _fullText.Equals(HotkeyParser.None, StringComparison.OrdinalIgnoreCase))
        {
            _fullText = "";
            return;
        }
        if (HotkeyParser.TryParse(_fullText, out uint mods, out Keys key))
            _fullOk = Native.RegisterHotKey(_hotkeyWindow.Handle, FullHotkeyId, mods | Native.MOD_NOREPEAT, (uint)key);
        if (!_fullOk)
            _tray.ShowBalloonTip(6000, "Screen Capture",
                $"Full screen hotkey {_fullText} is already used by another app. Choose another one in Settings.",
                ToolTipIcon.Warning);
    }

    private static string Truncate(string s) => s.Length <= 63 ? s : s[..63];

    private static void RunLater(int ms, Action action)
    {
        var t = new System.Windows.Forms.Timer { Interval = ms };
        t.Tick += (_, _) =>
        {
            t.Stop();
            t.Dispose();
            action();
        };
        t.Start();
    }

    private void StartRegionCapture()
    {
        if (_overlay != null)
        {
            _overlay.Activate();
            return;
        }
        try
        {
            var windows = ScreenGrabber.GetWindowRects();
            var bmp = ScreenGrabber.CaptureVirtualScreen(out var vb);
            var overlay = new OverlayForm(bmp, vb, windows, _settings);
            overlay.OcrRequested += (img, near) => _ = RunOcr(img, near);
            overlay.PinRequested += (img, loc) =>
            {
                var pin = new PinForm(img, loc, _settings);
                pin.Saved += OnSaved;
                pin.Show();
            };
            overlay.Saved += OnSaved;
            overlay.FormClosed += (_, _) => _overlay = null;
            _overlay = overlay;
            overlay.Show();
        }
        catch (Exception ex)
        {
            _overlay = null;
            ErrorLog.Write(ex);
        }
    }

    private int _ocrBusy;
    private static Icon? _busyIcon;

    /// <summary>OCR-busy tray icon: same design with the crosshair and corner dot in amber (app_busy.ico).</summary>
    private static Icon BusyIcon
    {
        get
        {
            if (_busyIcon != null) return _busyIcon;
            try
            {
                using var stream = typeof(TrayContext).Assembly.GetManifestResourceStream("ScreenCapture.app_busy.ico");
                _busyIcon = stream != null ? new Icon(stream, SystemInformation.SmallIconSize) : AppIcon;
            }
            catch { _busyIcon = AppIcon; }
            return _busyIcon;
        }
    }

    /// <summary>OCR without a window: busy tray icon/tooltip, copy to clipboard, short notification.</summary>
    private async Task RunOcr(Bitmap img, Point near)
    {
        _ocrBusy++;
        string idleText = _tray.Text;
        _tray.Icon = BusyIcon;
        _tray.Text = "Screen Capture - 인식 중…";
        bool handedOff = false;
        try
        {
            var r = await OcrRunner.RunAsync(img, _settings);
            // Silent by design: no notification after OCR. Failures are already written to error.log
            // (OcrRunner logs exceptions, ClipboardText logs clipboard errors).
            if (_settings.OcrShowWindow && r.Outcome != null)
            {
                new OcrResultForm(img, near, _settings, r.Outcome, r.Copied).Show();
                handedOff = true;
            }
        }
        finally
        {
            if (!handedOff) img.Dispose();
            if (--_ocrBusy == 0)
            {
                _tray.Icon = AppIcon;
                _tray.Text = idleText;
            }
        }
    }

    private void FillLanguageMenu(ToolStripMenuItem menu)
    {
        menu.DropDownItems.Clear();
        var installed = OcrLanguages.Installed();
        var active = OcrLanguages.Resolve(_settings.OcrLanguage);
        if (installed.Count == 0)
            menu.DropDownItems.Add(new ToolStripMenuItem("설치된 PaddleOCR 언어 없음") { Enabled = false });
        foreach (var l in installed)
        {
            var id = l.Info.Id;
            var item = new ToolStripMenuItem(l.Info.Name + (l.Bundled ? "  (기본)" : ""), null, (_, _) =>
            {
                _settings.OcrLanguage = id;
                _settings.Save();
            })
            { Checked = active?.Info.Id == id };
            menu.DropDownItems.Add(item);
        }
        menu.DropDownItems.Add(new ToolStripSeparator());
        menu.DropDownItems.Add("언어 추가…", null, (_, _) => ShowLanguageDialog(add: true));
        menu.DropDownItems.Add("언어 삭제…", null, (_, _) => ShowLanguageDialog(add: false));
    }

    private OcrLanguageDialog? _langDialog;

    private void ShowLanguageDialog(bool add)
    {
        if (_langDialog != null) { _langDialog.Activate(); return; }
        _langDialog = new OcrLanguageDialog(add, _settings);
        _langDialog.FormClosed += (_, _) => _langDialog = null;
        _langDialog.Show();
    }

    /// <summary>Full-screen hotkey: copy every monitor to the clipboard and save a PNG to the screenshots folder, silently.</summary>
    private void FullScreenCapture()
    {
        if (_overlay != null) return; // region capture is open; don't grab its overlay
        try
        {
            using var bmp = ScreenGrabber.CaptureVirtualScreen(out _);
            ImageOutput.CopyToClipboard(bmp);
            _lastSavedPath = ImageOutput.SaveAuto(bmp, _settings.EffectiveFolder);
        }
        catch (Exception ex)
        {
            ErrorLog.Write(ex); // silent by design: no notification
        }
    }

    private void OnSaved(string path)
    {
        _lastSavedPath = path; // silent: no "Saved" notification
    }

    private void OpenFolder()
    {
        try
        {
            Directory.CreateDirectory(_settings.EffectiveFolder);
            Process.Start(new ProcessStartInfo("explorer.exe", $"\"{_settings.EffectiveFolder}\"") { UseShellExecute = true });
        }
        catch (Exception ex) { ErrorLog.Write(ex); }
    }

    private void OpenLastSaved()
    {
        try
        {
            if (_lastSavedPath != null && File.Exists(_lastSavedPath))
                Process.Start(new ProcessStartInfo("explorer.exe", $"/select,\"{_lastSavedPath}\"") { UseShellExecute = true });
            else OpenFolder();
        }
        catch (Exception ex) { ErrorLog.Write(ex); }
    }

    private void ShowSettings()
    {
        if (_settingsForm != null)
        {
            _settingsForm.Activate();
            return;
        }
        using var form = new SettingsForm(_settings, HotkeyStatus);
        _settingsForm = form;
        try
        {
            if (form.ShowDialog() == DialogResult.OK) RegisterHotkey();
        }
        finally { _settingsForm = null; }
    }

    /// <summary>--fullscreen-hotkey-test: registers the saved hotkeys like the tray does, posts the full-screen
    /// hotkey's WM_HOTKEY to the real hotkey window, and reports what the handler did. Run with the tray app closed.</summary>
    internal static string FullscreenHotkeySelfTest()
    {
        var ctx = new TrayContext(startCapture: false);
        try
        {
            ClipboardText.TrySet("fullscreen-hotkey-test");
            Native.PostMessage(ctx._hotkeyWindow.Handle, Native.WM_HOTKEY, new IntPtr(FullHotkeyId), IntPtr.Zero);
            var sw = System.Diagnostics.Stopwatch.StartNew();
            while (sw.ElapsedMilliseconds < 2500 && ctx._lastSavedPath == null) { Application.DoEvents(); Thread.Sleep(20); }
            var img = Clipboard.ContainsImage() ? Clipboard.GetImage() : null;
            return $"capture hotkey={ctx._hotkeyText} registered={ctx._hotkeyOk}{Environment.NewLine}" +
                   $"fullscreen hotkey={(ctx._fullText.Length == 0 ? "None" : ctx._fullText)} registered={ctx._fullOk}{Environment.NewLine}" +
                   $"clipboard image={(img == null ? "none" : $"{img.Width}x{img.Height}")}{Environment.NewLine}" +
                   $"saved={ctx._lastSavedPath ?? "none"}{Environment.NewLine}";
        }
        finally { ctx.ExitApp(); }
    }

    private void ExitApp()
    {
        if (_hotkeyOk) Native.UnregisterHotKey(_hotkeyWindow.Handle, HotkeyId);
        if (_fullOk) Native.UnregisterHotKey(_hotkeyWindow.Handle, FullHotkeyId);
        _hotkeyWindow.Dispose();
        foreach (var f in Application.OpenForms.Cast<Form>().ToList()) f.Close();
        _tray.Visible = false;
        _tray.Dispose();
        ExitThread();
    }
}
