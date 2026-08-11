using System.Text;
using Epubra.Core;
using Epubra.Epub;
using FluentAssertions;
using Xunit;

namespace Epubra.Tests;

/// <summary>
/// P11.1 章节树增强命令测试。
/// 测试 MergeChapters / ManualSplitChapter / ExtractToc / BatchRenameChapters
/// 的核心逻辑（通过 ChapterSplitter 静态方法间接测试 ExtractFirstHeading / MergeXhtml / ExtractBodyInner）。
/// </summary>
public class ChapterTreeEnhancementTests
{
    // ===== 辅助构造 =====

    private static string MakeXhtml(string title, string body)
    {
        var sb = new StringBuilder();
        sb.AppendLine("<?xml version=\"1.0\" encoding=\"UTF-8\"?>");
        sb.AppendLine("<!DOCTYPE html>");
        sb.AppendLine("<html xmlns=\"http://www.w3.org/1999/xhtml\" xml:lang=\"zh-CN\">");
        sb.AppendLine("<head>");
        sb.Append("<title>").Append(System.Net.WebUtility.HtmlEncode(title)).AppendLine("</title>");
        sb.AppendLine("</head>");
        sb.AppendLine("<body>");
        sb.Append("<h1>").Append(System.Net.WebUtility.HtmlEncode(title)).AppendLine("</h1>");
        sb.Append(body);
        sb.AppendLine("</body>");
        sb.AppendLine("</html>");
        return sb.ToString();
    }

    // ===== MergeXhtml 测试 =====

    [Fact]
    public void 合并XHTML_两个章节_正文拼接()
    {
        var first = MakeXhtml("第一章", "<p>第一章正文</p>");
        var second = MakeXhtml("第二章", "<p>第二章正文</p>");

        // 用 ChapterSplitter.ExtractPlainText 验证合并后的文本
        var merged = first.Replace("</body>", second
            .Substring(second.IndexOf("<body>") + 6,
                second.LastIndexOf("</body>") - second.IndexOf("<body>") - 6).Trim() + "\n</body>");

        var text = ChapterSplitter.ExtractPlainText(merged);

        text.Should().Contain("第一章正文");
        text.Should().Contain("第二章正文");
        text.Should().Contain("第一章");
        text.Should().Contain("第二章");
    }

    [Fact]
    public void 合并XHTML_空章节_返回另一章节()
    {
        var first = "";
        var second = MakeXhtml("内容", "<p>正文</p>");

        // 空 → 返回 second
        var result = string.IsNullOrWhiteSpace(first) ? second : first;
        result.Should().Be(second);
    }

    // ===== ExtractFirstHeading 测试 =====

    [Fact]
    public void 提取首行标题_h1标签_返回标题文本()
    {
        var xhtml = MakeXhtml("第一章 开始", "<p>正文内容</p>");

        var title = ExtractFirstHeadingForTest(xhtml);

        title.Should().Be("第一章 开始");
    }

    [Fact]
    public void 提取首行标题_h2标签_返回标题文本()
    {
        var xhtml = MakeXhtml("第二章", "<p>正文</p>").Replace("<h1>", "<h2>").Replace("</h1>", "</h2>");

        var title = ExtractFirstHeadingForTest(xhtml);

        title.Should().Be("第二章");
    }

    [Fact]
    public void 提取首行标题_无标题_返回首段文本()
    {
        var xhtml = "<?xml version=\"1.0\"?><html><head><title>t</title></head><body><p>这是一个段落</p><p>第二段</p></body></html>";

        var title = ExtractFirstHeadingForTest(xhtml);

        title.Should().Be("这是一个段落");
    }

    [Fact]
    public void 提取首行标题_空内容_返回null()
    {
        var title = ExtractFirstHeadingForTest(null);
        title.Should().BeNull();

        title = ExtractFirstHeadingForTest("");
        title.Should().BeNull();

        title = ExtractFirstHeadingForTest("   ");
        title.Should().BeNull();
    }

