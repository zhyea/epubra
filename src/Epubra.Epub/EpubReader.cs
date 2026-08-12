using System.IO.Compression;
using System.Text;
using System.Xml;
using System.Xml.Linq;
using Epubra.Core;

namespace Epubra.Epub;

/// <summary>
/// 从 EPUB 3.0 ZIP 文件解析为 <see cref="Book"/> 模型。
/// 读取流程：ZIP → container.xml → content.opf → manifest/spine → 章节 XHTML + 资源。
/// </summary>
public sealed class EpubReader
{
    /// <summary>从文件路径读取 EPUB。</summary>
    public async Task<Book> ReadAsync(string filePath, CancellationToken ct = default)
    {
        ArgumentException.ThrowIfNullOrEmpty(filePath);
        await using var fs = new FileStream(filePath, FileMode.Open, FileAccess.Read, FileShare.Read, 8192, useAsync: true);
        return await ReadFromStreamAsync(fs, ct).ConfigureAwait(false);
    }

    /// <summary>从流读取 EPUB。</summary>
    public async Task<Book> ReadFromStreamAsync(Stream stream, CancellationToken ct = default)
    {
        using var archive = new ZipArchive(stream, ZipArchiveMode.Read, leaveOpen: true, entryNameEncoding: Encoding.UTF8);

        // 1) 找到 container.xml
        var containerEntry = archive.GetEntry(EpubNames.ContainerXmlPath)
            ?? throw new InvalidDataException("缺少 META-INF/container.xml，不是有效的 EPUB 文件。");

        string opfPath;
        using (var containerStream = containerEntry.Open())
        {
            opfPath = ParseContainerXml(containerStream);
        }

        // 2) 读取 OPF（content.opf）
        var opfEntry = archive.GetEntry(opfPath)
            ?? throw new InvalidDataException($"找不到 OPF 文件：{opfPath}");

        var opfDir = Path.GetDirectoryName(opfPath)?.Replace('\\', '/') ?? "";
        OpfData opf;
        using (var opfStream = opfEntry.Open())
        {
            opf = await ParseOpfAsync(opfStream, ct).ConfigureAwait(false);
        }

        // 3) 构建 Book
        var book = new Book
        {
            Title = opf.Title ?? "未命名书籍",
            Author = opf.Author ?? "",
            Language = opf.Language ?? "zh-CN",
            Publisher = opf.Publisher,
            Description = opf.Description,
            Subject = opf.Subject,
            Identifier = opf.Identifier
        };

        // 4) 读取章节（按 spine 顺序）
        var opfDirPrefix = string.IsNullOrEmpty(opfDir) ? "" : opfDir + "/";
        foreach (var spineItem in opf.Spine)
        {
            if (!opf.Manifest.TryGetValue(spineItem.IdRef, out var manifestItem))
                continue;

            var entryPath = NormalizePath(opfDirPrefix + manifestItem.Href);
            var entry = archive.GetEntry(entryPath);
            if (entry is null) continue;

            string xhtml;
            using (var reader = new StreamReader(entry.Open(), Encoding.UTF8))
            {
                xhtml = await reader.ReadToEndAsync(ct).ConfigureAwait(false);
            }

            var chapter = new Chapter
            {
                Title = ExtractTitle(xhtml) ?? manifestItem.Href,
                XhtmlContent = xhtml,
                FileName = Path.GetFileNameWithoutExtension(manifestItem.Href),
                Order = book.Chapters.Count,
                ParentId = null
            };
            book.Chapters.Add(chapter);
        }

        // 5) 读取资源（图片、字体、CSS）
        foreach (var (id, manifestItem) in opf.Manifest)
        {
            // 跳过章节 XHTML（已在上面处理）和 nav/ncx
            if (opf.Spine.Any(s => s.IdRef == id)) continue;
            if (id == "nav" || id == "ncx") continue;
            if (manifestItem.MediaType is "application/xhtml+xml" or "application/x-dtbncx+xml") continue;

            var entryPath = NormalizePath(opfDirPrefix + manifestItem.Href);
            var entry = archive.GetEntry(entryPath);
            if (entry is null) continue;

            byte[] data;
            using (var ms = new MemoryStream())
            {
                await using (var entryStream = entry.Open())
                {
                    await entryStream.CopyToAsync(ms, ct).ConfigureAwait(false);
                }
                data = ms.ToArray();
            }

            var kind = ClassifyResource(manifestItem.MediaType, manifestItem.Href);
            var resource = new EpubResource
            {
                Kind = kind,
                FileName = Path.GetFileName(manifestItem.Href),
                MimeType = manifestItem.MediaType,
                Data = data,
                Role = manifestItem.Properties == "cover-image" ? "cover" : null
            };
            book.Resources.Add(resource);

            // 如果是封面，设置 CoverResourceId
            if (manifestItem.Properties == "cover-image" || opf.CoverManifestId == id)
            {
                book.CoverResourceId = resource.Id;
            }
        }

        return book;
    }

    /// <summary>解析 container.xml，返回 OPF 文件路径。</summary>
    private static string ParseContainerXml(Stream stream)
    {
        using var reader = new StreamReader(stream, Encoding.UTF8);
        var doc = XDocument.Parse(reader.ReadToEnd());
        var ns = XNamespace.Get(EpubNames.ContainerNamespace);
        var rootfile = doc.Descendants(ns + "rootfile").FirstOrDefault()
            ?? throw new InvalidDataException("container.xml 缺少 rootfile 元素。");
        return rootfile.Attribute("full-path")?.Value
            ?? throw new InvalidDataException("rootfile 缺少 full-path 属性。");
    }

