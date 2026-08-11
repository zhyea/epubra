using System.IO;
using System.Text;
using System.Text.RegularExpressions;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using Epubra.App.ViewModels;
using Epubra.Core;
using Microsoft.Win32;

namespace Epubra.App.Views;

public partial class MetadataDialog : Window
{
    private readonly MainViewModel _vm;
    private byte[]? _coverData;
    private string? _coverFileName;
    private string? _coverMimeType;
    private bool _previewReady;
    private PreviewMode _currentMode = PreviewMode.BookInfo;

    /// <summary>当前是否已有封面（用于判断是否需要变更）。</summary>
    public bool CoverChanged { get; private set; }

    public MetadataDialog(MainViewModel vm)
    {
        InitializeComponent();
        _vm = vm;
        // 让 TreeView 的 {Binding Chapters} 命中 MainViewModel
        DataContext = vm;
        LoadCurrentMetadata();
        LoadCoverPreview();
        _ = InitializePreviewAsync();
    }

    // ===== 预览模式 =====
    private enum PreviewMode { BookInfo, Chapter, Description }

    private void SetMode(PreviewMode mode, string label)
    {
        _currentMode = mode;
        PreviewModeText.Text = label;
    }

    private async System.Threading.Tasks.Task InitializePreviewAsync()
    {
        try
        {
            await PreviewViewer.EnsureCoreWebView2Async();
            _previewReady = true;
            ShowBookInfo();
        }
        catch
        {
            PreviewFallback.Visibility = Visibility.Visible;
            PreviewViewer.Visibility = Visibility.Collapsed;
        }
    }

    // ===== 元数据加载 =====
    private void LoadCurrentMetadata()
    {
        TitleBox.Text = _vm.BookTitle;
        AuthorBox.Text = _vm.BookAuthor;
        SubjectBox.Text = _vm.BookSubject;
        PublisherBox.Text = _vm.BookPublisher;
        IsbnBox.Text = _vm.BookIsbn;
        DescriptionBox.Text = _vm.BookDescription;

        var lang = _vm.BookLanguage;
        var found = false;
        foreach (ComboBoxItem item in LanguageCombo.Items)
        {
            if (item.Content?.ToString() == lang)
            {
                item.IsSelected = true;
                found = true;
                break;
            }
        }
        if (!found)
        {
            LanguageCombo.Text = lang;
        }
    }

    // ===== 封面预览 =====
    private void LoadCoverPreview()
    {
        var data = _coverData ?? _vm.GetCoverImageBytes();
        if (data is null || data.Length == 0)
        {
            CoverPreview.Visibility = Visibility.Collapsed;
            CoverPlaceholder.Visibility = Visibility.Visible;
            CoverInfo.Text = "未设置封面";
            RemoveCoverBtn.IsEnabled = false;
            return;
        }

        try
        {
            var bitmap = new BitmapImage();
            using (var ms = new MemoryStream(data))
            {
                bitmap.BeginInit();
                bitmap.CacheOption = BitmapCacheOption.OnLoad;
                bitmap.StreamSource = ms;
                bitmap.EndInit();
            }
            CoverPreview.Source = bitmap;
            CoverPreview.Visibility = Visibility.Visible;
            CoverPlaceholder.Visibility = Visibility.Collapsed;

            var coverRes = _vm.Book.Resources.FirstOrDefault(r => r.Id == _vm.Book.CoverResourceId);
            var fileName = _coverFileName ?? coverRes?.FileName ?? "封面";
            CoverInfo.Text = $"{fileName} ({data.Length / 1024.0:F1} KB)";
            RemoveCoverBtn.IsEnabled = true;
        }
        catch
        {
            CoverPreview.Visibility = Visibility.Collapsed;
            CoverPlaceholder.Text = "预览失败";
            CoverPlaceholder.Visibility = Visibility.Visible;
        }
    }

    // ===== 封面多来源 =====
    private void BrowseCover_Click(object sender, RoutedEventArgs e)
    {
        var dialog = new OpenFileDialog
        {
            Filter = "图片文件|*.png;*.jpg;*.jpeg;*.gif;*.bmp|所有文件|*.*",
            Title = "选择封面图片"
        };

        if (dialog.ShowDialog() != true) return;

        var filePath = dialog.FileName;
        _coverData = File.ReadAllBytes(filePath);
        _coverFileName = Path.GetFileName(filePath);
        _coverMimeType = GetImageMimeType(filePath);
        CoverChanged = true;

        LoadCoverPreview();
        RefreshPreview();
    }

