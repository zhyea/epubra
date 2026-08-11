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
    /// 章节切分规则配置。多个规则可同时启用，按切分点位置合并去重。
    /// </summary>
    /// <param name="SmartEnabled">智能规则：按章节标记模式（中文章节/特殊/英文）切分。</param>
    /// <param name="MaxTitleLength">智能规则标题长度阈值：只把整行文本长度 ≤ 该值的视为章节标记。</param>
    /// <param name="EqualLengthEnabled">等长规则：按字节数强制切分。</param>
    /// <param name="EqualByteLength">等长字节数阈值（UTF-8 编码）。</param>
    /// <param name="FeatureEnabled">特征规则：按用户提供的字符串/正则切分。</param>
    /// <param name="FeaturePattern">特征字符串或正则。合法正则按正则匹配，否则按字面量包含匹配。</param>
    public sealed record SplitOptions(
        bool SmartEnabled = true,
        int MaxTitleLength = 40,
        bool EqualLengthEnabled = false,
        int EqualByteLength = 500_000,
        bool FeatureEnabled = false,
        string? FeaturePattern = null);

    /// <summary>
    /// 从纯文本中识别章节标记并切分。
    /// 返回按出现顺序排列的章节段列表。如果未识别到任何标记，返回包含整个文本的单个段。
    /// </summary>
    public List<ChapterSegment> Split(string text)
    {
        return Split(text, new SplitOptions());
    }

    /// <summary>
    /// 按指定规则配置切分章节。多个规则并行收集切分点，去重排序后切分。
    /// </summary>
    public List<ChapterSegment> Split(string text, SplitOptions options)
    {
        if (string.IsNullOrWhiteSpace(text))
        {
            return new List<ChapterSegment> { new("正文", string.Empty) };
        }

        // 规范化换行
        text = text.Replace("\r\n", "\n").Replace('\r', '\n');

        // 收集所有启用的规则产生的切分点
        var breakPoints = new SortedSet<int>();
        if (options.SmartEnabled)
        {
            foreach (var idx in CollectSmartMarkerIndices(text, options.MaxTitleLength))
            {
                breakPoints.Add(AdjustToLineStart(text, idx));
            }
        }
        if (options.FeatureEnabled && !string.IsNullOrWhiteSpace(options.FeaturePattern))
        {
            foreach (var idx in CollectFeatureIndices(text, options.FeaturePattern!))
            {
                breakPoints.Add(AdjustToLineStart(text, idx));
            }
        }
        if (options.EqualLengthEnabled && options.EqualByteLength > 0)
        {
            foreach (var idx in CollectEqualLengthIndices(text, options.EqualByteLength))
            {
                breakPoints.Add(idx);
            }
        }

        // 没有切分点 → 兜底：按段落拆分
        if (breakPoints.Count == 0)
        {
            return SplitByParagraphs(text);
        }

        return SplitAtBreakPoints(text, breakPoints);
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

    /// <summary>XML 实体转义。</summary>
    private static string EscapeXml(string text)
    {
        return System.Net.WebUtility.HtmlEncode(text);
    }

    /// <summary>把任意字符位置调整到所在行的开头（章节切分应按行进行）。</summary>
    private static int AdjustToLineStart(string text, int pos)
    {
        if (pos <= 0) return 0;
        var safePos = Math.Min(pos, text.Length);
        var lineStart = text.LastIndexOf('\n', safePos - 1);
        return lineStart < 0 ? 0 : lineStart + 1;
    }

    /// <summary>返回 pos 所在行的字符长度（含标题原始长度）。</summary>
    private static int LineLengthAt(string text, int pos)
    {
        if (text.Length == 0) return 0;
        int lineStart;
        if (pos <= 0)
        {
            lineStart = 0;
        }
        else
        {
            var safePos = Math.Min(pos, text.Length);
            var ls = text.LastIndexOf('\n', safePos - 1);
            lineStart = ls < 0 ? 0 : ls + 1;
        }
        var lineEnd = text.IndexOf('\n', lineStart);
        if (lineEnd < 0) lineEnd = text.Length;
        return lineEnd - lineStart;
    }

    /// <summary>智能规则：收集章节标记行的起始位置，受 MaxTitleLength 阈值约束。</summary>
    private static IEnumerable<int> CollectSmartMarkerIndices(string text, int maxTitleLength)
    {
        foreach (Match m in ChineseChapterPattern.Matches(text))
        {
            if (LineLengthAt(text, m.Index) <= maxTitleLength) yield return m.Index;
        }
        foreach (Match m in SpecialChapterPattern.Matches(text))
        {
            if (LineLengthAt(text, m.Index) <= maxTitleLength) yield return m.Index;
        }
        foreach (Match m in EnglishChapterPattern.Matches(text))
        {
            if (LineLengthAt(text, m.Index) <= maxTitleLength) yield return m.Index;
        }
    }

    /// <summary>特征规则：按用户字符串或正则收集切分点。合法正则按正则，否则按字面量包含。</summary>
    private static IEnumerable<int> CollectFeatureIndices(string text, string pattern)
    {
        Regex? regex = null;
        try
        {
            regex = new Regex(pattern, RegexOptions.Multiline);
        }
        catch (ArgumentException)
        {
            // 非合法正则，按字面量包含匹配
        }

        if (regex != null)
        {
            foreach (Match m in regex.Matches(text))
                yield return m.Index;
            yield break;
        }

        var idx = 0;
        while (idx < text.Length)
        {
            var found = text.IndexOf(pattern, idx, StringComparison.Ordinal);
            if (found < 0) yield break;
            yield return found;
            idx = found + Math.Max(1, pattern.Length);
        }
    }

    /// <summary>等长规则：按 UTF-8 字节数累积，超过阈值则在该位置切分。</summary>
    private static IEnumerable<int> CollectEqualLengthIndices(string text, int byteLength)
    {
        var encoding = Encoding.UTF8;
        var accumulated = 0;
        for (var i = 0; i < text.Length; i++)
        {
            var chBytes = encoding.GetByteCount(new[] { text[i] });
            if (accumulated + chBytes > byteLength && i > 0)
            {
                yield return i;
                accumulated = 0;
            }
            accumulated += chBytes;
        }
    }

    /// <summary>按切分点列表切分文本，每个切分点所在行作为新章节标题。</summary>
    private static List<ChapterSegment> SplitAtBreakPoints(string text, SortedSet<int> breakPoints)
    {
        var result = new List<ChapterSegment>();
        var sorted = breakPoints.ToList();

        for (var i = 0; i < sorted.Count; i++)
        {
            var start = sorted[i];
            var end = i + 1 < sorted.Count ? sorted[i + 1] : text.Length;

            // 标题：切分点所在行的章节标记部分（若是），否则整行
            var title = ExtractLineTitle(text, start, out int markerEnd);
            if (string.IsNullOrWhiteSpace(title))
                title = $"章节 {i + 1}";

            // 内容：从标记结束位置开始（含标题行标记后的同行文字）；
            //       若该行非标记行，则从整行结束之后开始
            var lineEnd = text.IndexOf('\n', start);
            var contentStart = markerEnd >= 0 ? markerEnd + 1 : (lineEnd >= 0 ? lineEnd + 1 : start);
            var content = contentStart < end ? text[contentStart..end].Trim() : string.Empty;

            result.Add(new ChapterSegment(title, content));
        }

        return result;
    }

    /// <summary>
    /// 提取 pos 所在行作为章节标题（去前后空白）。
    /// 若该行为章节标记行，只返回匹配的模式部分（如「第一章」）；否则返回整行。
    /// markerEnd 返回标记在文本中的结束位置（绝对索引）；无标记时为 -1。
    /// </summary>
    private static string ExtractLineTitle(string text, int pos, out int markerEnd)
    {
        if (text.Length == 0) { markerEnd = -1; return string.Empty; }
        int lineStart;
        if (pos <= 0)
        {
            lineStart = 0;
        }
        else
        {
            var safePos = Math.Min(pos, text.Length);
            var ls = text.LastIndexOf('\n', safePos - 1);
            lineStart = ls < 0 ? 0 : ls + 1;
        }
        var lineEnd = text.IndexOf('\n', lineStart);
        if (lineEnd < 0) lineEnd = text.Length;
        var line = text[lineStart..lineEnd];

        // 若该行为章节标记行，只取匹配的模式部分作为标题
        var m = ChineseChapterPattern.Match(line);
        if (!m.Success) m = SpecialChapterPattern.Match(line);
        if (!m.Success) m = EnglishChapterPattern.Match(line);
        if (m.Success)
        {
            markerEnd = lineStart + m.Index + m.Length;
            return m.Value;
        }

        markerEnd = -1;
        return line.Trim();
    }

    /// <summary>无切分点时按段落拆分（兜底）。</summary>
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
}

/// <summary>切分后的章节段：标题 + 纯文本内容。</summary>
public sealed record ChapterSegment(string Title, string Content);