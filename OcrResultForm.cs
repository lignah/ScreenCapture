namespace ScreenCapture;

/// <summary>Shows OCR output in an editable box; copies it to the clipboard automatically.</summary>
internal sealed class OcrResultForm : Form
{
    private readonly Bitmap _image;
    private readonly TextBox _text;
    private readonly Label _status;
    private readonly Button _copy;
    private readonly CheckBox _merge;
    private readonly AppSettings _settings;
    private OcrOutcome? _outcome;

    private readonly bool _copiedInitially;

    public const string CopiedMessage = "클립보드에 복사됨";

    public OcrResultForm(Bitmap image, Point nearScreen, AppSettings settings, OcrOutcome outcome, bool copied)
    {
        _image = image;
        _settings = settings;
        _outcome = outcome;
        _copiedInitially = copied;
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
            Text = "",
        };
        _status = new Label { AutoSize = false, Dock = DockStyle.Fill, TextAlign = ContentAlignment.MiddleLeft, Text = "" };
        _copy = new Button { Text = "Copy", AutoSize = true, Enabled = false, Anchor = AnchorStyles.Right };
        _merge = new CheckBox
        {
            Text = "문단으로 합치기",
            AutoSize = true,
            Checked = settings.OcrMergeParagraphs,
            Anchor = AnchorStyles.Left,
            Margin = new Padding(3, 6, 12, 3),
        };
        _merge.CheckedChanged += (_, _) => OnMergeToggled();
        new ToolTip().SetToolTip(_merge, "켜기: 줄바꿈된 줄을 문단으로 합치기 / 끄기: 원본 줄바꿈 유지");
        var close = new Button { Text = "Close", AutoSize = true, Anchor = AnchorStyles.Right, DialogResult = DialogResult.Cancel };
        _copy.Click += (_, _) => CopyText(true);
        close.Click += (_, _) => Close();
        CancelButton = close;

        var bottom = new TableLayoutPanel { Dock = DockStyle.Bottom, AutoSize = true, ColumnCount = 4, Padding = new Padding(6) };
        bottom.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
        bottom.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        bottom.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
        bottom.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
        bottom.Controls.Add(_merge, 0, 0);
        bottom.Controls.Add(_status, 1, 0);
        bottom.Controls.Add(_copy, 2, 0);
        bottom.Controls.Add(close, 3, 0);

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

    protected override void OnHandleCreated(EventArgs e)
    {
        base.OnHandleCreated(e);
        NoFlash.Prepare(Handle);
    }

    private string ModeName => _merge.Checked ? "문단으로 합치기" : "원본 줄바꿈";

    private void OnMergeToggled()
    {
        _settings.OcrMergeParagraphs = _merge.Checked;
        _settings.Save();
        if (_outcome == null || _outcome.LineCount == 0) return;
        _text.Text = _outcome.Format(_merge.Checked);
        CopyText(false);
    }

    private string _elapsed = "";

    private string Status(OcrOutcome o) =>
        $"{o.LineCount} lines · {o.LanguageTag}{_elapsed} · {ModeName}" + (o.Note != null ? $" · {o.Note}" : "");

    protected override void OnShown(EventArgs e)
    {
        base.OnShown(e);
        NoFlash.Reveal(this);
        Activate();
        var outcome = _outcome!;
        _text.ReadOnly = false;
        _text.Text = outcome.Format(_merge.Checked);
        _text.SelectionStart = 0;
        _text.SelectionLength = 0;
        if (outcome.Text.Trim().Length == 0)
        {
            _status.Text = "텍스트를 찾지 못했어요" + (outcome.Note != null ? $" · {outcome.Note}" : "");
            return;
        }
        _copy.Enabled = true;
        _status.Text = Status(outcome) + (_copiedInitially ? " · " + CopiedMessage : "");
    }

    /// <summary>Copies the selection (manual Copy) or the whole text; runs on the STA UI thread.</summary>
    private bool CopyText(bool manual)
    {
        var s = manual && _text.SelectionLength > 0 ? _text.SelectedText : _text.Text;
        if (string.IsNullOrEmpty(s)) return false;
        var error = ClipboardText.TrySet(s);
        if (error != null)
        {
            _status.Text = "복사 실패: " + error;
            return false;
        }
        _status.Text = (_outcome != null ? Status(_outcome) + " · " : "") + CopiedMessage;
        return true;
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
