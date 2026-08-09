using System.IO;
using System.Text;
using System.Text.RegularExpressions;
using Epubra.Core;

namespace Epubra.Export;

/// <summary>
/// 把 <see cref="Book"/> 导出为单个 HTML 文件。
/// 包含目录导航 + 各章节内容 + 基本 CSS 样式。
/// </summary>
public sealed class HtmlExporter
{
    public async Task ExportAsync(Book book, string filePath, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(book);
        ArgumentException.ThrowIfNullOrEmpty(filePath);

        var dir = Path.GetDirectoryName(filePath);
        if (!string.IsNullOrEmpty(dir) && !Directory.Exists(dir))
        {
            Directory.CreateDirectory(dir);
        }

        var sb = new StringBuilder();

        sb.Append("<!DOCTYPE html>\n");
        sb.Append("<html lang=\"").Append(book.Language).Append("\">\n<head>\n");
        sb.Append("<meta charset=\"utf-8\"/>\n");
        sb.Append("<meta name=\"viewport\" content=\"width=device-width, initial-scale=1\"/>\n");
        sb.Append("<title>").Append(EscapeHtml(book.Title)).Append("</title>\n");

        // CSS
        sb.Append("<style>\n");
        sb.Append("body { font-family: 'Songti SC', 'SimSun', serif; font-size: 16px; line-height: 1.8; margin: 40px auto; max-width: 800px; color: #333; }\n");
        sb.Append("h1.book-title { font-size: 2em; text-align: center; margin-bottom: 0.2em; }\n");
        sb.Append(".book-author { text-align: center; color: #666; margin-bottom: 2em; }\n");
        sb.Append(".toc { background: #f9f9f9; border: 1px solid #e0e0e0; padding: 16px 24px; margin: 2em 0; border-radius: 8px; }\n");
        sb.Append(".toc h2 { margin-top: 0; }\n");
        sb.Append(".toc ul { list-style: none; padding-left: 1em; }\n");
        sb.Append(".toc a { color: #0066cc; text-decoration: none; }\n");
        sb.Append(".toc a:hover { text-decoration: underline; }\n");
        sb.Append("h2.chapter-title { border-bottom: 1px solid #e0e0e0; padding-bottom: 0.3em; margin-top: 2em; }\n");
        sb.Append("img { max-width: 100%; }\n");
        sb.Append("p { text-indent: 2em; margin: 0.5em 0; }\n");
        sb.Append("</style>\n");
        sb.Append("</head>\n<body>\n");

        // 书名和作者
        sb.Append("<h1 class=\"book-title\">").Append(EscapeHtml(book.Title)).Append("</h1>\n");
        if (!string.IsNullOrWhiteSpace(book.Author))
        {
            sb.Append("<p class=\"book-author\">").Append(EscapeHtml(book.Author)).Append("</p>\n");
        }

        // 目录
        var chapters = ChapterTreeWalker.WalkInDocumentOrder(book.Chapters).ToList();
        var idx = 0;
        if (chapters.Count > 0)
        {
            sb.Append("<nav class=\"toc\">\n<h2>目录</h2>\n<ul>\n");
            idx = 0;
            foreach (var chapter in chapters)
            {
                sb.Append($"  <li><a href=\"#ch_{idx}\">").Append(EscapeHtml(chapter.Title)).Append("</a></li>\n");
                idx++;
            }
            sb.Append("</ul>\n</nav>\n");
        }

        // 各章节内容
        idx = 0;
        foreach (var chapter in chapters)
        {
            sb.Append($"<section id=\"ch_{idx}\">\n");
            sb.Append("<h2 class=\"chapter-title\">").Append(EscapeHtml(chapter.Title)).Append("</h2>\n");
            sb.Append(ExtractBodyContent(chapter.XhtmlContent));
            sb.Append("\n</section>\n\n");
            idx++;
        }

        sb.Append("</body>\n</html>\n");

        await using var fs = new FileStream(filePath, FileMode.Create, FileAccess.Write, FileShare.None, 8192, useAsync: true);
        var bytes = new UTF8Encoding(encoderShouldEmitUTF8Identifier: false).GetBytes(sb.ToString());
        await fs.WriteAsync(bytes, ct).ConfigureAwait(false);
    }

    /// <summary>从 XHTML 中提取 body 内容（去掉 html/head/body 标签）。</summary>
    private static string ExtractBodyContent(string xhtml)
    {
        if (string.IsNullOrWhiteSpace(xhtml))
            return "<p></p>";

        // 尝试提取 <body>...</body>
        var match = Regex.Match(xhtml, @"<body[^>]*>(.*?)</body>", RegexOptions.IgnoreCase | RegexOptions.Singleline);
        if (match.Success)
        {
            return match.Groups[1].Value.Trim();
        }

        // 如果不是完整 HTML 文档，直接返回内容
        return xhtml;
    }

    private static string EscapeHtml(string text)
    {
        if (string.IsNullOrEmpty(text)) return string.Empty;
        return System.Net.WebUtility.HtmlEncode(text);
    }
}
