using System.IO.Compression;
using System.Text;
using System.Text.RegularExpressions;
using Epubra.Core;
using Epubra.Epub;
using FluentAssertions;
using Xunit;

namespace Epubra.Tests;

/// <summary>
/// P1 端到端集成测试：模拟用户编辑章节内容 → 导出 EPUB → 验证结构。
/// 测试 "XHTML 内容 → EpubWriter → EPUB 文件" 这条链路。
/// </summary>
public class EpubExportIntegrationTests
{
    [Fact]
    public async Task 完整流程_多章节含富文本_导出有效EPUB()
    {
        var book = new Book { Title = "测试书籍", Author = "测试作者", Language = "zh-CN" };

        book.Chapters.Add(new Chapter
        {
            Title = "前言", FileName = "preface",
            Order = 0, ParentId = null,
            XhtmlContent = "<h1>前言</h1><p>这是一本<strong>测试</strong>书籍，用于验证<em>EPUB</em>导出功能。</p>"
        });

        book.Chapters.Add(new Chapter
        {
            Title = "第一章 开始", FileName = "ch01",
            Order = 1, ParentId = null,
            XhtmlContent = "<h2>第一章</h2><p>章节内容包含<u>下划线</u>和普通文字。</p><p>第二段落。</p>"
        });

        book.Chapters.Add(new Chapter
        {
            Title = "1.1 子章节", FileName = "ch0101",
            Order = 0, ParentId = book.Chapters[1].Id,
            XhtmlContent = "<p>子章节内容。</p>"
        });

        book.Chapters.Add(new Chapter
        {
            Title = "第二章 结束", FileName = "ch02",
            Order = 2, ParentId = null,
            XhtmlContent = "<h2>第二章</h2><p>完结。</p>"
        });

        // 导出
        using var ms = new MemoryStream();
        await new EpubWriter().WriteAsync(book, ms);

        // 验证
        ms.Position = 0;
        using var archive = new ZipArchive(ms, ZipArchiveMode.Read);

        archive.Entries.Should().HaveCountGreaterOrEqualTo(8);
        archive.Entries[0].FullName.Should().Be("mimetype");

        // content.opf
        var opf = ReadEntry(archive, "OEBPS/content.opf");
        opf.Should().Contain("测试书籍");
        opf.Should().Contain("测试作者");
        opf.Should().Contain("chap_001");
        opf.Should().Contain("chap_004");

        // nav.xhtml
        var nav = ReadEntry(archive, "OEBPS/nav.xhtml");
        nav.Should().Contain("前言");
        nav.Should().Contain("第一章 开始");
        nav.Should().Contain("1.1 子章节");
        nav.Should().Contain("第二章 结束");

        // toc.ncx
        var ncx = ReadEntry(archive, "OEBPS/toc.ncx");
        ncx.Should().Contain("前言");
        ncx.Should().Contain("1.1 子章节");

        // 章节内容
        var ch1 = ReadEntry(archive, "OEBPS/preface.xhtml");
        ch1.Should().Contain("<strong>测试</strong>");
        ch1.Should().Contain("<em>EPUB</em>");

        var ch3 = ReadEntry(archive, "OEBPS/ch0101.xhtml");
        ch3.Should().Contain("子章节内容");
    }

