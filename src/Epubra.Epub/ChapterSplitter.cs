using System.Text;
using System.Text.RegularExpressions;

namespace Epubra.Epub;

/// <summary>
/// 章节自动识别与切分器。
/// 从纯文本或 XHTML 内容中识别章节标记（中英文），自动拆分为多个章节段。
/// 识别规则：中文章节标记（第X章/卷/回/节）、特殊章节（前言/序言/楔子等）、英文章节（Chapter/Part/Section N）。
/// </summary>
public sealed class ChapterSplitter
{
    // 中文章节标记模式：第X章、第X卷、第X回、第X节 等
    // 必须出现在行首（允许前导空白），且标记后必须是空白/标点/行尾（避免误匹配正文）
    private static readonly Regex ChineseChapterPattern = new(
        @"^[ \t]*第[\u4e00-\u9fa5\d零一二三四五六七八九十百千万]+[章节卷回部篇集](?=[\s\u3000：:、，,.。！？]|$)",
        RegexOptions.Compiled | RegexOptions.Multiline);

    // 特殊章节：前言、序言、楔子、引子、尾声、后记、附录、番外、序章、终章
    // 必须独占一行（允许前后空白）
    private static readonly Regex SpecialChapterPattern = new(
        @"^[\s\u3000]*(前言|楔子|引子|序言|自序|代序|原序|译者序|尾声|后记|附录|番外|序章|终章)[\s\u3000]*$",
        RegexOptions.Compiled | RegexOptions.Multiline);

    // 英文章节标记：Chapter 1 / Part II / Section 3 等
    // 标记后必须是空白/标点/行尾（避免误匹配正文）
    private static readonly Regex EnglishChapterPattern = new(
        @"^(?:Chapter|Part|Section)\s+\d+(?=[\s：:,.]|$)",
        RegexOptions.Compiled | RegexOptions.Multiline | RegexOptions.IgnoreCase);

    /// <summary>
    /// 从纯文本中识别章节标记并切分。
    /// 返回按出现顺序排列的章节段列表。如果未识别到任何标记，返回包含整个文本的单个段。
    /// </summary>
    public List<ChapterSegment> Split(string text)
    {
        if (string.IsNullOrWhiteSpace(text))
        {
            return new List<ChapterSegment> { new("正文", string.Empty) };
        }

        // 规范化换行
        text = text.Replace("\r\n", "\n").Replace('\r', '\n');

        // 收集所有章节标记的位置
        var markers = CollectMarkers(text);

        // 没有标记 → 尝试按双换行拆分
        if (markers.Count == 0)
        {
            return SplitByParagraphs(text);
        }

        // 只有一个标记 → 前面内容作为引言
        if (markers.Count == 1)
        {
            return SplitSingleMarker(text, markers[0]);
        }

        // 多个标记 → 按标记切分
        return SplitMultipleMarkers(text, markers);
    }

    /// <summary>
    /// 判断一行文本是否匹配章节标记模式（中文/特殊/英文）。
    /// 用于头部元数据提取时判断是否已进入正文。
    /// </summary>
    public static bool IsChapterMarkerLine(string line)
    {
        if (string.IsNullOrWhiteSpace(line)) return false;
        return ChineseChapterPattern.IsMatch(line)
            || SpecialChapterPattern.IsMatch(line)
            || EnglishChapterPattern.IsMatch(line);
    }

    /// <summary>
    /// 从 XHTML 内容中提取纯文本，然后识别章节标记并切分。
    /// </summary>
    public List<ChapterSegment> SplitFromXhtml(string xhtml)
    {
        var text = ExtractPlainText(xhtml);
        return Split(text);
    }

    /// <summary>
    /// 从 HTML/XHTML 内容中提取纯文本：剥离标签、解码实体、保留换行。
    /// </summary>
    public static string ExtractPlainText(string html)
    {
        if (string.IsNullOrWhiteSpace(html))
            return string.Empty;

        // 移除 head/script/style 块
        var text = Regex.Replace(html, @"<(head|script|style)[^>]*>.*?</\1>", "",
            RegexOptions.IgnoreCase | RegexOptions.Singleline);

        // 块级元素后加换行
        text = Regex.Replace(text, @"</(p|div|h[1-6]|li|tr|blockquote)>", "\n",
            RegexOptions.IgnoreCase);
        text = Regex.Replace(text, @"<(br|hr)[^>]*/?>", "\n", RegexOptions.IgnoreCase);

        // 剥离所有标签
        text = Regex.Replace(text, @"<[^>]+>", "");

        // 解码 HTML 实体
        text = System.Net.WebUtility.HtmlDecode(text);

        // 清理多余空行
        text = Regex.Replace(text, @"\n{3,}", "\n\n");

        return text.Trim();
    }

