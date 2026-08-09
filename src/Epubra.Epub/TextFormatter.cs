using System.Text;
using System.Text.RegularExpressions;

namespace Epubra.Epub;

/// <summary>
/// 一键排版引擎：对文本进行自动化清洗和格式化。
/// 功能：合并断行、清理空白、修正中文标点（半角→全角）、修正引号（直→弯）、
///       段落分割、移除零宽字符。
/// 不负责添加缩进（缩进由 FlowDocument.TextIndent / CSS 处理）。
/// </summary>
public static class TextFormatter
{
    // ===== 配置常量 =====

    /// <summary>半角→全角标点映射（仅在中文上下文中转换）。</summary>
    private static readonly Dictionary<char, char> PunctuationMap = new()
    {
        { ',', '\uFF0C' },  // ，
        { '.', '\u3002' },  // 。
        { '!', '\uFF01' },  // ！
        { '?', '\uFF1F' },  // ？
        { ':', '\uFF1A' },  // ：
        { ';', '\uFF1B' },  // ；
        { '(', '\uFF08' },  // （
        { ')', '\uFF09' },  // ）
        { '[', '\u3010' },  // 【
        { ']', '\u3011' },  // 】
        { '~', '\uFF5E' },  // ～
    };

    /// <summary>句子结束标点（中英文）。</summary>
    private static readonly HashSet<char> SentenceEndChars = new()
    {
        '\u3002',   // 。
        '\uFF01',   // ！
        '\uFF1F',   // ？
        '\u2026',   // …
        '.', '!', '?',
        '\u201D',   // "
        '\u2019',   // '
        '\u300D',   // 』
        '\u300F',   // 』
        '\u3011',   // 】
        '\uFF09',   // ）
    };

    /// <summary>零宽字符集合。</summary>
    private static readonly string[] ZeroWidthChars =
    {
        "\u200B",   // Zero Width Space
        "\u200C",   // Zero Width Non-Joiner
        "\u200D",   // Zero Width Joiner
        "\u200E",   // Left-to-Right Mark
        "\u200F",   // Right-to-Left Mark
        "\uFEFF",   // BOM / Zero Width No-Break Space
    };

    /// <summary>
    /// 格式化整段文本（可能含多个段落）。
    /// 处理流程：规范换行 → 移除零宽字符 → 合并断行 → 分段 → 清洗每段 → 合并输出。
    /// </summary>
    public static string Format(string text)
    {
        if (string.IsNullOrWhiteSpace(text))
            return string.Empty;

        // 1. 规范换行
        text = text.Replace("\r\n", "\n").Replace('\r', '\n');

        // 2. 移除零宽字符
        foreach (var zw in ZeroWidthChars)
            text = text.Replace(zw, string.Empty);

        // 3. 合并断行 + 段落分割
        var paragraphs = MergeAndSplit(text);

        // 4. 清洗每段
        var formatted = new List<string>(paragraphs.Count);
        foreach (var para in paragraphs)
        {
            var cleaned = FormatParagraph(para);
            if (!string.IsNullOrWhiteSpace(cleaned))
                formatted.Add(cleaned);
        }

        return string.Join("\n\n", formatted);
    }

    /// <summary>
    /// 格式化单个段落的文本（不含段落分隔）。
    /// 处理：去除首尾空白 → 移除已有缩进符 → 合并多余空格 → 修正标点 → 修正引号。
    /// 不添加缩进字符（由调用方通过 CSS/TextIndent 处理）。
    /// </summary>
    public static string FormatParagraph(string text)
    {
        if (string.IsNullOrWhiteSpace(text))
            return string.Empty;

        // 移除零宽字符
        foreach (var zw in ZeroWidthChars)
            text = text.Replace(zw, string.Empty);

        // 去除首尾空白
        text = text.Trim();

        // 移除行首全角空格（旧式缩进）
        text = text.TrimStart('\u3000', '\u3001');

        // 合并连续空格/制表符为单个空格
        text = Regex.Replace(text, @"[ \t]+", " ");

        // 合并段内换行（同一段落内的手动换行）
        text = text.Replace("\n", "");

        // 修正省略号（在标点转换之前，避免 ... 被逐个转换为 。）
        text = FixEllipsis(text);

        // 修正破折号
        text = FixDash(text);

        // 修正标点：半角→全角（仅在中文字符之间）
        text = FixPunctuation(text);

        // 修正引号：直引号→弯引号
        text = FixQuotes(text);

        // 移除标点前的多余空格（如 "你好 ，" → "你好，"）
        text = RemoveSpaceBeforePunctuation(text);

        return text.Trim();
    }

    // ===== 内部方法 =====

