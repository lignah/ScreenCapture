using System.Drawing.Imaging;

namespace ScreenCapture;

internal static class ImageOutput
{
    public static string DefaultFolder => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.MyPictures), "Screenshots");

    public static string TimestampName() => $"Screenshot {DateTime.Now:yyyy-MM-dd HHmmss}.png";

    /// <summary>Saves to <paramref name="folder"/> with a unique timestamped name; returns the path.</summary>
    public static string SaveAuto(Bitmap bmp, string folder)
    {
        Directory.CreateDirectory(folder);
        var stamp = DateTime.Now.ToString("yyyy-MM-dd HHmmss");
        var path = Path.Combine(folder, $"Screenshot {stamp}.png");
        for (int i = 2; File.Exists(path); i++)
            path = Path.Combine(folder, $"Screenshot {stamp} ({i}).png");
        Save(bmp, path);
        return path;
    }

    public static void Save(Bitmap bmp, string path)
    {
        var ext = Path.GetExtension(path).ToLowerInvariant();
        var format = ext switch
        {
            ".jpg" or ".jpeg" => ImageFormat.Jpeg,
            ".bmp" => ImageFormat.Bmp,
            _ => ImageFormat.Png,
        };
        var dir = Path.GetDirectoryName(path);
        if (!string.IsNullOrEmpty(dir)) Directory.CreateDirectory(dir);
        bmp.Save(path, format);
    }

    /// <summary>Puts the image on the clipboard as both a bitmap and PNG; survives app exit.</summary>
    public static void CopyToClipboard(Bitmap bmp)
    {
        var data = new DataObject();
        data.SetImage(bmp);
        var png = new MemoryStream();
        bmp.Save(png, ImageFormat.Png);
        data.SetData("PNG", false, png);
        Clipboard.SetDataObject(data, true, 10, 100);
    }
}
