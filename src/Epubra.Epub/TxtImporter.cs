using System.IO;
using System.Text;
using Epubra.Core;

namespace Epubra.Epub;

/// <summary>
/// 从纯文本 TXT 文件导入为 <see cref="Book"/> 模型。
/// 自动检测编码（UTF-8/GBK/GB18030），按章节标记拆分并生成 XHTML。
/// </summary>
public sealed class TxtImporter
{
    private static readonly ChapterSplitter Splitter = new();

    /// <summary>从文件读取并导入。</summary>
    public Book Read(string filePath)
    {
        ArgumentException.ThrowIfNullOrEmpty(filePath);
        var raw = File.ReadAllBytes(filePath);
        var text = DetectEncodingAndRead(raw);
        return ParseText(text, Path.GetFileNameWithoutExtension(filePath));
    }

    /// <summary>从流读取并导入（由调用方管理流生命周期）。</summary>
    public Book ReadFromStream(Stream stream)
    {
        using var ms = new MemoryStream();
        stream.CopyTo(ms);
        var raw = ms.ToArray();
        var text = DetectEncodingAndRead(raw);
        return ParseText(text, "导入书籍");
    }

    // ===== 内部方法 =====

    /// <summary>编码检测 + 读取文本。</summary>
    private static string DetectEncodingAndRead(byte[] raw)
    {
        // 1) UTF-8 BOM
        if (raw.Length >= 3 && raw[0] == 0xEF && raw[1] == 0xBB && raw[2] == 0xBF)
        {
            return Encoding.UTF8.GetString(raw, 3, raw.Length - 3);
        }

        // 2) UTF-16 LE BOM
        if (raw.Length >= 2 && raw[0] == 0xFF && raw[1] == 0xFE)
        {
            return Encoding.Unicode.GetString(raw, 2, raw.Length - 2);
        }

        // 3) UTF-16 BE BOM
        if (raw.Length >= 2 && raw[0] == 0xFE && raw[1] == 0xFF)
        {
            return Encoding.BigEndianUnicode.GetString(raw, 2, raw.Length - 2);
        }

        // 4) 尝试 UTF-8（无 BOM）
        try
        {
            var utf8 = Encoding.UTF8.GetString(raw);
            // 验证：重新编码后字节一致说明是合法 UTF-8
            var reEncoded = Encoding.UTF8.GetBytes(utf8);
            if (reEncoded.AsSpan().SequenceEqual(raw))
            {
                return utf8;
            }
        }
        catch
        {
            // 不是 UTF-8
        }

        // 5) 尝试 GB18030（覆盖 GBK/GB2312）
        try
        {
            Encoding.RegisterProvider(CodePagesEncodingProvider.Instance);
            var gb18030 = Encoding.GetEncoding("GB18030");
            return gb18030.GetString(raw);
        }
        catch
        {
            // 回退到系统默认
        }

        return Encoding.Default.GetString(raw);
    }

    /// <summary>将纯文本解析为 Book。</summary>
    private static Book ParseText(string text, string defaultTitle)
    {
        text = NormalizeText(text);

        var (title, author, bodyText) = ExtractMetadata(text, defaultTitle);
        var segments = Splitter.Split(bodyText);

        var book = new Book
        {
            Title = title,
            Author = author,
            Language = "zh-CN"
        };

        for (var i = 0; i < segments.Count; i++)
        {
            var seg = segments[i];
            var chapter = new Chapter
            {
                Title = seg.Title,
                FileName = $"chapter_{i + 1:D3}",
                Order = i,
                ParentId = null,
                XhtmlContent = ChapterSplitter.WrapAsXhtml(seg.Title, seg.Content)
            };
            book.Chapters.Add(chapter);
        }

        return book;
    }

    /// <summary>文本规范化：统一换行为 \n，移除 BOM 头部杂质。</summary>
    private static string NormalizeText(string text)
    {
        return text.Replace("\r\n", "\n").Replace('\r', '\n').TrimStart('\uFEFF');
    }

    /// <summary>从文本头部提取书名和作者。返回 (书名, 作者, 正文文本)。</summary>
    private static (string title, string author, string bodyText) ExtractMetadata(
        string text, string defaultTitle)
    {
        var lines = text.Split('\n');
        var title = defaultTitle;
        var author = string.Empty;
        var bodyStartLine = 0;

        for (var i = 0; i < Math.Min(lines.Length, 10); i++)
        {
            var line = lines[i].Trim();
            if (string.IsNullOrEmpty(line))
            {
                bodyStartLine = i + 1;
                continue;
            }

            // 跳过章节标记行（前言/第一章等是正文内容）
            if (ChapterSplitter.IsChapterMarkerLine(line))
            {
                break;
            }

            // 第一行非空 → 书名
            if (title == defaultTitle && line.Length <= 30)
            {
                title = line;
                bodyStartLine = i + 1;
                continue;
            }

            // 第二行 → 可能是作者
            if (author == string.Empty && line.StartsWith("作者", StringComparison.Ordinal))
            {
                author = line["作者：".Length..].Trim();
                bodyStartLine = i + 1;
                continue;
            }

            // 作者行（英文）
            if (author == string.Empty && line.StartsWith("Author:", StringComparison.OrdinalIgnoreCase))
            {
                author = line[7..].Trim();
                bodyStartLine = i + 1;
                continue;
            }

            break;
        }

        bodyStartLine = Math.Max(bodyStartLine, 0);
        var bodyText = bodyStartLine < lines.Length
            ? string.Join("\n", lines.Skip(bodyStartLine))
            : string.Empty;
        return (title, author, bodyText);
    }
}
