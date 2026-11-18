namespace ScreenCapture;

internal sealed class SettingsForm : Form
{
    private readonly AppSettings _settings;
    private readonly TextBox _folder = new() { Width = 340, Anchor = AnchorStyles.Left | AnchorStyles.Right };
    private readonly CheckBox _ask = new() { Text = "Ask where to save (Save As dialog)", AutoSize = true };
    private readonly CheckBox _saveOnCopy = new() { Text = "Also save a PNG to the folder when copying", AutoSize = true };
    private readonly ComboBox _hotkey = new() { DropDownStyle = ComboBoxStyle.DropDown, Width = 200, IntegralHeight = false, Anchor = AnchorStyles.Left };
    // DropDownList: clicking anywhere on the box opens the list.
    private readonly ComboBox _fullHotkey = new() { DropDownStyle = ComboBoxStyle.DropDownList, Width = 200, IntegralHeight = false, Anchor = AnchorStyles.Left };
    private readonly CheckBox _startup = new() { Text = "Start with Windows", AutoSize = true };

    public SettingsForm(AppSettings settings, string hotkeyStatus)
    {
        _settings = settings;
        SuspendLayout();
        AutoScaleDimensions = new SizeF(96F, 96F);
        AutoScaleMode = AutoScaleMode.Dpi;
        Text = $"Screen Capture {Program.AppVersion} Settings";
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

        _fullHotkey.Items.AddRange(HotkeyParser.FullscreenPresets);
        string full = string.IsNullOrWhiteSpace(settings.FullscreenHotkey) ? HotkeyParser.None : HotkeyParser.Normalize(settings.FullscreenHotkey);
        if (!_fullHotkey.Items.Contains(full)) _fullHotkey.Items.Add(full); // hand-edited settings.json value stays selectable
        table.Controls.Add(new Label { Text = "Full screen hotkey:", AutoSize = true, Anchor = AnchorStyles.Left }, 0, 2);
        table.Controls.Add(_fullHotkey, 1, 2);
        FitLists();
        Load += (_, _) => FitLists(); // again after DPI scaling changed the font

        int row = 3;
        foreach (var cb in new[] { _ask, _saveOnCopy, _startup })
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
        _fullHotkey.SelectedItem = full;
        _ask.Checked = settings.AskWhereToSave;
        _saveOnCopy.Checked = settings.SaveOnCopy;
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
            string fullSel = _fullHotkey.SelectedItem as string ?? HotkeyParser.None;
            if (fullSel != HotkeyParser.None && HotkeyParser.Normalize(fullSel) == HotkeyParser.Normalize(_hotkey.Text))
            {
                MessageBox.Show(this, "Full screen hotkey must be different from the capture hotkey.",
                    "Screen Capture", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                DialogResult = DialogResult.None;
                return;
            }
            Apply();
        };
        ResumeLayout(false);
        PerformLayout();
    }

    /// <summary>Make both dropdowns tall enough to show every choice without scrolling.</summary>
    private void FitLists()
    {
        foreach (var cb in new[] { _hotkey, _fullHotkey })
            cb.DropDownHeight = cb.ItemHeight * cb.Items.Count + 4;
    }

    /// <summary>--settings-selftest: the dialog plus the full-screen hotkey list drawn open below its box (a dropped-down
    /// list is a separate popup that DrawToBitmap cannot capture, so the list is drawn from the real items).</summary>
    internal Bitmap RenderPreview(out string info)
    {
        var combo = _fullHotkey;
        var items = combo.Items.Cast<object>().Select(i => i.ToString() ?? "").ToList();
        var dlg = new Bitmap(Width, Height);
        DrawToBitmap(dlg, new Rectangle(Point.Empty, Size));
        var comboInForm = RectangleToClient(combo.RectangleToScreen(combo.ClientRectangle));
        var origin = PointToScreen(Point.Empty);
        int ox = origin.X - Left, oy = origin.Y - Top; // client offset inside the window bitmap
        var listRect = new Rectangle(comboInForm.X + ox, comboInForm.Bottom + oy, combo.Width, combo.DropDownHeight);
        var bmp = new Bitmap(Width, Math.Max(Height, listRect.Bottom + 8));
        using (var g = Graphics.FromImage(bmp))
        {
            g.Clear(Color.White);
            g.DrawImage(dlg, 0, 0);
            g.FillRectangle(Brushes.White, listRect);
            g.DrawRectangle(Pens.Gray, listRect.X, listRect.Y, listRect.Width - 1, listRect.Height - 1);
            int ih = combo.ItemHeight;
            for (int i = 0; i < items.Count; i++)
            {
                var r = new Rectangle(listRect.X + 1, listRect.Y + 2 + i * ih, listRect.Width - 2, ih);
                bool sel = i == combo.SelectedIndex;
                if (sel) g.FillRectangle(SystemBrushes.Highlight, r);
                TextRenderer.DrawText(g, items[i], combo.Font, new Rectangle(r.X + 3, r.Y, r.Width - 3, r.Height),
                    sel ? SystemColors.HighlightText : Color.Black, TextFormatFlags.VerticalCenter | TextFormatFlags.Left);
            }
        }
        dlg.Dispose();
        info = $"capture: style={_hotkey.DropDownStyle} text={_hotkey.Text} items={string.Join(",", _hotkey.Items.Cast<object>())}{Environment.NewLine}" +
               $"fullscreen: style={combo.DropDownStyle} selected={combo.SelectedItem} items={string.Join(",", items)} " +
               $"dropDownHeight={combo.DropDownHeight} needed={combo.ItemHeight * items.Count}{Environment.NewLine}";
        return bmp;
    }

    private void Apply()
    {
        _settings.SaveFolder = _folder.Text.Trim();
        _settings.Hotkey = HotkeyParser.Normalize(_hotkey.Text);
        string fullSel = _fullHotkey.SelectedItem as string ?? HotkeyParser.None;
        _settings.FullscreenHotkey = fullSel == HotkeyParser.None ? "" : HotkeyParser.Normalize(fullSel);
        _settings.AskWhereToSave = _ask.Checked;
        _settings.SaveOnCopy = _saveOnCopy.Checked;
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
