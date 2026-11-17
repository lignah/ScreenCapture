using System.Diagnostics;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.Runtime;
using System.Runtime.InteropServices;
using System.Text;
using Microsoft.ML.OnnxRuntime;
using Microsoft.ML.OnnxRuntime.Tensors;

namespace ScreenCapture;

/// <summary>
/// PaddleOCR (PP-OCRv5 mobile det + Korean rec) through ONNX Runtime on the CPU.
/// Sessions are created per call and disposed afterwards; <see cref="ReleaseMemory"/> then hands the memory back,
/// so the models only occupy RAM while OCR is running.
/// </summary>
internal static class PaddleOcr
{
    public const string DetFile = "det.onnx";
    public const string RecFile = "rec_korean.onnx";
    public const string DictFile = "korean_dict.txt";

    // DB post-processing (PaddleOCR defaults).
    private const float BinThresh = 0.3f;
    private const float BoxThresh = 0.6f;
    private const float UnclipRatio = 1.5f;
    private const int RecHeight = 48;
    private const float MinRecConfidence = 0.5f;

    private static readonly object Gate = new();

    /// <summary>Timing breakdown of the last run (for --ocr-test).</summary>
    public static string LastTimings { get; private set; } = "";

    public static string ModelDir => Path.Combine(AppContext.BaseDirectory, "models");

    /// <summary>Shared detection model present and at least one recognition language installed.</summary>
    public static bool IsAvailable =>
        File.Exists(Path.Combine(ModelDir, DetFile)) && OcrLanguages.Installed().Count > 0;

    /// <summary>Runs detection + recognition, then frees the sessions and trims the process memory.</summary>
    public static List<OcrLine> RecognizeAndRelease(Bitmap source, InstalledOcrLanguage lang)
    {
        lock (Gate)
        {
            try
            {
                return Recognize(source, lang);
            }
            finally
            {
                ReleaseMemory();
            }
        }
    }

    private static List<OcrLine> Recognize(Bitmap source, InstalledOcrLanguage lang)
    {
        var dict = File.ReadAllLines(lang.DictFile, Encoding.UTF8);
        using var options = new SessionOptions
        {
            // No arena / memory patterns: freed tensors go straight back to the OS heap.
            EnableCpuMemArena = false,
            EnableMemoryPattern = false,
            GraphOptimizationLevel = GraphOptimizationLevel.ORT_ENABLE_ALL,
            IntraOpNumThreads = Math.Clamp(Environment.ProcessorCount / 2, 1, 4),
            InterOpNumThreads = 1,
        };

        var sw = Stopwatch.StartNew();
        long tDetLoad, tDet, tRecLoad = 0, tRec = 0;
        using var src = To24bpp(source);
        List<RectangleF> boxes;
        using (var det = new InferenceSession(Path.Combine(ModelDir, DetFile), options))
        {
            tDetLoad = sw.ElapsedMilliseconds;
            boxes = Detect(det, src);
            tDet = sw.ElapsedMilliseconds - tDetLoad;
        }

        var pieces = new List<Piece>();
        if (boxes.Count > 0)
        {
            long t0 = sw.ElapsedMilliseconds;
            using var rec = new InferenceSession(lang.RecFile, options);
            tRecLoad = sw.ElapsedMilliseconds - t0;
            foreach (var box in boxes)
            {
                var p = RecognizeBox(rec, src, box, dict);
                if (p != null) pieces.Add(p);
            }
            tRec = sw.ElapsedMilliseconds - t0 - tRecLoad;
        }
        LastTimings = $"det load {tDetLoad} ms, det {tDet} ms, rec load {tRecLoad} ms, rec {tRec} ms ({boxes.Count} boxes), threads {options.IntraOpNumThreads}";
        return GroupIntoLines(pieces);
    }

    // ------------------------------------------------------------------ detection

