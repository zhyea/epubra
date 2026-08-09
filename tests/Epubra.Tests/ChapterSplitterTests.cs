using Epubra.Epub;
using FluentAssertions;
using Xunit;

namespace Epubra.Tests;

/// <summary>
/// ChapterSplitter 章节自动识别与切分器测试。
/// </summary>
public class ChapterSplitterTests
{
    private readonly ChapterSplitter _splitter = new();

    [Fact]
    public void 切分_中文章节标记_正确拆分()
    {
        var text = "第一章 开始\n这是第一章的内容。\n\n第二章 发展\n这是第二章的内容。\n\n第三章 结局\n最后的内容。";

        var segments = _splitter.Split(text);

        segments.Should().HaveCount(3);
        segments[0].Title.Should().Be("第一章");
        segments[0].Content.Should().Contain("这是第一章的内容");
        segments[1].Title.Should().Be("第二章");
        segments[1].Content.Should().Contain("这是第二章的内容");
        segments[2].Title.Should().Be("第三章");
        segments[2].Content.Should().Contain("最后的内容");
    }

    [Fact]
    public void 切分_中文卷回节标记_正确识别()
    {
        var text = "第一卷 起\n卷一内容\n\n第二十回 转\n回目内容\n\n第三节 合\n节内容";

        var segments = _splitter.Split(text);

        segments.Should().HaveCount(3);
        segments[0].Title.Should().Be("第一卷");
        segments[1].Title.Should().Be("第二十回");
        segments[2].Title.Should().Be("第三节");
    }

    [Fact]
    public void 切分_中文数字章节_正确识别()
    {
        var text = "第零章 序\n序言内容\n\n第十二章 终\n终章内容";

        var segments = _splitter.Split(text);

        segments.Should().HaveCount(2);
        segments[0].Title.Should().Be("第零章");
        segments[1].Title.Should().Be("第十二章");
    }

    [Fact]
    public void 切分_特殊章节标记_前言后记等()
    {
        var text = "前言\n前言内容。\n\n第一章 开始\n正文内容。\n\n后记\n后记内容。";

        var segments = _splitter.Split(text);

        segments.Should().HaveCount(3);
        segments[0].Title.Should().Be("前言");
        segments[1].Title.Should().Be("第一章");
        segments[2].Title.Should().Be("后记");
    }

    [Fact]
    public void 切分_英文章节标记_Chapter_Part_Section()
    {
        var text = "Chapter 1 Begin\nFirst chapter content.\n\nChapter 2 Middle\nSecond chapter.\n\nPart 3 End\nFinal part.";

        var segments = _splitter.Split(text);

        segments.Should().HaveCount(3);
        segments[0].Title.Should().Be("Chapter 1");
        segments[1].Title.Should().Be("Chapter 2");
        segments[2].Title.Should().Be("Part 3");
    }

    [Fact]
    public void 切分_无章节标记_按双换行拆分()
    {
        var text = "第一段内容。\n\n第二段内容。\n\n第三段内容。";

        var segments = _splitter.Split(text);

        segments.Should().HaveCount(3);
        segments[0].Title.Should().Be("章节 1");
        segments[1].Title.Should().Be("章节 2");
        segments[2].Title.Should().Be("章节 3");
    }

    [Fact]
    public void 切分_无章节标记_单段文本_返回一个段()
    {
        var text = "这是一段没有章节标记的文本内容。";

        var segments = _splitter.Split(text);

        segments.Should().HaveCount(1);
        segments[0].Title.Should().Be("正文");
        segments[0].Content.Should().Be(text);
    }

    [Fact]
    public void 切分_单个章节标记_前面有内容_生成引言()
    {
        var text = "这是一些前言性质的文字。\n\n第一章 开始\n正文内容。";

        var segments = _splitter.Split(text);

        segments.Should().HaveCount(2);
        segments[0].Title.Should().Be("引言");
        segments[0].Content.Should().Contain("前言性质");
        segments[1].Title.Should().Be("第一章");
        segments[1].Content.Should().Contain("正文内容");
    }

    [Fact]
    public void 切分_空文本_返回单个空段()
    {
        var segments = _splitter.Split("");

        segments.Should().HaveCount(1);
        segments[0].Title.Should().Be("正文");
    }

    [Fact]
    public void 切分_混合中英文章节标记()
    {
        var text = "Chapter 1 The Beginning\nEnglish content.\n\n第二章 中文标题\n中文内容。\n\nEpilogue\n尾声内容。";

        var segments = _splitter.Split(text);

        segments.Should().HaveCount(2);
        segments[0].Title.Should().Be("Chapter 1");
        segments[1].Title.Should().Be("第二章");
    }

