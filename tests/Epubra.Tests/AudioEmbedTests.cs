using System.IO.Compression;
using System.Text;
using System.Xml.Linq;
using Epubra.Core;
using Epubra.Epub;
using Xunit;

namespace Epubra.Tests;

/// <summary>
/// 音频嵌入 EPUB 的单元测试：资源打包路径、OPF manifest 声明、EPUB 往返、MIME 映射。
/// 全部通过公共 API（EpubWriter / EpubReader）测试。
/// </summary>
public class AudioEmbedTests
{
    /// <summary>EPUB 打包后 ZIP 中应包含音频文件在 OEBPS/audio/ 目录下。</summary>
    [Fact]
    public async Task EpubWriter_AudioResource_PackagedInAudioDir()
    {
        var book = CreateBookWithAudio("narration.mp3", "audio/mpeg");
        var writer = new EpubWriter();

        using var ms = new MemoryStream();
        await writer.WriteAsync(book, ms);
        ms.Position = 0;

        using var archive = new ZipArchive(ms, ZipArchiveMode.Read, leaveOpen: true);
        var audioEntry = archive.Entries.FirstOrDefault(e => e.FullName.Contains("audio/narration.mp3"));

        Assert.NotNull(audioEntry);
        Assert.Equal("OEBPS/audio/narration.mp3", audioEntry!.FullName);
    }

    /// <summary>OPF manifest 中应包含音频资源的 media-type 声明。</summary>
    [Fact]
    public async Task EpubWriter_AudioResource_OpfManifestHasMimeType()
    {
        var book = CreateBookWithAudio("chapter1.mp3", "audio/mpeg");
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
        var audioItem = doc.Descendants(opfNs + "item")
            .FirstOrDefault(i => i.Attribute("media-type")?.Value == "audio/mpeg");

        Assert.NotNull(audioItem);
        Assert.Equal("audio/chapter1.mp3", audioItem!.Attribute("href")?.Value);
    }

    /// <summary>多个音频资源应各自有唯一的 href 路径。</summary>
    [Fact]
    public async Task EpubWriter_MultipleAudioResources_UniquePaths()
    {
        var book = new Book
        {
            Title = "多音频测试",
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
            Kind = EpubResourceKind.Audio,
            FileName = "a.mp3",
            MimeType = "audio/mpeg",
            Data = new byte[] { 0x01, 0x02, 0x03 }
        });
        book.Resources.Add(new EpubResource
        {
            Kind = EpubResourceKind.Audio,
            FileName = "b.mp3",
            MimeType = "audio/mpeg",
            Data = new byte[] { 0x04, 0x05, 0x06 }
        });

        var writer = new EpubWriter();
        using var ms = new MemoryStream();
        await writer.WriteAsync(book, ms);
        ms.Position = 0;

        using var archive = new ZipArchive(ms, ZipArchiveMode.Read, leaveOpen: true);
        var audioEntries = archive.Entries
            .Where(e => e.FullName.StartsWith("OEBPS/audio/", StringComparison.Ordinal))
            .Select(e => e.FullName)
            .OrderBy(n => n)
            .ToList();

