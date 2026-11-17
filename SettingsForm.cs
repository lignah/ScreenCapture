namespace ScreenCapture;

internal sealed class SettingsForm : Form
{
    private readonly AppSettings _settings;
    private readonly TextBox _folder = new() { Width = 340, Anchor = AnchorStyles.Left | AnchorStyles.Right };
    private readonly CheckBox _ask = new() { Text = "Ask where to save (Save As dialog)", AutoSize = true };
    private readonly CheckBox _saveOnCopy = new() { Text = "Also save a PNG to the folder when copying", AutoSize = true };
    private readonly CheckBox _fullSave = new() { Text = "Full-screen capture also saves a PNG (it always copies)", AutoSize = true };
    private readonly ComboBox _hotkey = new() { DropDownStyle = ComboBoxStyle.DropDown, Width = 200, Anchor = AnchorStyles.Left };
    private readonly ComboBox _ocrEngine = new() { DropDownStyle = ComboBoxStyle.DropDownList, Width = 200, Anchor = AnchorStyles.Left };
    private readonly CheckBox _startup = new() { Text = "Start with Windows", AutoSize = true };

    public SettingsForm(AppSettings settings, string hotkeyStatus)
    {
        _settings = settings;
        SuspendLayout();
        AutoScaleDimensions = new SizeF(96F, 96F);
        AutoScaleMode = AutoScaleMode.Dpi;
        Text = "Screen Capture Settings";
        FormBorderStyle = FormBorderStyle.FixedDialog;
        MaximizeBox = false;
        MinimizeBox = false;
        StartPosition = FormStartPosition.CenterScreen;
        ShowInTaskbar = true;
        TopMost = true;
        AutoSize = true;
        AutoSizeMode = AutoSizeMode.GrowAndShrink;
        Icon = TrayContext.AppIcon;

        var table = new TableLayoutPanel
        {
            ColumnCount = 3,
            AutoSize = true,
            AutoSizeMode = AutoSizeMode.GrowAndShrink,
            Padding = new Padding(12),
            Dock = DockStyle.Fill,
        };
        table.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
        table.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
        table.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));

        var browse = new Button { Text = "Browse...", AutoSize = true };
        browse.Click += (_, _) =>
        {
            using var dlg = new FolderBrowserDialog { SelectedPath = _folder.Text, UseDescriptionForTitle = true, Description = "Screenshot folder" };
            if (dlg.ShowDialog(this) == DialogResult.OK) _folder.Text = dlg.SelectedPath;
        };
        table.Controls.Add(new Label { Text = "Save folder:", AutoSize = true, Anchor = AnchorStyles.Left }, 0, 0);
        table.Controls.Add(_folder, 1, 0);
        table.Controls.Add(browse, 2, 0);

        _hotkey.Items.AddRange(HotkeyParser.Presets);
        table.Controls.Add(new Label { Text = "Capture hotkey:", AutoSize = true, Anchor = AnchorStyles.Left }, 0, 1);
        table.Controls.Add(_hotkey, 1, 1);

        _ocrEngine.Items.AddRange(new object[] { "PaddleOCR (default)", "Windows OCR" });
        table.Controls.Add(new Label { Text = "OCR engine:", AutoSize = true, Anchor = AnchorStyles.Left }, 0, 2);
        table.Controls.Add(_ocrEngine, 1, 2);

        int row = 3;
        foreach (var cb in new[] { _ask, _saveOnCopy, _fullSave, _startup })
        {
            table.Controls.Add(cb, 0, row);
            table.SetColumnSpan(cb, 3);
            row++;
        }

        var hk = new Label { Text = hotkeyStatus, AutoSize = true, Padding = new Padding(0, 8, 0, 4) };
        table.Controls.Add(hk, 0, row);
        table.SetColumnSpan(hk, 3);
        row++;

        var ok = new Button { Text = "OK", DialogResult = DialogResult.OK, AutoSize = true };
        var cancel = new Button { Text = "Cancel", DialogResult = DialogResult.Cancel, AutoSize = true };
        var buttons = new FlowLayoutPanel { FlowDirection = FlowDirection.RightToLeft, AutoSize = true, Dock = DockStyle.Fill };
        buttons.Controls.Add(cancel);
        buttons.Controls.Add(ok);
        table.Controls.Add(buttons, 0, row);
        table.SetColumnSpan(buttons, 3);

        Controls.Add(table);
        AcceptButton = ok;
        CancelButton = cancel;

        _folder.Text = settings.EffectiveFolder;
        _hotkey.Text = settings.Hotkey;
        _ocrEngine.SelectedIndex = OcrService.IsWindowsEngine(settings.OcrEngine) ? 1 : 0;
        _ask.Checked = settings.AskWhereToSave;
        _saveOnCopy.Checked = settings.SaveOnCopy;
        _fullSave.Checked = settings.FullScreenSavesFile;
        try { _startup.Checked = AppSettings.StartWithWindows; } catch { _startup.Enabled = false; }

        ok.Click += (_, _) =>
        {
            if (!HotkeyParser.TryParse(_hotkey.Text, out _, out _))
            {
                MessageBox.Show(this, "Hotkey must look like Ctrl+Shift+A (modifiers: Ctrl, Alt, Shift, Win).",
                    "Screen Capture", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                DialogResult = DialogResult.None;
                return;
            }
            Apply();
        };
        ResumeLayout(false);
        PerformLayout();
    }

    private void Apply()
    {
        _settings.SaveFolder = _folder.Text.Trim();
        _settings.Hotkey = _hotkey.Text.Trim();
        _settings.OcrEngine = _ocrEngine.SelectedIndex == 1 ? "windows" : "paddle";
        _settings.AskWhereToSave = _ask.Checked;
        _settings.SaveOnCopy = _saveOnCopy.Checked;
        _settings.FullScreenSavesFile = _fullSave.Checked;
        _settings.Save();
        try
        {
            if (_startup.Enabled && _startup.Checked != AppSettings.StartWithWindows)
                AppSettings.StartWithWindows = _startup.Checked;
        }
        catch (Exception ex)
        {
            ErrorLog.Write(ex);
        }
    }
}
