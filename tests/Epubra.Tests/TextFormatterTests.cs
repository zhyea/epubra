using Epubra.Epub;
using Xunit;

namespace Epubra.Tests;

/// <summary>
/// TextFormatter 一键排版引擎单元测试。
/// </summary>
public class TextFormatterTests
{
    // ===== FormatParagraph 基础测试 =====

    [Fact]
    public void FormatParagraph_Empty_ReturnsEmpty()
    {
        Assert.Equal(string.Empty, TextFormatter.FormatParagraph(""));
        Assert.Equal(string.Empty, TextFormatter.FormatParagraph("   "));
        Assert.Equal(string.Empty, TextFormatter.FormatParagraph(null!));
    }

    [Fact]
    public void FormatParagraph_TrimsWhitespace()
    {
        var result = TextFormatter.FormatParagraph("  hello world  ");
        Assert.Equal("hello world", result);
    }

    [Fact]
    public void FormatParagraph_RemovesLeadingFullWidthSpaces()
    {
        var result = TextFormatter.FormatParagraph("\u3000\u3000你好世界");
        Assert.Equal("你好世界", result);
    }

    [Fact]
    public void FormatParagraph_CollapsesMultipleSpaces()
    {
        var result = TextFormatter.FormatParagraph("hello    world");
        Assert.Equal("hello world", result);
    }

    [Fact]
    public void FormatParagraph_RemovesInternalNewlines()
    {
        var result = TextFormatter.FormatParagraph("第一行\n第二行");
        Assert.Equal("第一行第二行", result);
    }

    // ===== 标点修正测试 =====

    [Fact]
    public void FormatParagraph_ConvertsHalfWidthCommaBetweenChinese()
    {
        var result = TextFormatter.FormatParagraph("你好,世界");
        Assert.Equal("你好\uFF0C世界", result);  // 你好，世界
    }

    [Fact]
    public void FormatParagraph_ConvertsHalfWidthPeriodBetweenChinese()
    {
        var result = TextFormatter.FormatParagraph("你好.世界");
        Assert.Equal("你好\u3002世界", result);  // 你好。世界
    }

    [Fact]
    public void FormatParagraph_ConvertsHalfWidthQuestionMarkBetweenChinese()
    {
        var result = TextFormatter.FormatParagraph("你好吗?世界");
        Assert.Equal("你好吗\uFF1F世界", result);  // 你好吗？世界
    }

    [Fact]
    public void FormatParagraph_ConvertsHalfWidthExclamationBetweenChinese()
    {
        var result = TextFormatter.FormatParagraph("太好了!世界");
        Assert.Equal("太好了\uFF01世界", result);  // 太好了！世界
    }

    [Fact]
    public void FormatParagraph_ConvertsHalfWidthColonBetweenChinese()
    {
        var result = TextFormatter.FormatParagraph("提示:这是内容");
        Assert.Equal("提示\uFF1A这是内容", result);  // 提示：这是内容
    }

    [Fact]
    public void FormatParagraph_ConvertsHalfWidthSemicolonBetweenChinese()
    {
        var result = TextFormatter.FormatParagraph("第一;第二");
        Assert.Equal("第一\uFF1B第二", result);  // 第一；第二
    }

    [Fact]
    public void FormatParagraph_ConvertsParenthesesBetweenChinese()
    {
        var result = TextFormatter.FormatParagraph("内容(注释)结束");
        Assert.Equal("内容\uFF08注释\uFF09结束", result);  // 内容（注释）结束
    }

    [Fact]
    public void FormatParagraph_KeepsHalfWidthBetweenAscii()
    {
        var result = TextFormatter.FormatParagraph("hello, world");
        Assert.Equal("hello, world", result);
    }

    [Fact]
    public void FormatParagraph_KeepsDecimalPoint()
    {
        var result = TextFormatter.FormatParagraph("价格是3.14元");
        Assert.Equal("价格是3.14元", result);
    }

    [Fact]
    public void FormatParagraph_KeepsAbbreviationPeriod()
    {
        var result = TextFormatter.FormatParagraph("Mr. Smith went home");
        Assert.Equal("Mr. Smith went home", result);
    }

    // ===== 引号修正测试 =====

    [Fact]
    public void FormatParagraph_ConvertsStraightQuotesToCurly()
    {
        var result = TextFormatter.FormatParagraph("他说\"你好\"然后走了");
        Assert.Equal("他说\u201C你好\u201D然后走了", result);  // 他说"你好"然后走了
    }

    [Fact]
    public void FormatParagraph_HandlesMultipleQuotePairs()
    {
        var result = TextFormatter.FormatParagraph("\"第一\"和\"第二\"");
        Assert.Equal("\u201C第一\u201D和\u201C第二\u201D", result);  // "第一"和"第二"
    }

    // ===== 省略号和破折号修正测试 =====

    [Fact]
    public void FormatParagraph_FixesEllipsis_Dots()
    {
        var result = TextFormatter.FormatParagraph("他走了...");
        Assert.Equal("他走了\u2026\u2026", result);  // 他走了……
    }

    [Fact]
    public void FormatParagraph_FixesEllipsis_FullWidthPeriods()
    {
        var result = TextFormatter.FormatParagraph("他走了。。。");
        Assert.Equal("他走了\u2026\u2026", result);  // 他走了……
    }

    [Fact]
    public void FormatParagraph_FixesDash()
    {
        var result = TextFormatter.FormatParagraph("他说--然后沉默了");
        Assert.Equal("他说\u2014\u2014然后沉默了", result);  // 他说——然后沉默了
    }

    // ===== 空格修正测试 =====

