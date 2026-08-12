using System.IO.Compression;
using System.Text;
using System.Xml.Linq;
using Epubra.Core;
using Epubra.Epub;
using Xunit;

namespace Epubra.Tests;

/// <summary>
/// 视频嵌入 EPUB 的单元测试：资源打包路径、OPF manifest 声明、EPUB 往返、MIME 映射。
/// 与音频测试对齐，验证 EpubResourceKind.Video 的真实打包链路。
/// </summary>
public class VideoEmbedTests
{
    /// <summary>EPUB 打包后 ZIP 中应包含视频文件在 OEBPS/video/ 目录下。</summary>
    [Fact]
    public async Task EpubWriter_VideoResource_PackagedInVideoDir()
    {
        var book = CreateBookWithVideo("clip.mp4", "video/mp4");
        var writer = new EpubWriter();

        using var ms = new MemoryStream();
        await writer.WriteAsync(book, ms);
        ms.Position = 0;

        using var archive = new ZipArchive(ms, ZipArchiveMode.Read, leaveOpen: true);
        var videoEntry = archive.Entries.FirstOrDefault(e => e.FullName.Contains("video/clip.mp4"));

        Assert.NotNull(videoEntry);
        Assert.Equal("OEBPS/video/clip.mp4", videoEntry!.FullName);
    }

    /// <summary>OPF manifest 中应包含视频资源的 media-type 声明。</summary>
    [Fact]
    public async Task EpubWriter_VideoResource_OpfManifestHasMimeType()
    {
        var book = CreateBookWithVideo("chapter1.mp4", "video/mp4");
        var writer = new EpubWriter();

        using var ms = new MemoryStream();
        await writer.WriteAsync(book, ms);
        ms.Position = 0;

        using var archive = new ZipArchive(ms, ZipArchiveMode.Read, leaveOpen: true);
        var opfEntry = archive.GetEntry("OEBPS/content.opf");
        Assert.NotNull(opfEntry);

        string opfContent;
        using (var reader = new StreamReader(opfEntry!.Open(), Encoding.UTF8))
        {
            opfContent = await reader.ReadToEndAsync();
        }

        var doc = XDocument.Parse(opfContent);
        var opfNs = XNamespace.Get("http://www.idpf.org/2007/opf");
        var videoItem = doc.Descendants(opfNs + "item")
            .FirstOrDefault(i => i.Attribute("media-type")?.Value == "video/mp4");

        Assert.NotNull(videoItem);
        Assert.Equal("video/chapter1.mp4", videoItem!.Attribute("href")?.Value);
    }

    /// <summary>EpubReader 应将视频资源识别为 Video 类型。</summary>
    [Fact]
    public async Task EpubReader_VideoResource_ClassifiedAsVideo()
    {
        var book = CreateBookWithVideo("intro.mp4", "video/mp4");
        var writer = new EpubWriter();

        using var ms = new MemoryStream();
        await writer.WriteAsync(book, ms);
        ms.Position = 0;

        var reader = new EpubReader();
        var readBook = await reader.ReadFromStreamAsync(ms);

        var videoRes = readBook.Resources.FirstOrDefault(r => r.Kind == EpubResourceKind.Video);
        Assert.NotNull(videoRes);
        Assert.Equal("video/mp4", videoRes!.MimeType);
        Assert.Equal("intro.mp4", videoRes.FileName);
    }

    /// <summary>EPUB 往返：视频资源数据在打包-读取后保持一致。</summary>
    [Fact]
    public async Task EpubRoundTrip_VideoData_Preserved()
    {
        var videoData = new byte[] { 0x00, 0x00, 0x00, 0x18, 0x66, 0x74, 0x79, 0x70, 0x6D, 0x70 };
        var book = new Book
        {
            Title = "视频往返测试",
            Author = "测试",
            Chapters =
            [
                new Chapter
                {
                    Title = "带视频的章节",
                    FileName = "chapter_001",
                    Order = 0,
                    XhtmlContent = "<?xml version=\"1.0\" encoding=\"utf-8\"?>\n<html xmlns=\"http://www.w3.org/1999/xhtml\"><head><title>带视频的章节</title></head><body><h1>带视频的章节</h1><video src=\"video/clip.mp4\" controls=\"controls\"/></body></html>"
                }
            ]
        };
        book.Resources.Add(new EpubResource
        {
            Kind = EpubResourceKind.Video,
            FileName = "clip.mp4",
            MimeType = "video/mp4",
            Data = videoData
        });

        var writer = new EpubWriter();
        using var ms = new MemoryStream();
        await writer.WriteAsync(book, ms);
        ms.Position = 0;

        var reader = new EpubReader();
        var readBook = await reader.ReadFromStreamAsync(ms);

        var videoRes = readBook.Resources.FirstOrDefault(r => r.Kind == EpubResourceKind.Video);
        Assert.NotNull(videoRes);
        Assert.Equal(videoData, videoRes!.Data);
    }

