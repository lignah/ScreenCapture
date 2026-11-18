using System.Drawing.Drawing2D;
using System.Drawing.Text;

namespace ScreenCapture;

/// <summary>
/// One text-drawing path shared by the in-place editor and the committed <see cref="TextAnnotation"/>, so text
/// lands exactly where it was typed: GDI+ DrawString (typographic, no padding), one line per row, line height
/// from the font, top-left at the click point.
/// </summary>
internal static class TextDraw
{
    private static readonly StringFormat Fmt = CreateFormat();

    private static StringFormat CreateFormat()
    {
        var f = (StringFormat)StringFormat.GenericTypographic.Clone();
        f.FormatFlags |= StringFormatFlags.MeasureTrailingSpaces | StringFormatFlags.NoWrap;
        return f;
    }

    public static float LineHeight(Graphics g, Font font) => font.GetHeight(g);

    public static string[] Lines(string text) => text.Split('\n');

    public static void Draw(Graphics g, string text, Font font, Color color, PointF at)
    {
        var state = g.Save();
        g.TextRenderingHint = TextRenderingHint.AntiAliasGridFit;
        using var brush = new SolidBrush(color);
        float lh = LineHeight(g, font);
        var lines = Lines(text);
        for (int i = 0; i < lines.Length; i++)
            if (lines[i].Length > 0) g.DrawString(lines[i], font, brush, at.X, at.Y + i * lh, Fmt);
        g.Restore(state);
    }

    public static float Width(Graphics g, string s, Font font)
    {
        if (s.Length == 0) return 0;
        var state = g.Save();
        g.TextRenderingHint = TextRenderingHint.AntiAliasGridFit;
        float w = g.MeasureString(s, font, PointF.Empty, Fmt).Width;
        g.Restore(state);
        return w;
    }

    public static SizeF Measure(Graphics g, string text, Font font)
    {
        var lines = Lines(text);
        float w = lines.Max(l => Width(g, l, font));
        return new SizeF(w, lines.Length * LineHeight(g, font));
    }
}

/// <summary>
/// In-overlay text editor: no TextBox, so the frozen screenshot shows through. Draws the text with
/// <see cref="TextDraw"/>, an inline IME composition (underlined), a caret and a dashed frame.
/// </summary>
internal sealed class TextEditor : IDisposable
{
    public Point Location { get; }
    public Font Font { get; private set; }
    public Color Color { get; set; }
    public string Text { get; private set; } = "";
    public int Caret { get; private set; }
    /// <summary>IME composition in progress (e.g. a Hangul syllable being built); not part of Text yet.</summary>
    public string Composition { get; private set; } = "";

    public TextEditor(Point location, Font font, Color color)
    {
        Location = location;
        Font = font;
        Color = color;
    }

    /// <summary>Text as shown, with the composition inserted at the caret.</summary>
    public string Display => Text.Insert(Caret, Composition);

    /// <summary>What gets committed (an unfinished composition is kept, like a normal text box would).</summary>
    public string Final => Display.TrimEnd();

    public void SetFont(Font font)
    {
        var old = Font;
        Font = font;
        old.Dispose();
    }

    public void Insert(string s)
    {
        s = s.Replace("\r\n", "\n").Replace('\r', '\n');
        Text = Text.Insert(Caret, s);
        Caret += s.Length;
    }

    public void SetComposition(string s) => Composition = s ?? "";

    public void Backspace()
    {
        if (Caret == 0) return;
        Text = Text.Remove(Caret - 1, 1);
        Caret--;
    }

    public void Delete()
    {
        if (Caret < Text.Length) Text = Text.Remove(Caret, 1);
    }

    public void Left() => Caret = Math.Max(0, Caret - 1);
    public void Right() => Caret = Math.Min(Text.Length, Caret + 1);

    public void Home()
    {
        int nl = Caret == 0 ? -1 : Text.LastIndexOf('\n', Caret - 1);
        Caret = nl + 1;
    }

    public void End()
    {
        int nl = Text.IndexOf('\n', Caret);
        Caret = nl < 0 ? Text.Length : nl;
    }

    /// <summary>Caret position (top of the line) in overlay coordinates, after any composition.</summary>
    public PointF CaretPoint(Graphics g)
    {
        int pos = Caret + Composition.Length;
        string before = Display[..pos];
        int line = before.Count(c => c == '\n');
        int start = before.LastIndexOf('\n') + 1;
        float x = TextDraw.Width(g, before[start..], Font);
        return new PointF(Location.X + x, Location.Y + line * TextDraw.LineHeight(g, Font));
    }

    /// <summary>Area the text occupies (at least one character wide), in overlay coordinates.</summary>
    public RectangleF Bounds(Graphics g)
    {
        var sz = TextDraw.Measure(g, Display, Font);
        float minW = TextDraw.Width(g, "W", Font);
        return new RectangleF(Location.X, Location.Y, Math.Max(sz.Width, minW), sz.Height);
    }

    public void Draw(Graphics g, bool caretVisible, bool chrome = true)
    {
        TextDraw.Draw(g, Display, Font, Color, Location);
        if (!chrome) return;

        var state = g.Save();
        g.SmoothingMode = SmoothingMode.None;
        float lh = TextDraw.LineHeight(g, Font);
        float px = Math.Max(1f, Font.Size / 16f);

        if (Composition.Length > 0)
        {
            // underline the syllable being composed, like a normal IME text box
            string before = Text[..Caret];
            int line = before.Count(c => c == '\n');
            int start = before.LastIndexOf('\n') + 1;
            float x0 = Location.X + TextDraw.Width(g, before[start..], Font);
            float x1 = x0 + TextDraw.Width(g, Composition, Font);
            float y = Location.Y + line * lh + lh - px;
            using var u = new Pen(Color, px);
            g.DrawLine(u, x0, y, x1, y);
        }

        // dashed frame, two-tone so it shows on light and dark backgrounds
        var b = Bounds(g);
        var r = Rectangle.Round(RectangleF.Inflate(b, 4 * px, 3 * px));
        using (var dark = new Pen(Color.FromArgb(160, 0, 0, 0), 1))
            g.DrawRectangle(dark, r.X, r.Y, r.Width, r.Height);
        using (var light = new Pen(Color.FromArgb(220, 255, 255, 255), 1) { DashStyle = DashStyle.Dash })
            g.DrawRectangle(light, r.X, r.Y, r.Width, r.Height);

        if (caretVisible)
        {
            var c = CaretPoint(g);
            using var caret = new Pen(Color, Math.Max(1f, Font.Size / 18f));
            g.DrawLine(caret, c.X, c.Y + lh * 0.08f, c.X, c.Y + lh * 0.92f);
        }
        g.Restore(state);
    }

    public void Dispose() => Font.Dispose();
}