    [Fact]
    public async Task 章节顺序_嵌套结构按深度优先写入spine()
    {
        var book = new Book { Title = "顺序测试", Author = "test" };

        // 结构：A → A1, A2; B → B1
        var chapterA = new Chapter { Title = "A", FileName = "ch_a", Order = 0, ParentId = null, XhtmlContent = "<p>A</p>" };
        var chapterA1 = new Chapter { Title = "A1", FileName = "ch_a1", Order = 0, ParentId = chapterA.Id, XhtmlContent = "<p>A1</p>" };
        var chapterA2 = new Chapter { Title = "A2", FileName = "ch_a2", Order = 1, ParentId = chapterA.Id, XhtmlContent = "<p>A2</p>" };
        var chapterB = new Chapter { Title = "B", FileName = "ch_b", Order = 1, ParentId = null, XhtmlContent = "<p>B</p>" };
        var chapterB1 = new Chapter { Title = "B1", FileName = "ch_b1", Order = 0, ParentId = chapterB.Id, XhtmlContent = "<p>B1</p>" };

        // 添加顺序是乱的
        book.Chapters.AddRange(new[] { chapterB, chapterA, chapterB1, chapterA1, chapterA2 });

        using var ms = new MemoryStream();
        await new EpubWriter().WriteAsync(book, ms);
        ms.Position = 0;

        using var archive = new ZipArchive(ms, ZipArchiveMode.Read);
        var opf = ReadEntry(archive, "OEBPS/content.opf");

        // 验证 spine 顺序：A, A1, A2, B, B1
        var spineOrder = new List<string>();
        foreach (Match m in Regex.Matches(opf, @"idref=""(chap_\d+)"""))
        {
            spineOrder.Add(m.Groups[1].Value);
        }

        spineOrder.Should().HaveCount(5);
        spineOrder[0].Should().Be("chap_001"); // A
        spineOrder[1].Should().Be("chap_002"); // A1
        spineOrder[2].Should().Be("chap_003"); // A2
        spineOrder[3].Should().Be("chap_004"); // B
        spineOrder[4].Should().Be("chap_005"); // B1

        // 验证各章节文件内容
        ReadEntry(archive, "OEBPS/ch_a.xhtml").Should().Contain("<p>A</p>");
        ReadEntry(archive, "OEBPS/ch_a1.xhtml").Should().Contain("<p>A1</p>");
        ReadEntry(archive, "OEBPS/ch_a2.xhtml").Should().Contain("<p>A2</p>");
        ReadEntry(archive, "OEBPS/ch_b.xhtml").Should().Contain("<p>B</p>");
        ReadEntry(archive, "OEBPS/ch_b1.xhtml").Should().Contain("<p>B1</p>");
    }

    [Fact]
    public async Task 空内容章节_导出不应失败()
    {
        var book = new Book { Title = "空章节测试", Author = "test" };
        book.Chapters.Add(new Chapter { Title = "空章节", FileName = "empty", Order = 0, XhtmlContent = "" });

        using var ms = new MemoryStream();
        var act = async () => await new EpubWriter().WriteAsync(book, ms);
        await act.Should().NotThrowAsync();

        ms.Position = 0;
        using var archive = new ZipArchive(ms, ZipArchiveMode.Read);
        var ch = ReadEntry(archive, "OEBPS/empty.xhtml");
        ch.Should().Contain("<body>");
        ch.Should().Contain("</body>");
    }

    [Fact]
    public async Task 带图片资源_导出后可从zip中读取()
    {
        var book = new Book { Title = "图片测试", Author = "test" };
        book.Chapters.Add(new Chapter
        {
            Title = "含图章节", FileName = "withimg",
            Order = 0,
            XhtmlContent = "<p><img src=\"images/test.png\" alt=\"\"/></p>"
        });

        var imageData = new byte[] { 0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A };
        book.Resources.Add(new EpubResource
        {
            Kind = EpubResourceKind.Image,
            FileName = "test.png",
            MimeType = "image/png",
            Data = imageData
        });

        using var ms = new MemoryStream();
        await new EpubWriter().WriteAsync(book, ms);
        ms.Position = 0;

        using var archive = new ZipArchive(ms, ZipArchiveMode.Read);
        var imgEntry = archive.GetEntry("OEBPS/images/test.png");
        imgEntry.Should().NotBeNull();

        using var imgStream = imgEntry!.Open();
        using var imgMs = new MemoryStream();
        await imgStream.CopyToAsync(imgMs);
        imgMs.ToArray().Should().Equal(imageData);
    }

    private static string ReadEntry(ZipArchive archive, string path)
    {
        var entry = archive.GetEntry(path);
        entry.Should().NotBeNull($"entry '{path}' should exist");
        using var stream = entry!.Open();
        using var reader = new StreamReader(stream, Encoding.UTF8);
        return reader.ReadToEnd();
    }
}