    /// <summary>不同格式视频资源应在往返后保持正确的 MIME 类型。</summary>
    [Theory]
    [InlineData("clip.mp4", "video/mp4")]
    [InlineData("scene.webm", "video/webm")]
    [InlineData("intro.ogv", "video/ogg")]
    [InlineData("movie.mov", "video/quicktime")]
    public async Task EpubRoundTrip_VariousVideoFormats_MimePreserved(string fileName, string expectedMime)
    {
        var book = new Book
        {
            Title = "视频格式测试",
            Author = "测试",
            Chapters =
            [
                new Chapter
                {
                    Title = "第一章",
                    FileName = "chapter_001",
                    Order = 0,
                    XhtmlContent = "<html><body><h1>第一章</h1></body></html>"
                }
            ]
        };
        book.Resources.Add(new EpubResource
        {
            Kind = EpubResourceKind.Video,
            FileName = fileName,
            MimeType = expectedMime,
            Data = new byte[] { 0x00 }
        });

        var writer = new EpubWriter();
        using var ms = new MemoryStream();
        await writer.WriteAsync(book, ms);
        ms.Position = 0;

        var reader = new EpubReader();
        var readBook = await reader.ReadFromStreamAsync(ms);

        var videoRes = readBook.Resources.FirstOrDefault(r => r.Kind == EpubResourceKind.Video);
        Assert.NotNull(videoRes);
        Assert.Equal(expectedMime, videoRes!.MimeType);
    }

    /// <summary>章节 XHTML 中的 video 标签在 EPUB 往返后应保留。</summary>
    [Fact]
    public async Task EpubRoundTrip_VideoTagInChapter_Preserved()
    {
        var book = new Book
        {
            Title = "视频标签测试",
            Author = "测试",
            Chapters =
            [
                new Chapter
                {
                    Title = "第一章",
                    FileName = "chapter_001",
                    Order = 0,
                    XhtmlContent = "<?xml version=\"1.0\" encoding=\"utf-8\"?>\n<html xmlns=\"http://www.w3.org/1999/xhtml\"><head><title>第一章</title></head><body><h1>第一章</h1><p>正文内容</p><video src=\"video/bgm.mp4\" controls=\"controls\"/></body></html>"
                }
            ]
        };
        book.Resources.Add(new EpubResource
        {
            Kind = EpubResourceKind.Video,
            FileName = "bgm.mp4",
            MimeType = "video/mp4",
            Data = new byte[] { 0x00, 0x01 }
        });

        var writer = new EpubWriter();
        using var ms = new MemoryStream();
        await writer.WriteAsync(book, ms);
        ms.Position = 0;

        var reader = new EpubReader();
        var readBook = await reader.ReadFromStreamAsync(ms);

        var chapter = Assert.Single(readBook.Chapters);
        Assert.Contains("video/bgm.mp4", chapter.XhtmlContent);
        Assert.Contains("<video", chapter.XhtmlContent);
    }

    // ===== 辅助方法 =====

    private static Book CreateBookWithVideo(string videoFileName, string mimeType)
    {
        var book = new Book
        {
            Title = "视频嵌入测试",
            Author = "测试作者",
            Chapters =
            [
                new Chapter
                {
                    Title = "第一章",
                    FileName = "chapter_001",
                    Order = 0,
                    XhtmlContent = "<html><body><h1>第一章</h1><video src=\"video/" + videoFileName + "\" controls=\"controls\"/></body></html>"
                }
            ]
        };

        book.Resources.Add(new EpubResource
        {
            Kind = EpubResourceKind.Video,
            FileName = videoFileName,
            MimeType = mimeType,
            Data = new byte[] { 0x01, 0x02, 0x03, 0x04, 0x05 }
        });

        return book;
    }
}
