using System.IO;
using System.Text;
using Epubra.Core;
using Epubra.Epub;
using Epubra.Export;
using FluentAssertions;
using Xunit;

namespace Epubra.Tests;

/// <summary>
/// P3 测试：EPUB 导入（往返）、TXT 导出、HTML 导出。
/// </summary>
public class EpubReaderTests
{
    [Fact]
    public async Task 往返测试_导出后导入_元数据和章节保持一致()
    {
        // 构造 Book
        var original = new Book
        {
            Title = "往返测试书籍",
            Author = "测试作者",
            Language = "zh-CN",
            Publisher = "测试出版社",
            Identifier = "978-0-000000-00-0"
        };

        original.Chapters.Add(new Chapter
        {
            Title = "第一章", FileName = "ch01",
            Order = 0, ParentId = null,
            XhtmlContent = "<h1>第一章</h1><p>这是<strong>测试</strong>内容。</p>"
        });

        original.Chapters.Add(new Chapter
        {
            Title = "第二章", FileName = "ch02",
            Order = 1, ParentId = null,
            XhtmlContent = "<h1>第二章</h1><p>第二章内容。</p>"
        });

        // 导出 EPUB
        using var ms = new MemoryStream();
        await new EpubWriter().WriteAsync(original, ms);
        ms.Position = 0;

        // 导入 EPUB
        var reader = new EpubReader();
        var imported = await reader.ReadFromStreamAsync(ms);

        // 验证元数据
        imported.Title.Should().Be("往返测试书籍");
        imported.Author.Should().Be("测试作者");
        imported.Language.Should().Be("zh-CN");
        imported.Publisher.Should().Be("测试出版社");
        imported.Identifier.Should().Be("978-0-000000-00-0");

        // 验证章节
        imported.Chapters.Should().HaveCount(2);
        imported.Chapters[0].Title.Should().Be("第一章");
        imported.Chapters[1].Title.Should().Be("第二章");

        // 验证章节内容包含原始标签
        imported.Chapters[0].XhtmlContent.Should().Contain("<strong>测试</strong>");
        imported.Chapters[1].XhtmlContent.Should().Contain("第二章内容");
    }

