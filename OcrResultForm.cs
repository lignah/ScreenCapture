namespace ScreenCapture;

/// <summary>Shows OCR output in an editable box; copies it to the clipboard automatically.</summary>
internal sealed class OcrResultForm : Form
{
    private readonly Bitmap _image;
    private readonly TextBox _text;
    private readonly Label _status;
    private readonly Button _copy;

    public OcrResultForm(Bitmap image, Point nearScreen)
    {
        _image = image;
        SuspendLayout();
        AutoScaleDimensions = new SizeF(96F, 96F);
        AutoScaleMode = AutoScaleMode.Dpi;
        Text = "Screen Capture - OCR";
        Icon = TrayContext.AppIcon;
        TopMost = true;
        ShowInTaskbar = true;
        KeyPreview = true;
        MinimizeBox = false;
        StartPosition = FormStartPosition.Manual;
        ClientSize = new Size(520, 340);
        MinimumSize = new Size(320, 200);

        _text = new TextBox
        {
            Multiline = true,
            AcceptsReturn = true,
            AcceptsTab = true,
            WordWrap = true,
            ScrollBars = ScrollBars.Vertical,
            Dock = DockStyle.Fill,
            Font = new Font("Malgun Gothic", 10.5f),
            ReadOnly = true,
            Text = "Recognizing text...",
        };
        _status = new Label { AutoSize = false, Dock = DockStyle.Fill, TextAlign = ContentAlignment.MiddleLeft, Text = "" };
        _copy = new Button { Text = "Copy", AutoSize = true, Enabled = false, Anchor = AnchorStyles.Right };
        var close = new Button { Text = "Close", AutoSize = true, Anchor = AnchorStyles.Right, DialogResult = DialogResult.Cancel };
        _copy.Click += (_, _) => CopyText(true);
        close.Click += (_, _) => Close();
        CancelButton = close;

        var bottom = new TableLayoutPanel { Dock = DockStyle.Bottom, AutoSize = true, ColumnCount = 3, Padding = new Padding(6) };
        bottom.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        bottom.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
        bottom.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
        bottom.Controls.Add(_status, 0, 0);
        bottom.Controls.Add(_copy, 1, 0);
        bottom.Controls.Add(close, 2, 0);

        var body = new Panel { Dock = DockStyle.Fill, Padding = new Padding(6, 6, 6, 0) };
        body.Controls.Add(_text);
        Controls.Add(body);
        Controls.Add(bottom);
        ResumeLayout(false);
        PerformLayout();

        var wa = Screen.FromPoint(nearScreen).WorkingArea;
        int x = Math.Clamp(nearScreen.X, wa.Left, Math.Max(wa.Left, wa.Right - Width));
        int y = Math.Clamp(nearScreen.Y, wa.Top, Math.Max(wa.Top, wa.Bottom - Height));
        Location = new Point(x, y);
    }

    protected override async void OnShown(EventArgs e)
    {
        base.OnShown(e);
        Activate();
        try
        {
            var outcome = await OcrService.RecognizeAsync(_image);
            if (IsDisposed) return;
            _text.ReadOnly = false;
            _text.Text = outcome.Text;
            _text.SelectionStart = 0;
            _text.SelectionLength = 0;
            if (outcome.Text.Trim().Length == 0)
            {
                _status.Text = $"No text found ({outcome.LanguageTag}).";
                if (!OcrService.HasKorean) _status.Text += " Korean OCR not installed.";
                return;
            }
            _copy.Enabled = true;
            CopyText(false);
            _status.Text = $"{outcome.LineCount} lines · {outcome.LanguageTag} · copied to clipboard";
            if (!OcrService.HasKorean) _status.Text += " · Korean OCR not installed";
        }
        catch (Exception ex)
        {
            ErrorLog.Write(ex);
            if (IsDisposed) return;
            _text.Text = "OCR failed: " + ex.Message;
            _status.Text = "Error";
        }
    }

    private void CopyText(bool announce)
    {
        var s = _text.SelectionLength > 0 ? _text.SelectedText : _text.Text;
        if (string.IsNullOrEmpty(s)) return;
        try
        {
            Clipboard.SetText(s);
            if (announce) _status.Text = "Copied to clipboard";
        }
        catch (Exception ex)
        {
            ErrorLog.Write(ex);
            _status.Text = "Could not copy: " + ex.Message;
        }
    }

    protected override void OnKeyDown(KeyEventArgs e)
    {
        base.OnKeyDown(e);
        if (e.KeyCode == Keys.Escape) Close();
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing) _image.Dispose();
        base.Dispose(disposing);
    }
}
