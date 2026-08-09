using System.IO.Compression;
using System.Net;
using System.Text;
using Epubra.Core;

namespace Epubra.Epub;

/// <summary>
/// 把 <see cref="Book"/> 写入到 EPUB 3.0 格式的 ZIP 流。
/// 关键规范约束：
///  - 第一个 zip 条目必须是 <c>mimetype</c>，且 CompressionLevel = NoCompression；
///  - 必须包含 <c>META-INF/container.xml</c>；
///  - 同时输出 <c>nav.xhtml</c>（EPUB 3）和 <c>toc.ncx</c>（EPUB 2 兼容）；
///  - 资源按 OEBPS/{images|fonts|styles|misc}/&lt;name&gt; 组织。
/// </summary>
public sealed class EpubWriter
{
    /// <summary>把书打包写入流。</summary>
    /// <param name="book">要打包的书。</param>
    /// <param name="output">目标流（写入完成后不会关闭，由调用方管理）。</param>
    /// <param name="ct">取消令牌。</param>
    public async Task WriteAsync(Book book, Stream output, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(book);
        ArgumentNullException.ThrowIfNull(output);

        if (string.IsNullOrWhiteSpace(book.Title))
        {
            throw new InvalidOperationException("Book.Title 不能为空。");
        }

        var plan = EpubPackPlan.Build(book);

        using var archive = new ZipArchive(output, ZipArchiveMode.Create, leaveOpen: true, entryNameEncoding: Encoding.UTF8);

        // 1) mimetype 必须是第一个 entry，且 NoCompression
        WriteTextEntry(archive, "mimetype", EpubNames.MimetypeContent, CompressionLevel.NoCompression, ct);

        // 2) META-INF/container.xml
        WriteTextEntry(archive, EpubNames.ContainerXmlPath, EpubXml.BuildContainerXml(), CompressionLevel.Optimal, ct);

        // 3) OEBPS/content.opf
        WriteTextEntry(archive, EpubNames.ContentOpfPath, EpubXml.BuildContentOpf(plan), CompressionLevel.Optimal, ct);

        // 4) OEBPS/nav.xhtml
        WriteTextEntry(archive, EpubNames.NavXhtmlPath, EpubXml.BuildNavXhtml(plan), CompressionLevel.Optimal, ct);

        // 5) OEBPS/toc.ncx
        WriteTextEntry(archive, EpubNames.TocNcxPath, EpubXml.BuildTocNcx(plan), CompressionLevel.Optimal, ct);

        // 6) 章节 XHTML
        foreach (var ch in plan.Spine)
        {
            var entryPath = $"OEBPS/{ch.FileName}.xhtml";
            var content = NormalizeChapterContent(ch.Source.XhtmlContent, plan.Book.Language);
            WriteTextEntry(archive, entryPath, content, CompressionLevel.Optimal, ct);
        }

        // 7) 资源（图片、字体、样式）
        foreach (var r in plan.Resources)
        {
            var entryPath = $"OEBPS/{r.Href}";
            await WriteBinaryEntry(archive, entryPath, r.Source.Data, CompressionLevel.Optimal, ct);
        }
    }

    /// <summary>便捷方法：写入到文件路径。</summary>
    public async Task WriteToFileAsync(Book book, string filePath, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(book);
        ArgumentException.ThrowIfNullOrEmpty(filePath);

        var dir = Path.GetDirectoryName(filePath);
        if (!string.IsNullOrEmpty(dir) && !Directory.Exists(dir))
        {
            Directory.CreateDirectory(dir);
        }

        await using var fs = new FileStream(filePath, FileMode.Create, FileAccess.Write, FileShare.None, 8192, useAsync: true);
        await WriteAsync(book, fs, ct).ConfigureAwait(false);
    }

    private static void WriteTextEntry(ZipArchive archive, string path, string content, CompressionLevel level, CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();
        var entry = archive.CreateEntry(path, level);
        using var stream = entry.Open();
        var bytes = new UTF8Encoding(encoderShouldEmitUTF8Identifier: false).GetBytes(content);
        stream.Write(bytes, 0, bytes.Length);
    }

    private static async Task WriteBinaryEntry(ZipArchive archive, string path, byte[] data, CompressionLevel level, CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();
        var entry = archive.CreateEntry(path, level);
        await using var stream = entry.Open();
        if (data.Length > 0)
        {
            await stream.WriteAsync(data, ct).ConfigureAwait(false);
        }
    }

    /// <summary>
    /// 把章节内容规范化为合法的 XHTML 文档：确保有 xml declaration、html/body 根节点、utf-8 编码。
    /// 如果用户提供的 XhtmlContent 已经是完整文档（以 &lt;html 开头），则直接返回。
    /// 否则包裹到最小骨架中。
    /// </summary>
    private static string NormalizeChapterContent(string content, string language)
    {
        if (string.IsNullOrWhiteSpace(content))
        {
            content = "<p></p>";
        }

        var trimmed = content.TrimStart();
        if (trimmed.StartsWith("<?xml", StringComparison.Ordinal) || trimmed.StartsWith("<html", StringComparison.OrdinalIgnoreCase))
        {
            return content;
        }

        // 包裹到最小骨架
        var langAttr = WebUtility.HtmlEncode(language);
        var sb = new StringBuilder();
        sb.Append("<?xml version=\"1.0\" encoding=\"utf-8\"?>\n");
        sb.Append("<html xmlns=\"").Append(EpubNames.XhtmlNamespace).Append("\" xml:lang=\"").Append(langAttr).Append("\">\n");
        sb.Append("<head><meta charset=\"utf-8\"/></head>\n");
        sb.Append("<body>\n");
        sb.Append(content);
        sb.Append("\n</body>\n");
        sb.Append("</html>\n");
        return sb.ToString();
    }
}