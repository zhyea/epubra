using System.IO;
using System.Text.Json;
using Epubra.Core;
using Epubra.Infrastructure;
using FluentAssertions;
using Xunit;

namespace Epubra.Tests;

/// <summary>
/// 项目保存/加载的往返测试：保存 Book → 加载 → 验证字段一致。
/// </summary>
public class ProjectServiceTests
{
    private readonly ProjectService _service = new();

    [Fact]
    public async Task 保存并加载_元数据和章节完整往返()
    {
        // Arrange
        var book = new Book
        {
            Title = "测试书籍",
            Author = "测试作者",
            Language = "zh-CN",
            Publisher = "测试出版社",
        };

        var ch1 = new Chapter { Title = "第一章", Order = 0, ParentId = null, XhtmlContent = "<p>内容一</p>" };
        var ch2 = new Chapter { Title = "子章", Order = 0, ParentId = ch1.Id, XhtmlContent = "<p>子内容</p>" };
        var ch3 = new Chapter { Title = "第二章", Order = 1, ParentId = null, XhtmlContent = "<p>内容二</p>" };
        book.Chapters.AddRange(new[] { ch1, ch2, ch3 });

        var tempFile = Path.Combine(Path.GetTempPath(), $"epubra_test_{Guid.NewGuid():N}.epubra");

        try
        {
            // Act
            await _service.SaveAsync(book, tempFile);
            var loaded = await _service.LoadAsync(tempFile);

            // Assert
            loaded.Title.Should().Be("测试书籍");
            loaded.Author.Should().Be("测试作者");
            loaded.Language.Should().Be("zh-CN");
            loaded.Publisher.Should().Be("测试出版社");
            loaded.Chapters.Should().HaveCount(3);

            // 章节顺序和标题
            loaded.Chapters[0].Title.Should().Be("第一章");
            loaded.Chapters[1].Title.Should().Be("子章");
            loaded.Chapters[1].ParentId.Should().Be(loaded.Chapters[0].Id);
            loaded.Chapters[2].Title.Should().Be("第二章");

            // XHTML 内容保留
            loaded.Chapters[0].XhtmlContent.Should().Contain("内容一");
            loaded.Chapters[2].XhtmlContent.Should().Contain("内容二");
        }
        finally
        {
            if (File.Exists(tempFile)) File.Delete(tempFile);
        }
    }

    [Fact]
    public async Task 保存并加载_图片资源二进制完整往返()
    {
        // Arrange
        var imageData = new byte[] { 0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A, 0xFF, 0xFE };
        var book = new Book
        {
            Title = "带图的书",
            Author = "",
        };
        book.Resources.Add(new EpubResource
        {
            Kind = EpubResourceKind.Image,
            FileName = "cover.png",
            MimeType = "image/png",
            Data = imageData
        });
        book.CoverResourceId = book.Resources[0].Id;

        var tempFile = Path.Combine(Path.GetTempPath(), $"epubra_test_{Guid.NewGuid():N}.epubra");

        try
        {
            // Act
            await _service.SaveAsync(book, tempFile);
            var loaded = await _service.LoadAsync(tempFile);

            // Assert
            loaded.Resources.Should().HaveCount(1);
            loaded.Resources[0].FileName.Should().Be("cover.png");
            loaded.Resources[0].MimeType.Should().Be("image/png");
            loaded.Resources[0].Kind.Should().Be(EpubResourceKind.Image);
            loaded.Resources[0].Data.Should().Equal(imageData);
            loaded.CoverResourceId.Should().Be(loaded.Resources[0].Id);
        }
        finally
        {
            if (File.Exists(tempFile)) File.Delete(tempFile);
        }
    }

    [Fact]
    public async Task 保存并加载_字体资源往返()
    {
        // Arrange
        var fontData = new byte[256];
        new Random(42).NextBytes(fontData);

        var book = new Book { Title = "带字体的书" };
        book.Resources.Add(new EpubResource
        {
            Kind = EpubResourceKind.Font,
            FileName = "SourceHanSans.ttf",
            MimeType = "font/ttf",
            Data = fontData,
            Role = "font"
        });

        var tempFile = Path.Combine(Path.GetTempPath(), $"epubra_test_{Guid.NewGuid():N}.epubra");

        try
        {
            await _service.SaveAsync(book, tempFile);
            var loaded = await _service.LoadAsync(tempFile);

            loaded.Resources[0].Kind.Should().Be(EpubResourceKind.Font);
            loaded.Resources[0].Data.Should().Equal(fontData);
            loaded.Resources[0].Role.Should().Be("font");
        }
        finally
        {
            if (File.Exists(tempFile)) File.Delete(tempFile);
        }
    }

    [Fact]
    public async Task 加载无效文件_应抛异常()
    {
        var tempFile = Path.Combine(Path.GetTempPath(), $"epubra_test_{Guid.NewGuid():N}.epubra");
        await File.WriteAllTextAsync(tempFile, "not valid json {{{");

        try
        {
            var act = async () => await _service.LoadAsync(tempFile);
            await act.Should().ThrowAsync<Exception>();
        }
        finally
        {
            if (File.Exists(tempFile)) File.Delete(tempFile);
        }
    }
}