    [Fact]
    public void 切分_章节内容包含章节关键词_不误切分()
    {
        // "这是第一章的内容" 不应被误匹配为章节标记
        var text = "第一章 开始\n这是第一章的内容。\n\n第二章 发展\n这里说到了第一章的事情。";

        var segments = _splitter.Split(text);

        segments.Should().HaveCount(2);
        segments[0].Title.Should().Be("第一章");
        segments[0].Content.Should().Contain("这是第一章的内容");
        segments[1].Title.Should().Be("第二章");
    }

    [Fact]
    public void SplitFromXhtml_从HTML切分_正确提取文本并拆分()
    {
        var xhtml = "<html><head><title>Test</title></head><body>" +
                     "<h1>第一章</h1><p>第一章<strong>内容</strong>。</p>" +
                     "<h1>第二章</h1><p>第二章<em>内容</em>。</p>" +
                     "</body></html>";

        var segments = _splitter.SplitFromXhtml(xhtml);

        segments.Should().HaveCount(2);
        segments[0].Title.Should().Be("第一章");
        segments[0].Content.Should().Contain("内容");
        segments[1].Title.Should().Be("第二章");
    }

    [Fact]
    public void SplitFromXhtml_带完整XHTML声明_正确切分()
    {
        var xhtml = "<?xml version=\"1.0\" encoding=\"UTF-8\"?>\n" +
                     "<html xmlns=\"http://www.w3.org/1999/xhtml\">" +
                     "<head><title>第一章</title></head><body>" +
                     "<h1>第一章 开始</h1><p>内容一。</p>" +
                     "<h1>第二章 结束</h1><p>内容二。</p>" +
                     "</body></html>";

        var segments = _splitter.SplitFromXhtml(xhtml);

        segments.Should().HaveCount(2);
        segments[0].Title.Should().Be("第一章");
        segments[1].Title.Should().Be("第二章");
    }

    [Fact]
    public void ExtractPlainText_剥离HTML标签_保留文本()
    {
        var html = "<p>这是<strong>加粗</strong>的文字。</p><p>第二段。</p>";

        var text = ChapterSplitter.ExtractPlainText(html);

        text.Should().NotContain("<");
        text.Should().Contain("加粗");
        text.Should().Contain("第二段");
    }

    [Fact]
    public void WrapAsXhtml_生成完整XHTML文档()
    {
        var xhtml = ChapterSplitter.WrapAsXhtml("测试章节", "第一段。\n\n第二段。");

        xhtml.Should().Contain("<?xml version=\"1.0\"");
        xhtml.Should().Contain("<html xmlns=\"http://www.w3.org/1999/xhtml\"");
        xhtml.Should().Contain("<title>测试章节</title>");
        xhtml.Should().Contain("<h1>测试章节</h1>");
        xhtml.Should().Contain("<p>");
        xhtml.Should().Contain("第一段");
        xhtml.Should().Contain("第二段");
    }

    [Fact]
    public void WrapAsXhtml_空内容_只有标题()
    {
        var xhtml = ChapterSplitter.WrapAsXhtml("空章节", "");

        xhtml.Should().Contain("<h1>空章节</h1>");
        xhtml.Should().Contain("<body>");
        xhtml.Should().Contain("</body>");
    }

    [Fact]
    public void IsChapterMarkerLine_中文章节标记_返回true()
    {
        ChapterSplitter.IsChapterMarkerLine("第一章 开始").Should().BeTrue();
        ChapterSplitter.IsChapterMarkerLine("第二十回 转折").Should().BeTrue();
        ChapterSplitter.IsChapterMarkerLine("  第三章").Should().BeTrue();
    }

    [Fact]
    public void IsChapterMarkerLine_特殊章节_返回true()
    {
        ChapterSplitter.IsChapterMarkerLine("前言").Should().BeTrue();
        ChapterSplitter.IsChapterMarkerLine("后记").Should().BeTrue();
        ChapterSplitter.IsChapterMarkerLine("楔子").Should().BeTrue();
    }

    [Fact]
    public void IsChapterMarkerLine_英文章节_返回true()
    {
        ChapterSplitter.IsChapterMarkerLine("Chapter 1").Should().BeTrue();
        ChapterSplitter.IsChapterMarkerLine("Part 2").Should().BeTrue();
        ChapterSplitter.IsChapterMarkerLine("chapter 10").Should().BeTrue();
    }

    [Fact]
    public void IsChapterMarkerLine_非章节行_返回false()
    {
        ChapterSplitter.IsChapterMarkerLine("这是一段普通的文字").Should().BeFalse();
        ChapterSplitter.IsChapterMarkerLine("第一章的内容是这样的").Should().BeFalse();
        ChapterSplitter.IsChapterMarkerLine("").Should().BeFalse();
    }
}