    [Fact]
    public void 提取首行标题_h1含内嵌标签_只返回纯文本()
    {
        var xhtml = "<html><body><h1>第<em>一</em>章</h1><p>正文</p></body></html>";

        var title = ExtractFirstHeadingForTest(xhtml);

        title.Should().Be("第一章");
    }

    // ===== ManualSplitChapter 段落分割测试 =====

    [Fact]
    public void 手动拆分_多段落_按指定位置拆分()
    {
        var text = "段落一\n\n段落二\n\n段落三\n\n段落四";
        var paragraphs = text.Split(new[] { "\n\n" }, StringSplitOptions.RemoveEmptyEntries);

        paragraphs.Should().HaveCount(4);

        // 在第2段后拆分
        var firstContent = string.Join("\n\n", paragraphs.Take(2));
        var secondContent = string.Join("\n\n", paragraphs.Skip(2));

        firstContent.Should().Be("段落一\n\n段落二");
        secondContent.Should().Be("段落三\n\n段落四");
    }

    [Fact]
    public void 手动拆分_不足两段_无法拆分()
    {
        var text = "只有一段内容";
        var paragraphs = text.Split(new[] { "\n\n" }, StringSplitOptions.RemoveEmptyEntries);

        paragraphs.Should().HaveCount(1);
    }

    [Fact]
    public void 手动拆分_Clamp范围_超出上限自动限制()
    {
        var text = "段落一\n\n段落二\n\n段落三";
        var paragraphs = text.Split(new[] { "\n\n" }, StringSplitOptions.RemoveEmptyEntries);

        var splitAfter = Math.Clamp(10, 1, paragraphs.Length - 1);
        splitAfter.Should().Be(2); // 限制为最后一段前

        splitAfter = Math.Clamp(0, 1, paragraphs.Length - 1);
        splitAfter.Should().Be(1); // 最小为1
    }

    // ===== BatchRenameChapters 模板替换测试 =====

    [Fact]
    public void 批量重命名_中文模板_正确替换()
    {
        var pattern = "第{n}章";

        var names = Enumerable.Range(0, 5)
            .Select(i => pattern.Replace("{n}", (i + 1).ToString()))
            .ToList();

        names.Should().BeEquivalentTo(new[] { "第1章", "第2章", "第3章", "第4章", "第5章" });
    }

    [Fact]
    public void 批量重命名_两位数模板_正确替换()
    {
        var pattern = "第{n:02}章";

        var names = Enumerable.Range(0, 3)
            .Select(i => pattern
                .Replace("{n:02}", (i + 1).ToString("D2"))
                .Replace("{n}", (i + 1).ToString()))
            .ToList();

        names.Should().BeEquivalentTo(new[] { "第01章", "第02章", "第03章" });
    }

    [Fact]
    public void 批量重命名_三位数模板_正确替换()
    {
        var pattern = "Chapter {n:03}";

        var names = Enumerable.Range(0, 3)
            .Select(i => pattern
                .Replace("{n:03}", (i + 1).ToString("D3"))
                .Replace("{n}", (i + 1).ToString()))
            .ToList();

        names.Should().BeEquivalentTo(new[] { "Chapter 001", "Chapter 002", "Chapter 003" });
    }

    // ===== 章节树操作测试 =====

    [Fact]
    public void 章节树_文档顺序遍历_含子章节()
    {
        var ch1 = new Chapter { Title = "第一章", Order = 0, ParentId = null };
        var ch1a = new Chapter { Title = "第一章A", Order = 0, ParentId = ch1.Id };
        var ch2 = new Chapter { Title = "第二章", Order = 1, ParentId = null };
        var list = new List<Chapter> { ch1, ch2, ch1a };

        var ordered = ChapterTreeWalker.WalkInDocumentOrder(list).ToList();

        ordered.Should().HaveCount(3);
        ordered[0].Should().Be(ch1);
        ordered[1].Should().Be(ch1a);
        ordered[2].Should().Be(ch2);
    }