    /// <summary>解析 OPF（content.opf），提取 metadata、manifest、spine。</summary>
    private static async Task<OpfData> ParseOpfAsync(Stream stream, CancellationToken ct)
    {
        using var reader = new StreamReader(stream, Encoding.UTF8);
        var doc = XDocument.Parse(await reader.ReadToEndAsync(ct).ConfigureAwait(false));

        var opfNs = XNamespace.Get(EpubNames.OpfNamespace);
        var dcNs = XNamespace.Get(EpubNames.DcNamespace);

        // metadata
        var metadata = doc.Descendants(opfNs + "metadata").FirstOrDefault();
        var title = metadata?.Element(dcNs + "title")?.Value?.Trim();
        var author = metadata?.Element(dcNs + "creator")?.Value?.Trim();
        var language = metadata?.Element(dcNs + "language")?.Value?.Trim();
        var publisher = metadata?.Element(dcNs + "publisher")?.Value?.Trim();
        var description = metadata?.Element(dcNs + "description")?.Value?.Trim();
        var subject = metadata?.Element(dcNs + "subject")?.Value?.Trim();
        var identifier = metadata?.Element(dcNs + "identifier")?.Value?.Trim();

        // cover meta (EPUB 2 style: <meta name="cover" content="id"/>)
        string? coverManifestId = null;
        if (metadata is not null)
        {
            var coverMeta = metadata.Elements(opfNs + "meta")
                .FirstOrDefault(m => m.Attribute("name")?.Value == "cover");
            coverManifestId = coverMeta?.Attribute("content")?.Value;
        }

        // manifest
        var manifest = new Dictionary<string, ManifestItem>(StringComparer.Ordinal);
        foreach (var item in doc.Descendants(opfNs + "item"))
        {
            var id = item.Attribute("id")?.Value;
            var href = item.Attribute("href")?.Value;
            var mediaType = item.Attribute("media-type")?.Value ?? "";
            var properties = item.Attribute("properties")?.Value;
            if (id is not null && href is not null)
            {
                manifest[id] = new ManifestItem(id, href, mediaType, properties);
            }
        }

        // spine
        var spine = new List<SpineItem>();
        foreach (var itemref in doc.Descendants(opfNs + "itemref"))
        {
            var idref = itemref.Attribute("idref")?.Value;
            if (idref is not null)
            {
                spine.Add(new SpineItem(idref));
            }
        }

        return new OpfData(
            Title: title,
            Author: author,
            Language: language,
            Publisher: publisher,
            Description: description,
            Subject: subject,
            Identifier: identifier,
            CoverManifestId: coverManifestId,
            Manifest: manifest,
            Spine: spine);
    }

    /// <summary>从 XHTML 中提取 &lt;title&gt; 或第一个标题文本。</summary>
    private static string? ExtractTitle(string xhtml)
    {
        try
        {
            var doc = XDocument.Parse(xhtml, LoadOptions.PreserveWhitespace);
            var ns = XNamespace.Get(EpubNames.XhtmlNamespace);
            var titleEl = doc.Descendants(ns + "title").FirstOrDefault();
            if (titleEl is not null && !string.IsNullOrWhiteSpace(titleEl.Value))
            {
                return titleEl.Value.Trim();
            }

            var h1 = doc.Descendants(ns + "h1").FirstOrDefault();
            if (h1 is not null && !string.IsNullOrWhiteSpace(h1.Value))
            {
                return h1.Value.Trim();
            }
        }
        catch
        {
            // XML 解析失败时返回 null
        }
        return null;
    }

    /// <summary>根据 MIME 类型分类资源。</summary>
    private static EpubResourceKind ClassifyResource(string mediaType, string href)
    {
        if (mediaType.StartsWith("image/", StringComparison.Ordinal))
            return EpubResourceKind.Image;
        if (mediaType.StartsWith("audio/", StringComparison.Ordinal))
            return EpubResourceKind.Audio;
        if (mediaType.StartsWith("video/", StringComparison.Ordinal))
            return EpubResourceKind.Video;
        if (mediaType.StartsWith("font/", StringComparison.Ordinal) || mediaType is "application/font-woff" or "application/font-woff2")
            return EpubResourceKind.Font;
        if (mediaType is "text/css")
            return EpubResourceKind.StyleSheet;
        return EpubResourceKind.Other;
    }

    /// <summary>规范化 ZIP 内部路径（统一正斜杠、去除前导 ./）。</summary>
    private static string NormalizePath(string path)
    {
        return path.Replace('\\', '/').TrimStart('.', '/');
    }

    // ===== 数据结构 =====

    private sealed record OpfData(
        string? Title,
        string? Author,
        string? Language,
        string? Publisher,
        string? Description,
        string? Subject,
        string? Identifier,
        string? CoverManifestId,
        Dictionary<string, ManifestItem> Manifest,
        List<SpineItem> Spine);

    private sealed record ManifestItem(string Id, string Href, string MediaType, string? Properties);

    private sealed record SpineItem(string IdRef);
}
