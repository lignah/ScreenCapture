using System.Diagnostics;

namespace ScreenCapture;

internal sealed class TrayContext : ApplicationContext
{
    private const int HotkeyId = 0xA11;
    private const string HotkeyText = "Ctrl+Shift+A";

    private static Icon? _appIcon;

    private readonly AppSettings _settings = AppSettings.Load();
    private readonly NotifyIcon _tray;
    private readonly HotkeyWindow _hotkeyWindow = new();
    private readonly bool _hotkeyOk;
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
        var region = new ToolStripMenuItem("Region capture", null, (_, _) => RunLater(180, StartRegionCapture))
        {
            ShortcutKeyDisplayString = HotkeyText,
            Font = new Font(menu.Font, FontStyle.Bold),
        };
        menu.Items.Add(region);
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
        _hotkeyOk = Native.RegisterHotKey(_hotkeyWindow.Handle, HotkeyId,
            Native.MOD_CONTROL | Native.MOD_SHIFT | Native.MOD_NOREPEAT, (uint)Keys.A);

        if (_hotkeyOk)
        {
            _tray.Text = $"Screen Capture ({HotkeyText})";
            if (!startCapture)
                _tray.ShowBalloonTip(2500, "Screen Capture", $"Running in the tray. Press {HotkeyText} to capture.", ToolTipIcon.Info);
        }
        else
        {
            _tray.Text = "Screen Capture (hotkey unavailable)";
            region.ShortcutKeyDisplayString = "hotkey in use";
            _tray.ShowBalloonTip(6000, "Screen Capture",
                $"{HotkeyText} is already used by another app. Click the tray icon to capture instead.", ToolTipIcon.Warning);
        }

        if (startCapture) RunLater(100, StartRegionCapture);
    }

    private string HotkeyStatus => _hotkeyOk
        ? $"Hotkey: {HotkeyText} (region capture)"
        : $"Hotkey: {HotkeyText} could not be registered (in use by another app). Use the tray icon.";

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
        try { form.ShowDialog(); }
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
