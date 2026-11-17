using System.Text;
using System.Text.RegularExpressions;

namespace ScreenCapture;

/// <summary>One recognized line with its bounding box (OCR image pixels).</summary>
internal sealed record OcrLine(string Text, RectangleF Box, float FirstWordWidth)
{
    /// <summary>Average glyph width; spaces count half.</summary>
    public float CharWidth
    {
        get
        {
            float n = Text.Count(c => !char.IsWhiteSpace(c)) + Text.Count(char.IsWhiteSpace) * 0.5f;
            return n <= 0 ? Box.Height * 0.6f : Box.Width / n;
        }
    }
}

/// <summary>Turns OCR lines into text, either keeping line breaks or re-flowing wrapped lines into paragraphs.</summary>
internal static partial class OcrTextLayout
{
    // Syllables that usually continue the previous word (particles / verb endings) when a line wraps mid-word.
    private static readonly HashSet<char> Continuations = new(
        "로는은이가을를에의도만요다고서며면게지니죠께와과랑야어아해돼된될할한함".ToCharArray());

    // Common words that start with one of the syllables above; a line starting with these begins a new word.
    private static readonly string[] WordStarts =
    {
        "이미", "이제", "이번", "이런", "이렇", "이것", "이후", "이상", "이전", "이용", "이름", "이유", "이야기",
        "가장", "가능", "가끔", "가지", "가격", "가방", "도움", "도구", "도착", "도시", "만약", "만들", "만나",
        "다시", "다음", "다른", "다만", "다양", "고객", "고급", "고민", "서로", "서버", "서비스", "서울",
        "지금", "지난", "지원", "지역", "아직", "아니", "아래", "아마", "어떤", "어떻", "어디", "어느", "어제",
        "해당", "해결", "한번", "한국", "한글", "한편", "할인", "함께", "게임", "게시", "요즘", "요청", "요약",
        "와이", "과정", "과거", "의견", "의미", "에러", "면접", "니다", "로그", "로봇", "야간", "된장", "될까",
    };

    // Bullets, "1." "1)" "(1)" "a)" "(a)" "가)" "(가)" "①". Not "다." / "요." which are usually wrapped sentence endings.
    [GeneratedRegex(@"^\s*([-–—•·∙●○◦▪■□*※>]|\(?\d{1,3}[.)]|\(?[A-Za-z]\)|\(?[가나다라마바사아자차카타파하]\)|[①-⑳])(\s|$)")]
    private static partial Regex ListMarker();

    /// <summary>Original line breaks; a blank line where the vertical gap suggests a paragraph break.</summary>
    public static string Raw(IReadOnlyList<OcrLine> lines)
    {
        var sb = new StringBuilder();
        for (int i = 0; i < lines.Count; i++)
        {
            if (i > 0)
            {
                sb.Append("\r\n");
                if (BigGap(lines[i - 1], lines[i])) sb.Append("\r\n");
            }
            sb.Append(lines[i].Text);
        }
        return sb.ToString();
    }

    /// <summary>Joins wrapped lines into paragraphs using line geometry.</summary>
    public static string Merge(IReadOnlyList<OcrLine> lines)
    {
        if (lines.Count == 0) return "";
        float maxRight = lines.Max(l => l.Box.Right);
        var sb = new StringBuilder(lines[0].Text);
        var prev = lines[0];
        for (int i = 1; i < lines.Count; i++)
        {
            var next = lines[i];
            if (SameRow(prev, next))
            {
                sb.Append(' ').Append(next.Text.TrimStart());
                // Treat both pieces as one visual line from here on.
                prev = new OcrLine(prev.Text + " " + next.Text, RectangleF.Union(prev.Box, next.Box), prev.FirstWordWidth);
                continue;
            }
            if (BigGap(prev, next)) sb.Append("\r\n\r\n").Append(next.Text);
            else if (ShouldMerge(prev, next, maxRight)) Join(sb, next.Text);
            else sb.Append("\r\n").Append(next.Text);
            prev = next;
        }
        return sb.ToString();
    }

    /// <summary>OCR sometimes splits one visual line into two pieces side by side.</summary>
    private static bool SameRow(OcrLine prev, OcrLine next) =>
        next.Box.Left > prev.Box.Left + prev.Box.Width * 0.5f &&
        next.Box.Top < prev.Box.Bottom - Math.Min(prev.Box.Height, next.Box.Height) * 0.5f;

    private static bool BigGap(OcrLine prev, OcrLine next) =>
        next.Box.Top - prev.Box.Bottom > Math.Max(prev.Box.Height, next.Box.Height) * 1.2f;

    private static bool ShouldMerge(OcrLine prev, OcrLine next, float maxRight)
    {
        float cw = Math.Max(1f, prev.CharWidth);
        // The previous line was wrapped if it reaches (almost) the column edge, i.e. the next line's
        // first word would not have fit after it.
        float allowed = Math.Max(1.5f * cw, next.FirstWordWidth + cw);
        // A line that starts with a bare particle/ending ("면", "로 ...") almost never starts a paragraph,
        // so tolerate a ragged right edge (or a glyph the OCR engine dropped) in that case.
        var pt = prev.Text.TrimEnd();
        var nt = next.Text.TrimStart();
        if (pt.Length > 0 && nt.Length > 0 && IsHangul(pt[^1]) && IsHangul(nt[0]) && !StartsNewKoreanWord(nt))
            allowed = Math.Max(allowed, 5f * cw);
        if (maxRight - prev.Box.Right > allowed) return false;
        if (next.Box.Left > prev.Box.Left + 1.5f * cw) return false; // indented: new paragraph / item
        if (ListMarker().IsMatch(next.Text)) return false;
        return true;
    }

    /// <summary>Appends <paramref name="next"/> to the paragraph with the right separator.</summary>
    internal static void Join(StringBuilder sb, string next)
    {
        next = next.TrimStart();
        if (next.Length == 0) return;
        while (sb.Length > 0 && char.IsWhiteSpace(sb[^1])) sb.Length--;
        if (sb.Length == 0)
        {
            sb.Append(next);
            return;
        }
        char a = sb[^1], b = next[0];

        // "infor-\nmation" -> "information"
        if (a == '-' && sb.Length >= 2 && IsLatin(sb[^2]) && char.IsLower(b))
        {
            sb.Length--;
            sb.Append(next);
            return;
        }
        if (IsHangul(a) && IsHangul(b))
        {
            if (!StartsNewKoreanWord(next)) sb.Append(next);
            else sb.Append(' ').Append(next);
            return;
        }
        if (".,;:!?)]}%'\"’”」』…".Contains(b) || "+/\\([{@#_~".Contains(a))
        {
            sb.Append(next);
            return;
        }
        sb.Append(' ').Append(next);
    }

    private static bool StartsNewKoreanWord(string next)
    {
        int end = 0;
        while (end < next.Length && !char.IsWhiteSpace(next[end])) end++;
        var token = next[..end];
        if (!Continuations.Contains(token[0])) return true;
        foreach (var w in WordStarts)
            if (token.StartsWith(w, StringComparison.Ordinal)) return true;
        return false;
    }

    public static bool IsHangul(char c) =>
        c is >= '\uAC00' and <= '\uD7A3' or >= '\u1100' and <= '\u11FF' or >= '\u3130' and <= '\u318F';

    private static bool IsLatin(char c) => c is >= 'A' and <= 'Z' or >= 'a' and <= 'z';
}