    private static List<RectangleF> Detect(InferenceSession det, Bitmap src)
    {
        int W = src.Width, H = src.Height;
        // Upscale only small captures (detection cost grows with pixel count); cap the long side.
        double r = Math.Clamp(960.0 / Math.Max(W, H), 1.0, 2.0);
        r = Math.Min(r, 2560.0 / Math.Max(W, H));
        int nw = Math.Max(32, (int)Math.Round(W * r / 32) * 32);
        int nh = Math.Max(32, (int)Math.Round(H * r / 32) * 32);

        float[] input = new float[3 * nh * nw];
        using (var resized = Resize(src, nw, nh))
            FillBgr(resized, input, nw, nh, (0.485f, 0.229f), (0.456f, 0.224f), (0.406f, 0.225f));

        string inName = det.InputMetadata.Keys.First();
        float[] prob;
        using (var results = det.Run(new[] { NamedOnnxValue.CreateFromTensor(inName, new DenseTensor<float>(input, new[] { 1, 3, nh, nw })) }))
            prob = TensorData(results[0].AsTensor<float>());

        var comps = Components(prob, nw, nh);
        var boxes = new List<RectangleF>();
        float sx = (float)W / nw, sy = (float)H / nh;
        foreach (var c in comps)
        {
            int w = c.MaxX - c.MinX + 1, h = c.MaxY - c.MinY + 1;
            if (Math.Min(w, h) < 3) continue;
            if (c.Sum / c.Count < BoxThresh) continue;
            float d = w * h * UnclipRatio / (2f * (w + h));
            var rect = RectangleF.FromLTRB((c.MinX - d) * sx, (c.MinY - d) * sy, (c.MaxX + 1 + d) * sx, (c.MaxY + 1 + d) * sy);
            rect.Intersect(new RectangleF(0, 0, W, H));
            if (rect.Width >= 4 && rect.Height >= 4) boxes.Add(rect);
        }
        return boxes;
    }

    private sealed class Comp
    {
        public int MinX = int.MaxValue, MinY = int.MaxValue, MaxX = -1, MaxY = -1, Count;
        public double Sum;
    }

    /// <summary>4-connected components of prob &gt; BinThresh (two-pass labeling with union-find).</summary>
    private static List<Comp> Components(float[] prob, int w, int h)
    {
        var labels = new int[w * h];
        var parent = new List<int> { 0 };
        int Find(int i)
        {
            while (parent[i] != i)
            {
                parent[i] = parent[parent[i]];
                i = parent[i];
            }
            return i;
        }

        for (int y = 0; y < h; y++)
        {
            int row = y * w;
            for (int x = 0; x < w; x++)
            {
                int i = row + x;
                if (prob[i] <= BinThresh) continue;
                int left = x > 0 ? labels[i - 1] : 0;
                int up = y > 0 ? labels[i - w] : 0;
                if (left == 0 && up == 0)
                {
                    parent.Add(parent.Count);
                    labels[i] = parent.Count - 1;
                }
                else if (left != 0 && up != 0)
                {
                    int a = Find(left), b = Find(up);
                    if (a != b) parent[Math.Max(a, b)] = Math.Min(a, b);
                    labels[i] = Math.Min(a, b);
                }
                else labels[i] = left != 0 ? left : up;
            }
        }

        var map = new Dictionary<int, Comp>();
        for (int y = 0; y < h; y++)
        {
            for (int x = 0; x < w; x++)
            {
                int i = y * w + x;
                if (labels[i] == 0) continue;
                int root = Find(labels[i]);
                if (!map.TryGetValue(root, out var c)) map[root] = c = new Comp();
                c.Count++;
                c.Sum += prob[i];
                if (x < c.MinX) c.MinX = x;
                if (x > c.MaxX) c.MaxX = x;
                if (y < c.MinY) c.MinY = y;
                if (y > c.MaxY) c.MaxY = y;
            }
        }
        return map.Values.ToList();
    }

    // ------------------------------------------------------------------ recognition

    private sealed record Piece(string Text, RectangleF Box, float FirstWordWidth);

