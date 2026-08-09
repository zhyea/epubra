using System.Text.Json;
using System.Text.Json.Serialization;
using Epubra.Core;

namespace Epubra.Infrastructure;

/// <summary>
/// 项目文件的保存/加载服务。
/// .epubra 文件本质是 JSON，包含 Book 的完整状态（元数据 + 章节树 + 资源二进制）。
/// </summary>
public sealed class ProjectService
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
        Converters = { new JsonStringEnumConverter() }
    };

    /// <summary>保存项目到文件。</summary>
    public async Task SaveAsync(Book book, string filePath, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(book);
        ArgumentException.ThrowIfNullOrEmpty(filePath);

        var dir = Path.GetDirectoryName(filePath);
        if (!string.IsNullOrEmpty(dir) && !Directory.Exists(dir))
        {
            Directory.CreateDirectory(dir);
        }

        var dto = BookDto.FromBook(book);
        dto.ProjectPath = filePath;

        await using var fs = new FileStream(filePath, FileMode.Create, FileAccess.Write, FileShare.None, 8192, useAsync: true);
        await JsonSerializer.SerializeAsync(fs, dto, JsonOptions, ct).ConfigureAwait(false);

        book.ProjectPath = filePath;
    }

    /// <summary>从文件加载项目。</summary>
    public async Task<Book> LoadAsync(string filePath, CancellationToken ct = default)
    {
        ArgumentException.ThrowIfNullOrEmpty(filePath);

        await using var fs = new FileStream(filePath, FileMode.Open, FileAccess.Read, FileShare.Read, 8192, useAsync: true);
        var dto = await JsonSerializer.DeserializeAsync<BookDto>(fs, JsonOptions, ct).ConfigureAwait(false)
            ?? throw new InvalidDataException("项目文件内容无效或为空。");

        var book = dto.ToBook();
        book.ProjectPath = filePath;
        return book;
    }

    // ===== DTO =====

    private sealed class BookDto
    {
        public Guid Id { get; set; }
        public string Title { get; set; } = string.Empty;
        public string Author { get; set; } = string.Empty;
        public string Language { get; set; } = "zh-CN";
        public string? Publisher { get; set; }
        public string? Description { get; set; }
        public string? Subject { get; set; }
        public string? Identifier { get; set; }
        public Guid? CoverResourceId { get; set; }
        public List<ChapterDto> Chapters { get; set; } = new();
        public List<ResourceDto> Resources { get; set; } = new();
        public string? ProjectPath { get; set; }
        public DateTimeOffset CreatedAt { get; set; }
        public DateTimeOffset ModifiedAt { get; set; }
        public int FileVersion { get; set; } = 1;

        public static BookDto FromBook(Book book)
        {
            return new BookDto
            {
                Id = book.Id,
                Title = book.Title,
                Author = book.Author,
                Language = book.Language,
                Publisher = book.Publisher,
                Description = book.Description,
                Subject = book.Subject,
                Identifier = book.Identifier,
                CoverResourceId = book.CoverResourceId,
                Chapters = book.Chapters.Select(ChapterDto.FromChapter).ToList(),
                Resources = book.Resources.Select(ResourceDto.FromResource).ToList(),
                ProjectPath = book.ProjectPath,
                CreatedAt = book.CreatedAt,
                ModifiedAt = DateTimeOffset.UtcNow,
            };
        }

        public Book ToBook()
        {
            return new Book
            {
                Id = Id,
                Title = Title,
                Author = Author,
                Language = string.IsNullOrWhiteSpace(Language) ? "zh-CN" : Language,
                Publisher = Publisher,
                Description = Description,
                Subject = Subject,
                Identifier = Identifier,
                CoverResourceId = CoverResourceId,
                Chapters = Chapters.Select(c => c.ToChapter()).ToList(),
                Resources = Resources.Select(r => r.ToResource()).ToList(),
                CreatedAt = CreatedAt,
                ModifiedAt = ModifiedAt,
            };
        }
    }

    private sealed class ChapterDto
    {
        public Guid Id { get; set; }
        public string Title { get; set; } = string.Empty;
        public string XhtmlContent { get; set; } = string.Empty;
        public Guid? ParentId { get; set; }
        public int Order { get; set; }
        public string FileName { get; set; } = string.Empty;
        public string? SectionLabel { get; set; }

        public static ChapterDto FromChapter(Chapter c) => new()
        {
            Id = c.Id,
            Title = c.Title,
            XhtmlContent = c.XhtmlContent,
            ParentId = c.ParentId,
            Order = c.Order,
            FileName = c.FileName,
            SectionLabel = c.SectionLabel,
        };

        public Chapter ToChapter() => new()
        {
            Id = Id,
            Title = Title,
            XhtmlContent = XhtmlContent,
            ParentId = ParentId,
            Order = Order,
            FileName = FileName,
            SectionLabel = SectionLabel,
        };
    }

    private sealed class ResourceDto
    {
        public Guid Id { get; set; }
        public EpubResourceKind Kind { get; set; }
        public string FileName { get; set; } = string.Empty;
        public string OebpsPath { get; set; } = string.Empty;
        public string MimeType { get; set; } = string.Empty;
        public string? Role { get; set; }
        public string? DataBase64 { get; set; }

        public static ResourceDto FromResource(EpubResource r) => new()
        {
            Id = r.Id,
            Kind = r.Kind,
            FileName = r.FileName,
            OebpsPath = r.OebpsPath,
            MimeType = r.MimeType,
            Role = r.Role,
            DataBase64 = r.Data.Length > 0 ? Convert.ToBase64String(r.Data) : null,
        };

        public EpubResource ToResource() => new()
        {
            Id = Id,
            Kind = Kind,
            FileName = FileName,
            OebpsPath = OebpsPath,
            MimeType = MimeType,
            Role = Role,
            Data = !string.IsNullOrEmpty(DataBase64) ? Convert.FromBase64String(DataBase64) : Array.Empty<byte>(),
        };
    }
}
