using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.Drawing.Text;

namespace ScreenCapture;

/// <summary>A markup item. Coordinates are overlay-client pixels (same space as the frozen screen bitmap).</summary>
internal abstract class Annotation : IDisposable
{
    protected Annotation(Color color, float width)
    {
        Color = color;
        Width = width;
    }

    public Color Color { get; }
    public float Width { get; }

    public abstract void Update(Point p);
    public abstract bool IsValid { get; }
    public abstract void Draw(Graphics g, Bitmap source);
    public virtual void Finish(Bitmap source) { }
    public virtual void Dispose() { }

    protected Pen MakePen() => new(Color, Width)
    {
        StartCap = LineCap.Round,
        EndCap = LineCap.Round,
        LineJoin = LineJoin.Round,
    };

    /// <param name="headLength">Arrowhead length; null = automatic (used for drawn arrows).</param>
    /// <param name="headSpread">Half-width of the head as a fraction of its length (drawn arrows: 0.45).</param>
    public static void DrawArrow(Graphics g, Color color, float width, PointF a, PointF b, float? headLength = null, float headSpread = 0.45f)
    {
        float dx = b.X - a.X, dy = b.Y - a.Y;
        float len = MathF.Sqrt(dx * dx + dy * dy);
        if (len < 1) return;
        float head = headLength is > 0 ? Math.Min(len, headLength.Value) : Math.Min(len, Math.Max(10f, width * 4.5f));
        float half = head * headSpread;
        float ux = dx / len, uy = dy / len;
        var basePt = new PointF(b.X - ux * head, b.Y - uy * head);
        var p1 = new PointF(basePt.X - uy * half, basePt.Y + ux * half);
        var p2 = new PointF(basePt.X + uy * half, basePt.Y - ux * half);
        var lineEnd = new PointF(b.X - ux * head * 0.7f, b.Y - uy * head * 0.7f);
        using (var pen = new Pen(color, width) { StartCap = LineCap.Round, EndCap = LineCap.Flat })
            g.DrawLine(pen, a, lineEnd);
        using var brush = new SolidBrush(color);
        g.FillPolygon(brush, new[] { b, p1, p2 });
    }
}

internal abstract class DragAnnotation : Annotation
{
    protected Point Start, End;

    protected DragAnnotation(Point p, Color color, float width) : base(color, width) => Start = End = p;

    public override void Update(Point p) => End = p;

    public override bool IsValid => Math.Abs(End.X - Start.X) > 2 || Math.Abs(End.Y - Start.Y) > 2;

    protected Rectangle Bounds => Rectangle.FromLTRB(
        Math.Min(Start.X, End.X), Math.Min(Start.Y, End.Y), Math.Max(Start.X, End.X), Math.Max(Start.Y, End.Y));
}

internal sealed class RectAnnotation : DragAnnotation
{
    public RectAnnotation(Point p, Color c, float w) : base(p, c, w) { }

    public override void Draw(Graphics g, Bitmap source)
    {
        using var pen = MakePen();
        pen.LineJoin = LineJoin.Miter;
        g.DrawRectangle(pen, Bounds);
    }
}

internal sealed class ArrowAnnotation : DragAnnotation
{
    public ArrowAnnotation(Point p, Color c, float w) : base(p, c, w) { }

    public override void Draw(Graphics g, Bitmap source) => DrawArrow(g, Color, Width, Start, End);
}

internal sealed class PenAnnotation : Annotation
{
    private readonly List<Point> _points = new();

    public PenAnnotation(Point p, Color c, float w) : base(c, w) => _points.Add(p);

    public override void Update(Point p)
    {
        if (_points[^1] != p) _points.Add(p);
    }

    public override bool IsValid => _points.Count >= 2;

    public override void Draw(Graphics g, Bitmap source)
    {
        if (_points.Count < 2) return;
        using var pen = MakePen();
        g.DrawLines(pen, _points.ToArray());
    }
}

internal sealed class MosaicAnnotation : DragAnnotation
{
    private readonly int _block;
    private Bitmap? _cache;
    private Rectangle _cacheRect;

    public MosaicAnnotation(Point p, int block) : base(p, Color.Empty, 0) => _block = Math.Max(3, block);

    public override void Draw(Graphics g, Bitmap source)
    {
        if (_cache != null)
        {
            DrawSmall(g, _cache, _cacheRect);
            return;
        }
        using var small = Downscale(source, Bounds, out var r);
        if (small != null) DrawSmall(g, small, r);
    }

    public override void Finish(Bitmap source)
    {
        _cache?.Dispose();
        _cache = Downscale(source, Bounds, out _cacheRect);
    }

    public override void Dispose() => _cache?.Dispose();

    private Bitmap? Downscale(Bitmap src, Rectangle r, out Rectangle clipped)
    {
        r.Intersect(new Rectangle(0, 0, src.Width, src.Height));
        clipped = r;
        if (r.Width < 1 || r.Height < 1) return null;
        int sw = Math.Max(1, (r.Width + _block - 1) / _block);
        int sh = Math.Max(1, (r.Height + _block - 1) / _block);
        var small = new Bitmap(sw, sh, PixelFormat.Format32bppArgb);
        using var g = Graphics.FromImage(small);
        g.InterpolationMode = InterpolationMode.HighQualityBicubic;
        g.PixelOffsetMode = PixelOffsetMode.Half;
        using var ia = new ImageAttributes();
        ia.SetWrapMode(WrapMode.TileFlipXY);
        g.DrawImage(src, new Rectangle(0, 0, sw, sh), r.X, r.Y, r.Width, r.Height, GraphicsUnit.Pixel, ia);
        return small;
    }

    private static void DrawSmall(Graphics g, Bitmap small, Rectangle r)
    {
        var state = g.Save();
        g.InterpolationMode = InterpolationMode.NearestNeighbor;
        g.PixelOffsetMode = PixelOffsetMode.Half;
        g.SmoothingMode = SmoothingMode.None;
        g.DrawImage(small, r, 0, 0, small.Width, small.Height, GraphicsUnit.Pixel);
        g.Restore(state);
    }
}

internal sealed class TextAnnotation : Annotation
{
    private readonly Point _location;
    private readonly string _text;
    private readonly Font _font;

    public TextAnnotation(Point location, string text, Font font, Color color) : base(color, 0)
    {
        _location = location;
        _text = text;
        _font = font;
    }

    public override void Update(Point p) { }

    public override bool IsValid => !string.IsNullOrWhiteSpace(_text);

    public override void Draw(Graphics g, Bitmap source)
    {
        var state = g.Save();
        g.TextRenderingHint = TextRenderingHint.AntiAliasGridFit;
        using var brush = new SolidBrush(Color);
        g.DrawString(_text, _font, brush, _location, StringFormat.GenericTypographic);
        g.Restore(state);
    }

    public override void Dispose() => _font.Dispose();
}
