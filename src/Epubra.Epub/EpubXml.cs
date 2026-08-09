using System.Globalization;
using System.Text;
using System.Xml;
using System.Xml.Linq;
using Epubra.Core;

namespace Epubra.Epub;

/// <summary>
/// 生成 EPUB 内部固定 XML 文档的工厂集合（container.xml / content.opf / nav.xhtml / toc.ncx）。
/// 所有输出使用 UTF-8（无 BOM），使用 LF 换行。
/// </summary>
internal static class EpubXml
{
    private static readonly XmlWriterSettings WriterSettings = new()
    {
        Indent = true,
        IndentChars = "  ",
        NewLineChars = "\n",
        NewLineHandling = NewLineHandling.Replace,
        Encoding = new UTF8Encoding(encoderShouldEmitUTF8Identifier: false),
        OmitXmlDeclaration = false
    };

    /// <summary>生成 META-INF/container.xml。</summary>
    public static string BuildContainerXml()
    {
        var doc = new XDocument(
            new XDeclaration("1.0", "utf-8", null),
            new XElement(XName.Get("container", EpubNames.ContainerNamespace),
                new XAttribute("version", "1.0"),
                new XElement(XName.Get("rootfiles", EpubNames.ContainerNamespace),
                    new XElement(XName.Get("rootfile", EpubNames.ContainerNamespace),
                        new XAttribute("full-path", EpubNames.ContentOpfPath),
                        new XAttribute("media-type", "application/oebps-package+xml")))));

        return Serialize(doc);
    }

    /// <summary>生成 OEBPS/content.opf（包络文件）。</summary>
    public static string BuildContentOpf(EpubPackPlan plan)
    {
        var opfNs = XNamespace.Get(EpubNames.OpfNamespace);
        var dcNs = XNamespace.Get(EpubNames.DcNamespace);

        var package = new XElement(opfNs + "package",
            new XAttribute("version", "3.0"),
            new XAttribute(XNamespace.Xml + "lang", plan.Book.Language),
            new XAttribute("unique-identifier", "bookid"));

        // metadata
        var metadata = new XElement(opfNs + "metadata");

        // identifier
        var identifier = string.IsNullOrWhiteSpace(plan.Book.Identifier)
            ? $"urn:uuid:{plan.Book.Id:D}"
            : plan.Book.Identifier!;
        metadata.Add(new XElement(dcNs + "identifier",
            new XAttribute("id", "bookid"),
            identifier));

        metadata.Add(new XElement(dcNs + "title", plan.Book.Title));
        metadata.Add(new XElement(dcNs + "language", plan.Book.Language));

        if (!string.IsNullOrWhiteSpace(plan.Book.Author))
        {
            metadata.Add(new XElement(dcNs + "creator",
                new XAttribute("id", "creator"),
                plan.Book.Author));
        }

        if (!string.IsNullOrWhiteSpace(plan.Book.Publisher))
        {
            metadata.Add(new XElement(dcNs + "publisher", plan.Book.Publisher));
        }

        if (!string.IsNullOrWhiteSpace(plan.Book.Description))
        {
            metadata.Add(new XElement(dcNs + "description", plan.Book.Description));
        }

        if (!string.IsNullOrWhiteSpace(plan.Book.Subject))
        {
            metadata.Add(new XElement(dcNs + "subject", plan.Book.Subject));
        }

        // dcterms:modified (EPUB 3 必需)
        var modified = plan.Book.ModifiedAt.UtcDateTime.ToString("yyyy-MM-ddTHH:mm:ssZ", CultureInfo.InvariantCulture);
        metadata.Add(new XElement(opfNs + "meta",
            new XAttribute("property", "dcterms:modified"),
            modified));

        // cover
        if (plan.CoverHref is not null)
        {
            var coverResource = plan.Resources.FirstOrDefault(r => r.Href == plan.CoverHref);
            if (coverResource is not null)
            {
                metadata.Add(new XElement(opfNs + "meta",
                    new XAttribute("name", "cover"),
                    new XAttribute("content", coverResource.ManifestId)));
            }
        }

        package.Add(metadata);

        // manifest
        var manifest = new XElement(opfNs + "manifest");

        // nav
        manifest.Add(new XElement(opfNs + "item",
            new XAttribute("id", "nav"),
            new XAttribute("href", Path.GetFileName(EpubNames.NavXhtmlPath)),
            new XAttribute("media-type", "application/xhtml+xml"),
            new XAttribute("properties", "nav")));

        // ncx
        manifest.Add(new XElement(opfNs + "item",
            new XAttribute("id", "ncx"),
            new XAttribute("href", Path.GetFileName(EpubNames.TocNcxPath)),
            new XAttribute("media-type", "application/x-dtbncx+xml")));

        // chapters
        foreach (var ch in plan.Spine)
        {
            manifest.Add(new XElement(opfNs + "item",
                new XAttribute("id", ch.ManifestId),
                new XAttribute("href", $"{ch.FileName}.xhtml"),
                new XAttribute("media-type", "application/xhtml+xml")));
        }

        // resources
        foreach (var r in plan.Resources)
        {
            var item = new XElement(opfNs + "item",
                new XAttribute("id", r.ManifestId),
                new XAttribute("href", r.Href),
                new XAttribute("media-type", r.MimeType));

            if (r.IsCover)
            {
                item.Add(new XAttribute("properties", "cover-image"));
            }

            manifest.Add(item);
        }

        package.Add(manifest);

        // spine
        var spine = new XElement(opfNs + "spine",
            new XAttribute("toc", "ncx"));
        foreach (var ch in plan.Spine)
        {
            spine.Add(new XElement(opfNs + "itemref",
                new XAttribute("idref", ch.ManifestId)));
        }
        package.Add(spine);

        var doc = new XDocument(
            new XDeclaration("1.0", "utf-8", null),
            package);

        return Serialize(doc);
    }