    /// <summary>
    /// 将纯文本章节内容包裹为完整的 XHTML 文档。
    /// 按双换行分段落，单换行加 &lt;br/&gt;。
    /// </summary>
    public static string WrapAsXhtml(string title, string content)
    {
        var sb = new StringBuilder();
        sb.AppendLine("<?xml version=\"1.0\" encoding=\"UTF-8\"?>");
        sb.AppendLine("<!DOCTYPE html>");
        sb.AppendLine("<html xmlns=\"http://www.w3.org/1999/xhtml\" xml:lang=\"zh-CN\">");
        sb.AppendLine("<head>");
        sb.Append("<title>").Append(EscapeXml(title)).AppendLine("</title>");
        sb.AppendLine("</head>");
        sb.AppendLine("<body>");
        sb.Append("<h1>").Append(EscapeXml(title)).AppendLine("</h1>");

        if (!string.IsNullOrWhiteSpace(content))
        {
            // 段落化：按双换行分段落
            var paragraphs = content.Split(new[] { "\n\n" }, StringSplitOptions.None);
            foreach (var para in paragraphs)
            {
                if (string.IsNullOrWhiteSpace(para)) continue;

                var lines = para.Split('\n');
                sb.Append("<p>");
                for (var i = 0; i < lines.Length; i++)
                {
                    var line = lines[i].Trim();
                    if (string.IsNullOrEmpty(line)) continue;

                    sb.Append(EscapeXml(line));
                    if (i < lines.Length - 1)
                    {
                        sb.Append("<br/>");
                    }
                }
                sb.AppendLine("</p>");
            }
        }

        sb.AppendLine("</body>");
        sb.AppendLine("</html>");
        return sb.ToString();
    }

    // ===== 内部方法 =====

    /// <summary>收集所有章节标记（中文+特殊+英文），按位置排序。</summary>
    private List<Marker> CollectMarkers(string text)
    {
        var markers = new List<Marker>();

        foreach (Match m in ChineseChapterPattern.Matches(text))
        {
            markers.Add(new Marker(m.Index, m.Value.Trim(), m.Length));
        }

        foreach (Match m in SpecialChapterPattern.Matches(text))
        {
            markers.Add(new Marker(m.Index, m.Value.Trim(), m.Length));
        }

        foreach (Match m in EnglishChapterPattern.Matches(text))
        {
            markers.Add(new Marker(m.Index, m.Value.Trim(), m.Length));
        }

        // 按位置排序
        markers.Sort((a, b) => a.Index.CompareTo(b.Index));
        return markers;
    }

    /// <summary>无章节标记时，尝试按双换行拆分。无法拆分则整体作为一个段。</summary>
    private static List<ChapterSegment> SplitByParagraphs(string text)
    {
        var paragraphs = text.Split(new[] { "\n\n" }, StringSplitOptions.RemoveEmptyEntries);
        if (paragraphs.Length > 1)
        {
            return paragraphs
                .Select((p, i) => new ChapterSegment($"章节 {i + 1}", p.Trim()))
                .ToList();
        }

        return new List<ChapterSegment> { new("正文", text.Trim()) };
    }

    /// <summary>只有一个章节标记时：标记前的内容作为引言，标记后的内容作为该章节正文。</summary>
    private static List<ChapterSegment> SplitSingleMarker(string text, Marker marker)
    {
        var title = marker.Title;
        var contentStart = marker.Index + marker.MatchLength;
        var content = contentStart < text.Length
            ? text[contentStart..].Trim()
            : string.Empty;

        var preContent = text[..marker.Index].Trim();
        if (!string.IsNullOrWhiteSpace(preContent))
        {
            return new List<ChapterSegment>
            {
                new("引言", preContent),
                new(title, content)
            };
        }

        return new List<ChapterSegment> { new(title, content) };
    }

    /// <summary>多个章节标记时：按标记位置切分，每个标记到下一个标记之间的文本为该章节内容。</summary>
    private static List<ChapterSegment> SplitMultipleMarkers(string text, List<Marker> markers)
    {
        var result = new List<ChapterSegment>();

        for (var i = 0; i < markers.Count; i++)
        {
            var marker = markers[i];
            var contentStart = marker.Index + marker.MatchLength + 1; // +1 for newline after title
            if (contentStart >= text.Length) contentStart = text.Length;

            var end = i + 1 < markers.Count ? markers[i + 1].Index : text.Length;
            var content = contentStart < end
                ? text[contentStart..end].Trim()
                : string.Empty;

            result.Add(new ChapterSegment(marker.Title, content));
        }

        return result;
    }

    /// <summary>XML 实体转义。</summary>
    private static string EscapeXml(string text)
    {
        return System.Net.WebUtility.HtmlEncode(text);
    }

    /// <summary>章节标记位置信息。</summary>
    private readonly record struct Marker(int Index, string Title, int MatchLength);
}

/// <summary>切分后的章节段：标题 + 纯文本内容。</summary>
public sealed record ChapterSegment(string Title, string Content);
