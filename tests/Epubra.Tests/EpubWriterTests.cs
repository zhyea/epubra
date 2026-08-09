using System.IO.Compression;
using System.Text;
using Epubra.Core;
using Epubra.Epub;
using FluentAssertions;
using Xunit;

namespace Epubra.Tests;

public class EpubWriterTests
{
    [Fact]
    public async Task 最小可打包_只含一本书和一个章节()
    {
        var book = new Book
        {
            Title = "测试书",
            Author = "测试作者",
            Language = "zh-CN",
            Chapters =
            {
                new Chapter
                {
                    Title = "第一章",
                    XhtmlContent = "<p>你好，世界。</p>",
                    Order = 0,
                    FileName = "chapter_001"
                }
            }
        };

        using var ms = new MemoryStream();
        await new EpubWriter().WriteAsync(book, ms);

        ms.Position = 0;
        using var archive = new ZipArchive(ms, ZipArchiveMode.Read);

        archive.Entries.Select(e => e.FullName).Should().Contain(new[]
        {
            "mimetype",
            "META-INF/container.xml",
            "OEBPS/content.opf",
            "OEBPS/nav.xhtml",
            "OEBPS/toc.ncx",
            "OEBPS/chapter_001.xhtml"
        });
    }

    [Fact]
    public async Task mimetype必须是第一个Entry且不压缩()
    {
        var book = new Book
        {
            Title = "测试",
            Chapters =
            {
                new Chapter { Title = "C", XhtmlContent = "<p>x</p>", Order = 0 }
            }
        };

        using var ms = new MemoryStream();
        await new EpubWriter().WriteAsync(book, ms);

        ms.Position = 0;
        using var archive = new ZipArchive(ms, ZipArchiveMode.Read);

        archive.Entries[0].FullName.Should().Be("mimetype");
        // mimetype 必须未压缩：CompressedLength 应等于 Length
        archive.Entries[0].CompressedLength.Should().Be(archive.Entries[0].Length);

        using var entryStream = archive.Entries[0].Open();
        using var reader = new StreamReader(entryStream);
        var content = await reader.ReadToEndAsync();
        content.Should().Be("application/epub+zip");
    }

    [Fact]
    public async Task 包含图片和字体资源()
    {
        var coverBytes = new byte[] { 0xFF, 0xD8, 0xFF, 0xE0, 0x00, 0x10 };
        var fontBytes = new byte[] { 0x00, 0x01, 0x00, 0x00 };

        var book = new Book
        {
            Title = "带资源",
            CoverResourceId = Guid.NewGuid(),
            Chapters =
            {
                new Chapter { Title = "C", XhtmlContent = "<p>x</p>", Order = 0 }
            },
            Resources =
            {
                new EpubResource
                {
                    Kind = EpubResourceKind.Image,
                    FileName = "cover.jpg",
                    MimeType = "image/jpeg",
                    Data = coverBytes,
                    Role = "cover"
                },
                new EpubResource
                {
                    Kind = EpubResourceKind.Font,
                    FileName = "SourceHan.otf",
                    MimeType = "font/otf",
                    Data = fontBytes
                }
            }
        };

        // 把 CoverResourceId 指向真实的资源
        book.CoverResourceId = book.Resources[0].Id;

        using var ms = new MemoryStream();
        await new EpubWriter().WriteAsync(book, ms);

        ms.Position = 0;
        using var archive = new ZipArchive(ms, ZipArchiveMode.Read);

        archive.GetEntry("OEBPS/images/cover.jpg").Should().NotBeNull();
        archive.GetEntry("OEBPS/fonts/SourceHan.otf").Should().NotBeNull();

        // 校验 content.opf 引用了封面
        var opfEntry = archive.GetEntry("OEBPS/content.opf")!;
        using var opfStream = opfEntry.Open();
        var opfText = await new StreamReader(opfStream).ReadToEndAsync();
        opfText.Should().Contain("cover-image");
    }

    [Fact]
    public async Task 嵌套章节按文档顺序写入spine()
    {
        var top = new Chapter { Id = Guid.NewGuid(), Title = "卷一", XhtmlContent = "<p>卷一内容</p>", Order = 0 };
        var sub1 = new Chapter { Id = Guid.NewGuid(), Title = "第一章", XhtmlContent = "<p>第一章</p>", ParentId = top.Id, Order = 0 };
        var sub2 = new Chapter { Id = Guid.NewGuid(), Title = "第二章", XhtmlContent = "<p>第二章</p>", ParentId = top.Id, Order = 1 };

        var book = new Book
        {
            Title = "嵌套测试",
            Chapters = { top, sub1, sub2 }
        };

        using var ms = new MemoryStream();
        await new EpubWriter().WriteAsync(book, ms);

        ms.Position = 0;
        using var archive = new ZipArchive(ms, ZipArchiveMode.Read);

        var opfEntry = archive.GetEntry("OEBPS/content.opf")!;
        using var opfStream = opfEntry.Open();
        var opfText = await new StreamReader(opfStream, Encoding.UTF8).ReadToEndAsync();

        // spine 中应按 卷一 -> 第一章 -> 第二章 的顺序出现
        var idxJuan = opfText.IndexOf("卷一", StringComparison.Ordinal);
        var idxCh1 = opfText.IndexOf("第一章", StringComparison.Ordinal);
        var idxCh2 = opfText.IndexOf("第二章", StringComparison.Ordinal);

        idxJuan.Should().BeLessThan(idxCh1);
        idxCh1.Should().BeLessThan(idxCh2);
    }

    [Fact]
    public async Task 空Title应抛异常()
    {
        var book = new Book { Title = "" };

        using var ms = new MemoryStream();
        var act = async () => await new EpubWriter().WriteAsync(book, ms);

        await act.Should().ThrowAsync<InvalidOperationException>();
    }

    [Fact]
    public async Task 中文标题应正确保留()
    {
        var book = new Book
        {
            Title = "方与圆全集",
            Author = "丁远峙",
            Chapters =
            {
                new Chapter { Title = "第一章 心灵的种子", XhtmlContent = "<p>正文</p>", Order = 0 }
            }
        };

        using var ms = new MemoryStream();
        await new EpubWriter().WriteAsync(book, ms);

        ms.Position = 0;
        using var archive = new ZipArchive(ms, ZipArchiveMode.Read);

        var opfEntry = archive.GetEntry("OEBPS/content.opf")!;
        var opfText = await new StreamReader(opfEntry.Open(), Encoding.UTF8).ReadToEndAsync();

        opfText.Should().Contain("方与圆全集");
        opfText.Should().Contain("丁远峙");

        // 章节标题应在 nav.xhtml 中保留（OPF 只存 href，不存标题）
        var navEntry = archive.GetEntry("OEBPS/nav.xhtml")!;
        var navText = await new StreamReader(navEntry.Open(), Encoding.UTF8).ReadToEndAsync();
        navText.Should().Contain("第一章 心灵的种子");
    }
}