    /// <summary>生成 OEBPS/nav.xhtml（EPUB 3 导航）。</summary>
    public static string BuildNavXhtml(EpubPackPlan plan)
    {
        var xhtml = XNamespace.Get(EpubNames.XhtmlNamespace);
        var epub = XNamespace.Get(EpubNames.EpubNamespace);

        var html = new XElement(xhtml + "html",
            new XAttribute(XNamespace.Xml + "lang", plan.Book.Language));

        var head = new XElement(xhtml + "head",
            new XElement(xhtml + "title", plan.Book.Title),
            new XElement(xhtml + "meta",
                new XAttribute("charset", "utf-8")));

        html.Add(head);

        var body = new XElement(xhtml + "body");
        var nav = new XElement(xhtml + "nav",
            new XAttribute(epub + "type", "toc"),
            new XAttribute(XNamespace.Xml + "lang", plan.Book.Language),
            new XElement(xhtml + "h1", "目录"));

        var topOl = new XElement(xhtml + "ol");

        // 构建章节树：找到顶级章节，按父-子关系构建嵌套 ol
        foreach (var topChapter in plan.Spine.Where(c => c.Source.ParentId is null))
        {
            topOl.Add(BuildNavListItem(topChapter, plan, xhtml));
        }

        nav.Add(topOl);
        body.Add(nav);
        html.Add(body);

        var doc = new XDocument(
            new XDeclaration("1.0", "utf-8", null),
            html);

        return Serialize(doc);
    }

    private static XElement BuildNavListItem(EpubPackPlan.PlannedChapter chapter, EpubPackPlan plan, XNamespace xhtml)
    {
        var li = new XElement(xhtml + "li",
            new XElement(xhtml + "a",
                new XAttribute("href", $"{chapter.FileName}.xhtml"),
                chapter.Title));

        var children = plan.Spine.Where(c => c.Source.ParentId == chapter.Source.Id).ToList();
        if (children.Count > 0)
        {
            var subOl = new XElement(xhtml + "ol");
            foreach (var child in children)
            {
                subOl.Add(BuildNavListItem(child, plan, xhtml));
            }
            li.Add(subOl);
        }

        return li;
    }