    private static Piece? RecognizeBox(InferenceSession rec, Bitmap src, RectangleF box, string[] dict)
    {
        var crop = Rectangle.FromLTRB((int)Math.Floor(box.Left), (int)Math.Floor(box.Top),
            (int)Math.Ceiling(box.Right), (int)Math.Ceiling(box.Bottom));
        crop.Intersect(new Rectangle(0, 0, src.Width, src.Height));
        if (crop.Width < 2 || crop.Height < 2) return null;

        int w = Math.Clamp((int)Math.Ceiling(crop.Width * (double)RecHeight / crop.Height), 16, 4000);
        float[] input = new float[3 * RecHeight * w];
        using (var bmp = new Bitmap(w, RecHeight, PixelFormat.Format24bppRgb))
        {
            using (var g = Graphics.FromImage(bmp))
            {
                g.InterpolationMode = InterpolationMode.HighQualityBilinear;
                g.PixelOffsetMode = PixelOffsetMode.Half;
                using var ia = new ImageAttributes();
                ia.SetWrapMode(WrapMode.TileFlipXY);
                g.DrawImage(src, new Rectangle(0, 0, w, RecHeight), crop.X, crop.Y, crop.Width, crop.Height, GraphicsUnit.Pixel, ia);
            }
            FillBgr(bmp, input, w, RecHeight, (0.5f, 0.5f), (0.5f, 0.5f), (0.5f, 0.5f));
        }

        string inName = rec.InputMetadata.Keys.First();
        using var results = rec.Run(new[] { NamedOnnxValue.CreateFromTensor(inName, new DenseTensor<float>(input, new[] { 1, 3, RecHeight, w })) });
        var t = results[0].AsTensor<float>();
        int T = t.Dimensions[1], C = t.Dimensions[2];
        float[] o = TensorData(t);

        // Greedy CTC: index 0 = blank, 1..dict = characters, last = space.
        var sb = new StringBuilder();
        var steps = new List<int>();
        int prev = 0;
        double confSum = 0;
        for (int s = 0; s < T; s++)
        {
            int best = 0;
            float bestV = float.MinValue;
            int off = s * C;
            for (int k = 0; k < C; k++)
            {
                float v = o[off + k];
                if (v > bestV) { bestV = v; best = k; }
            }
            if (best != 0 && best != prev)
            {
                sb.Append(best - 1 < dict.Length ? dict[best - 1] : " ");
                steps.Add(s);
                confSum += bestV;
            }
            prev = best;
        }
        if (steps.Count == 0) return null;
        if (confSum / steps.Count < MinRecConfidence) return null;

        string text = sb.ToString();
        // Width of the first word, from the CTC time step of the first space.
        float firstWord = crop.Width;
        int sp = text.IndexOf(' ');
        if (sp > 0 && sp < steps.Count) firstWord = (float)steps[sp] / T * crop.Width;

        text = FixPunctuationSpacing(text.Trim());
        return text.Length == 0 ? null : new Piece(text, box, firstWord);
    }

    /// <summary>The recognizer tends to drop the space after sentence punctuation ("요.새" -> "요. 새").</summary>
    internal static string FixPunctuationSpacing(string s)
    {
        if (s.Length < 3) return s;
        var sb = new StringBuilder(s.Length + 8);
        for (int i = 0; i < s.Length; i++)
        {
            sb.Append(s[i]);
            if (i + 1 >= s.Length) break;
            char c = s[i], n = s[i + 1];
            bool hangulNext = OcrTextLayout.IsHangul(n);
            if ((c is '.' or '?' or '!' or ',' or ';' or ':') && hangulNext && i > 0 && !char.IsDigit(s[i - 1]))
                sb.Append(' ');
            else if ((c is '.' or '?' or '!') && char.IsUpper(n) && i > 0 && (char.IsLower(s[i - 1]) || OcrTextLayout.IsHangul(s[i - 1])))
                sb.Append(' ');
        }
        return sb.ToString();
    }

    /// <summary>Boxes on the same row (vertical overlap) become one line, left to right, rows top to bottom.</summary>
    private static List<OcrLine> GroupIntoLines(List<Piece> pieces)
    {
        var rows = new List<List<Piece>>();
        foreach (var p in pieces.OrderBy(p => p.Box.Top + p.Box.Height / 2))
        {
            List<Piece>? target = null;
            foreach (var row in rows)
            {
                var last = row[^1];
                float overlap = Math.Min(last.Box.Bottom, p.Box.Bottom) - Math.Max(last.Box.Top, p.Box.Top);
                if (overlap > 0.5f * Math.Min(last.Box.Height, p.Box.Height)) { target = row; break; }
            }
            if (target == null) rows.Add(target = new List<Piece>());
            target.Add(p);
        }

        var lines = new List<OcrLine>();
        foreach (var row in rows.OrderBy(r => r.Min(p => p.Box.Top)))
        {
            var ordered = row.OrderBy(p => p.Box.Left).ToList();
            var box = ordered.Select(p => p.Box).Aggregate(RectangleF.Union);
            string text = string.Join(" ", ordered.Select(p => p.Text));
            lines.Add(new OcrLine(text, box, ordered[0].FirstWordWidth));
        }
        return lines;
    }

