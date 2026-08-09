namespace Epubra.Core;

/// <summary>
/// 表示一本书（epub 项目）。
/// 这是 Epubra 的核心领域模型，所有编辑器/导出器围绕它工作。
/// </summary>
public sealed class Book
{
    /// <summary>唯一标识（UUID v4）。</summary>
    public Guid Id { get; init; } = Guid.NewGuid();

    /// <summary>书名（必填）。</summary>
    public string Title { get; set; } = string.Empty;

    /// <summary>作者（多个以 ; 分隔）。</summary>
    public string Author { get; set; } = string.Empty;

    /// <summary>语言 BCP-47 标签，默认 zh-CN。</summary>
    public string Language { get; set; } = "zh-CN";

    /// <summary>出版者。</summary>
    public string? Publisher { get; set; }

    /// <summary>简介/描述。</summary>
    public string? Description { get; set; }

    /// <summary>类型/分类（Subject），多个以分号分隔。</summary>
    public string? Subject { get; set; }

    /// <summary>ISBN / 书籍 ID。</summary>
    public string? Identifier { get; set; }

    /// <summary>封面图片资源 ID（引用 <see cref="Resources"/>）。</summary>
    public Guid? CoverResourceId { get; set; }

    /// <summary>章节列表（有序，扁平存储，子章节通过 <see cref="Chapter.ParentId"/> 关联）。</summary>
    public List<Chapter> Chapters { get; set; } = new();

    /// <summary>资源池（图片、字体、样式表等）。</summary>
    public List<EpubResource> Resources { get; set; } = new();

    /// <summary>项目文件路径（epubra 自身格式，非最终 epub 文件）。</summary>
    public string? ProjectPath { get; set; }

    /// <summary>创建时间。</summary>
    public DateTimeOffset CreatedAt { get; init; } = DateTimeOffset.UtcNow;

    /// <summary>最后修改时间。</summary>
    public DateTimeOffset ModifiedAt { get; set; } = DateTimeOffset.UtcNow;
}