    [Fact]
    public async Task 往返测试_带图片资源_导入后保留()
    {
        var book = new Book { Title = "图片往返", Author = "test" };
        book.Chapters.Add(new Chapter
        {
            Title = "含图章节", FileName = "withimg",
            Order = 0, XhtmlContent = "<p><img src=\"images/test.png\" alt=\"\"/></p>"
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

        var imported = await new EpubReader().ReadFromStreamAsync(ms);

        // 验证资源
        imported.Resources.Should().Contain(r => r.FileName == "test.png");
        var imgRes = imported.Resources.First(r => r.FileName == "test.png");
        imgRes.Data.Should().Equal(imageData);
        imgRes.MimeType.Should().Be("image/png");
    }

    [Fact]
    public async Task 导入无效文件_应抛出异常()
    {
        // 创建一个不包含 container.xml 的 ZIP 文件
        using var ms = new MemoryStream();
        using (var archive = new System.IO.Compression.ZipArchive(ms, System.IO.Compression.ZipArchiveMode.Create, leaveOpen: true))
        {
            var entry = archive.CreateEntry("dummy.txt");
            using var sw = new StreamWriter(entry.Open());
            sw.Write("not an epub");
        }

        ms.Position = 0;
        var act = async () => await new EpubReader().ReadFromStreamAsync(ms);
        await act.Should().ThrowAsync<Exception>();
    }
}

public class TxtExporterTests
{
    [Fact]
    public async Task 导出TXT_包含书名作者和章节内容()
    {
        var book = new Book { Title = "TXT测试", Author = "作者甲" };
        book.Chapters.Add(new Chapter
        {
            Title = "第一章",
            Order = 0, ParentId = null,
            XhtmlContent = "<h1>第一章</h1><p>这是<strong>加粗</strong>的文字。</p>"
        });
        book.Chapters.Add(new Chapter
        {
            Title = "第二章",
            Order = 1, ParentId = null,
            XhtmlContent = "<p>普通段落。</p>"
        });

        var tempFile = Path.Combine(Path.GetTempPath(), $"epubra_txt_{Guid.NewGuid():N}.txt");
        try
        {
            await new TxtExporter().ExportAsync(book, tempFile);
            var content = await File.ReadAllTextAsync(tempFile, Encoding.UTF8);

            content.Should().Contain("TXT测试");
            content.Should().Contain("作者甲");
            content.Should().Contain("第一章");
            content.Should().Contain("加粗");
            content.Should().Contain("这是加粗的文字。");
            content.Should().Contain("第二章");
            content.Should().Contain("普通段落。");
            content.Should().NotContain("<strong>");
            content.Should().NotContain("<p>");
        }
        finally
        {
            if (File.Exists(tempFile)) File.Delete(tempFile);
        }
    }

    [Fact]
    public async Task 导出TXT_空内容章节不崩溃()
    {
        var book = new Book { Title = "空章节TXT", Author = "test" };
        book.Chapters.Add(new Chapter { Title = "空", Order = 0, XhtmlContent = "" });

        var tempFile = Path.Combine(Path.GetTempPath(), $"epubra_empty_{Guid.NewGuid():N}.txt");
        try
        {
            var act = async () => await new TxtExporter().ExportAsync(book, tempFile);
            await act.Should().NotThrowAsync();
            File.Exists(tempFile).Should().BeTrue();
        }
        finally
        {
            if (File.Exists(tempFile)) File.Delete(tempFile);
        }
    }
}

public class HtmlExporterTests
{
    [Fact]
    public async Task 导出HTML_包含目录和章节内容()
    {
        var book = new Book { Title = "HTML测试", Author = "作者乙", Language = "zh-CN" };
        book.Chapters.Add(new Chapter
        {
            Title = "前言",
            Order = 0, ParentId = null,
            XhtmlContent = "<p>前言<em>内容</em>。</p>"
        });
        book.Chapters.Add(new Chapter
        {
            Title = "正篇",
            Order = 1, ParentId = null,
            XhtmlContent = "<p>正文。</p>"
        });

        var tempFile = Path.Combine(Path.GetTempPath(), $"epubra_html_{Guid.NewGuid():N}.html");
        try
        {
            await new HtmlExporter().ExportAsync(book, tempFile);
            var content = await File.ReadAllTextAsync(tempFile, Encoding.UTF8);

            content.Should().Contain("<!DOCTYPE html>");
            content.Should().Contain("HTML测试");
            content.Should().Contain("作者乙");
            content.Should().Contain("目录");
            content.Should().Contain("href=\"#ch_0\"");
            content.Should().Contain("href=\"#ch_1\"");
            content.Should().Contain("id=\"ch_0\"");
            content.Should().Contain("id=\"ch_1\"");
            content.Should().Contain("前言");
            content.Should().Contain("正文。");
            content.Should().Contain("<em>内容</em>");
        }
        finally
        {
            if (File.Exists(tempFile)) File.Delete(tempFile);
        }
    }

    [Fact]
    public async Task 导出HTML_包含CSS样式()
    {
        var book = new Book { Title = "样式测试", Author = "test" };
        book.Chapters.Add(new Chapter { Title = "ch1", Order = 0, XhtmlContent = "<p>text</p>" });

        var tempFile = Path.Combine(Path.GetTempPath(), $"epubra_css_{Guid.NewGuid():N}.html");
        try
        {
            await new HtmlExporter().ExportAsync(book, tempFile);
            var content = await File.ReadAllTextAsync(tempFile, Encoding.UTF8);

            content.Should().Contain("<style>");
            content.Should().Contain("font-family");
            content.Should().Contain("line-height");
        }
        finally
        {
            if (File.Exists(tempFile)) File.Delete(tempFile);
        }
    }
}

public class TxtImporterTests
{
    [Fact]
    public void 导入_按章节标记拆分()
    {
        var text = "测试书籍\n作者：测试作者\n\n第一章 开始\n这是第一章的内容。\n\n第二章 发展\n这是第二章的内容，有多段话。\n\n这里第二段。\n\n第三章 结局\n最后的内容。";
        var tempFile = Path.Combine(Path.GetTempPath(), $"epubra_import_{Guid.NewGuid():N}.txt");
        try
        {
            File.WriteAllText(tempFile, text, Encoding.UTF8);
            var book = new TxtImporter().Read(tempFile);

            book.Title.Should().Be("测试书籍");
            book.Author.Should().Be("测试作者");
            book.Chapters.Should().HaveCount(3);
            book.Chapters[0].Title.Should().Be("第一章");
            book.Chapters[1].Title.Should().Be("第二章");
            book.Chapters[2].Title.Should().Be("第三章");

            book.Chapters[0].XhtmlContent.Should().Contain("<h1>第一章</h1>");
            book.Chapters[0].XhtmlContent.Should().Contain("开始");
            book.Chapters[1].XhtmlContent.Should().Contain("<p>");
            book.Chapters[1].XhtmlContent.Should().Contain("<br/>");
            book.Chapters[2].XhtmlContent.Should().Contain("<h1>第三章</h1>");
        }
        finally
        {
            if (File.Exists(tempFile)) File.Delete(tempFile);
        }
    }

    [Fact]
    public void 导入_特殊章节标记()
    {
        var text = "前言\n前言内容。\n\n第一章 开始\n正文内容。\n\n后记\n后记内容。";
        var tempFile = Path.Combine(Path.GetTempPath(), $"epubra_special_{Guid.NewGuid():N}.txt");
        try
        {
            File.WriteAllText(tempFile, text, Encoding.UTF8);
            var book = new TxtImporter().Read(tempFile);

            book.Chapters.Should().HaveCount(3);
            book.Chapters[0].Title.Should().Be("前言");
            book.Chapters[1].Title.Should().Be("第一章");
            book.Chapters[2].Title.Should().Be("后记");
        }
        finally
        {
            if (File.Exists(tempFile)) File.Delete(tempFile);
        }
    }

    [Fact]
    public void 导入_无章节标记_按双换行拆分()
    {
        var text = "短文本\n\n这是没有章节标记的内容。\n\n包含多个段落。";
        var tempFile = Path.Combine(Path.GetTempPath(), $"epubra_nomark_{Guid.NewGuid():N}.txt");
        try
        {
            File.WriteAllText(tempFile, text, Encoding.UTF8);
            var book = new TxtImporter().Read(tempFile);

            book.Chapters.Should().HaveCount(2);
            book.Chapters[0].Title.Should().Be("章节 1");
            book.Chapters[1].Title.Should().Be("章节 2");
        }
        finally
        {
            if (File.Exists(tempFile)) File.Delete(tempFile);
        }
    }

    [Fact]
    public void 导入_GBK编码()
    {
        var text = "GBK测试\n\n第一章 编码\n中文内容测试。";
        var tempFile = Path.Combine(Path.GetTempPath(), $"epubra_gbk_{Guid.NewGuid():N}.txt");
        try
        {
            Encoding.RegisterProvider(CodePagesEncodingProvider.Instance);
            var gbk = Encoding.GetEncoding("GBK");
            File.WriteAllBytes(tempFile, gbk.GetBytes(text));

            var book = new TxtImporter().Read(tempFile);

            book.Title.Should().Be("GBK测试");
            book.Chapters.Should().HaveCount(1);
            book.Chapters[0].Title.Should().Be("第一章");
        }
        finally
        {
            if (File.Exists(tempFile)) File.Delete(tempFile);
        }
    }

    [Fact]
    public void 导入_空文件_生成单章节()
    {
        var tempFile = Path.Combine(Path.GetTempPath(), $"epubra_empty_{Guid.NewGuid():N}.txt");
        try
        {
            File.WriteAllText(tempFile, "", Encoding.UTF8);
            var book = new TxtImporter().Read(tempFile);

            book.Chapters.Should().HaveCount(1);
            book.Chapters[0].XhtmlContent.Should().Contain("<body>");
        }
        finally
        {
            if (File.Exists(tempFile)) File.Delete(tempFile);
        }
    }
}
