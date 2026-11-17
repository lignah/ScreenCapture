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
            overlay.OcrRequested += (img, near) => new OcrResultForm(img, near).Show();
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