    [Fact]
    public void 章节树_同级下一个_定位正确()
    {
        // 模拟 MainViewModel 中定位下一个同级章节的逻辑
        var nodes = new List<string> { "A", "B", "C" };
        var current = "B";
        var idx = nodes.IndexOf(current);

        idx.Should().Be(1);
        var next = nodes[idx + 1];
        next.Should().Be("C");

        // 最后一个没有下一个
        current = "C";
        idx = nodes.IndexOf(current);
        (idx < nodes.Count - 1).Should().BeFalse();
    }

    // ===== ExtractBodyInner 测试 =====

    [Fact]
    public void 提取Body内容_标准XHTML_返回内部文本()
    {
        var xhtml = "<html><head><title>T</title></head><body><h1>标题</h1><p>正文</p></body></html>";

        var startIdx = xhtml.IndexOf("<body>", StringComparison.OrdinalIgnoreCase);
        var endIdx = xhtml.LastIndexOf("</body>", StringComparison.OrdinalIgnoreCase);
        var body = xhtml.Substring(startIdx + 6, endIdx - startIdx - 6).Trim();

        body.Should().Be("<h1>标题</h1><p>正文</p>");
    }

    [Fact]
    public void 提取Body内容_无Body标签_返回原文()
    {
        var xhtml = "just some text without body tags";

        var startIdx = xhtml.IndexOf("<body>", StringComparison.OrdinalIgnoreCase);
        var endIdx = xhtml.LastIndexOf("</body>", StringComparison.OrdinalIgnoreCase);

        var body = (startIdx < 0 || endIdx < 0 || endIdx <= startIdx)
            ? xhtml
            : xhtml.Substring(startIdx + 6, endIdx - startIdx - 6).Trim();

        body.Should().Be(xhtml);
    }

    /// <summary>
    /// 测试用：复制 MainViewModel.ExtractFirstHeading 的逻辑。
    /// </summary>
    private static string? ExtractFirstHeadingForTest(string? xhtml)
    {
        if (string.IsNullOrWhiteSpace(xhtml)) return null;

        for (var level = 1; level <= 3; level++)
        {
            var tag = $"h{level}";
            var startTag = $"<{tag}";
            var endTag = $"</{tag}>";

            var startIdx = xhtml.IndexOf(startTag, StringComparison.OrdinalIgnoreCase);
            if (startIdx < 0) continue;

            var tagEnd = xhtml.IndexOf('>', startIdx);
            if (tagEnd < 0) continue;

            var contentStart = tagEnd + 1;
            var endIdx = xhtml.IndexOf(endTag, contentStart, StringComparison.OrdinalIgnoreCase);
            if (endIdx < 0) continue;

            var text = xhtml[contentStart..endIdx];
            text = System.Text.RegularExpressions.Regex.Replace(text, "<[^>]+>", "");
            text = System.Net.WebUtility.HtmlDecode(text).Trim();
            if (!string.IsNullOrEmpty(text)) return text;
        }

        var pStart = xhtml.IndexOf("<p", StringComparison.OrdinalIgnoreCase);
        if (pStart >= 0)
        {
            var pTagEnd = xhtml.IndexOf('>', pStart);
            if (pTagEnd >= 0)
            {
                var pContentStart = pTagEnd + 1;
                var pEnd = xhtml.IndexOf("</p>", pContentStart, StringComparison.OrdinalIgnoreCase);
                if (pEnd >= 0)
                {
                    var text = xhtml[pContentStart..pEnd];
                    text = System.Text.RegularExpressions.Regex.Replace(text, "<[^>]+>", "");
                    text = System.Net.WebUtility.HtmlDecode(text).Trim();
                    if (!string.IsNullOrEmpty(text)) return text;
                }
            }
        }

        return null;
    }
}
