namespace Epubra.Core;

/// <summary>
/// 资源类型：图片、音频、字体、CSS、其它。
/// </summary>
public enum EpubResourceKind
{
    Image,
    Audio,
    Video,
    Font,
    StyleSheet,
    Other
}

/// <summary>
/// 表示一本书中嵌入的资源（图片、字体、CSS 等）。
/// 资源在打包时会被放入 OEBPS 下的对应目录，章节 XHTML 通过相对路径引用。
/// </summary>
public sealed class EpubResource
{
    /// <summary>唯一标识。</summary>
    public Guid Id { get; init; } = Guid.NewGuid();

    /// <summary>资源类型。</summary>
    public EpubResourceKind Kind { get; set; }

    /// <summary>原始文件名（含扩展名，例如 <c>cover.jpg</c>、<c>font.ttf</c>）。</summary>
    public string FileName { get; set; } = string.Empty;

    /// <summary>
    /// 在 OEBPS 中的相对路径（含子目录和扩展名），例如 <c>images/cover.jpg</c>、<c>fonts/SourceHanSerifCN-Regular.otf</c>。
    /// 由 <see cref="Epubra.Epub.EpubWriter"/> 在打包时分配或验证。
    /// </summary>
    public string OebpsPath { get; set; } = string.Empty;

    /// <summary>资源的 MIME 类型，例如 <c>image/jpeg</c>、<c>font/ttf</c>、<c>text/css</c>。</summary>
    public string MimeType { get; set; } = string.Empty;

    /// <summary>原始字节内容。</summary>
    public byte[] Data { get; set; } = Array.Empty<byte>();

    /// <summary>用途标记（可选），例如 <c>cover</c> / <c>thumbnail</c> / <c>font:serif</c>。</summary>
    public string? Role { get; set; }
}