    /// <summary>
    /// 合并断行并分割段落。
    /// 规则：仅以空行作为段落分隔。连续的非空行视为断行，合并为同一段落。
    /// 这是最可预测的行为——用户如需分段，应在段落间留空行。
    /// </summary>
    private static List<string> MergeAndSplit(string text)
    {
        var lines = text.Split('\n');
        var paragraphs = new List<string>();
        var current = new StringBuilder();

        foreach (var line in lines)
        {
            var trimmed = line.Trim();

            if (string.IsNullOrEmpty(trimmed))
            {
                // 空行 → 段落分隔
                if (current.Length > 0)
                {
                    paragraphs.Add(current.ToString());
                    current.Clear();
                }
                continue;
            }

            if (current.Length > 0)
            {
                // 合并断行：英文单词间加空格，中文直接拼接
                var lastChar = current[current.Length - 1];
                if (IsAsciiAlpha(lastChar) && IsAsciiAlpha(trimmed[0]))
                    current.Append(' ');
                current.Append(trimmed);
            }
            else
            {
                current.Append(trimmed);
            }
        }

        if (current.Length > 0)
            paragraphs.Add(current.ToString());

        return paragraphs;
    }

    /// <summary>
    /// 修正标点：在中文上下文中将半角标点转为全角。
    /// 判断条件：标点前一个或后一个字符是中文字符。
    /// </summary>
    private static string FixPunctuation(string text)
    {
        var sb = new StringBuilder(text.Length);
        for (var i = 0; i < text.Length; i++)
        {
            var c = text[i];

            if (PunctuationMap.TryGetValue(c, out var fullWidth))
            {
                var prevChar = i > 0 ? text[i - 1] : '\0';
                var nextChar = i < text.Length - 1 ? text[i + 1] : '\0';

                // 特殊处理句号：英文缩写（如 Mr. Dr. etc.）不转换
                if (c == '.' && IsAsciiAlpha(prevChar) && IsAsciiAlpha(nextChar))
                {
                    sb.Append(c);
                    continue;
                }

                // 数字之间的小数点不转换（如 3.14）
                if (c == '.' && char.IsDigit(prevChar) && char.IsDigit(nextChar))
                {
                    sb.Append(c);
                    continue;
                }

                if (IsChinese(prevChar) || IsChinese(nextChar))
                    sb.Append(fullWidth);
                else
                    sb.Append(c);
            }
            else
            {
                sb.Append(c);
            }
        }
        return sb.ToString();
    }

    /// <summary>
    /// 修正引号：直双引号 " → 中文弯引号 "/" 。
    /// 交替使用左引号和右引号。
    /// </summary>
    private static string FixQuotes(string text)
    {
        var sb = new StringBuilder(text.Length);
        var expectOpen = true;

        foreach (var c in text)
        {
            if (c == '"')
            {
                sb.Append(expectOpen ? '\u201C' : '\u201D');
                expectOpen = !expectOpen;
            }
            else
            {
                sb.Append(c);
            }
        }
        return sb.ToString();
    }

    /// <summary>
    /// 修正省略号：3 个及以上连续的 . 或 。 统一为 ……
    /// </summary>
    private static string FixEllipsis(string text)
    {
        // 连续 3+ 个 .  → ……
        text = Regex.Replace(text, @"\.{3,}", "\u2026\u2026");
        // 连续 3+ 个 。 → ……
        text = Regex.Replace(text, "\u3002{3,}", "\u2026\u2026");
        // 已经是 … 但数量不是 2 的 → 统一为 2 个
        text = Regex.Replace(text, "\u2026{1,}", "\u2026\u2026");
        return text;
    }

    /// <summary>
    /// 修正破折号：2 个及以上连续的 - 统一为 ——
    /// </summary>
    private static string FixDash(string text)
    {
        text = Regex.Replace(text, @"-{2,}", "\u2014\u2014");
        return text;
    }

    /// <summary>
    /// 移除标点前的多余空格（如 "你好 ，" → "你好，"）。
    /// </summary>
    private static string RemoveSpaceBeforePunctuation(string text)
    {
        return Regex.Replace(text, @"\s+([\uFF0C\u3002\uFF01\uFF1F\uFF1A\uFF1B\uFF08\uFF09\u3010\u3011\uFF5E\u2026\u2014])", "$1");
    }

    /// <summary>判断字符是否为中文字符（CJK 统一汉字区）。</summary>
    private static bool IsChinese(char c) => c >= '\u4e00' && c <= '\u9fff';

    /// <summary>判断字符是否为 ASCII 字母。</summary>
    private static bool IsAsciiAlpha(char c) => (c >= 'a' && c <= 'z') || (c >= 'A' && c <= 'Z');

    /// <summary>判断字符是否为句子结束标点。</summary>
    private static bool IsSentenceEnd(char c) => SentenceEndChars.Contains(c);
}