    private void PasteCover_Click(object sender, RoutedEventArgs e)
    {
        if (Clipboard.ContainsImage() && Clipboard.GetImage() is { } img)
        {
            _coverData = BitmapSourceToBytes(img);
            _coverMimeType = "image/png";
            _coverFileName = "clipboard.png";
            CoverChanged = true;
            LoadCoverPreview();
            RefreshPreview();
            return;
        }
        CoverInfo.Text = "剪贴板中没有图片";
    }

    private void ExtractCover_Click(object sender, RoutedEventArgs e)
    {
        foreach (var node in _vm.Chapters.SelectMany(n => n.DescendAndSelf()))
        {
            var src = ExtractFirstImageSrc(node.Chapter.XhtmlContent);
            if (src is null) continue;

            byte[]? data = null;
            string? mime = null;
            string? fname = null;

            if (src.StartsWith("data:image", StringComparison.OrdinalIgnoreCase))
            {
                var comma = src.IndexOf(',');
                if (comma > 0)
                {
                    var meta = src.Substring(5, comma - 5); // e.g. png;base64
                    mime = "image/" + meta.Split(';')[0];
                    data = Convert.FromBase64String(src[(comma + 1)..]);
                    fname = "extracted.png";
                }
            }
            else
            {
                var name = src.Split('/').Last();
                var res = _vm.Book.Resources.FirstOrDefault(r => r.FileName == name || r.FileName == src);
                if (res?.Data is { } rd)
                {
                    data = rd;
                    mime = res.MimeType;
                    fname = res.FileName;
                }
            }

            if (data is not null)
            {
                _coverData = data;
                _coverMimeType = mime ?? "image/png";
                _coverFileName = fname;
                CoverChanged = true;
                LoadCoverPreview();
                RefreshPreview();
                return;
            }
        }
        CoverInfo.Text = "未找到章节内的图片";
    }

    private void RemoveCover_Click(object sender, RoutedEventArgs e)
    {
        _coverData = null;
        _coverFileName = null;
        _coverMimeType = null;
        CoverChanged = true;

        CoverPreview.Source = null;
        CoverPreview.Visibility = Visibility.Collapsed;
        CoverPlaceholder.Text = "无封面";
        CoverPlaceholder.Visibility = Visibility.Visible;
        CoverInfo.Text = "未设置封面";
        RemoveCoverBtn.IsEnabled = false;
        RefreshPreview();
    }

    // ===== 章节树 → 章节预览 =====
    private void ChapterTree_SelectedItemChanged(object sender, RoutedPropertyChangedEventArgs<object> e)
    {
        if (e.NewValue is ChapterNode node)
        {
            ShowChapter(node);
        }
    }

    // ===== 字段变化 → 实时刷新书籍信息页 =====
    private void Field_TextChanged(object sender, TextChangedEventArgs e) => RefreshPreview();

    private void Description_TextChanged(object sender, TextChangedEventArgs e) => RefreshPreview();

    private void LanguageCombo_SelectionChanged(object sender, SelectionChangedEventArgs e) => RefreshPreview();

    private void PreviewDescription_Click(object sender, RoutedEventArgs e) => ShowDescription();

    private void RefreshPreview()
    {
        if (!_previewReady) return;
        if (_currentMode == PreviewMode.Chapter) return;
        if (_currentMode == PreviewMode.Description)
            ShowDescription();
        else
            ShowBookInfo();
    }

