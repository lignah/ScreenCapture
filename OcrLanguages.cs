using System.Net.Http;
using System.Security.Cryptography;

namespace ScreenCapture;

/// <summary>A downloadable PaddleOCR recognition model (ONNX, from the RapidAI/RapidOCR ModelScope repo).</summary>
internal sealed record OcrLanguageInfo(
    string Id, string Name, string Version,
    string RecPath, long RecSize, string RecSha256,
    string DictPath, string DictSha256);

/// <summary>An installed recognition language: model + dictionary files.</summary>
internal sealed record InstalledOcrLanguage(OcrLanguageInfo Info, string RecFile, string DictFile, bool Bundled);

/// <summary>
/// PaddleOCR recognition languages. The Korean model ships next to the exe (models\, read-only baseline);
/// others are downloaded per user into %LOCALAPPDATA%\ScreenCapture\models\&lt;id&gt;\ (rec.onnx + dict.txt).
/// The detection model (det.onnx) is shared by all languages and always bundled.
/// </summary>
internal static class OcrLanguages
{
    public const string BaseUrl = "https://www.modelscope.cn/models/RapidAI/RapidOCR/resolve/master/";
    public const string DefaultId = "korean";

    public static readonly IReadOnlyList<OcrLanguageInfo> Catalog = new OcrLanguageInfo[]
    {
        new("korean", "한국어 (+영어)", "PP-OCRv5",
            "onnx/PP-OCRv5/rec/korean_PP-OCRv5_rec_mobile.onnx", 13488748, "cd6e2ea50f6943ca7271eb8c56a877a5a90720b7047fe9c41a2e541a25773c9b",
            "paddle/PP-OCRv5/rec/korean_PP-OCRv5_rec_mobile/ppocrv5_korean_dict.txt", "a88071c68c01707489baa79ebe0405b7beb5cca229f4fc94cc3ef992328802d7"),
        new("ch", "중국어 간체·번체 + 영어 + 일본어", "PP-OCRv5",
            "onnx/PP-OCRv5/rec/ch_PP-OCRv5_rec_mobile.onnx", 16631306, "5825fc7ebf84ae7a412be049820b4d86d77620f204a041697b0494669b1742c5",
            "paddle/PP-OCRv5/rec/ch_PP-OCRv5_rec_mobile/ppocrv5_dict.txt", "d1979e9f794c464c0d2e0b70a7fe14dd978e9dc644c0e71f14158cdf8342af1b"),
        new("en", "영어", "PP-OCRv5",
            "onnx/PP-OCRv5/rec/en_PP-OCRv5_rec_mobile.onnx", 7872351, "c3461add59bb4323ecba96a492ab75e06dda42467c9e3d0c18db5d1d21924be8",
            "paddle/PP-OCRv5/rec/en_PP-OCRv5_rec_mobile/ppocrv5_en_dict.txt", "e025a66d31f327ba0c232e03f407ae8d105e1e709e7ccb3f408aa778c24e70d6"),
        new("japan", "일본어", "PP-OCRv4",
            "onnx/PP-OCRv4/rec/japan_PP-OCRv4_rec_mobile.onnx", 9753335, "e1075a67dba758ecfc7ebc78a10ae61c95ac8fb66a9c86fab5541e33f085cb7a",
            "paddle/PP-OCRv4/rec/japan_PP-OCRv4_rec_mobile/japan_dict.txt", "1dcfcb41eec90576a945b3084f22ade11ced506e24f14879245b071698f308e8"),
        new("latin", "라틴 문자 (프랑스어·독일어·스페인어·베트남어 등)", "PP-OCRv5",
            "onnx/PP-OCRv5/rec/latin_PP-OCRv5_rec_mobile.onnx", 7904513, "b20bd37c168a570f583afbc8cd7925603890efbcdc000a59e22c269d160b5f5a",
            "paddle/PP-OCRv5/rec/latin_PP-OCRv5_rec_mobile/ppocrv5_latin_dict.txt", "3c0a8a79b612653c25f765271714f71281e4e955962c153e272b7b8c1d2b13ff"),
        new("cyrillic", "키릴 문자 (러시아어·불가리아어·세르비아어 등)", "PP-OCRv5",
            "onnx/PP-OCRv5/rec/cyrillic_PP-OCRv5_rec_mobile.onnx", 8074092, "90f761b4bfcce0c8c561c0cb5c887b0971d3ec01c32164bdf7374a35b0982711",
            "paddle/PP-OCRv5/rec/cyrillic_PP-OCRv5_rec_mobile/ppocrv5_cyrillic_dict.txt", "db40aa52ceb112055be80c694afdf655d5d2c4f7873704524cc16a447ca913ba"),
        new("eslav", "동슬라브어 (러시아어·우크라이나어·벨라루스어)", "PP-OCRv5",
            "onnx/PP-OCRv5/rec/eslav_PP-OCRv5_rec_mobile.onnx", 7911802, "08705d6721849b1347d26187f15a5e362c431963a2a62bfff4feac578c489aab",
            "paddle/PP-OCRv5/rec/eslav_PP-OCRv5_rec_mobile/ppocrv5_eslav_dict.txt", "3e95f1581557162870cacdba5af91a4c6be2890710d395b0c3c7578e7ee5e6eb"),
        new("el", "그리스어", "PP-OCRv5",
            "onnx/PP-OCRv5/rec/el_PP-OCRv5_rec_mobile.onnx", 7832350, "b4368bccd557123c702b7549fee6cd1e94b581337d1c9b65310f109131542b7f",
            "paddle/PP-OCRv5/rec/el_PP-OCRv5_rec_mobile/ppocrv5_el_dict.txt", "31defc62c0c3ad3674a82da6192226a2ba98ef4ff014a7045cb88d59f9c3de31"),
        new("arabic", "아랍 문자 (아랍어·페르시아어·우르두어 등)", "PP-OCRv5",
            "onnx/PP-OCRv5/rec/arabic_PP-OCRv5_rec_mobile.onnx", 8023828, "c1192e632d0baa9146ae5b756a0e635e3dc63c1733737ebfd1629e87144e9295",
            "paddle/PP-OCRv5/rec/arabic_PP-OCRv5_rec_mobile/ppocrv5_arabic_dict.txt", "7f92f7dbb9b75a4787a83bfb4f6d14a8ab515525130c9d40a9036f61cf6999e9"),
        new("devanagari", "데바나가리 (힌디어·마라티어·네팔어 등)", "PP-OCRv5",
            "onnx/PP-OCRv5/rec/devanagari_PP-OCRv5_rec_mobile.onnx", 7940361, "d6f0a906580e3fa6b324a318718f1f31f268b6ea8ef985f91c2012a37f52c91e",
            "paddle/PP-OCRv5/rec/devanagari_PP-OCRv5_rec_mobile/ppocrv5_devanagari_dict.txt", "09c7440bfc5477e5c41052304b6b185aff8c4a5e8b2b4c23c1c706f6fe1ee9fc"),
        new("th", "태국어", "PP-OCRv5",
            "onnx/PP-OCRv5/rec/th_PP-OCRv5_rec_mobile.onnx", 7915294, "de541dd83161c241ff426f7ecfd602a0ba77d686cf3ab9a6c255ea82fd08006e",
            "paddle/PP-OCRv5/rec/th_PP-OCRv5_rec_mobile/ppocrv5_th_dict.txt", "57f5406f94bb6688fb7077f7be65f08bbd71cecf48c01ea26c522cb5c4836b7a"),
        new("ta", "타밀어", "PP-OCRv5",
            "onnx/PP-OCRv5/rec/ta_PP-OCRv5_rec_mobile.onnx", 7909926, "a42448808b7dea87597336f12438935f40353f1949e8360acd9e06b4da21bfe1",
            "paddle/PP-OCRv5/rec/ta_PP-OCRv5_rec_mobile/ppocrv5_ta_dict.txt", "85b541352ae18dc6ba6d47152d8bf8adff6b0266e605d2eef2990c1bf466117b"),
        new("te", "텔루구어", "PP-OCRv5",
            "onnx/PP-OCRv5/rec/te_PP-OCRv5_rec_mobile.onnx", 7923102, "a3690451b50028a09a3316a1274f7c05728151ea3f8fd392696397a7fefcbd92",
            "paddle/PP-OCRv5/rec/te_PP-OCRv5_rec_mobile/ppocrv5_te_dict.txt", "42f83f5d3fdb50778e4fa5b66c58d99a59ab7792151c5e74f34b8ffd7b61c9d6"),
    };

