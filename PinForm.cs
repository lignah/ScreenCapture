using System.Drawing.Drawing2D;

namespace ScreenCapture;

/// <summary>Borderless always-on-top window showing a pinned capture. Drag to move, wheel to zoom.</summary>
internal sealed class PinForm : Form
{
    private const int B = 1; // border thickness in pixels

    private readonly Bitmap _img;
    private readonly AppSettings _settings;
    private readonly System.Windows.Forms.Timer _labelTimer = new() { Interval = 900 };
    private float _zoom = 1f;
    private bool _dragging;
    private Point _dragOffset;
    private string? _zoomLabel;

    public event Action<string>? Saved;

    public PinForm(Bitmap img, Point screenLocation, AppSettings settings)
    {
        _img = img;
        _settings = settings;

        Text = "Pinned screenshot";
        FormBorderStyle = FormBorderStyle.None;
        ShowInTaskbar = false;
        TopMost = true;
        MaximizeBox = false;
        MinimizeBox = false;
        StartPosition = FormStartPosition.Manual;
        AutoScaleMode = AutoScaleMode.None;
        DoubleBuffered = true;
        KeyPreview = true;
        Bounds = new Rectangle(screenLocation.X - B, screenLocation.Y - B, img.Width + 2 * B, img.Height + 2 * B);

        var menu = new ContextMenuStrip();
        menu.Items.Add("Copy", null, (_, _) => CopyImage());
        menu.Items.Add("Save as...", null, (_, _) => SaveImage());
        menu.Items.Add("Zoom 100%", null, (_, _) => SetZoom(1f, new Point(Width / 2, Height / 2)));
        menu.Items.Add(new ToolStripSeparator());
        menu.Items.Add("Close", null, (_, _) => Close());
        ContextMenuStrip = menu;

        _labelTimer.Tick += (_, _) =>
        {
            _labelTimer.Stop();
            _zoomLabel = null;
            Invalidate();
        };
    }

    protected override CreateParams CreateParams
    {
        get
        {
            var cp = base.CreateParams;
            cp.ClassStyle |= 0x20000; // CS_DROPSHADOW
            cp.ExStyle |= 0x80;       // WS_EX_TOOLWINDOW
            return cp;
        }
    }

    protected override void OnShown(EventArgs e)
    {
        base.OnShown(e);
        var b = Bounds;
        Native.SetWindowPos(Handle, Native.HWND_TOPMOST, b.X, b.Y, b.Width, b.Height, Native.SWP_SHOWWINDOW);
        Activate();
    }

    protected override void WndProc(ref Message m)
    {
        if (m.Msg == Native.WM_DPICHANGED)
        {
            m.Result = IntPtr.Zero; // keep 1:1 pixels when dragged across monitors
            return;
        }
        base.WndProc(ref m);
    }

    protected override void OnMouseDown(MouseEventArgs e)
    {
        base.OnMouseDown(e);
        if (e.Button != MouseButtons.Left) return;
        if (e.Clicks >= 2)
        {
            Close();
            return;
        }
        _dragging = true;
        _dragOffset = e.Location;
        Cursor = Cursors.SizeAll;
    }

    protected override void OnMouseMove(MouseEventArgs e)
    {
        base.OnMouseMove(e);
        if (!_dragging) return;
        var c = Cursor.Position;
        Location = new Point(c.X - _dragOffset.X, c.Y - _dragOffset.Y);
    }

    protected override void OnMouseUp(MouseEventArgs e)
    {
        base.OnMouseUp(e);
        _dragging = false;
        Cursor = Cursors.Default;
    }

    protected override void OnMouseWheel(MouseEventArgs e)
    {
        base.OnMouseWheel(e);
        float z = _zoom * (e.Delta > 0 ? 1.15f : 1 / 1.15f);
        if (Math.Abs(z - 1f) < 0.07f) z = 1f;
        SetZoom(Math.Clamp(z, 0.1f, 8f), e.Location);
    }

    private void SetZoom(float zoom, Point anchorClient)
    {
        float fx = Math.Clamp((anchorClient.X - B) / (float)Math.Max(1, Width - 2 * B), 0, 1);
        float fy = Math.Clamp((anchorClient.Y - B) / (float)Math.Max(1, Height - 2 * B), 0, 1);
        var anchorScreen = PointToScreen(anchorClient);
        _zoom = zoom;
        int iw = Math.Max(8, (int)Math.Round(_img.Width * _zoom));
        int ih = Math.Max(8, (int)Math.Round(_img.Height * _zoom));
        int nx = anchorScreen.X - (int)(fx * iw) - B;
        int ny = anchorScreen.Y - (int)(fy * ih) - B;
        SetBounds(nx, ny, iw + 2 * B, ih + 2 * B);
        _zoomLabel = $"{_zoom * 100:0}%";
        _labelTimer.Stop();
        _labelTimer.Start();
        Invalidate();
    }

    protected override void OnKeyDown(KeyEventArgs e)
    {
        base.OnKeyDown(e);
        if (e.KeyCode == Keys.Escape) Close();
        else if (e.Control && e.KeyCode == Keys.C) CopyImage();
        else if (e.Control && e.KeyCode == Keys.S) SaveImage();
    }

    protected override void OnPaint(PaintEventArgs e)
    {
        var g = e.Graphics;
        g.InterpolationMode = _zoom < 1f ? InterpolationMode.HighQualityBicubic : InterpolationMode.NearestNeighbor;
        g.PixelOffsetMode = PixelOffsetMode.Half;
        g.DrawImage(_img, new Rectangle(B, B, Width - 2 * B, Height - 2 * B));
        g.PixelOffsetMode = PixelOffsetMode.Default;
        using (var pen = new Pen(Color.FromArgb(0, 120, 215), 1))
            g.DrawRectangle(pen, 0, 0, Width - 1, Height - 1);

        if (_zoomLabel != null)
        {
            using var f = new Font("Segoe UI", 12, FontStyle.Regular, GraphicsUnit.Pixel);
            var sz = TextRenderer.MeasureText(_zoomLabel, f);
            var r = new Rectangle(6, 6, sz.Width + 8, sz.Height + 4);
            using (var bg = new SolidBrush(Color.FromArgb(200, 20, 20, 20))) g.FillRectangle(bg, r);
            TextRenderer.DrawText(g, _zoomLabel, f, r, Color.White,
                TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter);
        }
    }

    private void CopyImage()
    {
        try { ImageOutput.CopyToClipboard(_img); }
        catch (Exception ex) { ErrorLog.Write(ex); }
    }

    private void SaveImage()
    {
        try
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
            };
            if (dlg.ShowDialog(this) != DialogResult.OK) return;
            ImageOutput.Save(_img, dlg.FileName);
            Saved?.Invoke(dlg.FileName);
        }
        catch (Exception ex)
        {
            ErrorLog.Write(ex);
            MessageBox.Show(this, "Could not save: " + ex.Message, "Screen Capture", MessageBoxButtons.OK, MessageBoxIcon.Error);
        }
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            _img.Dispose();
            _labelTimer.Dispose();
            ContextMenuStrip?.Dispose();
        }
        base.Dispose(disposing);
    }
}