        Assert.Equal(2, audioEntries.Count);
        Assert.Contains("OEBPS/audio/a.mp3", audioEntries);
        Assert.Contains("OEBPS/audio/b.mp3", audioEntries);
    }

    /// <summary>MimeTypeMap 应正确映射常见音频格式。</summary>
    [Theory]
    [InlineData("test.mp3", "audio/mpeg")]
    [InlineData("test.wav", "audio/wav")]
    [InlineData("test.m4a", "audio/mp4")]
    [InlineData("test.ogg", "audio/ogg")]
    [InlineData("test.aac", "audio/aac")]
    [InlineData("test.flac", "audio/flac")]
    public void MimeTypeMap_AudioExtensions_CorrectMapping(string fileName, string expectedMime)
    {
        var mime = MimeTypeMap.Get(fileName);
        Assert.Equal(expectedMime, mime);
    }

    /// <summary>EpubReader 应将音频资源识别为 Audio 类型。</summary>
    [Fact]
    public async Task EpubReader_AudioResource_ClassifiedAsAudio()
    {
        var book = CreateBookWithAudio("intro.mp3", "audio/mpeg");
        var writer = new EpubWriter();

        using var ms = new MemoryStream();
        await writer.WriteAsync(book, ms);
        ms.Position = 0;

        var reader = new EpubReader();
        var readBook = await reader.ReadFromStreamAsync(ms);

        var audioRes = readBook.Resources.FirstOrDefault(r => r.Kind == EpubResourceKind.Audio);
        Assert.NotNull(audioRes);
        Assert.Equal("audio/mpeg", audioRes!.MimeType);
        Assert.Equal("intro.mp3", audioRes.FileName);
    }

    /// <summary>EPUB 往返：音频资源数据在打包-读取后保持一致。</summary>
    [Fact]
    public async Task EpubRoundTrip_AudioData_Preserved()
    {
        var audioData = new byte[] { 0x49, 0x44, 0x33, 0x03, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00 };
        var book = new Book
        {
            Title = "往返测试",
            Author = "测试",
            Chapters =
            [
                new Chapter
                {
                    Title = "带音频的章节",
                    FileName = "chapter_001",
                    Order = 0,
                    XhtmlContent = "<?xml version=\"1.0\" encoding=\"utf-8\"?>\n<html xmlns=\"http://www.w3.org/1999/xhtml\"><head><title>带音频的章节</title></head><body><h1>带音频的章节</h1><audio src=\"audio/track.mp3\" controls=\"controls\"/></body></html>"
                }
            ]
        };
        book.Resources.Add(new EpubResource
        {
            Kind = EpubResourceKind.Audio,
            FileName = "track.mp3",
            MimeType = "audio/mpeg",
            Data = audioData
        });

        var writer = new EpubWriter();
        using var ms = new MemoryStream();
        await writer.WriteAsync(book, ms);
        ms.Position = 0;

        var reader = new EpubReader();
        var readBook = await reader.ReadFromStreamAsync(ms);

        var audioRes = readBook.Resources.FirstOrDefault(r => r.Kind == EpubResourceKind.Audio);
        Assert.NotNull(audioRes);
        Assert.Equal(audioData, audioRes!.Data);
    }

    /// <summary>不同格式音频资源应在往返后保持正确的 MIME 类型。</summary>
    [Theory]
    [InlineData("narration.mp3", "audio/mpeg")]
    [InlineData("voice.wav", "audio/wav")]
    [InlineData("intro.m4a", "audio/mp4")]
    public async Task EpubRoundTrip_VariousAudioFormats_MimePreserved(string fileName, string expectedMime)
    {
        var book = new Book
        {
            Title = "格式测试",
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
            Kind = EpubResourceKind.Audio,
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

        var audioRes = readBook.Resources.FirstOrDefault(r => r.Kind == EpubResourceKind.Audio);
        Assert.NotNull(audioRes);
        Assert.Equal(expectedMime, audioRes!.MimeType);
    }

    /// <summary>章节 XHTML 中的 audio 标签在 EPUB 往返后应保留。</summary>
    [Fact]
    public async Task EpubRoundTrip_AudioTagInChapter_Preserved()
    {
        var book = new Book
        {
            Title = "音频标签测试",
            Author = "测试",
            Chapters =
            [
                new Chapter
                {
                    Title = "第一章",
                    FileName = "chapter_001",
                    Order = 0,
                    XhtmlContent = "<?xml version=\"1.0\" encoding=\"utf-8\"?>\n<html xmlns=\"http://www.w3.org/1999/xhtml\"><head><title>第一章</title></head><body><h1>第一章</h1><p>正文内容</p><audio src=\"audio/bgm.mp3\" controls=\"controls\"/></body></html>"
                }
            ]
        };
        book.Resources.Add(new EpubResource
        {
            Kind = EpubResourceKind.Audio,
            FileName = "bgm.mp3",
            MimeType = "audio/mpeg",
            Data = new byte[] { 0xFF, 0xFB }
        });

        var writer = new EpubWriter();
        using var ms = new MemoryStream();
        await writer.WriteAsync(book, ms);
        ms.Position = 0;

        var reader = new EpubReader();
        var readBook = await reader.ReadFromStreamAsync(ms);

        var chapter = Assert.Single(readBook.Chapters);
        Assert.Contains("audio/bgm.mp3", chapter.XhtmlContent);
        Assert.Contains("<audio", chapter.XhtmlContent);
    }

    // ===== 辅助方法 =====

    private static Book CreateBookWithAudio(string audioFileName, string mimeType)
    {
        var book = new Book
        {
            Title = "音频嵌入测试",
            Author = "测试作者",
            Chapters =
            [
                new Chapter
                {
                    Title = "第一章",
                    FileName = "chapter_001",
                    Order = 0,
                    XhtmlContent = "<html><body><h1>第一章</h1><audio src=\"audio/" + audioFileName + "\" controls=\"controls\"/></body></html>"
                }
            ]
        };

        book.Resources.Add(new EpubResource
        {
            Kind = EpubResourceKind.Audio,
            FileName = audioFileName,
            MimeType = mimeType,
            Data = new byte[] { 0x01, 0x02, 0x03, 0x04, 0x05 }
        });

        return book;
    }
}