    // ===== 预览渲染 =====
    private void ShowBookInfo()
    {
        if (!_previewReady) return;

        var uri = GetCoverDataUri();
        var coverHtml = uri is null
            ? "<div class='nocover'>无封面</div>"
            : $"<img class='cover' src='{uri}' />";

        var title = TitleBox.Text.Trim();
        var author = AuthorBox.Text.Trim();
        var lang = LanguageCombo.SelectedItem is ComboBoxItem li ? li.Content?.ToString() ?? "zh-CN" : (LanguageCombo.Text.Trim() is { Length: > 0 } t ? t : "zh-CN");
        var publisher = PublisherBox.Text.Trim();
        var isbn = IsbnBox.Text.Trim();
        var subject = SubjectBox.Text.Trim();

        var meta = new StringBuilder();
        meta.Append($"<div class='meta'>作者：{HttpU(author)}</div>");
        if (lang != "zh-CN") meta.Append($"<div class='meta'>语言：{HttpU(lang)}</div>");
        if (subject.Length > 0) meta.Append($"<div class='meta'>类型：{HttpU(subject)}</div>");
        if (publisher.Length > 0) meta.Append($"<div class='meta'>出版社：{HttpU(publisher)}</div>");
        if (isbn.Length > 0) meta.Append($"<div class='meta'>ISBN：{HttpU(isbn)}</div>");

        var html = $@"<!DOCTYPE html><html><head><meta charset='utf-8'><style>
body{{font-family:'Microsoft YaHei','PingFang SC',sans-serif;margin:28px;color:#2b2b2b;line-height:1.7}}
.cover{{max-width:150px;border:1px solid #ddd;border-radius:6px;display:block;margin-bottom:16px}}
.nocover{{width:150px;height:210px;border:1px dashed #ccc;border-radius:6px;display:flex;align-items:center;justify-content:center;color:#aaa;margin-bottom:16px;font-size:13px}}
h1{{font-size:26px;margin:0 0 6px}}
.meta{{color:#888;font-size:13px;margin:1px 0}}
h2{{font-size:17px;margin:20px 0 8px;border-bottom:1px solid #eee;padding-bottom:4px;color:#444}}
ul{{padding-left:20px}} li{{margin:2px 0}}
</style></head><body>
{coverHtml}
<h1>{HttpU(title.Length == 0 ? "未命名书籍" : title)}</h1>
{meta}
<h2>简介</h2>
{MarkdownToHtml(DescriptionBox.Text)}
<h2>目录</h2>
{BuildTocHtml()}
</body></html>";

        PreviewViewer.NavigateToString(html);
        SetMode(PreviewMode.BookInfo, "书籍信息");
    }

    private void ShowChapter(ChapterNode node)
    {
        if (!_previewReady) return;

        var html = node.Chapter.XhtmlContent;
        if (string.IsNullOrWhiteSpace(html))
        {
            html = $"<html><body style='font-family:sans-serif;color:#999;text-align:center;margin-top:40px'>「{HttpU(node.Title)}」暂无内容</body></html>";
        }

        PreviewViewer.NavigateToString(html);
        SetMode(PreviewMode.Chapter, $"章节：{node.Title}");
    }

    private void ShowDescription()
    {
        if (!_previewReady) return;

        var html = $@"<!DOCTYPE html><html><head><meta charset='utf-8'><style>
body{{font-family:'Microsoft YaHei','PingFang SC',sans-serif;margin:28px;color:#2b2b2b;line-height:1.7}}
h1{{font-size:20px;color:#444;margin:0 0 12px}}
ul{{padding-left:20px}} li{{margin:2px 0}}
</style></head><body>
<h1>简介预览</h1>
{MarkdownToHtml(DescriptionBox.Text)}
</body></html>";

        PreviewViewer.NavigateToString(html);
        SetMode(PreviewMode.Description, "简介预览");
    }

    private string? GetCoverDataUri()
    {
        var data = _coverData ?? _vm.GetCoverImageBytes();
        if (data is null || data.Length == 0) return null;
        var mime = _coverMimeType ?? "image/png";
        return $"data:{mime};base64," + Convert.ToBase64String(data);
    }

    private string BuildTocHtml()
    {
        var sb = new StringBuilder();
        void Walk(System.Collections.Generic.IEnumerable<ChapterNode> nodes)
        {
            foreach (var n in nodes)
            {
                sb.Append("<li>").Append(HttpU(n.Title));
                if (n.Children.Count > 0)
                {
                    sb.Append("<ul>");
                    Walk(n.Children);
                    sb.Append("</ul>");
                }
                sb.Append("</li>");
            }
        }
        sb.Append("<ul>");
        Walk(_vm.Chapters);
        sb.Append("</ul>");
        return sb.ToString();
    }

    // ===== 确定 / 取消 =====
    private void OK_Click(object sender, RoutedEventArgs e)
    {
        _vm.BookTitle = TitleBox.Text.Trim();
        _vm.BookAuthor = AuthorBox.Text.Trim();
        _vm.BookSubject = SubjectBox.Text.Trim();
        _vm.BookPublisher = PublisherBox.Text.Trim();
        _vm.BookIsbn = IsbnBox.Text.Trim();
        _vm.BookDescription = DescriptionBox.Text;

        if (LanguageCombo.SelectedItem is ComboBoxItem langItem)
        {
            _vm.BookLanguage = langItem.Content?.ToString() ?? "zh-CN";
        }
        else if (!string.IsNullOrWhiteSpace(LanguageCombo.Text))
        {
            _vm.BookLanguage = LanguageCombo.Text.Trim();
        }

        if (CoverChanged)
        {
            if (_coverData is not null && _coverFileName is not null)
            {
                _vm.SetCoverImage(_coverFileName, _coverData, _coverMimeType ?? "image/jpeg");
            }
            else
            {
                _vm.RemoveCover();
            }
        }

        DialogResult = true;
        Close();
    }

    private void Cancel_Click(object sender, RoutedEventArgs e)
    {
        DialogResult = false;
        Close();
    }

    // ===== 辅助 =====
    private static string HttpU(string s) => System.Net.WebUtility.HtmlEncode(s);

    private static byte[] BitmapSourceToBytes(BitmapSource src)
    {
        var enc = new PngBitmapEncoder();
        enc.Frames.Add(BitmapFrame.Create(src));
        using var ms = new MemoryStream();
        enc.Save(ms);
        return ms.ToArray();
    }

    private static string GetImageMimeType(string fileName)
    {
        var ext = Path.GetExtension(fileName).ToLowerInvariant();
        return ext switch
        {
            ".png" => "image/png",
            ".jpg" or ".jpeg" => "image/jpeg",
            ".gif" => "image/gif",
            ".bmp" => "image/bmp",
            ".svg" => "image/svg+xml",
            _ => "application/octet-stream"
        };
    }

    private static string? ExtractFirstImageSrc(string? xhtml)
    {
        if (string.IsNullOrWhiteSpace(xhtml)) return null;
        var match = Regex.Match(xhtml, @"<img[^>]+src\s*=\s*[""']([^""']+)[""']", RegexOptions.IgnoreCase);
        return match.Success ? match.Groups[1].Value : null;
    }

    // ===== 轻量 Markdown → HTML（零外部依赖） =====
    private static string MarkdownToHtml(string md)
    {
        if (string.IsNullOrWhiteSpace(md)) return "<p style='color:#aaa'>（暂无简介）</p>";

        var lines = md.Replace("\r\n", "\n").Split('\n');
        var sb = new StringBuilder();
        var inList = false;
        var ordered = false;

        void CloseList()
        {
            if (inList)
            {
                sb.Append(ordered ? "</ol>" : "</ul>");
                inList = false;
            }
        }

        foreach (var line in lines)
        {
            var h = Regex.Match(line, @"^\s*(#{1,3})\s+(.*)$");
            if (h.Success)
            {
                CloseList();
                var lvl = h.Groups[1].Value.Length;
                sb.Append($"<h{lvl}>").Append(Inline(HttpU(h.Groups[2].Value))).Append($"</h{lvl}>");
                continue;
            }

            var ul = Regex.Match(line, @"^\s*[-*]\s+(.*)$");
            if (ul.Success)
            {
                if (!inList) { sb.Append("<ul>"); inList = true; ordered = false; }
                sb.Append("<li>").Append(Inline(HttpU(ul.Groups[1].Value))).Append("</li>");
                continue;
            }

            var ol = Regex.Match(line, @"^\s*\d+\.\s+(.*)$");
            if (ol.Success)
            {
                if (!inList) { sb.Append("<ol>"); inList = true; ordered = true; }
                sb.Append("<li>").Append(Inline(HttpU(ol.Groups[1].Value))).Append("</li>");
                continue;
            }

            if (string.IsNullOrWhiteSpace(line))
            {
                CloseList();
                continue;
            }

            CloseList();
            sb.Append("<p>").Append(Inline(HttpU(line))).Append("</p>");
        }

        CloseList();
        return sb.ToString();
    }

    private static string Inline(string s)
    {
        s = Regex.Replace(s, @"\*\*(.+?)\*\*", "<b>$1</b>");
        s = Regex.Replace(s, @"__(.+?)__", "<b>$1</b>");
        s = Regex.Replace(s, @"(?<!\*)\*(?!\*)(.+?)\*(?!\*)", "<i>$1</i>");
        s = Regex.Replace(s, @"\[([^\]]+)\]\(([^)]+)\)", "<a href=\"$2\">$1</a>");
        return s;
    }
}
