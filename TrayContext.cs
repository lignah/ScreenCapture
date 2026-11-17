using System.Diagnostics;

namespace ScreenCapture;

internal sealed class TrayContext : ApplicationContext
{
    private const int HotkeyId = 0xA11;

    private static Icon? _appIcon;

    private readonly AppSettings _settings = AppSettings.Load();
    private readonly NotifyIcon _tray;
    private readonly HotkeyWindow _hotkeyWindow = new();
    private readonly ToolStripMenuItem _regionItem;
    private bool _hotkeyOk;
    private string _hotkeyText = HotkeyParser.Default;
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
        _regionItem = new ToolStripMenuItem("Region capture", null, (_, _) => RunLater(180, StartRegionCapture))
        {
            Font = new Font(menu.Font, FontStyle.Bold),
        };
        menu.Items.Add(_regionItem);
        menu.Items.Add("OCR capture (extract text)", null, (_, _) => RunLater(180, () => StartRegionCapture(ocrMode: true)));
        menu.Items.Add("Full screen capture", null, (_, _) => RunLater(250, FullScreenCapture));
        menu.Items.Add(new ToolStripSeparator());
        var showWindow = new ToolStripMenuItem("결과 창 표시") { CheckOnClick = true };
        showWindow.CheckedChanged += (_, _) => { _settings.OcrShowWindow = showWindow.Checked; _settings.Save(); };
        var merge = new ToolStripMenuItem("문단으로 합치기") { CheckOnClick = true };
        merge.CheckedChanged += (_, _) => { _settings.OcrMergeParagraphs = merge.Checked; _settings.Save(); };
        var paddle = new ToolStripMenuItem("PaddleOCR", null, (_, _) => { _settings.OcrEngine = "paddle"; _settings.Save(); });
        var windows = new ToolStripMenuItem("Windows OCR", null, (_, _) => { _settings.OcrEngine = "windows"; _settings.Save(); });
        var engine = new ToolStripMenuItem("OCR 엔진");
        engine.DropDownItems.AddRange(new ToolStripItem[] { paddle, windows });
        var languages = new ToolStripMenuItem("OCR 언어");
        languages.DropDownItems.Add("…"); // filled on open
        var ocrSettings = new ToolStripMenuItem("OCR 설정");
        ocrSettings.DropDownItems.AddRange(new ToolStripItem[] { merge, showWindow, new ToolStripSeparator(), engine, languages });
        menu.Items.Add(ocrSettings);
        // Settings can also change in the Settings dialog or the OCR window; refresh check marks on open.
        menu.Opening += (_, _) =>
        {
            showWindow.Checked = _settings.OcrShowWindow;
            merge.Checked = _settings.OcrMergeParagraphs;
            bool win = OcrService.IsWindowsEngine(_settings.OcrEngine);
            paddle.Checked = !win;
            windows.Checked = win;
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
        };
        RegisterHotkey(notifySuccess: !startCapture);