    private static float[] TensorData(Tensor<float> t) =>
        t is DenseTensor<float> dense ? dense.Buffer.ToArray() : t.ToArray();

    // ------------------------------------------------------------------ image helpers

    private static Bitmap To24bpp(Bitmap source)
    {
        var bmp = new Bitmap(source.Width, source.Height, PixelFormat.Format24bppRgb);
        using var g = Graphics.FromImage(bmp);
        g.Clear(Color.White);
        g.DrawImage(source, new Rectangle(0, 0, source.Width, source.Height));
        return bmp;
    }

    private static Bitmap Resize(Bitmap src, int w, int h)
    {
        var bmp = new Bitmap(w, h, PixelFormat.Format24bppRgb);
        using var g = Graphics.FromImage(bmp);
        g.InterpolationMode = InterpolationMode.HighQualityBilinear;
        g.PixelOffsetMode = PixelOffsetMode.Half;
        using var ia = new ImageAttributes();
        ia.SetWrapMode(WrapMode.TileFlipXY);
        g.DrawImage(src, new Rectangle(0, 0, w, h), 0, 0, src.Width, src.Height, GraphicsUnit.Pixel, ia);
        return bmp;
    }

    /// <summary>Writes CHW float planes in B,G,R order: (v/255 - mean) / std.</summary>
    private static void FillBgr(Bitmap bmp, float[] dst, int w, int h,
        (float Mean, float Std) b, (float Mean, float Std) g, (float Mean, float Std) r)
    {
        var data = bmp.LockBits(new Rectangle(0, 0, w, h), ImageLockMode.ReadOnly, PixelFormat.Format24bppRgb);
        try
        {
            int plane = w * h;
            var row = new byte[data.Stride];
            for (int y = 0; y < h; y++)
            {
                Marshal.Copy(data.Scan0 + y * data.Stride, row, 0, data.Stride);
                int o = y * w;
                for (int x = 0; x < w; x++)
                {
                    int p = x * 3;
                    dst[o + x] = (row[p] / 255f - b.Mean) / b.Std;
                    dst[plane + o + x] = (row[p + 1] / 255f - g.Mean) / g.Std;
                    dst[2 * plane + o + x] = (row[p + 2] / 255f - r.Mean) / r.Std;
                }
            }
        }
        finally
        {
            bmp.UnlockBits(data);
        }
    }

    // ------------------------------------------------------------------ memory

    [DllImport("kernel32.dll")]
    private static extern IntPtr GetCurrentProcess();

    [DllImport("kernel32.dll")]
    private static extern bool SetProcessWorkingSetSize(IntPtr process, IntPtr min, IntPtr max);

    [DllImport("kernel32.dll")]
    private static extern int GetProcessHeaps(int count, IntPtr[] heaps);

    [DllImport("kernel32.dll")]
    private static extern UIntPtr HeapCompact(IntPtr heap, uint flags);

    /// <summary>Full compacting GC, decommit free native heap blocks, and trim the working set.</summary>
    public static void ReleaseMemory()
    {
        try
        {
            GCSettings.LargeObjectHeapCompactionMode = GCLargeObjectHeapCompactionMode.CompactOnce;
            GC.Collect(2, GCCollectionMode.Forced, true, true);
            GC.WaitForPendingFinalizers();
            GC.Collect(2, GCCollectionMode.Forced, true, true);

            int n = GetProcessHeaps(0, Array.Empty<IntPtr>());
            if (n > 0)
            {
                var heaps = new IntPtr[n];
                n = GetProcessHeaps(n, heaps);
                for (int i = 0; i < n; i++) HeapCompact(heaps[i], 0);
            }
            SetProcessWorkingSetSize(GetCurrentProcess(), new IntPtr(-1), new IntPtr(-1));
        }
        catch (Exception ex)
        {
            Debug.WriteLine(ex);
        }
    }
}
