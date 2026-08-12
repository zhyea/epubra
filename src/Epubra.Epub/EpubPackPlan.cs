using Epubra.Core;

namespace Epubra.Epub;

/// <summary>
/// 打包前的"计划"：规范化资源路径、章节文件名、生成 manifest id 等。
/// 这是 EpubWriter 的输入预处理，把"领域模型"映射到"epub 文件结构"。
/// </summary>
internal sealed class EpubPackPlan
{
    private EpubPackPlan(
        Book book,
        IReadOnlyList<PlannedChapter> spine,
        IReadOnlyList<PlannedResource> resources,
        string? coverHref)
    {
        Book = book;
        Spine = spine;
        Resources = resources;
        CoverHref = coverHref;
    }

    public Book Book { get; }
    public IReadOnlyList<PlannedChapter> Spine { get; }
    public IReadOnlyList<PlannedResource> Resources { get; }

    /// <summary>封面图在 OEBPS 中的相对路径（href），无封面时为 null。</summary>
    public string? CoverHref { get; }

    /// <summary>构建计划：规范化章节文件名、推断资源子目录、检查冲突。</summary>
    public static EpubPackPlan Build(Book book)
    {
        ArgumentNullException.ThrowIfNull(book);

        var chapters = ChapterTreeWalker.WalkInDocumentOrder(book.Chapters).ToList();
        var usedNames = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var planned = new List<PlannedChapter>(chapters.Count);

        var index = 1;
        foreach (var c in chapters)
        {
            var baseName = string.IsNullOrWhiteSpace(c.FileName)
                ? (string.IsNullOrWhiteSpace(c.Title) ? $"chapter_{index:000}" : c.Title)
                : c.FileName;

            var safe = EpubSafeName.Sanitize(baseName);
            var candidate = safe;
            var suffix = index;
            while (usedNames.Contains(candidate))
            {
                suffix++;
                candidate = $"{safe}_{suffix}";
            }
            usedNames.Add(candidate);

            planned.Add(new PlannedChapter(
                ManifestId: $"chap_{index:000}",
                FileName: candidate,
                Title: string.IsNullOrWhiteSpace(c.Title) ? candidate : c.Title,
                Source: c));
            index++;
        }

        var resources = new List<PlannedResource>();
        var usedResourceHrefs = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        string? coverHref = null;

        foreach (var r in book.Resources)
        {
            var subDir = r.Kind switch
            {
                EpubResourceKind.Image => "images",
                EpubResourceKind.Audio => "audio",
                EpubResourceKind.Video => "video",
                EpubResourceKind.Font => "fonts",
                EpubResourceKind.StyleSheet => "styles",
                _ => "misc"
            };

            var fileName = string.IsNullOrWhiteSpace(r.FileName)
                ? $"{r.Id:N}{GetDefaultExt(r.Kind)}"
                : r.FileName;

            var href = $"{subDir}/{fileName}";

            // 去重：同名资源自动追加序号
            var uniqueHref = href;
            var dedup = 1;
            while (!usedResourceHrefs.Add(uniqueHref))
            {
                var nameNoExt = Path.GetFileNameWithoutExtension(fileName);
                var ext = Path.GetExtension(fileName);
                uniqueHref = $"{subDir}/{nameNoExt}_{dedup}{ext}";
                dedup++;
            }

            var manifestId = resources.Count == 0
                ? "res_001"
                : $"res_{resources.Count + 1:000}";

            var mime = string.IsNullOrWhiteSpace(r.MimeType) ? MimeTypeMap.Get(fileName) : r.MimeType;

            var isCover = book.CoverResourceId == r.Id || string.Equals(r.Role, "cover", StringComparison.OrdinalIgnoreCase);
            if (isCover)
            {
                coverHref = uniqueHref;
            }

            resources.Add(new PlannedResource(
                ManifestId: manifestId,
                Href: uniqueHref,
                MimeType: mime,
                Kind: r.Kind,
                IsCover: isCover,
                Source: r));
        }

        return new EpubPackPlan(book, planned, resources, coverHref);
    }

    private static string GetDefaultExt(EpubResourceKind kind) => kind switch
    {
        EpubResourceKind.Image => ".png",
        EpubResourceKind.Audio => ".mp3",
        EpubResourceKind.Video => ".mp4",
        EpubResourceKind.Font => ".ttf",
        EpubResourceKind.StyleSheet => ".css",
        _ => ".bin"
    };

    /// <summary>打包计划中的章节信息。</summary>
    internal sealed record PlannedChapter(
        string ManifestId,
        string FileName,
        string Title,
        Chapter Source);

    /// <summary>打包计划中的资源信息。</summary>
    internal sealed record PlannedResource(
        string ManifestId,
        string Href,
        string MimeType,
        EpubResourceKind Kind,
        bool IsCover,
        EpubResource Source);
}