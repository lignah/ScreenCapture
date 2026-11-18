using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.Drawing.Text;

namespace ScreenCapture;

/// <summary>
/// Full-virtual-desktop overlay showing a frozen screenshot. Client coordinates are physical pixels and map
/// 1:1 onto the frozen bitmap (client (0,0) == virtual-screen origin).
/// </summary>
internal sealed class OverlayForm : Form
{
    private enum Mode { Idle, Creating, Selected, Moving, Resizing, Drawing }
    private enum Tool { None, Rect, Arrow, Pen, Text, Mosaic }
    private sealed record Btn(string Id, string Tip, Rectangle Rect, bool Small);

    private static readonly Color Accent = Color.FromArgb(0, 120, 215);
    private static readonly Color[] Palette =
    {
        Color.FromArgb(232, 17, 35), Color.FromArgb(255, 185, 0), Color.FromArgb(16, 185, 80),
        Color.FromArgb(0, 120, 215), Color.White, Color.Black,
    };
    private static readonly string[] PaletteNames = { "Red", "Yellow", "Green", "Blue", "White", "Black" };
    private static readonly int[] Thickness = { 2, 4, 8 };
    private static readonly int[] FontPx = { 16, 24, 36 };
    private static readonly string? IconFont = DetectIconFont();

    private readonly Bitmap _src;
    private readonly Bitmap _dim;
    private readonly Rectangle _vb;
    private readonly List<Rectangle> _windows;
    private readonly List<Rectangle> _monitors;
    private readonly AppSettings _settings;

    private Mode _mode = Mode.Idle;
    private Tool _tool = Tool.None;
    private Rectangle _sel;
    private Point _down;
    private Rectangle _selAtDown;
    private int _handle = -1;
    private bool _pendingClick;
    private Rectangle? _hover;
    private Point _mouse = new(-10000, -10000);
    private Annotation? _current;
    private readonly List<Annotation> _ann = new();
    private int _colorIdx;
    private int _thickIdx = 1;
    private TextBox? _textBox;
    private readonly List<Btn> _buttons = new();
    private readonly List<int> _separators = new();
    private Rectangle _toolbarRect;
    private int _hoverBtn = -1;
    private Rectangle _lastDirty;
    private float _s = 1f;
    private readonly Dictionary<string, Font> _fonts = new();

    /// <summary>Raised with the rendered image and its screen position. Receiver owns the bitmap.</summary>
    public event Action<Bitmap, Point>? PinRequested;

    /// <summary>Raised with the raw (unannotated) selection and a screen point to place the result near.</summary>
    public event Action<Bitmap, Point>? OcrRequested;

    /// <summary>When true, finishing a selection immediately runs OCR (tray "OCR capture").</summary>
    public bool OcrMode { get; set; }

    /// <summary>Raised after a file was written.</summary>
    public event Action<string>? Saved;

