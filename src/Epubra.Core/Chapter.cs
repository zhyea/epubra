namespace Epubra.Core;

/// <summary>
/// 表示书中的一个章节。
/// 章节内容以 XHTML 字符串存储，便于直接打包进 epub（OEBPS）。
/// </summary>
public sealed class Chapter
{
    /// <summary>唯一标识。</summary>
    public Guid Id { get; init; } = Guid.NewGuid();

    /// <summary>章节标题（用户可编辑）。</summary>
    public string Title { get; set; } = string.Empty;

    /// <summary>
    /// 章节 XHTML 内容（含 &lt;html&gt;&lt;body&gt;...&lt;/body&gt;&lt;/html&gt; 完整文档）。
    /// 由 Epubra.Editor 的 FlowDocument → XHTML 转换器生成。
    /// </summary>
    public string XhtmlContent { get; set; } = string.Empty;

    /// <summary>父章节 ID，null 表示顶级章节。</summary>
    public Guid? ParentId { get; set; }

    /// <summary>排序序号（同父章节下从 0 开始）。</summary>
    public int Order { get; set; }

    /// <summary>
    /// 在最终 epub 中使用的文件名（不含扩展名），例如 <c>chapter_001</c>。
    /// </summary>
    public string FileName { get; set; } = string.Empty;

    /// <summary>用户自定义片段标记，便于导航（如"卷一"、"前言"）。</summary>
    public string? SectionLabel { get; set; }
}