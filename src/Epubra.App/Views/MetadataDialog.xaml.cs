using System.IO;
using System.Windows;
using System.Windows.Controls;
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

    /// <summary>当前是否已有封面（用于判断是否需要变更）。</summary>
    public bool CoverChanged { get; private set; }

    public MetadataDialog(MainViewModel vm)
    {
        InitializeComponent();
        _vm = vm;
        LoadCurrentMetadata();
        LoadCoverPreview();
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

        // 语言下拉框匹配
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
        var data = _vm.GetCoverImageBytes();
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

            // 找到封面资源文件名
            var coverRes = _vm.Book.Resources.FirstOrDefault(r => r.Id == _vm.Book.CoverResourceId);
            var fileName = coverRes?.FileName ?? "封面";
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

    // ===== 封面操作 =====

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

        // 更新预览
        try
        {
            var bitmap = new BitmapImage();
            bitmap.BeginInit();
            bitmap.UriSource = new Uri(filePath, UriKind.Absolute);
            bitmap.CacheOption = BitmapCacheOption.OnLoad;
            bitmap.EndInit();
            CoverPreview.Source = bitmap;
            CoverPreview.Visibility = Visibility.Visible;
            CoverPlaceholder.Visibility = Visibility.Collapsed;
            CoverInfo.Text = $"{_coverFileName} ({_coverData.Length / 1024.0:F1} KB)";
            RemoveCoverBtn.IsEnabled = true;
        }
        catch
        {
            // 预览失败不影响选择
        }
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
    }

    // ===== 确定按钮 =====

    private void OK_Click(object sender, RoutedEventArgs e)
    {
        // 同步元数据到 ViewModel
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

        // 同步封面
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

    // ===== 辅助 =====

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
}
