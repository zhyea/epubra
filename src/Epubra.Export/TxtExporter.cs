using System.IO;
using System.Text;
using System.Text.RegularExpressions;
using Epubra.Core;

namespace Epubra.Export;

/// <summary>
/// 把 <see cref="Book"/> 导出为纯文本 TXT 文件。
/// 每个章节以标题行 + 分隔线开始，内容剥离所有 HTML 标签。
/// </summary>
public sealed class TxtExporter
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

        // 书名和作者
        if (!string.IsNullOrWhiteSpace(book.Title))
        {
            sb.AppendLine(book.Title);
            sb.AppendLine(new string('=', 40));
        }

        if (!string.IsNullOrWhiteSpace(book.Author))
        {
            sb.AppendLine($"作者：{book.Author}");
        }

        sb.AppendLine();

        // 按文档顺序输出章节
        var chapters = ChapterTreeWalker.WalkInDocumentOrder(book.Chapters).ToList();
        foreach (var chapter in chapters)
        {
            sb.AppendLine(chapter.Title);
            sb.AppendLine(new string('-', 30));

            var text = HtmlToPlainText(chapter.XhtmlContent);
            sb.AppendLine(text);
            sb.AppendLine();
            sb.AppendLine();
        }

        await using var fs = new FileStream(filePath, FileMode.Create, FileAccess.Write, FileShare.None, 8192, useAsync: true);
        var bytes = new UTF8Encoding(encoderShouldEmitUTF8Identifier: true).GetBytes(sb.ToString());
        await fs.WriteAsync(bytes, ct).ConfigureAwait(false);
    }

    /// <summary>把 HTML/XHTML 内容转为纯文本：剥离标签、解码实体、保留换行。</summary>
    private static string HtmlToPlainText(string html)
    {
        if (string.IsNullOrWhiteSpace(html))
            return string.Empty;

        // 移除 head/script/style 块
        var text = Regex.Replace(html, @"<(head|script|style)[^>]*>.*?</\1>", "", RegexOptions.IgnoreCase | RegexOptions.Singleline);

        // 块级元素后加换行
        text = Regex.Replace(text, @"</(p|div|h[1-6]|li|tr|blockquote)>", "\n", RegexOptions.IgnoreCase);
        text = Regex.Replace(text, @"<(br|hr)[^>]*/?>", "\n", RegexOptions.IgnoreCase);

        // 剥离所有标签
        text = Regex.Replace(text, @"<[^>]+>", "");

        // 解码 HTML 实体
        text = System.Net.WebUtility.HtmlDecode(text);

        // 清理多余空行
        text = Regex.Replace(text, @"\n{3,}", "\n\n");

        return text.Trim();
    }
}