    public OverlayForm(Bitmap frozen, Rectangle virtualBounds, List<Rectangle> windowRectsScreen, AppSettings settings)
    {
        _src = frozen;
        _vb = virtualBounds;
        _settings = settings;
        var client = new Rectangle(Point.Empty, _vb.Size);
        _windows = windowRectsScreen
            .Select(r => { r.Offset(-_vb.X, -_vb.Y); r.Intersect(client); return r; })
            .Where(r => r.Width > 0 && r.Height > 0)
            .ToList();
        _monitors = Screen.AllScreens
            .Select(s => { var r = s.Bounds; r.Offset(-_vb.X, -_vb.Y); return r; })
            .ToList();

        _dim = new Bitmap(_src.Width, _src.Height, PixelFormat.Format32bppPArgb);
        using (var g = Graphics.FromImage(_dim))
        using (var shade = new SolidBrush(Color.FromArgb(110, 0, 0, 0)))
        {
            g.CompositingMode = CompositingMode.SourceCopy;
            g.DrawImageUnscaled(_src, 0, 0);
            g.CompositingMode = CompositingMode.SourceOver;
            g.FillRectangle(shade, client);
        }

        Text = "Screen Capture";
        FormBorderStyle = FormBorderStyle.None;
        StartPosition = FormStartPosition.Manual;
        ShowInTaskbar = false;
        TopMost = true;
        AutoScaleMode = AutoScaleMode.None;
        BackColor = Color.Black;
        Bounds = _vb;
        Cursor = Cursors.Cross;
        KeyPreview = false;
        SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.UserPaint |
                 ControlStyles.OptimizedDoubleBuffer | ControlStyles.Opaque, true);
    }

    protected override CreateParams CreateParams
    {
        get
        {
            var cp = base.CreateParams;
            cp.ExStyle |= 0x80; // WS_EX_TOOLWINDOW: keep out of Alt+Tab
            return cp;
        }
    }

    private Rectangle ClientR => new(Point.Empty, _vb.Size);

    private bool HasSel =>
        _mode != Mode.Idle && !(_mode == Mode.Creating && _pendingClick) && _sel.Width > 0 && _sel.Height > 0;

    private bool ShowHover => _mode == Mode.Idle || (_mode == Mode.Creating && _pendingClick);

    private bool ToolbarVisible => HasSel && _mode is Mode.Selected or Mode.Drawing;

    private bool ShowMagnifier =>
        _mode is Mode.Idle or Mode.Creating or Mode.Resizing && ClientR.Contains(_mouse);

    // ---------------------------------------------------------------- lifecycle

    protected override void OnHandleCreated(EventArgs e)
    {
        base.OnHandleCreated(e);
        NoFlash.Prepare(Handle);
        PlaceExactly();
    }

    protected override void OnShown(EventArgs e)
    {
        base.OnShown(e);
        PlaceExactly();
        Activate();
        Native.SetForegroundWindow(Handle);
        _mouse = PointToClient(Cursor.Position);
        _hover = WindowAt(Clamp(_mouse));
        _lastDirty = Rectangle.Empty;
        NoFlash.Reveal(this); // first visible frame is the frozen screenshot, never a blank/white window
    }

    private void PlaceExactly() =>
        Native.SetWindowPos(Handle, Native.HWND_TOPMOST, _vb.X, _vb.Y, _vb.Width, _vb.Height, Native.SWP_SHOWWINDOW);

    protected override void WndProc(ref Message m)
    {
        // The overlay spans monitors with possibly different DPIs; never let WinForms rescale/move it.
        if (m.Msg == Native.WM_DPICHANGED)
        {
            m.Result = IntPtr.Zero;
            return;
        }
        if (m.Msg == NoFlash.WM_ERASEBKGND)
        {
            m.Result = (IntPtr)1; // everything is painted in OnPaint
            return;
        }
        base.WndProc(ref m);
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            _src.Dispose();
            _dim.Dispose();
            foreach (var a in _ann) a.Dispose();
            _current?.Dispose();
            foreach (var f in _fonts.Values) f.Dispose();
            _textBox?.Dispose();
        }
        base.Dispose(disposing);
    }

    // ---------------------------------------------------------------- geometry helpers

    private Point Clamp(Point p) => new(
        Math.Clamp(p.X, 0, Math.Max(0, _vb.Width - 1)),
        Math.Clamp(p.Y, 0, Math.Max(0, _vb.Height - 1)));

    private static Point ClampTo(Point p, Rectangle r) => new(
        Math.Clamp(p.X, r.Left, r.Right - 1), Math.Clamp(p.Y, r.Top, r.Bottom - 1));

    private Rectangle Norm(Point a, Point b)
    {
        var r = Rectangle.FromLTRB(Math.Min(a.X, b.X), Math.Min(a.Y, b.Y), Math.Max(a.X, b.X) + 1, Math.Max(a.Y, b.Y) + 1);
        r.Intersect(ClientR);
        return r;
    }

    private Rectangle KeepInside(Rectangle r)
    {
        r.X = Math.Clamp(r.X, 0, Math.Max(0, _vb.Width - r.Width));
        r.Y = Math.Clamp(r.Y, 0, Math.Max(0, _vb.Height - r.Height));
        return r;
    }

    private Rectangle MonitorAt(Point client)
    {
        foreach (var m in _monitors)
            if (m.Contains(client)) return m;
        return _monitors.Count > 0
            ? _monitors.OrderBy(m => Dist2(m, client)).First()
            : ClientR;
    }

    private static long Dist2(Rectangle r, Point p)
    {
        long dx = p.X < r.Left ? r.Left - p.X : p.X >= r.Right ? p.X - r.Right + 1 : 0;
        long dy = p.Y < r.Top ? r.Top - p.Y : p.Y >= r.Bottom ? p.Y - r.Bottom + 1 : 0;
        return dx * dx + dy * dy;
    }

    private Rectangle? WindowAt(Point p)
    {
        foreach (var w in _windows)
            if (w.Contains(p)) return w;
        var m = MonitorAt(p);
        m.Intersect(ClientR);
        return m.IsEmpty ? null : m;
    }

    private float ScaleAt(Point client) => ScreenGrabber.DpiScaleAt(new Point(client.X + _vb.X, client.Y + _vb.Y));

    private int S(float v) => (int)Math.Round(v * _s);

    private Font GetFont(string family, float px, FontStyle style = FontStyle.Regular)
    {
        var key = $"{family}|{px:0.0}|{style}";
        if (!_fonts.TryGetValue(key, out var f))
            _fonts[key] = f = new Font(family, px, style, GraphicsUnit.Pixel);
        return f;
    }

    private static string? DetectIconFont()
    {
        foreach (var name in new[] { "Segoe Fluent Icons", "Segoe MDL2 Assets" })
        {
            using var f = new Font(name, 10);
            if (string.Equals(f.Name, name, StringComparison.OrdinalIgnoreCase)) return name;
        }
        return null;
    }

    private Point[] HandlePoints()
    {
        var r = _sel;
        int cx = r.X + r.Width / 2, cy = r.Y + r.Height / 2, rr = r.Right - 1, bb = r.Bottom - 1;
        return new[]
        {
            new Point(r.Left, r.Top), new Point(cx, r.Top), new Point(rr, r.Top), new Point(rr, cy),
            new Point(rr, bb), new Point(cx, bb), new Point(r.Left, bb), new Point(r.Left, cy),
        };
    }

    private int HitHandle(Point p)
    {
        if (!HasSel) return -1;
        int rad = Math.Max(6, S(7));
        var hp = HandlePoints();
        for (int i = 0; i < hp.Length; i++)
            if (Math.Abs(p.X - hp[i].X) <= rad && Math.Abs(p.Y - hp[i].Y) <= rad) return i;
        int edge = Math.Max(4, S(4));
        bool inY = p.Y > _sel.Top && p.Y < _sel.Bottom, inX = p.X > _sel.Left && p.X < _sel.Right;
        if (inY && Math.Abs(p.X - _sel.Left) <= edge) return 7;
        if (inY && Math.Abs(p.X - (_sel.Right - 1)) <= edge) return 3;
        if (inX && Math.Abs(p.Y - _sel.Top) <= edge) return 1;
        if (inX && Math.Abs(p.Y - (_sel.Bottom - 1)) <= edge) return 5;
        return -1;
    }

    private static Cursor HandleCursor(int h) => h switch
    {
        0 or 4 => Cursors.SizeNWSE,
        2 or 6 => Cursors.SizeNESW,
        1 or 5 => Cursors.SizeNS,
        _ => Cursors.SizeWE,
    };

    private Rectangle ResizeRect(Rectangle r, int h, int dx, int dy)
    {
        int l = r.Left, t = r.Top, rt = r.Right, b = r.Bottom;
        switch (h)
        {
            case 0: l += dx; t += dy; break;
            case 1: t += dy; break;
            case 2: rt += dx; t += dy; break;
            case 3: rt += dx; break;
            case 4: rt += dx; b += dy; break;
            case 5: b += dy; break;
            case 6: l += dx; b += dy; break;
            case 7: l += dx; break;
        }
        var n = Rectangle.FromLTRB(Math.Min(l, rt), Math.Min(t, b), Math.Max(l, rt), Math.Max(t, b));
        n.Intersect(ClientR);
        if (n.Width < 1) n.Width = 1;
        if (n.Height < 1) n.Height = 1;
        return n;
    }

    // ---------------------------------------------------------------- invalidation

    private void Changed()
    {
        if (IsDisposed) return;
        _s = HasSel ? ScaleAt(new Point(_sel.X + _sel.Width / 2, _sel.Y + _sel.Height / 2)) : ScaleAt(Clamp(_mouse));
        LayoutToolbar();
        var cur = ComputeDirty();
        var u = _lastDirty.IsEmpty ? cur : cur.IsEmpty ? _lastDirty : Rectangle.Union(_lastDirty, cur);
        if (!u.IsEmpty) Invalidate(u);
        _lastDirty = cur;
    }

    private Rectangle ComputeDirty()
    {
        var r = Rectangle.Empty;
        void U(Rectangle x)
        {
            if (x.IsEmpty) return;
            r = r.IsEmpty ? x : Rectangle.Union(r, x);
        }

        if (HasSel)
        {
            var s = _sel;
            s.Inflate(S(14), S(14));
            U(s);
            U(Inflated(SizeLabelRect(), 2));
            if (ToolbarVisible) U(Rectangle.Inflate(_toolbarRect, S(120), S(44)));
        }
        if (ShowHover && _hover is { } h) U(Inflated(h, 6));
        if (ShowMagnifier) U(Inflated(MagnifierRect(), 3));
        return r;
    }

    private static Rectangle Inflated(Rectangle r, int by)
    {
        if (r.IsEmpty) return r;
        r.Inflate(by, by);
        return r;
    }

    // ---------------------------------------------------------------- mouse

    protected override void OnMouseDown(MouseEventArgs e)
    {
        base.OnMouseDown(e);
        var p = Clamp(e.Location);

        if (e.Button == MouseButtons.Right)
        {
            if (_textBox != null) { CancelText(); return; }
            if (_mode == Mode.Drawing)
            {
                _current?.Dispose();
                _current = null;
                _mode = Mode.Selected;
                Changed();
                return;
            }
            if (HasSel) ResetSelection();
            else Close();
            return;
        }
        if (e.Button != MouseButtons.Left) return;

        if (ToolbarVisible && _toolbarRect.Contains(e.Location))
        {
            int i = ButtonAt(e.Location);
            if (i >= 0) OnButton(_buttons[i].Id);
            return;
        }

        if (_textBox != null) { CommitText(); return; }

        if (e.Clicks >= 2 && HasSel && _sel.Contains(p) && _tool != Tool.Text)
        {
            Copy();
            return;
        }

        _down = p;
        switch (_mode)
        {
            case Mode.Idle:
                _mode = Mode.Creating;
                _pendingClick = true;
                _sel = Rectangle.Empty;
                break;
            case Mode.Selected:
                if (_tool != Tool.None && _sel.Contains(p))
                {
                    StartDrawing(p);
                    break;
                }
                int h = _tool == Tool.None ? HitHandle(p) : -1;
                if (h >= 0)
                {
                    _mode = Mode.Resizing;
                    _handle = h;
                    _selAtDown = _sel;
                }
                else if (_sel.Contains(p) && _tool == Tool.None)
                {
                    _mode = Mode.Moving;
                    _selAtDown = _sel;
                }
                else if (!_sel.Contains(p) && _ann.Count == 0)
                {
                    // Start over with a fresh selection.
                    _tool = Tool.None;
                    _mode = Mode.Creating;
                    _pendingClick = true;
                    _sel = Rectangle.Empty;
                    _hover = WindowAt(p);
                    _lastDirty = Rectangle.Empty;
                    Invalidate();
                }
                break;
        }
        Changed();
    }

    protected override void OnMouseMove(MouseEventArgs e)
    {
        base.OnMouseMove(e);
        _mouse = e.Location;
        var p = Clamp(e.Location);
        switch (_mode)
        {
            case Mode.Idle:
                _hover = WindowAt(p);
                break;
            case Mode.Creating:
                if (_pendingClick && (Math.Abs(p.X - _down.X) > 3 || Math.Abs(p.Y - _down.Y) > 3))
                    _pendingClick = false;
                if (_pendingClick) _hover = WindowAt(p);
                else _sel = Norm(_down, p);
                break;
            case Mode.Moving:
                var r = _selAtDown;
                r.Offset(p.X - _down.X, p.Y - _down.Y);
                _sel = KeepInside(r);
                break;
            case Mode.Resizing:
                _sel = ResizeRect(_selAtDown, _handle, p.X - _down.X, p.Y - _down.Y);
                break;
            case Mode.Drawing:
                _current?.Update(ClampTo(p, _sel));
                break;
        }
        _hoverBtn = ToolbarVisible ? ButtonAt(e.Location) : -1;
        UpdateCursor(e.Location);
        Changed();
    }

    protected override void OnMouseUp(MouseEventArgs e)
    {
        base.OnMouseUp(e);
        if (e.Button != MouseButtons.Left) return;
        var p = Clamp(e.Location);
        switch (_mode)
        {
            case Mode.Creating:
                if (_pendingClick)
                {
                    _pendingClick = false;
                    _sel = WindowAt(p) ?? Rectangle.Empty;
                }
                if (_sel.Width >= 2 && _sel.Height >= 2)
                {
                    _mode = Mode.Selected;
                    if (OcrMode) BeginInvoke(new Action(Ocr));
                }
                else
                {
                    _mode = Mode.Idle;
                    _sel = Rectangle.Empty;
                }
                _hover = null;
                break;
            case Mode.Moving:
            case Mode.Resizing:
                _mode = Mode.Selected;
                _handle = -1;
                break;
            case Mode.Drawing:
                if (_current != null)
                {
                    if (_current.IsValid)
                    {
                        _current.Finish(_src);
                        _ann.Add(_current);
                    }
                    else _current.Dispose();
                    _current = null;
                }
                _mode = Mode.Selected;
                break;
        }
        UpdateCursor(e.Location);
        Changed();
    }

    protected override void OnMouseLeave(EventArgs e)
    {
        base.OnMouseLeave(e);
        _hoverBtn = -1;
        Changed();
    }

    private void UpdateCursor(Point loc)
    {
        Cursor c = Cursors.Cross;
        if (ToolbarVisible && _toolbarRect.Contains(loc)) c = Cursors.Hand;
        else if (_mode == Mode.Moving) c = Cursors.SizeAll;
        else if (_mode == Mode.Resizing) c = HandleCursor(_handle);
        else if (_mode is Mode.Selected or Mode.Drawing)
        {
            if (_tool != Tool.None)
                c = _sel.Contains(loc) ? (_tool == Tool.Text ? Cursors.IBeam : Cursors.Cross) : Cursors.Default;
            else
            {
                int h = HitHandle(loc);
                if (h >= 0) c = HandleCursor(h);
                else if (_sel.Contains(loc)) c = Cursors.SizeAll;
                else c = _ann.Count == 0 ? Cursors.Cross : Cursors.Default;
            }
        }
        if (Cursor != c) Cursor = c;
    }

    private void ResetSelection()
    {
        CancelText();
        foreach (var a in _ann) a.Dispose();
        _ann.Clear();
        _current?.Dispose();
        _current = null;
        _tool = Tool.None;
        _mode = Mode.Idle;
        _sel = Rectangle.Empty;
        _hover = WindowAt(Clamp(_mouse));
        UpdateCursor(_mouse);
        Changed();
    }

    // ---------------------------------------------------------------- keyboard

    protected override bool ProcessCmdKey(ref Message msg, Keys keyData)
    {
        if (_textBox != null && _textBox.Focused)
        {
            if (keyData == Keys.Enter) { CommitText(); return true; }
            if (keyData == Keys.Escape) { CancelText(); return true; }
            return base.ProcessCmdKey(ref msg, keyData);
        }

        switch (keyData)
        {
            case Keys.Escape:
                Close();
                return true;
            case Keys.Enter:
            case Keys.Control | Keys.C:
                if (HasSel && _mode == Mode.Selected) Copy();
                return true;
            case Keys.Control | Keys.S:
                if (HasSel && _mode == Mode.Selected) Save();
                return true;
            case Keys.Control | Keys.Z:
                Undo();
                return true;
            case Keys.Control | Keys.T:
                if (HasSel && _mode == Mode.Selected) Ocr();
                return true;
            case Keys.C:
                // Plain C (no modifier) copies the color under the magnifier and closes; Ctrl+C above copies the image.
                if (ShowMagnifier) { CopyColorUnderCursor(); return true; }
                break;
            case Keys.Left:
            case Keys.Right:
            case Keys.Up:
            case Keys.Down:
                if (HasSel && _mode == Mode.Selected && _ann.Count == 0)
                {
                    var r = _sel;
                    r.Offset(keyData == Keys.Left ? -1 : keyData == Keys.Right ? 1 : 0,
                             keyData == Keys.Up ? -1 : keyData == Keys.Down ? 1 : 0);
                    _sel = KeepInside(r);
                    Changed();
                }
                return true;
        }
        return base.ProcessCmdKey(ref msg, keyData);
    }

    // ---------------------------------------------------------------- tools

    private static string ToolId(Tool t) => t switch
    {
        Tool.Rect => "rect",
        Tool.Arrow => "arrow",
        Tool.Pen => "pen",
        Tool.Text => "text",
        Tool.Mosaic => "mosaic",
        _ => "",
    };

    private void OnButton(string id)
    {
        bool styleButton = id.StartsWith("col", StringComparison.Ordinal) || id.StartsWith("th", StringComparison.Ordinal);
        if (_textBox != null && !styleButton) CommitText();

        switch (id)
        {
            case "rect": SetTool(Tool.Rect); break;
            case "arrow": SetTool(Tool.Arrow); break;
            case "pen": SetTool(Tool.Pen); break;
            case "text": SetTool(Tool.Text); break;
            case "mosaic": SetTool(Tool.Mosaic); break;
            case "undo": Undo(); break;
            case "ocr": Ocr(); return;
            case "pin": Pin(); return;
            case "copy": Copy(); return;
            default:
                if (id.StartsWith("col", StringComparison.Ordinal))
                {
                    _colorIdx = id[3] - '0';
                    if (_textBox != null)
                    {
                        _textBox.ForeColor = Palette[_colorIdx];
                        _textBox.BackColor = TextBack(Palette[_colorIdx]);
                        _textBox.Focus();
                    }
                }
                else if (id.StartsWith("th", StringComparison.Ordinal))
                {
                    _thickIdx = id[2] - '0';
                    if (_textBox != null)
                    {
                        var old = _textBox.Font;
                        _textBox.Font = new Font("Segoe UI", FontPx[_thickIdx] * _s, FontStyle.Regular, GraphicsUnit.Pixel);
                        old.Dispose();
                        FitTextBox();
                        _textBox.Focus();
                    }
                }
                break;
        }
        Changed();
    }

    private void SetTool(Tool t)
    {
        _tool = _tool == t ? Tool.None : t;
        UpdateCursor(_mouse);
    }

    private void Undo()
    {
        if (_textBox != null) { CancelText(); return; }
        if (_ann.Count == 0) return;
        _ann[^1].Dispose();
        _ann.RemoveAt(_ann.Count - 1);
        Changed();
    }

    private void StartDrawing(Point p)
    {
        var c = Palette[_colorIdx];
        float w = Thickness[_thickIdx] * _s;
        switch (_tool)
        {
            case Tool.Text: BeginText(p); return;
            case Tool.Rect: _current = new RectAnnotation(p, c, w); break;
            case Tool.Arrow: _current = new ArrowAnnotation(p, c, w); break;
            case Tool.Pen: _current = new PenAnnotation(p, c, w); break;
            case Tool.Mosaic:
                _current = new MosaicAnnotation(p, (int)Math.Round((6 + Thickness[_thickIdx] * 1.5f) * _s));
                break;
            default: return;
        }
        _mode = Mode.Drawing;
    }

    private static Color TextBack(Color fg) => fg.GetBrightness() > 0.6f ? Color.FromArgb(48, 48, 48) : Color.White;

    private void BeginText(Point p)
    {
        var c = Palette[_colorIdx];
        _textBox = new TextBox
        {
            Multiline = true,
            AcceptsReturn = true,
            WordWrap = false,
            ScrollBars = ScrollBars.None,
            BorderStyle = BorderStyle.FixedSingle,
            Font = new Font("Segoe UI", FontPx[_thickIdx] * _s, FontStyle.Regular, GraphicsUnit.Pixel),
            ForeColor = c,
            BackColor = TextBack(c),
            Location = p,
        };
        _textBox.TextChanged += (_, _) => FitTextBox();
        Controls.Add(_textBox);
        FitTextBox();
        _textBox.Focus();
    }

    private void FitTextBox()
    {
        if (_textBox == null) return;
        var text = _textBox.Text.Length == 0 ? "W" : _textBox.Text + "W";
        var sz = TextRenderer.MeasureText(text, _textBox.Font);
        _textBox.Size = new Size(Math.Max(sz.Width + 10, S(60)), sz.Height + 8);
    }

    private void CommitText()
    {
        var tb = _textBox;
        if (tb == null) return;
        _textBox = null;
        if (!string.IsNullOrWhiteSpace(tb.Text))
        {
            var loc = new Point(tb.Left + 3, tb.Top + 2);
            _ann.Add(new TextAnnotation(loc, tb.Text.TrimEnd(), (Font)tb.Font.Clone(), tb.ForeColor));
        }
        Controls.Remove(tb);
        tb.Font.Dispose();
        tb.Dispose();
        Focus();
        Changed();
    }

    private void CancelText()
    {
        var tb = _textBox;
        if (tb == null) return;
        _textBox = null;
        Controls.Remove(tb);
        tb.Font.Dispose();
        tb.Dispose();
        Focus();
        Changed();
    }

    // ---------------------------------------------------------------- output

    private Bitmap Render()
    {
        CommitText();
        var bmp = new Bitmap(_sel.Width, _sel.Height, PixelFormat.Format32bppArgb);
        using var g = Graphics.FromImage(bmp);
        g.DrawImage(_src, new Rectangle(0, 0, _sel.Width, _sel.Height), _sel, GraphicsUnit.Pixel);
        g.SmoothingMode = SmoothingMode.AntiAlias;
        g.TextRenderingHint = TextRenderingHint.AntiAliasGridFit;
        g.TranslateTransform(-_sel.X, -_sel.Y);
        g.SetClip(_sel);
        foreach (var a in _ann) a.Draw(g, _src);
        return bmp;
    }

    private void Copy()
    {
        if (!HasSel) return;
        try
        {
            using var bmp = Render();
            ImageOutput.CopyToClipboard(bmp);
            if (_settings.SaveOnCopy)
                Saved?.Invoke(ImageOutput.SaveAuto(bmp, _settings.EffectiveFolder));
        }
        catch (Exception ex)
        {
            ErrorLog.Write(ex);
            MessageBox.Show(this, "Could not copy the capture: " + ex.Message, "Screen Capture",
                MessageBoxButtons.OK, MessageBoxIcon.Error);
            return;
        }
        Close();
    }

    private void Save()
    {
        if (!HasSel) return;
        try
        {
            using var bmp = Render();
            string path;
            if (_settings.AskWhereToSave)
            {
                var folder = _settings.EffectiveFolder;
                Directory.CreateDirectory(folder);
                using var dlg = new SaveFileDialog
                {
                    Title = "Save screenshot",
                    Filter = "PNG image (*.png)|*.png|JPEG image (*.jpg)|*.jpg;*.jpeg|Bitmap (*.bmp)|*.bmp",
                    InitialDirectory = folder,
                    FileName = ImageOutput.TimestampName(),
                    DefaultExt = "png",
                    AddExtension = true,
                    OverwritePrompt = true,
                };
                if (dlg.ShowDialog(this) != DialogResult.OK)
                {
                    Activate();
                    return;
                }
                path = dlg.FileName;
                ImageOutput.Save(bmp, path);
            }
            else
            {
                path = ImageOutput.SaveAuto(bmp, _settings.EffectiveFolder);
            }
            Saved?.Invoke(path);
        }
        catch (Exception ex)
        {
            ErrorLog.Write(ex);
            MessageBox.Show(this, "Could not save the capture: " + ex.Message, "Screen Capture",
                MessageBoxButtons.OK, MessageBoxIcon.Error);
            return;
        }
        Close();
    }

    private void Pin()
    {
        if (!HasSel) return;
        var bmp = Render();
        var loc = new Point(_sel.X + _vb.X, _sel.Y + _vb.Y);
        Close();
        PinRequested?.Invoke(bmp, loc);
    }

    private void Ocr()
    {
        if (!HasSel || IsDisposed) return;
        CancelText();
        var bmp = _src.Clone(_sel, PixelFormat.Format32bppArgb);
        var near = new Point(_sel.X + _vb.X, _sel.Bottom + _vb.Y + 8);
        Close();
        OcrRequested?.Invoke(bmp, near);
    }

    /// <summary>Headless render-pipeline check used by --overlay-selftest.</summary>
    internal Bitmap SelfTestRender()
    {
        var cr = ClientR;
        _sel = new Rectangle(Math.Min(100, cr.Width / 4), Math.Min(100, cr.Height / 4),
            Math.Min(480, cr.Width / 2), Math.Min(320, cr.Height / 2));
        _mode = Mode.Selected;
        var x = _sel;
        void Add(Annotation a, Point end)
        {
            a.Update(end);
            a.Finish(_src);
            _ann.Add(a);
        }
        Add(new RectAnnotation(new Point(x.X + 20, x.Y + 20), Palette[0], 4), new Point(x.X + 160, x.Y + 110));
        Add(new ArrowAnnotation(new Point(x.X + 30, x.Y + 280), Palette[3], 4), new Point(x.X + 200, x.Y + 150));
        var pen = new PenAnnotation(new Point(x.X + 340, x.Y + 200), Palette[1], 4);
        for (int i = 1; i <= 20; i++) pen.Update(new Point(x.X + 340 + i * 5, x.Y + 200 + (int)(20 * Math.Sin(i / 3.0))));
        _ann.Add(pen);
        Add(new MosaicAnnotation(new Point(x.X + 250, x.Y + 130), 12), new Point(x.X + 460, x.Y + 190));
        _ann.Add(new TextAnnotation(new Point(x.X + 220, x.Y + 240), "Self-test",
            new Font("Segoe UI", 24, FontStyle.Regular, GraphicsUnit.Pixel), Palette[0]));
        return Render();
    }

    /// <summary>Headless magnifier check used by --magnifier-selftest: paints the idle overlay with the cursor at
    /// <paramref name="at"/> (client coords) and returns the magnifier area plus the expected #RRGGBB text.</summary>
    /// <summary>Test hook: route a key through the real shortcut handler.</summary>
    internal bool SimulateKey(Keys keys)
    {
        var msg = new Message();
        return ProcessCmdKey(ref msg, keys);
    }

    internal Bitmap MagnifierSelfTest(Point at, out string hex)
    {
        _mode = Mode.Idle;
        _mouse = at;
        _hover = null;
        var cr = ClientR;
        using var full = new Bitmap(cr.Width, cr.Height);
        using (var g = Graphics.FromImage(full))
            OnPaint(new PaintEventArgs(g, cr));
        var m = Clamp(_mouse);
        hex = HexColor(_src.GetPixel(m.X, m.Y));
        var r = Rectangle.Intersect(MagnifierRect(), cr);
        return full.Clone(r, full.PixelFormat);
    }

    // ---------------------------------------------------------------- toolbar

    private void LayoutToolbar()
    {
        _buttons.Clear();
        _separators.Clear();
        _toolbarRect = Rectangle.Empty;
        if (!ToolbarVisible) return;

        int bw = S(32), sw = S(24), sep = S(11), pad = S(5), h = S(42);
        var items = new List<(string Id, string Tip, bool Small)?>
        {
            ("rect", "Rectangle", false), ("arrow", "Arrow", false),
            ("pen", "Pen", false), ("text", "Text", false), ("mosaic", "Mosaic / blur", false),
            null, ("undo", "Undo (Ctrl+Z)", false), null,
        };
        for (int i = 0; i < Palette.Length; i++) items.Add(($"col{i}", $"{PaletteNames[i]} {HexColor(Palette[i])}", true));
        items.Add(null);
        string[] thickNames = { "Thin", "Medium", "Thick" };
        for (int i = 0; i < Thickness.Length; i++) items.Add(($"th{i}", thickNames[i] + " (also text size / mosaic block)", true));
        items.Add(null);
        items.Add(("ocr", "Extract text - OCR (Ctrl+T)", false));
        items.Add(("pin", "Pin to screen", false));
        items.Add(("copy", "Copy to clipboard (Enter / double-click)", false));

        int width = pad * 2 + items.Sum(it => it is null ? sep : it.Value.Small ? sw : bw);

        var mon = MonitorAt(new Point(_sel.Right - 1, _sel.Bottom - 1));
        int x = _sel.Right - width;
        x = Math.Max(mon.Left, Math.Min(x, mon.Right - width));
        int gap = S(6);
        int y = _sel.Bottom + gap;
        if (y + h > mon.Bottom)
        {
            y = _sel.Top - gap - h;
            if (y < mon.Top) y = Math.Max(mon.Top, _sel.Bottom - gap - h);
        }
        _toolbarRect = new Rectangle(x, y, width, h);

        int cx = x + pad, by = y + (h - bw) / 2;
        foreach (var it in items)
        {
            if (it is null)
            {
                _separators.Add(cx + sep / 2);
                cx += sep;
                continue;
            }
            int w = it.Value.Small ? sw : bw;
            _buttons.Add(new Btn(it.Value.Id, it.Value.Tip, new Rectangle(cx, by, w, bw), it.Value.Small));
            cx += w;
        }
    }

    private int ButtonAt(Point p)
    {
        for (int i = 0; i < _buttons.Count; i++)
            if (_buttons[i].Rect.Contains(p)) return i;
        return -1;
    }

    // ---------------------------------------------------------------- painting

    protected override void OnPaintBackground(PaintEventArgs e) { }

    protected override void OnPaint(PaintEventArgs e)
    {
        var g = e.Graphics;
        var clip = e.ClipRectangle;

        g.CompositingMode = CompositingMode.SourceCopy;
        g.InterpolationMode = InterpolationMode.NearestNeighbor;
        g.PixelOffsetMode = PixelOffsetMode.Half;
        g.DrawImage(_dim, clip, clip, GraphicsUnit.Pixel);

        Rectangle bright = HasSel ? _sel : ShowHover && _hover is { } hv ? hv : Rectangle.Empty;
        if (!bright.IsEmpty)
        {
            var r = Rectangle.Intersect(bright, clip);
            if (!r.IsEmpty) g.DrawImage(_src, r, r, GraphicsUnit.Pixel);
        }
        g.CompositingMode = CompositingMode.SourceOver;
        g.PixelOffsetMode = PixelOffsetMode.Default;

        if (HasSel)
        {
            g.SmoothingMode = SmoothingMode.AntiAlias;
            var state = g.Save();
            g.SetClip(_sel);
            foreach (var a in _ann) a.Draw(g, _src);
            _current?.Draw(g, _src);
            g.Restore(state);

            g.SmoothingMode = SmoothingMode.None;
            using (var pen = new Pen(Accent, Math.Max(1, S(1.5f))))
                g.DrawRectangle(pen, _sel.X, _sel.Y, _sel.Width - 1, _sel.Height - 1);

            if (_tool == Tool.None && _mode is Mode.Selected or Mode.Moving or Mode.Resizing)
                DrawHandles(g);
            DrawSizeLabel(g);
            if (ToolbarVisible) DrawToolbar(g);
        }
        else if (ShowHover && _hover is { } h)
        {
            using var pen = new Pen(Accent, Math.Max(2, S(3)));
            pen.Alignment = PenAlignment.Inset;
            g.DrawRectangle(pen, h);
        }

        if (ShowMagnifier) DrawMagnifier(g);
    }

    private void DrawHandles(Graphics g)
    {
        int hs = Math.Max(5, S(7));
        using var border = new Pen(Accent, Math.Max(1, S(1)));
        foreach (var p in HandlePoints())
        {
            var r = new Rectangle(p.X - hs / 2, p.Y - hs / 2, hs, hs);
            g.FillRectangle(Brushes.White, r);
            g.DrawRectangle(border, r);
        }
    }

    private string SizeText() => $"{_sel.Width} × {_sel.Height}";

    private Rectangle SizeLabelRect()
    {
        if (!HasSel) return Rectangle.Empty;
        var f = GetFont("Segoe UI", 12 * _s);
        var sz = TextRenderer.MeasureText(SizeText(), f);
        int w = sz.Width + S(6), h = sz.Height + S(4);
        int x = _sel.X, y = _sel.Y - h - S(4);
        if (y < MonitorAt(new Point(_sel.X, _sel.Y)).Top || y < 0)
        {
            x += S(4);
            y = _sel.Y + S(4);
        }
        return new Rectangle(x, y, w, h);
    }

    private void DrawSizeLabel(Graphics g)
    {
        var r = SizeLabelRect();
        g.SmoothingMode = SmoothingMode.AntiAlias;
        using (var path = RoundedRect(r, S(3)))
        using (var bg = new SolidBrush(Color.FromArgb(220, 30, 30, 30)))
            g.FillPath(bg, path);
        DrawCentered(g, SizeText(), GetFont("Segoe UI", 12 * _s), Color.White, r);
    }

    private void DrawToolbar(Graphics g)
    {
        g.SmoothingMode = SmoothingMode.AntiAlias;
        g.TextRenderingHint = TextRenderingHint.AntiAliasGridFit;
        using (var path = RoundedRect(_toolbarRect, S(6)))
        using (var bg = new SolidBrush(Color.FromArgb(245, 38, 38, 38)))
            g.FillPath(bg, path);

        using (var sepPen = new Pen(Color.FromArgb(80, 255, 255, 255), 1))
            foreach (var sx in _separators)
                g.DrawLine(sepPen, sx, _toolbarRect.Top + S(10), sx, _toolbarRect.Bottom - S(10));

        for (int i = 0; i < _buttons.Count; i++)
        {
            var b = _buttons[i];
            bool active = b.Id == ToolId(_tool) || b.Id == "th" + _thickIdx;
            bool isColor = b.Id.StartsWith("col", StringComparison.Ordinal);
            if (active || (i == _hoverBtn && !isColor))
            {
                var r = b.Rect;
                r.Inflate(-S(2), -S(2));
                using var path = RoundedRect(r, S(4));
                using var brush = new SolidBrush(active ? Accent : Color.FromArgb(75, 75, 75));
                g.FillPath(brush, path);
            }
            DrawGlyph(g, b, i == _hoverBtn);
        }

        if (_hoverBtn >= 0 && _hoverBtn < _buttons.Count) DrawTooltip(g, _buttons[_hoverBtn]);
    }

    private void DrawGlyph(Graphics g, Btn b, bool hovered)
    {
        var r = b.Rect;
        float s = _s;
        var c = new PointF(r.X + r.Width / 2f, r.Y + r.Height / 2f);
        Color fg = Color.White;
        if (b.Id == "undo" && _ann.Count == 0 && _textBox == null) fg = Color.FromArgb(110, 110, 110);
        if (b.Id == "copy") fg = Color.FromArgb(90, 210, 130);
        using var pen = new Pen(fg, 1.6f * s) { LineJoin = LineJoin.Round, StartCap = LineCap.Round, EndCap = LineCap.Round };

        switch (b.Id)
        {
            case "rect":
                g.DrawRectangle(pen, c.X - 7 * s, c.Y - 5.5f * s, 14 * s, 11 * s);
                break;
            case "arrow":
                Annotation.DrawArrow(g, fg, 1.6f * s, new PointF(c.X - 6 * s, c.Y + 6 * s), new PointF(c.X + 6.5f * s, c.Y - 6.5f * s));
                break;
            case "pen":
                g.DrawBezier(pen, c.X - 7 * s, c.Y + 4 * s, c.X - 3 * s, c.Y - 9 * s, c.X + 2 * s, c.Y + 10 * s, c.X + 7 * s, c.Y - 4 * s);
                break;
            case "text":
                DrawCentered(g, "T", GetFont("Segoe UI", 17 * s, FontStyle.Bold), fg, r);
                break;
            case "mosaic":
                using (var brush = new SolidBrush(fg))
                {
                    float cell = 4 * s, ox = c.X - 6 * s, oy = c.Y - 6 * s;
                    for (int i = 0; i < 3; i++)
                        for (int j = 0; j < 3; j++)
                            if ((i + j) % 2 == 0) g.FillRectangle(brush, ox + i * cell, oy + j * cell, cell, cell);
                    g.DrawRectangle(pen, ox, oy, cell * 3, cell * 3);
                }
                break;
            case "ocr":
                DrawCentered(g, "OCR", GetFont("Segoe UI", 10 * s, FontStyle.Bold), fg, r);
                break;
            case "undo": IconGlyph(g, "\uE7A7", "↶", fg, r); break;
            case "pin": IconGlyph(g, "\uE718", "P", fg, r); break;
            case "copy": IconGlyph(g, "\uE8FB", "✓", fg, r); break;
            default:
                if (b.Id.StartsWith("col", StringComparison.Ordinal))
                {
                    int idx = b.Id[3] - '0';
                    float rad = 7 * s;
                    using (var brush = new SolidBrush(Palette[idx]))
                        g.FillEllipse(brush, c.X - rad, c.Y - rad, rad * 2, rad * 2);
                    using (var ring = new Pen(Color.FromArgb(140, 140, 140), 1))
                        g.DrawEllipse(ring, c.X - rad, c.Y - rad, rad * 2, rad * 2);
                    if (idx == _colorIdx || hovered)
                    {
                        float rr = 10 * s;
                        using var sel = new Pen(idx == _colorIdx ? Color.White : Color.FromArgb(150, 255, 255, 255), 1.6f * s);
                        g.DrawEllipse(sel, c.X - rr, c.Y - rr, rr * 2, rr * 2);
                    }
                }
                else if (b.Id.StartsWith("th", StringComparison.Ordinal))
                {
                    int idx = b.Id[2] - '0';
                    float d = (2.5f + idx * 3f) * s;
                    using var brush = new SolidBrush(Color.White);
                    g.FillEllipse(brush, c.X - d / 2, c.Y - d / 2, d, d);
                }
                break;
        }
    }

    private void IconGlyph(Graphics g, string glyph, string fallback, Color fg, Rectangle r)
    {
        if (IconFont != null) DrawCentered(g, glyph, GetFont(IconFont, 16 * _s), fg, r);
        else DrawCentered(g, fallback, GetFont("Segoe UI", 15 * _s, FontStyle.Bold), fg, r);
    }

    private void DrawTooltip(Graphics g, Btn b)
    {
        var f = GetFont("Segoe UI", 12 * _s);
        var sz = TextRenderer.MeasureText(b.Tip, f);
        int w = sz.Width + S(8), h = sz.Height + S(6);
        int x = b.Rect.X + b.Rect.Width / 2 - w / 2;
        var mon = MonitorAt(new Point(b.Rect.X, b.Rect.Y));
        x = Math.Max(mon.Left, Math.Min(x, mon.Right - w));
        int y = _toolbarRect.Bottom + S(4);
        if (y + h > mon.Bottom) y = _toolbarRect.Top - S(4) - h;
        var r = new Rectangle(x, y, w, h);
        using (var path = RoundedRect(r, S(3)))
        using (var bg = new SolidBrush(Color.FromArgb(235, 20, 20, 20)))
            g.FillPath(bg, path);
        DrawCentered(g, b.Tip, f, Color.White, r);
    }

    private Rectangle MagnifierRect()
    {
        float s = ScaleAt(Clamp(_mouse));
        int box = (int)Math.Round(120 * s / 15f) * 15;
        int info = (int)(40 * s);
        int w = box, h = box + info;
        int off = (int)(22 * s);
        int x = _mouse.X + off, y = _mouse.Y + off;
        var mon = MonitorAt(Clamp(_mouse));
        if (x + w > mon.Right) x = _mouse.X - off - w;
        if (y + h > mon.Bottom) y = _mouse.Y - off - h;
        return new Rectangle(x, y, w, h);
    }

    private void DrawMagnifier(Graphics g)
    {
        var r = MagnifierRect();
        float s = ScaleAt(Clamp(_mouse));
        int box = r.Width;
        var zr = new Rectangle(r.X, r.Y, box, box);
        var m = Clamp(_mouse);

        using (var bg = new SolidBrush(Color.FromArgb(235, 25, 25, 25)))
            g.FillRectangle(bg, r);

        var state = g.Save();
        g.InterpolationMode = InterpolationMode.NearestNeighbor;
        g.PixelOffsetMode = PixelOffsetMode.Half;
        g.SmoothingMode = SmoothingMode.None;
        g.SetClip(zr);
        g.DrawImage(_src, zr, new Rectangle(m.X - 7, m.Y - 7, 15, 15), GraphicsUnit.Pixel);
        g.Restore(state);

        float cell = box / 15f;
        using (var cross = new SolidBrush(Color.FromArgb(80, 0, 174, 255)))
        {
            g.FillRectangle(cross, zr.X, zr.Y + 7 * cell, box, cell);
            g.FillRectangle(cross, zr.X + 7 * cell, zr.Y, cell, box);
        }
        g.DrawRectangle(Pens.White, zr.X + 7 * cell, zr.Y + 7 * cell, cell, cell);
        using (var border = new Pen(Color.FromArgb(200, 255, 255, 255), 1))
            g.DrawRectangle(border, r.X, r.Y, r.Width - 1, r.Height - 1);

        var px = _src.GetPixel(m.X, m.Y);
        var f = GetFont("Segoe UI", 11 * s);
        g.TextRenderingHint = TextRenderingHint.AntiAliasGridFit;
        int tx = r.X + (int)(6 * s), ty = r.Y + box + (int)(3 * s);
        g.DrawString($"X {m.X + _vb.X}  Y {m.Y + _vb.Y}", f, Brushes.White, tx, ty);
        float sw = 10 * s;
        using (var sb = new SolidBrush(px)) g.FillRectangle(sb, tx, ty + 18 * s, sw, sw);
        g.DrawRectangle(Pens.Gray, tx, ty + 18 * s, sw, sw);
        g.DrawString(HexColor(px), f, Brushes.White, tx + sw + 4 * s, ty + 16 * s);
    }

    /// <summary>Plain C while the magnifier is visible: copy the pixel color under the cursor as #RRGGBB,
    /// then close the overlay (cancels the capture). Returns the copied text, or null if nothing was copied.</summary>
    private string? CopyColorUnderCursor()
    {
        if (!ShowMagnifier) return null;
        var m = Clamp(_mouse);
        string hex = HexColor(_src.GetPixel(m.X, m.Y));
        if (ClipboardText.TrySet(hex) != null) return null;
        Close();
        return hex;
    }

    /// <summary>Color as uppercase #RRGGBB (alpha ignored).</summary>
    internal static string HexColor(Color c) => $"#{c.R:X2}{c.G:X2}{c.B:X2}";

    private static void DrawCentered(Graphics g, string text, Font f, Color c, Rectangle r)
    {
        using var brush = new SolidBrush(c);
        using var sf = new StringFormat { Alignment = StringAlignment.Center, LineAlignment = StringAlignment.Center };
        g.DrawString(text, f, brush, r, sf);
    }

    internal static GraphicsPath RoundedRect(RectangleF r, float radius)
    {
        var path = new GraphicsPath();
        float d = Math.Max(1, radius * 2);
        d = Math.Min(d, Math.Min(r.Width, r.Height));
        path.AddArc(r.X, r.Y, d, d, 180, 90);
        path.AddArc(r.Right - d, r.Y, d, d, 270, 90);
        path.AddArc(r.Right - d, r.Bottom - d, d, d, 0, 90);
        path.AddArc(r.X, r.Bottom - d, d, d, 90, 90);
        path.CloseFigure();
        return path;
    }
}
