namespace Epubra.Epub;

/// <summary>
/// 根据文件扩展名推断 MIME 类型（覆盖 epub 内常用资源）。
/// </summary>
public static class MimeTypeMap
{
    /// <summary>获取资源的 MIME 类型；未知类型回退为 <c>application/octet-stream</c>。</summary>
    public static string Get(string fileName)
    {
        ArgumentException.ThrowIfNullOrEmpty(fileName);

        var ext = Path.GetExtension(fileName).TrimStart('.').ToLowerInvariant();
        return ext switch
        {
            "xhtml" or "xht" or "html" or "htm" => "application/xhtml+xml",
            "css" => "text/css",
            "jpg" or "jpeg" => "image/jpeg",
            "png" => "image/png",
            "gif" => "image/gif",
            "svg" => "image/svg+xml",
            "webp" => "image/webp",
            "mp3" => "audio/mpeg",
            "wav" => "audio/wav",
            "m4a" => "audio/mp4",
            "ogg" => "audio/ogg",
            "oga" => "audio/ogg",
            "aac" => "audio/aac",
            "flac" => "audio/flac",
            "mp4" or "m4v" => "video/mp4",
            "webm" => "video/webm",
            "ogv" => "video/ogg",
            "mov" => "video/quicktime",
            "mkv" => "video/x-matroska",
            "avi" => "video/x-msvideo",
            "ttf" => "font/ttf",
            "otf" => "font/otf",
            "woff" => "font/woff",
            "woff2" => "font/woff2",
            "ncx" => "application/x-dtbncx+xml",
            "opf" => "application/oebps-package+xml",
            "xml" => "application/xml",
            "txt" => "text/plain",
            "js" => "application/javascript",
            _ => "application/octet-stream"
        };
    }
}