        if (startCapture) RunLater(100, StartRegionCapture);
    }

    private string HotkeyStatus => _hotkeyOk
        ? $"Current hotkey {_hotkeyText} is active (region capture)."
        : $"Current hotkey {_hotkeyText} could not be registered (in use by another app?). Pick another one above.";

    private void RegisterHotkey(bool notifySuccess)
    {
        if (_hotkeyOk) Native.UnregisterHotKey(_hotkeyWindow.Handle, HotkeyId);
        _hotkeyOk = false;
        _hotkeyText = string.IsNullOrWhiteSpace(_settings.Hotkey) ? HotkeyParser.Default : _settings.Hotkey.Trim();
        if (HotkeyParser.TryParse(_hotkeyText, out uint mods, out Keys key))
            _hotkeyOk = Native.RegisterHotKey(_hotkeyWindow.Handle, HotkeyId, mods | Native.MOD_NOREPEAT, (uint)key);

        if (_hotkeyOk)
        {
            _regionItem.ShortcutKeyDisplayString = _hotkeyText;
            _tray.Text = Truncate($"Screen Capture ({_hotkeyText})");
            if (notifySuccess)
                _tray.ShowBalloonTip(2500, "Screen Capture", $"Running in the tray. Press {_hotkeyText} to capture.", ToolTipIcon.Info);
        }
        else
        {
            _regionItem.ShortcutKeyDisplayString = "hotkey unavailable";
            _tray.Text = "Screen Capture (hotkey unavailable)";
            _tray.ShowBalloonTip(6000, "Screen Capture",
                $"{_hotkeyText} is already used by another app. Choose another hotkey in Settings, or click the tray icon to capture.",
                ToolTipIcon.Warning);
        }
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

    private void StartRegionCapture() => StartRegionCapture(ocrMode: false);

    private void StartRegionCapture(bool ocrMode)
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
            var overlay = new OverlayForm(bmp, vb, windows, _settings) { OcrMode = ocrMode };
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
            _tray.ShowBalloonTip(4000, "Screen Capture", "Capture failed: " + ex.Message, ToolTipIcon.Error);
        }
    }

    private int _ocrBusy;
    private static Icon? _busyIcon;

    /// <summary>Tray icon with an orange dot, shown while OCR runs.</summary>
    private static Icon BusyIcon
    {
        get
        {
            if (_busyIcon != null) return _busyIcon;
            try
            {
                using var bmp = AppIcon.ToBitmap();
                using (var g = Graphics.FromImage(bmp))
                {
                    g.SmoothingMode = System.Drawing.Drawing2D.SmoothingMode.AntiAlias;
                    int d = Math.Max(6, bmp.Width / 2);
                    g.FillEllipse(Brushes.White, bmp.Width - d - 1, bmp.Height - d - 1, d + 1, d + 1);
                    g.FillEllipse(Brushes.DarkOrange, bmp.Width - d, bmp.Height - d, d - 1, d - 1);
                }
                _busyIcon = Icon.FromHandle(bmp.GetHicon());
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
            if (r.Error != null && !r.Copied && r.Outcome == null)
                _tray.ShowBalloonTip(3000, "OCR 실패", r.Error, ToolTipIcon.Error);
            else if (r.Lines == 0)
                _tray.ShowBalloonTip(2000, "Screen Capture", "텍스트를 찾지 못했어요", ToolTipIcon.Info);
            else if (r.Copied)
                _tray.ShowBalloonTip(2000, $"OCR 복사됨 · {r.Lines}줄", r.Preview(), ToolTipIcon.None);
            else
                _tray.ShowBalloonTip(3000, "OCR 복사 실패", r.Error ?? "", ToolTipIcon.Warning);

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
        if (OcrService.IsWindowsEngine(_settings.OcrEngine))
        {
            string langs;
            try { langs = string.Join(", ", OcrService.AvailableLanguages); } catch { langs = "?"; }
            menu.DropDownItems.Add(new ToolStripMenuItem("Windows OCR 언어: " + langs) { Enabled = false });
            menu.DropDownItems.Add(new ToolStripMenuItem("(아래 PaddleOCR 언어는 PaddleOCR 엔진에서 사용)") { Enabled = false });
            menu.DropDownItems.Add(new ToolStripSeparator());
        }
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

    private void FullScreenCapture()
    {
        try
        {
            using var bmp = ScreenGrabber.CaptureVirtualScreen(out _);
            ImageOutput.CopyToClipboard(bmp);
            string msg = $"Full screen ({bmp.Width} × {bmp.Height}) copied to clipboard.";
            if (_settings.FullScreenSavesFile)
            {
                _lastSavedPath = ImageOutput.SaveAuto(bmp, _settings.EffectiveFolder);
                msg += $"\nSaved {Path.GetFileName(_lastSavedPath)} (click to open folder)";
            }
            _tray.ShowBalloonTip(3000, "Screen Capture", msg, ToolTipIcon.Info);
        }
        catch (Exception ex)
        {
            ErrorLog.Write(ex);
            _tray.ShowBalloonTip(4000, "Screen Capture", "Full screen capture failed: " + ex.Message, ToolTipIcon.Error);
        }
    }

    private void OnSaved(string path)
    {
        _lastSavedPath = path;
        _tray.ShowBalloonTip(2500, "Screen Capture", $"Saved {Path.GetFileName(path)} (click to open folder)", ToolTipIcon.Info);
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
            if (form.ShowDialog() == DialogResult.OK) RegisterHotkey(notifySuccess: true);
        }
        finally { _settingsForm = null; }
    }

    private void ExitApp()
    {
        if (_hotkeyOk) Native.UnregisterHotKey(_hotkeyWindow.Handle, HotkeyId);
        _hotkeyWindow.Dispose();
        foreach (var f in Application.OpenForms.Cast<Form>().ToList()) f.Close();
        _tray.Visible = false;
        _tray.Dispose();
        ExitThread();
    }
}
