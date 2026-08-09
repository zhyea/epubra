using System.Collections.ObjectModel;
using System.IO;
using System.Windows;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Epubra.Core;
using Epubra.Epub;
using Epubra.Editor;
using Epubra.Export;
using Epubra.Infrastructure;
using Microsoft.Win32;

namespace Epubra.App.ViewModels;

/// <summary>
/// 主窗口 ViewModel：管理 Book 项目、章节树 CRUD、EPUB 导出、项目保存/加载。
/// </summary>
public partial class MainViewModel : ObservableObject
{
    /// <summary>底层 Book 领域模型。</summary>
    public Book Book { get; private set; }

    /// <summary>章节树根节点集合。</summary>
    public ObservableCollection<ChapterNode> Chapters { get; } = new();

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(DeleteChapterCommand))]
    [NotifyCanExecuteChangedFor(nameof(AddSubChapterCommand))]
    [NotifyCanExecuteChangedFor(nameof(MoveUpCommand))]
    [NotifyCanExecuteChangedFor(nameof(MoveDownCommand))]
    [NotifyCanExecuteChangedFor(nameof(IndentCommand))]
    [NotifyCanExecuteChangedFor(nameof(OutdentCommand))]
    [NotifyCanExecuteChangedFor(nameof(RenameChapterCommand))]
    [NotifyCanExecuteChangedFor(nameof(AutoSplitChapterCommand))]
    [NotifyCanExecuteChangedFor(nameof(AutoFormatChapterCommand))]
    private ChapterNode? _selectedChapter;

    [ObservableProperty]
    private string _bookTitle = "未命名书籍";

    [ObservableProperty]
    private string _bookAuthor = string.Empty;

    [ObservableProperty]
    private string _bookPublisher = string.Empty;

    [ObservableProperty]
    private string _bookIsbn = string.Empty;

    [ObservableProperty]
    private string _bookDescription = string.Empty;

    [ObservableProperty]
    private string _bookSubject = string.Empty;

    [ObservableProperty]
    private string _bookLanguage = "zh-CN";

    [ObservableProperty]
    private string _statusMessage = "就绪";

    [ObservableProperty]
    private int _chapterCount;

    [ObservableProperty]
    private int _wordCount;

    [ObservableProperty]
    private int _charCount;

    /// <summary>是否处于预览模式（true=WebView2 预览，false=RichTextBox 编辑）。</summary>
    [ObservableProperty]
    private bool _isPreviewMode;

    /// <summary>当前项目文件路径（null 表示未保存过）。</summary>
    [ObservableProperty]
    private string? _projectFilePath;

    /// <summary>窗口标题。</summary>
    [ObservableProperty]
    private string _windowTitle = "Epubra - EPUB 编辑器";

    private readonly ProjectService _projectService = new();

    /// <summary>
    /// 保存当前编辑器内容到 SelectedChapter 的回调。
    /// 由 MainWindow 在切换章节或预览时调用。
    /// </summary>
    public Action<ChapterNode>? SaveEditorContent { get; set; }

    /// <summary>
    /// 加载章节内容到编辑器的回调。
    /// 由 MainWindow 在选中章节变化时调用。
    /// </summary>
    public Action<ChapterNode?>? LoadEditorContent { get; set; }

    /// <summary>
    /// 切换到预览模式时，由 View 获取当前章节的渲染 HTML。
    /// </summary>
    public Func<ChapterNode?, string>? GetPreviewHtml { get; set; }

    /// <summary>
    /// 预览模式切换时，由 View 执行 WebView2 导航。
    /// </summary>
    public Action<string>? NavigatePreview { get; set; }

    /// <summary>
    /// 撤销回调（由 View 绑定到 RichTextBox.Undo）。
    /// </summary>
    public Action? UndoAction { get; set; }

    /// <summary>
    /// 重做回调（由 View 绑定到 RichTextBox.Redo）。
    /// </summary>
    public Action? RedoAction { get; set; }

    /// <summary>
    /// 切换查找替换栏可见性的回调。
    /// </summary>
    public Action? ToggleFindReplaceAction { get; set; }

    /// <summary>
    /// 一键排版回调（由 View 绑定到 FlowDocument 格式化逻辑）。
    /// </summary>
    public Action? ApplyFormatAction { get; set; }

    public MainViewModel()
    {
        Book = new Book { Title = "未命名书籍", Author = "" };
        BookTitle = Book.Title;
        BookAuthor = Book.Author;
        BookPublisher = Book.Publisher ?? "";
        BookIsbn = Book.Identifier ?? "";
        BookDescription = Book.Description ?? "";
        BookSubject = Book.Subject ?? "";
        BookLanguage = Book.Language;
    }

    // ===== 元数据同步 =====

    partial void OnBookTitleChanged(string value)
    {
        Book.Title = value;
        Book.ModifiedAt = DateTimeOffset.UtcNow;
        UpdateWindowTitle();
    }

    partial void OnBookAuthorChanged(string value)
    {
        Book.Author = value;
        Book.ModifiedAt = DateTimeOffset.UtcNow;
    }

    partial void OnBookPublisherChanged(string value)
    {
        Book.Publisher = value;
        Book.ModifiedAt = DateTimeOffset.UtcNow;
    }

    partial void OnBookIsbnChanged(string value)
    {
        Book.Identifier = value;
        Book.ModifiedAt = DateTimeOffset.UtcNow;
    }

    partial void OnBookDescriptionChanged(string value)
    {
        Book.Description = value;
        Book.ModifiedAt = DateTimeOffset.UtcNow;
    }

    partial void OnBookSubjectChanged(string value)
    {
        Book.Subject = value;
        Book.ModifiedAt = DateTimeOffset.UtcNow;
    }

    partial void OnBookLanguageChanged(string value)
    {
        Book.Language = value;
        Book.ModifiedAt = DateTimeOffset.UtcNow;
    }

    partial void OnProjectFilePathChanged(string? value)
    {
        UpdateWindowTitle();
    }

    private void UpdateWindowTitle()
    {
        var name = !string.IsNullOrEmpty(ProjectFilePath)
            ? Path.GetFileNameWithoutExtension(ProjectFilePath)
            : BookTitle;
        WindowTitle = $"Epubra - {name}";
    }

    // ===== 章节树 CRUD =====

    /// <summary>添加顶级章节。</summary>
    [RelayCommand]
    public void AddChapter()
    {
        var chapter = new Chapter
        {
            Title = $"新章节 {Chapters.Count + 1}",
            Order = Chapters.Count,
            ParentId = null
        };
        Book.Chapters.Add(chapter);
        var node = new ChapterNode(chapter);
        Chapters.Add(node);
        SelectedChapter = node;
        UpdateChapterCount();
        StatusMessage = $"已添加章节：{chapter.Title}";
    }

    /// <summary>添加子章节（在选中章节下）。</summary>
    [RelayCommand(CanExecute = nameof(CanAddSubChapter))]
    public void AddSubChapter()
    {
        if (SelectedChapter is null) return;

        var chapter = new Chapter
        {
            Title = $"子章节 {SelectedChapter.Children.Count + 1}",
            Order = SelectedChapter.Children.Count,
            ParentId = SelectedChapter.Chapter.Id
        };
        Book.Chapters.Add(chapter);
        var node = new ChapterNode(chapter, SelectedChapter);
        SelectedChapter.Children.Add(node);
        SelectedChapter.IsExpanded = true;
        SelectedChapter = node;
        UpdateChapterCount();
        StatusMessage = $"已添加子章节：{chapter.Title}";
    }

    private bool CanAddSubChapter() => SelectedChapter is not null;

    /// <summary>删除选中章节（含子章节）。</summary>
    [RelayCommand(CanExecute = nameof(CanDeleteChapter))]
    public void DeleteChapter()
    {
        if (SelectedChapter is null) return;

        // 收集要删除的所有节点（含后代）
        var toRemove = SelectedChapter.DescendAndSelf().ToList();
        var chapterIds = toRemove.Select(n => n.Chapter.Id).ToHashSet();

        // 从 Book.Chapters 中移除
        Book.Chapters.RemoveAll(c => chapterIds.Contains(c.Id));

        // 从树中移除
        if (SelectedChapter.Parent is { } parent)
        {
            parent.Children.Remove(SelectedChapter);
        }
        else
        {
            Chapters.Remove(SelectedChapter);
        }

        // 重新编号
        RenumberChapters();

        SelectedChapter = null;
        UpdateChapterCount();
        StatusMessage = $"已删除 {toRemove.Count} 个章节";
    }

    private bool CanDeleteChapter() => SelectedChapter is not null;

    /// <summary>重命名选中章节。</summary>
    [RelayCommand(CanExecute = nameof(CanRenameChapter))]
    public void RenameChapter()
    {
        if (SelectedChapter is not null)
        {
            StatusMessage = $"正在重命名：{SelectedChapter.Title}";
        }
    }

    private bool CanRenameChapter() => SelectedChapter is not null;

    /// <summary>上移（同级内）。</summary>
    [RelayCommand(CanExecute = nameof(CanMoveUp))]
    public void MoveUp()
    {
        if (SelectedChapter is null) return;
        var siblings = SelectedChapter.Parent?.Children ?? Chapters;
        var idx = siblings.IndexOf(SelectedChapter);
        if (idx <= 0) return;

        siblings.Move(idx, idx - 1);
        RenumberChapters();
        StatusMessage = "已上移";
    }

    private bool CanMoveUp()
    {
        if (SelectedChapter is null) return false;
        var siblings = SelectedChapter.Parent?.Children ?? Chapters;
        return siblings.IndexOf(SelectedChapter) > 0;
    }

    /// <summary>下移（同级内）。</summary>
    [RelayCommand(CanExecute = nameof(CanMoveDown))]
    public void MoveDown()
    {
        if (SelectedChapter is null) return;
        var siblings = SelectedChapter.Parent?.Children ?? Chapters;
        var idx = siblings.IndexOf(SelectedChapter);
        if (idx < 0 || idx >= siblings.Count - 1) return;

        siblings.Move(idx, idx + 1);
        RenumberChapters();
        StatusMessage = "已下移";
    }

    private bool CanMoveDown()
    {
        if (SelectedChapter is null) return false;
        var siblings = SelectedChapter.Parent?.Children ?? Chapters;
        var idx = siblings.IndexOf(SelectedChapter);
        return idx >= 0 && idx < siblings.Count - 1;
    }

    /// <summary>缩进（变为上一个同级章节的子章节）。</summary>
    [RelayCommand(CanExecute = nameof(CanIndent))]
    public void Indent()
    {
        if (SelectedChapter is null) return;
        var siblings = SelectedChapter.Parent?.Children ?? Chapters;
        var idx = siblings.IndexOf(SelectedChapter);
        if (idx <= 0) return;

        var newParent = siblings[idx - 1];
        siblings.RemoveAt(idx);

        SelectedChapter.Parent = newParent;
        SelectedChapter.Chapter.ParentId = newParent.Chapter.Id;
        newParent.Children.Add(SelectedChapter);
        newParent.IsExpanded = true;

        RenumberChapters();
        StatusMessage = $"已缩进到：{newParent.Title}";
    }

    private bool CanIndent()
    {
        if (SelectedChapter is null) return false;
        var siblings = SelectedChapter.Parent?.Children ?? Chapters;
        return siblings.IndexOf(SelectedChapter) > 0;
    }

    /// <summary>取消缩进（变为父章节的同级下一个）。</summary>
    [RelayCommand(CanExecute = nameof(CanOutdent))]
    public void Outdent()
    {
        if (SelectedChapter is null || SelectedChapter.Parent is not { } parent) return;

        var grandparent = parent.Parent;
        var parentSiblings = grandparent?.Children ?? Chapters;
        var parentIdx = parentSiblings.IndexOf(parent);

        parent.Children.Remove(SelectedChapter);
        SelectedChapter.Parent = grandparent;
        SelectedChapter.Chapter.ParentId = grandparent?.Chapter.Id;

        // 插入到父节点后面
        parentSiblings.Insert(parentIdx + 1, SelectedChapter);

        RenumberChapters();
        StatusMessage = "已取消缩进";
    }

    private bool CanOutdent() => SelectedChapter?.Parent is not null;

    /// <summary>
    /// 自动识别选中章节内容中的章节标记（第X章/前言/Chapter N 等），
    /// 将该章节拆分为多个同级新章节。
    /// </summary>
    [RelayCommand(CanExecute = nameof(CanAutoSplitChapter))]
    public void AutoSplitChapter()
    {
        if (SelectedChapter is null) return;

        // 先保存编辑器内容
        SaveEditorContent?.Invoke(SelectedChapter);

        var splitter = new ChapterSplitter();
        var segments = splitter.SplitFromXhtml(SelectedChapter.Chapter.XhtmlContent);

        if (segments.Count <= 1)
        {
            StatusMessage = "未识别到多个章节标记，无需切分";
            return;
        }

        // 记录原章节在树中的位置
        var oldNode = SelectedChapter;
        var siblings = oldNode.Parent?.Children ?? Chapters;
        var idx = siblings.IndexOf(oldNode);
        var parentId = oldNode.Parent?.Chapter.Id;

        // 移除原章节
        siblings.RemoveAt(idx);
        Book.Chapters.Remove(oldNode.Chapter);

        // 为每个段创建新章节并插入到原位置
        ChapterNode? firstNode = null;
        for (var i = 0; i < segments.Count; i++)
        {
            var seg = segments[i];
            var chapter = new Chapter
            {
                Title = seg.Title,
                FileName = $"chapter_{Book.Chapters.Count + i + 1:D3}",
                Order = idx + i,
                ParentId = parentId,
                XhtmlContent = ChapterSplitter.WrapAsXhtml(seg.Title, seg.Content)
            };
            Book.Chapters.Add(chapter);
            var node = new ChapterNode(chapter, oldNode.Parent);
            siblings.Insert(idx + i, node);
            if (i == 0) firstNode = node;
        }

        // 重新编号
        RenumberChapters();
        UpdateChapterCount();

        // 选中第一个新章节
        if (firstNode is not null)
        {
            SelectedChapter = firstNode;
        }

        StatusMessage = $"已自动切分为 {segments.Count} 个章节";
    }

    private bool CanAutoSplitChapter() => SelectedChapter is not null;

    /// <summary>
    /// 一键排版：对当前章节内容进行自动化清洗和格式化。
    /// 合并断行、清理空白、修正中文标点、修正引号、统一段落格式。
    /// </summary>
    [RelayCommand(CanExecute = nameof(CanAutoFormatChapter))]
    public void AutoFormatChapter()
    {
        if (SelectedChapter is null) return;

        // 先保存编辑器内容
        SaveEditorContent?.Invoke(SelectedChapter);

        // 执行排版
        ApplyFormatAction?.Invoke();

        StatusMessage = "一键排版完成";
    }

    private bool CanAutoFormatChapter() => SelectedChapter is not null;

    // ===== 编辑/预览切换 =====

    partial void OnIsPreviewModeChanged(bool value)
    {
        if (value)
        {
            // 切换到预览前先保存编辑器内容
            if (SelectedChapter is not null)
            {
                SaveEditorContent?.Invoke(SelectedChapter);
            }

            var html = GetPreviewHtml?.Invoke(SelectedChapter) ?? "<html><body><p>（无内容）</p></body></html>";
            NavigatePreview?.Invoke(html);
            StatusMessage = "预览模式";
        }
        else
        {
            // 切回编辑模式时重新加载
            if (SelectedChapter is not null)
            {
                LoadEditorContent?.Invoke(SelectedChapter);
            }
            StatusMessage = "编辑模式";
        }
    }

    [RelayCommand]
    public void TogglePreview()
    {
        // 仅翻转属性，OnIsPreviewModeChanged 处理逻辑
        IsPreviewMode = !IsPreviewMode;
    }

    // ===== 封面图片 =====

    /// <summary>获取当前封面图片的字节数据（无封面返回 null）。</summary>
    public byte[]? GetCoverImageBytes()
    {
        if (Book.CoverResourceId is { } id)
        {
            return Book.Resources.FirstOrDefault(r => r.Id == id)?.Data;
        }
        return null;
    }

    /// <summary>设置封面图片（由 View 调用，传入文件路径）。</summary>
    public void SetCoverImage(string filePath, byte[] data, string mimeType)
    {
        // 移除旧封面资源
        if (Book.CoverResourceId is { } oldId)
        {
            var oldRes = Book.Resources.FirstOrDefault(r => r.Id == oldId);
            if (oldRes is not null && oldRes.Role == "cover")
            {
                Book.Resources.Remove(oldRes);
            }
        }

        var resource = new EpubResource
        {
            Kind = EpubResourceKind.Image,
            FileName = Path.GetFileName(filePath),
            MimeType = mimeType,
            Data = data,
            Role = "cover"
        };
        Book.Resources.Add(resource);
        Book.CoverResourceId = resource.Id;
        Book.ModifiedAt = DateTimeOffset.UtcNow;

        StatusMessage = $"已设置封面：{resource.FileName} ({data.Length / 1024.0:F1} KB)";
    }

    /// <summary>清除封面图片。</summary>
    [RelayCommand]
    public void RemoveCover()
    {
        if (Book.CoverResourceId is { } id)
        {
            var res = Book.Resources.FirstOrDefault(r => r.Id == id);
            if (res is not null)
            {
                Book.Resources.Remove(res);
            }
            Book.CoverResourceId = null;
            StatusMessage = "已移除封面";
        }
    }

    // ===== 字数统计 =====

    /// <summary>更新当前章节字数统计（由 View 在编辑器内容变化时调用）。</summary>
    public void UpdateWordCount(int charCount, int wordCount)
    {
        CharCount = charCount;
        WordCount = wordCount;
    }

    // ===== EPUB 导出 =====

    /// <summary>导出 EPUB 文件。</summary>
    [RelayCommand]
    public async Task ExportEpubAsync()
    {
        // 先保存当前编辑器内容
        if (SelectedChapter is not null)
        {
            SaveEditorContent?.Invoke(SelectedChapter);
        }

        if (Chapters.Count == 0)
        {
            StatusMessage = "没有章节可导出";
            return;
        }

        // 同步元数据
        Book.Title = BookTitle;
        Book.Author = BookAuthor;
        Book.Publisher = string.IsNullOrWhiteSpace(BookPublisher) ? null : BookPublisher;
        Book.Description = string.IsNullOrWhiteSpace(BookDescription) ? null : BookDescription;
        Book.Subject = string.IsNullOrWhiteSpace(BookSubject) ? null : BookSubject;
        Book.Identifier = string.IsNullOrWhiteSpace(BookIsbn) ? null : BookIsbn;
        Book.Language = BookLanguage;
        Book.ModifiedAt = DateTimeOffset.UtcNow;

        var dialog = new SaveFileDialog
        {
            Filter = "EPUB 文件|*.epub",
            FileName = $"{BookTitle}.epub",
            DefaultExt = ".epub"
        };

        if (dialog.ShowDialog() != true)
        {
            StatusMessage = "已取消导出";
            return;
        }

        try
        {
            // 从树重建 Book.Chapters（确保 Order 和 ParentId 正确）
            SyncChaptersFromTree();

            var writer = new EpubWriter();
            await writer.WriteToFileAsync(Book, dialog.FileName);

            var fileSize = new FileInfo(dialog.FileName).Length;
            StatusMessage = $"已导出：{Path.GetFileName(dialog.FileName)} ({fileSize / 1024.0:F1} KB)";
        }
        catch (Exception ex)
        {
            StatusMessage = $"导出失败：{ex.Message}";
        }
    }

    // ===== 项目保存/加载 =====

    /// <summary>保存项目到 .epubra 文件。</summary>
    [RelayCommand]
    public async Task SaveProjectAsync()
    {
        // 先保存当前编辑器内容
        if (SelectedChapter is not null)
        {
            SaveEditorContent?.Invoke(SelectedChapter);
        }

        string filePath;

        if (!string.IsNullOrEmpty(ProjectFilePath))
        {
            filePath = ProjectFilePath;
        }
        else
        {
            var dialog = new SaveFileDialog
            {
                Filter = "Epubra 项目|*.epubra",
                FileName = $"{BookTitle}.epubra",
                DefaultExt = ".epubra"
            };

            if (dialog.ShowDialog() != true)
            {
                StatusMessage = "已取消保存";
                return;
            }
            filePath = dialog.FileName;
        }

        try
        {
            SyncChaptersFromTree();
            Book.Title = BookTitle;
            Book.Author = BookAuthor;
            Book.Publisher = string.IsNullOrWhiteSpace(BookPublisher) ? null : BookPublisher;
            Book.Description = string.IsNullOrWhiteSpace(BookDescription) ? null : BookDescription;
            Book.Subject = string.IsNullOrWhiteSpace(BookSubject) ? null : BookSubject;
            Book.Identifier = string.IsNullOrWhiteSpace(BookIsbn) ? null : BookIsbn;
            Book.Language = BookLanguage;
            Book.ModifiedAt = DateTimeOffset.UtcNow;

            await _projectService.SaveAsync(Book, filePath);
            ProjectFilePath = filePath;
            StatusMessage = $"已保存：{Path.GetFileName(filePath)}";
        }
        catch (Exception ex)
        {
            StatusMessage = $"保存失败：{ex.Message}";
        }
    }

    /// <summary>从 .epubra 文件加载项目。</summary>
    [RelayCommand]
    public async Task OpenProjectAsync()
    {
        var dialog = new OpenFileDialog
        {
            Filter = "Epubra 项目|*.epubra",
            Title = "打开项目"
        };

        if (dialog.ShowDialog() != true)
        {
            return;
        }

        try
        {
            var book = await _projectService.LoadAsync(dialog.FileName);
            LoadBookIntoView(book, dialog.FileName);
            StatusMessage = $"已打开项目：{Path.GetFileName(dialog.FileName)}";
        }
        catch (Exception ex)
        {
            StatusMessage = $"打开失败：{ex.Message}";
        }
    }

    /// <summary>把 Book 加载到 ViewModel 视图状态（章节树、元数据、路径）。</summary>
    private void LoadBookIntoView(Book book, string? filePath)
    {
        Book = book;
        BookTitle = book.Title;
        BookAuthor = book.Author;
        BookPublisher = book.Publisher ?? "";
        BookIsbn = book.Identifier ?? "";
        BookDescription = book.Description ?? "";
        BookSubject = book.Subject ?? "";
        BookLanguage = book.Language;
        ProjectFilePath = filePath;

        // 重建章节树
        Chapters.Clear();
        var nodeLookup = new Dictionary<Guid, ChapterNode>();
        var ordered = ChapterTreeWalker.WalkInDocumentOrder(book.Chapters).ToList();

        // 先创建所有节点
        foreach (var ch in ordered)
        {
            var node = new ChapterNode(ch);
            nodeLookup[ch.Id] = node;
        }

        // 再构建父子关系
        foreach (var ch in ordered)
        {
            var node = nodeLookup[ch.Id];
            if (ch.ParentId is not null && nodeLookup.TryGetValue(ch.ParentId.Value, out var parent))
            {
                node.Parent = parent;
                parent.Children.Add(node);
            }
            else
            {
                Chapters.Add(node);
            }
        }

        SelectedChapter = null;
        UpdateChapterCount();
        UpdateWordCount(0, 0);
    }

    // ===== 新建项目 =====

    /// <summary>导入 TXT 纯文本文件为可编辑项目。</summary>
    [RelayCommand]
    public void ImportTxt()
    {
        var dialog = new OpenFileDialog
        {
            Filter = "文本文件|*.txt|所有文件|*.*",
            Title = "导入 TXT"
        };

        if (dialog.ShowDialog() != true)
        {
            return;
        }

        try
        {
            StatusMessage = "正在导入 TXT…";
            var importer = new TxtImporter();
            var book = importer.Read(dialog.FileName);

            LoadBookIntoView(book, null);
            StatusMessage = $"已导入 TXT：{Path.GetFileName(dialog.FileName)}（{book.Chapters.Count} 章）";
        }
        catch (Exception ex)
        {
            StatusMessage = $"导入 TXT 失败：{ex.Message}";
        }
    }

    /// <summary>导入现有 EPUB 文件为可编辑项目。</summary>
    [RelayCommand]
    public async Task ImportEpubAsync()
    {
        var dialog = new OpenFileDialog
        {
            Filter = "EPUB 文件|*.epub",
            Title = "导入 EPUB"
        };

        if (dialog.ShowDialog() != true)
        {
            return;
        }

        try
        {
            StatusMessage = "正在导入 EPUB…";
            var reader = new EpubReader();
            var book = await reader.ReadAsync(dialog.FileName);

            LoadBookIntoView(book, null);
            StatusMessage = $"已导入 EPUB：{Path.GetFileName(dialog.FileName)}（{book.Chapters.Count} 章）";
        }
        catch (Exception ex)
        {
            StatusMessage = $"导入 EPUB 失败：{ex.Message}";
        }
    }

    /// <summary>导出为纯文本 TXT。</summary>
    [RelayCommand]
    public async Task ExportTxtAsync()
    {
        if (SelectedChapter is not null)
        {
            SaveEditorContent?.Invoke(SelectedChapter);
        }

        if (Chapters.Count == 0)
        {
            StatusMessage = "没有章节可导出";
            return;
        }

        SyncChaptersFromTree();
        Book.Title = BookTitle;
        Book.Author = BookAuthor;

        var dialog = new SaveFileDialog
        {
            Filter = "文本文件|*.txt",
            FileName = $"{BookTitle}.txt",
            DefaultExt = ".txt"
        };

        if (dialog.ShowDialog() != true)
        {
            StatusMessage = "已取消导出";
            return;
        }

        try
        {
            var exporter = new TxtExporter();
            await exporter.ExportAsync(Book, dialog.FileName);
            var fileSize = new FileInfo(dialog.FileName).Length;
            StatusMessage = $"已导出 TXT：{Path.GetFileName(dialog.FileName)} ({fileSize / 1024.0:F1} KB)";
        }
        catch (Exception ex)
        {
            StatusMessage = $"导出失败：{ex.Message}";
        }
    }

    /// <summary>导出为单个 HTML 文件。</summary>
    [RelayCommand]
    public async Task ExportHtmlAsync()
    {
        if (SelectedChapter is not null)
        {
            SaveEditorContent?.Invoke(SelectedChapter);
        }

        if (Chapters.Count == 0)
        {
            StatusMessage = "没有章节可导出";
            return;
        }

        SyncChaptersFromTree();
        Book.Title = BookTitle;
        Book.Author = BookAuthor;

        var dialog = new SaveFileDialog
        {
            Filter = "HTML 文件|*.html",
            FileName = $"{BookTitle}.html",
            DefaultExt = ".html"
        };

        if (dialog.ShowDialog() != true)
        {
            StatusMessage = "已取消导出";
            return;
        }

        try
        {
            var exporter = new HtmlExporter();
            await exporter.ExportAsync(Book, dialog.FileName);
            var fileSize = new FileInfo(dialog.FileName).Length;
            StatusMessage = $"已导出 HTML：{Path.GetFileName(dialog.FileName)} ({fileSize / 1024.0:F1} KB)";
        }
        catch (Exception ex)
        {
            StatusMessage = $"导出失败：{ex.Message}";
        }
    }

    // ===== 新建项目（原位置）=====

    /// <summary>新建项目。</summary>
    [RelayCommand]
    public void NewProject()
    {
        // 保存当前内容
        if (SelectedChapter is not null)
        {
            SaveEditorContent?.Invoke(SelectedChapter);
        }

        Book = new Book { Title = "未命名书籍", Author = "" };
        Book.Chapters.Clear();
        Book.Resources.Clear();
        Chapters.Clear();
        BookTitle = "未命名书籍";
        BookAuthor = "";
        BookPublisher = "";
        BookIsbn = "";
        BookDescription = "";
        BookSubject = "";
        BookLanguage = "zh-CN";
        ProjectFilePath = null;
        SelectedChapter = null;
        UpdateChapterCount();
        UpdateWordCount(0, 0);
        StatusMessage = "已新建项目";
    }

    // ===== 撤销/重做 =====

    [RelayCommand]
    public void Undo()
    {
        UndoAction?.Invoke();
    }

    [RelayCommand]
    public void Redo()
    {
        RedoAction?.Invoke();
    }

    [RelayCommand]
    public void FindReplace()
    {
        ToggleFindReplaceAction?.Invoke();
    }

    // ===== 拖拽辅助（供 View 调用） =====

    /// <summary>从树中移除节点（处理顶级和子级两种情况）。</summary>
    public void RemoveNode(ChapterNode node)
    {
        if (node.Parent is { } parent)
        {
            parent.Children.Remove(node);
        }
        else
        {
            Chapters.Remove(node);
        }
    }

    /// <summary>重新编号所有章节（供 View 在拖拽完成后调用）。</summary>
    public void RenumberChaptersPublic()
    {
        RenumberChapters();
    }

    // ===== 内部方法 =====

    /// <summary>从树结构同步 Order 和 ParentId 到 Book.Chapters。</summary>
    private void SyncChaptersFromTree()
    {
        Book.Chapters.Clear();
        SyncNodeList(Chapters, null);
    }

    private void SyncNodeList(ObservableCollection<ChapterNode> nodes, Guid? parentId)
    {
        for (var i = 0; i < nodes.Count; i++)
        {
            var node = nodes[i];
            node.Chapter.Order = i;
            node.Chapter.ParentId = parentId;
            node.Chapter.Title = node.Title;
            Book.Chapters.Add(node.Chapter);

            if (node.Children.Count > 0)
            {
                SyncNodeList(node.Children, node.Chapter.Id);
            }
        }
    }

    /// <summary>重新编号所有章节的 Order。</summary>
    private void RenumberChapters()
    {
        RenumberList(Chapters, null);
    }

    private void RenumberList(ObservableCollection<ChapterNode> nodes, Guid? parentId)
    {
        for (var i = 0; i < nodes.Count; i++)
        {
            var node = nodes[i];
            node.Chapter.Order = i;
            node.Chapter.ParentId = parentId;
            RenumberList(node.Children, node.Chapter.Id);
        }
    }

    private void UpdateChapterCount()
    {
        var count = CountNodes(Chapters);
        ChapterCount = count;
    }

    private static int CountNodes(IEnumerable<ChapterNode> nodes)
    {
        var count = 0;
        foreach (var node in nodes)
        {
            count++;
            count += CountNodes(node.Children);
        }
        return count;
    }
}