    [Fact]
    public void FormatParagraph_RemovesSpaceBeforePunctuation()
    {
        var result = TextFormatter.FormatParagraph("你好 ， 世界");
        // 空格被移除，逗号转全角
        Assert.DoesNotContain(" ，", result);
    }

    // ===== 零宽字符测试 =====

    [Fact]
    public void FormatParagraph_RemovesZeroWidthChars()
    {
        var text = "你好\u200B世界\uFEFF";
        var result = TextFormatter.FormatParagraph(text);
        Assert.Equal("你好世界", result);
    }

    // ===== Format 多段落测试 =====

    [Fact]
    public void Format_Empty_ReturnsEmpty()
    {
        Assert.Equal(string.Empty, TextFormatter.Format(""));
        Assert.Equal(string.Empty, TextFormatter.Format("   "));
    }

    [Fact]
    public void Format_SingleParagraph_ReturnsFormatted()
    {
        var result = TextFormatter.Format("你好,世界");
        Assert.Equal("你好\uFF0C世界", result);  // 你好，世界
    }

    [Fact]
    public void Format_MultipleParagraphs_PreservesBreaks()
    {
        var input = "第一段内容。\n\n第二段内容。";
        var result = TextFormatter.Format(input);
        var parts = result.Split("\n\n");
        Assert.Equal(2, parts.Length);
        Assert.Equal("第一段内容\u3002", parts[0]);  // 第一段内容。
        Assert.Equal("第二段内容\u3002", parts[1]);  // 第二段内容。
    }

    [Fact]
    public void Format_MergesWrappedLines()
    {
        // 两行没有空行分隔，第一行不以句末标点结尾 → 合并
        var input = "这是一段很长的文字\n被换行截断了";
        var result = TextFormatter.Format(input);
        Assert.Equal("这是一段很长的文字被换行截断了", result);
    }

    [Fact]
    public void Format_MergesMultipleWrappedLines()
    {
        var input = "第一行\n第二行\n第三行。";
        var result = TextFormatter.Format(input);
        Assert.Equal("第一行第二行第三行\u3002", result);  // 第一行第二行第三行。
    }

    [Fact]
    public void Format_DoesNotSplitOnSentenceEndWithoutBlankLine()
    {
        // 没有空行分隔时，即使句子以句号结尾也不分段（合并断行）
        var input = "第一段结束。\n第二段开始。";
        var result = TextFormatter.Format(input);
        var parts = result.Split("\n\n");
        Assert.Single(parts);
        Assert.Contains("第一段结束", parts[0]);
        Assert.Contains("第二段开始", parts[0]);
    }

    [Fact]
    public void Format_CollapsesMultipleBlankLines()
    {
        var input = "第一段。\n\n\n\n第二段。";
        var result = TextFormatter.Format(input);
        var parts = result.Split("\n\n");
        Assert.Equal(2, parts.Length);
    }

    [Fact]
    public void Format_RemovesEmptyParagraphs()
    {
        var input = "内容。\n\n\n\n内容2。";
        var result = TextFormatter.Format(input);
        Assert.DoesNotContain("\n\n\n", result);
    }

    [Fact]
    public void Format_NormalizesLineEndings()
    {
        var input = "第一行\r\n第二行";
        var result = TextFormatter.Format(input);
        Assert.DoesNotContain("\r", result);
    }

    [Fact]
    public void Format_PreservesEnglishParagraphs()
    {
        var input = "Hello world.\n\nThis is a test.";
        var result = TextFormatter.Format(input);
        var parts = result.Split("\n\n");
        Assert.Equal(2, parts.Length);
        Assert.Equal("Hello world.", parts[0]);
        Assert.Equal("This is a test.", parts[1]);
    }

    // ===== 综合场景测试 =====

    [Fact]
    public void Format_RealWorldScenario_MessyChineseText()
    {
        var input = " 　第一章 大闹天宫\r\n" +
                     "话说孙悟空,在花果山称王.他大闹天宫!\n" +
                     "被如来佛祖压在五行山下.\n\n" +
                     " 　第二章 取经之路\n" +
                     "五百年后,唐僧路过五行山...\n" +
                     "救出了孙悟空--师徒踏上西行之路";

        var result = TextFormatter.Format(input);

        var paragraphs = result.Split("\n\n");
        Assert.Equal(2, paragraphs.Length);

        // 第一段应合并断行
        Assert.Contains("孙悟空", paragraphs[0]);
        Assert.Contains("花果山", paragraphs[0]);
        Assert.Contains("五行山", paragraphs[0]);

        // 标点修正
        Assert.Contains("\uFF0C", paragraphs[0]);  // ，
        Assert.Contains("\u3002", paragraphs[0]);  // 。
        Assert.Contains("\uFF01", paragraphs[0]);  // ！

        // 第二段
        Assert.Contains("唐僧", paragraphs[1]);
        Assert.Contains("\u2026\u2026", paragraphs[1]);  // ……
        Assert.Contains("\u2014\u2014", paragraphs[1]);  // ——

        // 不应有前导空格
        Assert.False(paragraphs[0].StartsWith(" "));
        Assert.False(paragraphs[0].StartsWith("\u3000"));
    }

    [Fact]
    public void Format_MixedChineseEnglish()
    {
        var input = "这是Chinese mixed 文本, ok?";
        var result = TextFormatter.Format(input);
        // 逗号在中文字符旁边 → 转全角
        Assert.Contains("\uFF0C", result);
        // 问号紧跟英文 "ok" → 保持半角（仅在中文字符之间才转换）
        Assert.Contains("ok?", result);
    }

    [Fact]
    public void Format_MergesEnglishWrappedLines_WithSpace()
    {
        var input = "This is a long\nsentence that was wrapped.";
        var result = TextFormatter.Format(input);
        // 英文断行合并时应加空格
        Assert.Contains("long sentence", result);
    }
}
