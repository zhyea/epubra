namespace Epubra.Epub;

/// <summary>
/// EPUB 规范相关的固定值（路径、命名空间、MIME 等）。
/// 参考 EPUB 3.3 规范（https://www.w3.org/TR/epub-33/）。
/// </summary>
internal static class EpubNames
{
    /// <summary>EPUB 文件根目录的 mimetype 文件固定值，**且必须是 ZIP 第一个条目且不压缩**。</summary>
    public const string MimetypeContent = "application/epub+zip";

    /// <summary>容器描述文件的固定路径（epub 根目录下的 META-INF/container.xml）。</summary>
    public const string ContainerXmlPath = "META-INF/container.xml";

    /// <summary>OPF 包络文件的相对路径。</summary>
    public const string ContentOpfPath = "OEBPS/content.opf";

    /// <summary>EPUB 3 导航文档路径。</summary>
    public const string NavXhtmlPath = "OEBPS/nav.xhtml";

    /// <summary>EPUB 2 兼容的 NCX 路径（保留以兼容旧阅读器）。</summary>
    public const string TocNcxPath = "OEBPS/toc.ncx";

    /// <summary>OPF 命名空间。</summary>
    public const string OpfNamespace = "http://www.idpf.org/2007/opf";

    /// <summary>Dublin Core 命名空间（metadata 用）。</summary>
    public const string DcNamespace = "http://purl.org/dc/elements/1.1/";

    /// <summary>XHTML 命名空间。</summary>
    public const string XhtmlNamespace = "http://www.w3.org/1999/xhtml";

    /// <summary>EPUB 命名空间（nav 属性用）。</summary>
    public const string EpubNamespace = "http://www.idpf.org/2007/ops";

    /// <summary>容器 XML 命名空间。</summary>
    public const string ContainerNamespace = "urn:oasis:names:tc:opendocument:xmlns:container";

    /// <summary>NCX 命名空间。</summary>
    public const string NcxNamespace = "http://www.daisy.org/z3986/2005/ncx/";

    /// <summary>Daisy 命名空间（NCX meta 用）。</summary>
    public const string NcxDaisyNamespace = "http://www.daisy.org/z3986/2005/ncx/";
}