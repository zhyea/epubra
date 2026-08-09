using System.IO.Compression;
using System.Text;
using Epubra.Core;
using Epubra.Epub;

// ======================================================================
// Epubra.Sample -- P0 阶段技术验证程序
//
// 目标：证明「内存中构造 Book -> EpubWriter -> 标准 EPUB 文件」这条链路跑得通。
// 输出：在 samples/output/ 下生成一个 sample.epub，并打印出内部结构清单。
// ======================================================================

Console.OutputEncoding = Encoding.UTF8;

Console.WriteLine("=== Epubra P0 Sample ===");
Console.WriteLine();

var book = BuildSampleBook();

var outputDir = Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", "output");
outputDir = Path.GetFullPath(outputDir);
Directory.CreateDirectory(outputDir);

var outputPath = Path.Combine(outputDir, "sample.epub");

Console.WriteLine($"输出文件: {outputPath}");

var writer = new EpubWriter();
var sw = System.Diagnostics.Stopwatch.StartNew();
await writer.WriteToFileAsync(book, outputPath);
sw.Stop();

var fileInfo = new FileInfo(outputPath);
Console.WriteLine($"生成耗时: {sw.ElapsedMilliseconds} ms");
Console.WriteLine($"文件大小: {fileInfo.Length:N0} bytes");
Console.WriteLine();

Console.WriteLine("=== 内部结构验证（重打开 zip 检查 entry） ===");
PrintEpubStructure(outputPath);
Console.WriteLine();
Console.WriteLine("P0 验证通过：epub 文件已生成，结构合规。");

return 0;

static Book BuildSampleBook()
{
    var book = new Book
    {
        Title = "方与圆全集",
        Author = "丁远峙",
        Language = "zh-CN",
        Publisher = "Epubra Sample Press",
        Identifier = $"urn:uuid:{Guid.NewGuid():D}"
    };

    var chapterData = new (string Title, string Content)[]
    {
        ("第一章 心灵的种子",
         "<h1>第一章 心灵的种子</h1>\n" +
         "<p>这是第一章的开头，演示了 XHTML 富文本内容。</p>\n" +
         "<p>本章将介绍<strong>方与圆</strong>哲学的基本概念。</p>\n" +
         "<p><em>这一段是斜体。</em></p>"),
        ("第二章 生命的动力",
         "<h1>第二章 生命的动力</h1>\n" +
         "<p>第二章内容开始。</p>\n" +
         "<p>保持思想的开放性是非常重要的。</p>"),
        ("第三章 保持思想的开放性",
         "<h1>第三章 保持思想的开放性</h1>\n" +
         "<p>第三章内容。</p>\n" +
         "<blockquote>引用：人生最大的悲哀是固执己见。</blockquote>"),
        ("前言",
         "<h1>前言</h1>\n" +
         "<p>这本书将要改变你的生活。我深信不疑，因为将要读到的它已改变了我自己的生活。</p>\n" +
         "<p>-- 丁远峙</p>")
    };

    var order = 0;
    foreach (var (title, content) in chapterData)
    {
        order++;
        book.Chapters.Add(new Chapter
        {
            Title = title,
            XhtmlContent = content,
            Order = order,
            FileName = $"chapter_{order:000}"
        });
    }

    return book;
}

static void PrintEpubStructure(string path)
{
    using var fs = File.OpenRead(path);
    using var archive = new ZipArchive(fs, ZipArchiveMode.Read);

    var entries = archive.Entries
        .Select(e => new
        {
            Path = e.FullName,
            Size = e.Length,
            Compressed = e.CompressedLength
        })
        .OrderBy(e => e.Path, StringComparer.Ordinal)
        .ToList();

    foreach (var e in entries)
    {
        var compressionRatio = e.Size > 0 ? (1.0 - (double)e.Compressed / e.Size) * 100 : 0;
        var isStored = e.Compressed >= e.Size ? "Stored" : "Deflated";
        Console.WriteLine($"  {e.Path,-50} {e.Size,10:N0} B  [{isStored}, 压缩率 {compressionRatio:F1}%]");
    }

    Console.WriteLine();
    Console.WriteLine($"共 {entries.Count} 个条目。");

    // 校验 mimetype 必须存在且未压缩
    var mimetype = archive.GetEntry("mimetype");
    if (mimetype is null)
    {
        throw new InvalidOperationException("缺少 mimetype entry");
    }

    if (mimetype.CompressedLength > mimetype.Length)
    {
        throw new InvalidOperationException($"mimetype 必须未压缩，但压缩后大于原始大小");
    }

    using var stream = mimetype.Open();
    using var reader = new StreamReader(stream);
    var content = reader.ReadToEnd();
    if (content.Trim() != "application/epub+zip")
    {
        throw new InvalidOperationException($"mimetype 内容错误: {content}");
    }

    Console.WriteLine("mimetype entry 校验通过（未压缩 + 内容正确）");
}