    /// <summary>生成 OEBPS/toc.ncx（EPUB 2 兼容）。</summary>
    public static string BuildTocNcx(EpubPackPlan plan)
    {
        var ncxNs = XNamespace.Get(EpubNames.NcxNamespace);

        var identifier = string.IsNullOrWhiteSpace(plan.Book.Identifier)
            ? $"urn:uuid:{plan.Book.Id:D}"
            : plan.Book.Identifier!;

        var depth = ComputeDepth(plan);

        var ncx = new XElement(ncxNs + "ncx",
            new XAttribute("version", "2005-1"),
            new XAttribute(XNamespace.Xml + "lang", plan.Book.Language));

        var head = new XElement(ncxNs + "head");
        head.Add(new XElement(ncxNs + "meta",
            new XAttribute("name", "dtb:uid"),
            new XAttribute("content", identifier)));
        head.Add(new XElement(ncxNs + "meta",
            new XAttribute("name", "dtb:depth"),
            new XAttribute("content", depth.ToString(CultureInfo.InvariantCulture))));
        head.Add(new XElement(ncxNs + "meta",
            new XAttribute("name", "dtb:totalPageCount"),
            new XAttribute("content", "0")));
        head.Add(new XElement(ncxNs + "meta",
            new XAttribute("name", "dtb:maxPageNumber"),
            new XAttribute("content", "0")));

        ncx.Add(head);
        ncx.Add(new XElement(ncxNs + "docTitle",
            new XElement(ncxNs + "text", plan.Book.Title)));

        var navMap = new XElement(ncxNs + "navMap");
        var playOrder = 1;
        foreach (var ch in plan.Spine)
        {
            navMap.Add(BuildNavPoint(ch, plan, ref playOrder));
        }

        ncx.Add(navMap);

        var doc = new XDocument(
            new XDeclaration("1.0", "utf-8", null),
            ncx);

        return Serialize(doc);
    }

    private static XElement BuildNavPoint(
        EpubPackPlan.PlannedChapter chapter,
        EpubPackPlan plan,
        ref int playOrder)
    {
        var ncxNs = XNamespace.Get(EpubNames.NcxNamespace);
        var currentOrder = playOrder++;

        var np = new XElement(ncxNs + "navPoint",
            new XAttribute("id", $"navPoint-{currentOrder}"),
            new XAttribute("playOrder", currentOrder.ToString(CultureInfo.InvariantCulture)),
            new XElement(ncxNs + "navLabel",
                new XElement(ncxNs + "text", chapter.Title)),
            new XElement(ncxNs + "content",
                new XAttribute("src", $"{chapter.FileName}.xhtml")));

        var children = plan.Spine.Where(c => c.Source.ParentId == chapter.Source.Id).ToList();
        foreach (var child in children)
        {
            np.Add(BuildNavPoint(child, plan, ref playOrder));
        }

        return np;
    }

    private static int ComputeDepth(EpubPackPlan plan)
    {
        var maxDepth = 1;
        foreach (var ch in plan.Spine)
        {
            var depth = 1;
            var current = ch.Source;
            while (current.ParentId is not null)
            {
                depth++;
                current = plan.Spine.FirstOrDefault(x => x.Source.Id == current.ParentId)?.Source;
                if (current is null) break;
            }
            if (depth > maxDepth) maxDepth = depth;
        }
        return maxDepth;
    }

    /// <summary>把 XDocument 序列化为 UTF-8 字符串（无 BOM，LF 换行）。</summary>
    private static string Serialize(XDocument doc)
    {
        var sb = new StringBuilder();
        using (var writer = XmlWriter.Create(sb, WriterSettings))
        {
            doc.WriteTo(writer);
        }
        return sb.ToString();
    }
}