    public static string BundledDir => Path.Combine(AppContext.BaseDirectory, "models");

    public static string UserDir => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "ScreenCapture", "models");

    public static OcrLanguageInfo? Find(string? id) =>
        Catalog.FirstOrDefault(c => string.Equals(c.Id, id, StringComparison.OrdinalIgnoreCase));

    private static string UserLangDir(string id) => Path.Combine(UserDir, id);

    /// <summary>Installed languages in catalog order (bundled Korean first when present).</summary>
    public static List<InstalledOcrLanguage> Installed()
    {
        var list = new List<InstalledOcrLanguage>();
        foreach (var info in Catalog)
        {
            if (info.Id == DefaultId)
            {
                string br = Path.Combine(BundledDir, PaddleOcr.RecFile), bd = Path.Combine(BundledDir, PaddleOcr.DictFile);
                if (File.Exists(br) && File.Exists(bd)) { list.Add(new(info, br, bd, true)); continue; }
            }
            string ur = Path.Combine(UserLangDir(info.Id), "rec.onnx"), ud = Path.Combine(UserLangDir(info.Id), "dict.txt");
            if (File.Exists(ur) && File.Exists(ud)) list.Add(new(info, ur, ud, false));
        }
        return list;
    }

    /// <summary>The requested language if installed, else the first installed one, else null.</summary>
    public static InstalledOcrLanguage? Resolve(string? id)
    {
        var all = Installed();
        return all.FirstOrDefault(l => string.Equals(l.Info.Id, id, StringComparison.OrdinalIgnoreCase)) ?? all.FirstOrDefault();
    }

    /// <summary>Downloads model + dict to temp files, verifies SHA-256, then moves them into place.</summary>
    public static async Task DownloadAsync(OcrLanguageInfo info, IProgress<(long Done, long Total)>? progress, CancellationToken ct)
    {
        string dir = UserLangDir(info.Id);
        Directory.CreateDirectory(dir);
        string recTmp = Path.Combine(dir, "rec.onnx.part"), dictTmp = Path.Combine(dir, "dict.txt.part");
        try
        {
            using var http = new HttpClient { Timeout = TimeSpan.FromMinutes(10) };
            http.DefaultRequestHeaders.UserAgent.ParseAdd("ScreenCapture/1.0");
            await Fetch(http, BaseUrl + info.DictPath, dictTmp, info.DictSha256, null, 0, ct);
            await Fetch(http, BaseUrl + info.RecPath, recTmp, info.RecSha256, progress, info.RecSize, ct);
            File.Move(dictTmp, Path.Combine(dir, "dict.txt"), true);
            File.Move(recTmp, Path.Combine(dir, "rec.onnx"), true);
        }
        finally
        {
            TryDelete(recTmp);
            TryDelete(dictTmp);
            if (Directory.Exists(dir) && !Directory.EnumerateFileSystemEntries(dir).Any()) Directory.Delete(dir);
        }
    }

    private static async Task Fetch(HttpClient http, string url, string path, string sha256,
        IProgress<(long, long)>? progress, long expected, CancellationToken ct)
    {
        using var resp = await http.GetAsync(url, HttpCompletionOption.ResponseHeadersRead, ct);
        resp.EnsureSuccessStatusCode();
        long total = resp.Content.Headers.ContentLength ?? expected;
        using var hash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
        await using (var input = await resp.Content.ReadAsStreamAsync(ct))
        await using (var output = File.Create(path))
        {
            var buf = new byte[81920];
            long done = 0;
            int n;
            while ((n = await input.ReadAsync(buf, ct)) > 0)
            {
                await output.WriteAsync(buf.AsMemory(0, n), ct);
                hash.AppendData(buf, 0, n);
                done += n;
                progress?.Report((done, total));
            }
        }
        string got = Convert.ToHexString(hash.GetHashAndReset()).ToLowerInvariant();
        if (got != sha256) throw new InvalidDataException($"SHA-256 mismatch for {Path.GetFileName(url)}");
    }

    /// <summary>Deletes a downloaded (user-folder) language. Bundled models are never deleted.</summary>
    public static bool Delete(string id)
    {
        var lang = Installed().FirstOrDefault(l => l.Info.Id == id);
        if (lang == null || lang.Bundled) return false;
        string dir = UserLangDir(id);
        if (Directory.Exists(dir)) Directory.Delete(dir, true);
        return !Directory.Exists(dir);
    }

    private static void TryDelete(string path)
    {
        try { if (File.Exists(path)) File.Delete(path); } catch { }
    }

    public static string Display(OcrLanguageInfo info) => $"{info.Name}";
}
