using System.IO;
using System.Text;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Documents;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using Epubra.App.Services;
using Epubra.App.ViewModels;
using Epubra.Editor;
using Epubra.Infrastructure;
using Microsoft.Win32;
using System.Runtime.InteropServices;
using System.Windows.Interop;

namespace Epubra.App.Views;

public partial class MainWindow : Window
{
    private MainViewModel ViewModel => (MainViewModel)DataContext;

    private bool _isLoadingContent;
    private bool _webViewReady;

    public MainWindow()
    {
        InitializeComponent();
        var vm = new MainViewModel();
        DataContext = vm;

        // 设置编辑器内容同步回调
        vm.SaveEditorContent = SaveCurrentEditorContent;
        vm.LoadEditorContent = LoadChapterContent;
        vm.GetPreviewHtml = GetPreviewHtmlForChapter;
        vm.NavigatePreview = NavigateToPreview;
        vm.UndoAction = () => Editor.Undo();
        vm.RedoAction = () => Editor.Redo();
        vm.ToggleFindReplaceAction = ToggleFindReplace;
        vm.ApplyFormatAction = ApplyAutoFormat;

        // WebView2 初始化（异步，不阻塞窗口打开）
        _ = InitializeWebViewAsync();

        // 挂钩 SourceInitialized 以处理 WM_GETMINMAXINFO（最大化不覆盖任务栏）
        SourceInitialized += OnSourceInitialized;

        // 预览内容重新加载后以当前缩放重新应用
        PreviewViewer.NavigationCompleted += (_, _) => _ = ApplyPreviewZoom();
    }

    private async Task InitializeWebViewAsync()
    {
        try
        {
            await PreviewViewer.EnsureCoreWebView2Async();
            _webViewReady = true;
            // CoreWebView2 就绪后按当前缩放应用预览
            _ = ApplyPreviewZoom();
        }
        catch (Exception ex)
        {
            // WebView2 运行时可能未安装，降级为只显示编辑模式
            ViewModel.StatusMessage = $"WebView2 不可用：{ex.Message}（预览功能受限）";
        }
    }

    private void MainWindow_Loaded(object sender, RoutedEventArgs e)
    {
        ViewModel.StatusMessage = "就绪 - 点击「+」添加章节开始编辑";
        SyncThemeCombo();
        SyncMaxRestoreIcon();
        ThemeService.ThemeChanged += OnThemeChanged;
    }

    // ===== 主题切换 =====

    /// <summary>主题下拉框选择变更 → 应用并持久化主题。</summary>
    private void ThemeCombo_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (ThemeCombo.SelectedItem is not ComboBoxItem item || item.Tag is not string tag) return;
        if (!Enum.TryParse<ThemeKind>(tag, out var kind)) return;
        // 守卫：选择与当前已生效主题一致的项（如启动同步）时跳过，避免重复持久化与字典热替换
        if (kind == ThemeService.Current) return;
        ThemeService.Apply(kind);
    }

    /// <summary>根据当前生效主题，选中下拉框中对应的项。</summary>
    private void SyncThemeCombo()
    {
        foreach (ComboBoxItem item in ThemeCombo.Items)
        {
            if (item.Tag is string tag && Enum.TryParse<ThemeKind>(tag, out var kind) && kind == ThemeService.Current)
            {
                ThemeCombo.SelectedItem = item;
                break;
            }
        }
    }

    /// <summary>
    /// 从应用资源按 key 取主题语义画刷；找不到时回退到 fallback，
    /// 保证在资源系统未就绪（如测试）时也不抛空引用。
    /// </summary>
    private static Brush GetThemeBrush(string key, Brush fallback)
    {
        return Application.Current?.TryFindResource(key) as Brush ?? fallback;
    }

    /// <summary>
    /// 主题热替换后刷新「已渲染正文」，消除外壳秒变而正文保留旧色的割裂感：
    /// 预览模式重新导航 WebView2（与预览切换相同逻辑），编辑模式重建 FlowDocument。
    /// </summary>
    private void OnThemeChanged(object? sender, ThemeKind kind)
    {
        if (ViewModel.IsPreviewMode)
        {
            var html = GetPreviewHtmlForChapter(ViewModel.SelectedChapter);
            NavigateToPreview(html);
        }
        else
        {
            LoadChapterContent(ViewModel.SelectedChapter);
        }
    }

    // ===== Ribbon 工具栏折叠/展开 =====

    private const double CollapsedRibbonHeight = 30;
    private bool _isRibbonCollapsed;
    private bool _isRibbonTempExpanded;

    /// <summary>切换工具栏折叠/展开状态（持久切换）。</summary>
    private void ToggleRibbon()
    {
        _isRibbonCollapsed = !_isRibbonCollapsed;
        _isRibbonTempExpanded = false;
        UpdateRibbonState();
        ViewModel.StatusMessage = _isRibbonCollapsed
            ? "工具栏已折叠（单击标签页临时展开，双击或 Ctrl+F1 固定展开）"
            : "工具栏已展开";
    }

    /// <summary>更新工具栏可视状态。</summary>
    private void UpdateRibbonState()
    {
        var showExpanded = !_isRibbonCollapsed || _isRibbonTempExpanded;

        RibbonTabs.Height = showExpanded ? double.NaN : CollapsedRibbonHeight;

        // 按钮和菜单文本随状态变化
        var label = _isRibbonCollapsed ? "展开工具栏" : "折叠工具栏";
        ToggleRibbonBtn.Content = label;
        ToggleRibbonMenuItem.Header = $"{label}(_R)";
    }

    /// <summary>按钮/菜单点击切换。</summary>
    private void ToggleRibbon_Click(object sender, RoutedEventArgs e) => ToggleRibbon();

    /// <summary>双击 Tab 头 → 持久切换折叠/展开。</summary>
    private void RibbonTabs_MouseDoubleClick(object sender, MouseButtonEventArgs e)
    {
        var pos = e.GetPosition(RibbonTabs);
        if (pos.Y <= CollapsedRibbonHeight)
        {
            ToggleRibbon();
            e.Handled = true;
        }
    }

    /// <summary>折叠状态下单击 Tab 头 → 临时展开。</summary>
    private void RibbonTabs_PreviewMouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        if (!_isRibbonCollapsed || _isRibbonTempExpanded) return;

        var pos = e.GetPosition(RibbonTabs);
        if (pos.Y <= CollapsedRibbonHeight)
        {
            _isRibbonTempExpanded = true;
            UpdateRibbonState();
        }
    }

    /// <summary>临时展开状态下点击工具栏外部 → 收回。</summary>
    private void Window_PreviewMouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        if (!_isRibbonCollapsed || !_isRibbonTempExpanded) return;

        var pos = e.GetPosition(RibbonTabs);
        var bounds = new Rect(0, 0, RibbonTabs.ActualWidth, RibbonTabs.ActualHeight);
        if (!bounds.Contains(pos))
        {
            _isRibbonTempExpanded = false;
            UpdateRibbonState();
        }
    }

    /// <summary>Ctrl+F1 切换工具栏折叠/展开。</summary>
    private void Window_PreviewKeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.F1 && Keyboard.Modifiers == ModifierKeys.Control)
        {
            ToggleRibbon();
            e.Handled = true;
        }
    }

    // ===== TreeView 选中 =====

    private void ChapterTree_SelectedItemChanged(object sender, RoutedPropertyChangedEventArgs<object> e)
    {
        if (e.NewValue is ChapterNode node)
        {
            ViewModel.SelectedChapter = node;
            CurrentChapterLabel.Text = node.Title;

            // 如果在预览模式，更新预览
            if (ViewModel.IsPreviewMode)
            {
                var html = GetPreviewHtmlForChapter(node);
                NavigateToPreview(html);
            }
            else
            {
                LoadChapterContent(node);
            }
        }
        else
        {
            ViewModel.SelectedChapter = null;
            CurrentChapterLabel.Text = "（未选择章节）";
            if (!ViewModel.IsPreviewMode)
            {
                LoadChapterContent(null);
            }
        }
    }

    private void ChapterTree_MouseDoubleClick(object sender, MouseButtonEventArgs e)
    {
        // 双击进入重命名模式
        if (ChapterTree.SelectedItem is ChapterNode node && e.OriginalSource is TextBlock)
        {
            node.Title = PromptRename(node.Title);
        }
    }

    private void ChapterTree_KeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.F2 && ViewModel.SelectedChapter is { } node)
        {
            node.Title = PromptRename(node.Title);
            e.Handled = true;
        }
    }

    private string PromptRename(string current)
    {
        var dialog = new Window
        {
            Title = "重命名章节",
            Width = 360,
            Height = 140,
            WindowStartupLocation = WindowStartupLocation.CenterOwner,
            Owner = this
        };
        var panel = new StackPanel { Margin = new Thickness(16) };
        var label = new TextBlock { Text = "章节标题：" };
        var textBox = new TextBox { Text = current, Margin = new Thickness(0, 8, 0, 8) };
        var btnPanel = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right };
        var okBtn = new Button { Content = "确定", Padding = new Thickness(16, 4, 16, 4), IsDefault = true };
        var cancelBtn = new Button { Content = "取消", Padding = new Thickness(16, 4, 16, 4), Margin = new Thickness(8, 0, 0, 0), IsCancel = true };
        btnPanel.Children.Add(okBtn);
        btnPanel.Children.Add(cancelBtn);
        panel.Children.Add(label);
        panel.Children.Add(textBox);
        panel.Children.Add(btnPanel);
        dialog.Content = panel;

        okBtn.Click += (_, _) =>
        {
            if (!string.IsNullOrWhiteSpace(textBox.Text))
            {
                dialog.DialogResult = true;
                dialog.Close();
            }
        };

        textBox.SelectAll();
        textBox.Focus();

        return dialog.ShowDialog() == true ? textBox.Text.Trim() : current;
    }

    /// <summary>弹出输入对话框，返回用户输入的字符串。</summary>
    private string PromptForInput(string title, string prompt, string defaultValue = "")
    {
        var dialog = new Window
        {
            Title = title,
            Width = 360,
            Height = 150,
            WindowStartupLocation = WindowStartupLocation.CenterOwner,
            Owner = this,
            ResizeMode = ResizeMode.NoResize
        };
        var panel = new StackPanel { Margin = new Thickness(16) };
        var label = new TextBlock { Text = prompt };
        var textBox = new TextBox { Text = defaultValue, Margin = new Thickness(0, 8, 0, 8) };
        var btnPanel = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right };
        var okBtn = new Button { Content = "确定", Padding = new Thickness(16, 4, 16, 4), IsDefault = true };
        var cancelBtn = new Button { Content = "取消", Padding = new Thickness(16, 4, 16, 4), Margin = new Thickness(8, 0, 0, 0), IsCancel = true };
        btnPanel.Children.Add(okBtn);
        btnPanel.Children.Add(cancelBtn);
        panel.Children.Add(label);
        panel.Children.Add(textBox);
        panel.Children.Add(btnPanel);
        dialog.Content = panel;

        okBtn.Click += (_, _) =>
        {
            dialog.DialogResult = true;
            dialog.Close();
        };

        textBox.SelectAll();
        textBox.Focus();

        return dialog.ShowDialog() == true ? textBox.Text : "";
    }

    // ===== 章节拖拽排序 =====

    private Point _dragStartPoint;
    private ChapterNode? _draggedNode;

    private void ChapterTree_PreviewMouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        _dragStartPoint = e.GetPosition(null);
        _draggedNode = FindTreeViewItem(e.OriginalSource as DependencyObject);
    }

    private void ChapterTree_PreviewMouseMove(object sender, MouseEventArgs e)
    {
        if (e.LeftButton != MouseButtonState.Pressed || _draggedNode is null)
            return;

        var pos = e.GetPosition(null);
        var diff = _dragStartPoint - pos;

        // 超过系统拖拽阈值才启动
        if (Math.Abs(diff.X) < SystemParameters.MinimumHorizontalDragDistance &&
            Math.Abs(diff.Y) < SystemParameters.MinimumVerticalDragDistance)
            return;

        // 启动拖拽
        DragDrop.DoDragDrop(ChapterTree, _draggedNode, DragDropEffects.Move | DragDropEffects.Copy);
    }

    private void ChapterTree_DragEnter(object sender, DragEventArgs e)
    {
        if (_draggedNode is not null)
        {
            e.Effects = DragDropEffects.Move;
        }
        else
        {
            e.Effects = DragDropEffects.None;
        }
        e.Handled = true;
    }

    private void ChapterTree_DragOver(object sender, DragEventArgs e)
    {
        if (_draggedNode is not null)
        {
            e.Effects = e.KeyStates.HasFlag(DragDropKeyStates.ControlKey)
                ? DragDropEffects.Copy
                : DragDropEffects.Move;
        }
        else
        {
            e.Effects = DragDropEffects.None;
        }
        e.Handled = true;
    }

    private void ChapterTree_Drop(object sender, DragEventArgs e)
    {
        if (_draggedNode is null) return;

        var targetNode = FindTreeViewItem(VisualTreeHelper.HitTest(ChapterTree, e.GetPosition(ChapterTree))?.VisualHit);

        if (targetNode is null || targetNode == _draggedNode)
        {
            _draggedNode = null;
            return;
        }

        // 不允许拖到自己的后代节点下（会形成环）
        if (IsDescendantOf(targetNode, _draggedNode))
        {
            ViewModel.StatusMessage = "不能拖到自身子节点下";
            _draggedNode = null;
            return;
        }

        // 从原位置移除
        ViewModel.RemoveNode(_draggedNode);

        // 插入到目标位置：作为目标的子节点（追加到末尾）
        targetNode.Children.Add(_draggedNode);
        _draggedNode.Parent = targetNode;
        _draggedNode.Chapter.ParentId = targetNode.Chapter.Id;
        targetNode.IsExpanded = true;

        ViewModel.RenumberChaptersPublic();
        ViewModel.StatusMessage = $"已移动「{_draggedNode.Title}」到「{targetNode.Title}」下";
        _draggedNode = null;
    }

    /// <summary>判断 candidate 是否是 ancestor 的后代。</summary>
    private static bool IsDescendantOf(ChapterNode candidate, ChapterNode ancestor)
    {
        var current = candidate.Parent;
        while (current is not null)
        {
            if (current == ancestor) return true;
            current = current.Parent;
        }
        return false;
    }

    /// <summary>通过命中测试找到 TreeView 中的 ChapterNode。</summary>
    private ChapterNode? FindTreeViewItem(DependencyObject? visual)
    {
        while (visual is not null)
        {
            if (visual is TreeViewItem item && item.DataContext is ChapterNode node)
            {
                return node;
            }
            visual = VisualTreeHelper.GetParent(visual);
        }
        return null;
    }

    // ===== 编辑器内容同步 =====

    /// <summary>保存当前编辑器内容到 Chapter.XhtmlContent。</summary>
    private void SaveCurrentEditorContent(ChapterNode node)
    {
        if (_isLoadingContent || !Editor.IsEnabled) return;

        var flowDoc = Editor.Document;
        var xhtml = FlowDocumentToXhtmlConverter.Convert(flowDoc);
        node.Chapter.XhtmlContent = xhtml;
    }

    /// <summary>加载章节内容到编辑器。</summary>
    private void LoadChapterContent(ChapterNode? node)
    {
        // 先保存当前章节
        if (ViewModel.SelectedChapter is not null && !_isLoadingContent)
        {
            SaveCurrentEditorContent(ViewModel.SelectedChapter);
        }

        _isLoadingContent = true;
        try
        {
            Editor.Document = new FlowDocument();

            if (node is null)
            {
                Editor.IsEnabled = false;
                return;
            }

            Editor.IsEnabled = true;

            // 如果有已保存的 XHTML 内容，尝试加载
            if (!string.IsNullOrWhiteSpace(node.Chapter.XhtmlContent))
            {
                var doc = XhtmlToFlowDocumentConverter.Convert(node.Chapter.XhtmlContent);
                if (doc is not null)
                {
                    Editor.Document = doc;
                }
            }

            Editor.Focus();
        }
        finally
        {
            _isLoadingContent = false;
        }

        UpdateWordCount();
    }

    // ===== 预览 =====

    /// <summary>获取章节的预览 HTML。</summary>
    private string GetPreviewHtmlForChapter(ChapterNode? node)
    {
        if (node is null)
        {
            return "<html><body><p style='color:#999;text-align:center;margin-top:40px;'>请选择一个章节进行预览</p></body></html>";
        }

        // 确保最新内容已保存
        if (Editor.IsEnabled && !_isLoadingContent)
        {
            SaveCurrentEditorContent(node);
        }

        var content = node.Chapter.XhtmlContent;
        if (string.IsNullOrWhiteSpace(content))
        {
            return $"<html><body><p style='color:#999;text-align:center;margin-top:40px;'>「{node.Title}」暂无内容</p></body></html>";
        }

        // 如果已经是完整 HTML 文档，直接返回
        var trimmed = content.TrimStart();
        if (trimmed.StartsWith("<?xml", StringComparison.Ordinal) || trimmed.StartsWith("<html", StringComparison.OrdinalIgnoreCase))
        {
            return content;
        }

        // 包裹为完整 HTML
        var sb = new System.Text.StringBuilder();
        sb.Append("<!DOCTYPE html>\n<html lang=\"zh-CN\">\n<head>\n<meta charset=\"utf-8\"/>\n");
        sb.Append("<style>\n");
        sb.Append("body { font-family: 'Songti SC', 'SimSun', serif; font-size: 16px; line-height: 1.8; margin: 40px; color: #333; }\n");
        sb.Append("h1 { font-size: 1.8em; margin: 1em 0 0.5em; }\n");
        sb.Append("h2 { font-size: 1.5em; margin: 1em 0 0.5em; }\n");
        sb.Append("h3 { font-size: 1.2em; margin: 1em 0 0.5em; }\n");
        sb.Append("img { max-width: 100%; }\n");
        sb.Append("p { text-indent: 2em; margin: 0.5em 0; }\n");
        sb.Append("</style>\n</head>\n<body>\n");
        sb.Append(content);
        sb.Append("\n</body>\n</html>");
        return sb.ToString();
    }

    /// <summary>导航 WebView2 到预览内容。</summary>
    private void NavigateToPreview(string html)
    {
        if (!_webViewReady || PreviewViewer.CoreWebView2 is null)
        {
            ViewModel.StatusMessage = "WebView2 尚未就绪，请稍候";
            return;
        }

        PreviewViewer.NavigateToString(html);
    }

    // ===== 格式化按钮 =====

    private void Bold_Click(object sender, RoutedEventArgs e)
    {
        var sel = Editor.Selection;
        if (sel.IsEmpty) return;
        var td = sel.GetPropertyValue(Inline.FontWeightProperty);
        var current = td is FontWeight fw ? fw : FontWeights.Normal;
        sel.ApplyPropertyValue(Inline.FontWeightProperty,
            current == FontWeights.Bold ? FontWeights.Normal : FontWeights.Bold);
    }

    private void Italic_Click(object sender, RoutedEventArgs e)
    {
        var sel = Editor.Selection;
        if (sel.IsEmpty) return;
        var td = sel.GetPropertyValue(Inline.FontStyleProperty);
        var current = td is FontStyle fs ? fs : FontStyles.Normal;
        sel.ApplyPropertyValue(Inline.FontStyleProperty,
            current == FontStyles.Italic ? FontStyles.Normal : FontStyles.Italic);
    }

    private void Underline_Click(object sender, RoutedEventArgs e)
    {
        var sel = Editor.Selection;
        if (sel.IsEmpty) return;
        var td = sel.GetPropertyValue(Inline.TextDecorationsProperty);
        var hasUnderline = td is TextDecorationCollection tdc && tdc.Contains(TextDecorations.Underline[0]);
        sel.ApplyPropertyValue(Inline.TextDecorationsProperty,
            hasUnderline ? new TextDecorationCollection() : TextDecorations.Underline);
    }

    private void Style_Click(object sender, RoutedEventArgs e)
    {
        if (Editor is null || !Editor.IsEnabled) return;
        if (sender is not Button btn) return;

        var tag = btn.Tag?.ToString() ?? "";
        var para = GetCurrentParagraph();
        if (para is null) return;

        switch (tag)
        {
            case "h1": para.FontSize = 28; para.FontWeight = FontWeights.Bold; break;
            case "h2": para.FontSize = 24; para.FontWeight = FontWeights.Bold; break;
            case "h3": para.FontSize = 20; para.FontWeight = FontWeights.Bold; break;
            case "h4": para.FontSize = 18; para.FontWeight = FontWeights.Bold; break;
            default: para.FontSize = 16; para.FontWeight = FontWeights.Normal; break;
        }
    }

    private void FontFamily_Changed(object sender, SelectionChangedEventArgs e)
    {
        if (Editor is null || !Editor.IsEnabled) return;
        if (FontFamilyCombo.SelectedItem is not ComboBoxItem item) return;
        Editor.Selection.ApplyPropertyValue(Inline.FontFamilyProperty, new FontFamily(item.Content.ToString()!));
    }

    private void FontSize_Changed(object sender, SelectionChangedEventArgs e)
    {
        if (Editor is null || !Editor.IsEnabled) return;
        if (FontSizeCombo.SelectedItem is not ComboBoxItem item) return;
        if (double.TryParse(item.Content?.ToString(), out var size))
        {
            Editor.Selection.ApplyPropertyValue(Inline.FontSizeProperty, size);
        }
    }

    private void AlignLeft_Click(object sender, RoutedEventArgs e)
    {
        var para = GetCurrentParagraph();
        if (para is not null) para.TextAlignment = TextAlignment.Left;
        AlignLeftToggle.IsChecked = true;
        AlignCenterToggle.IsChecked = false;
        AlignRightToggle.IsChecked = false;
        AlignJustifyToggle.IsChecked = false;
    }

    private void AlignCenter_Click(object sender, RoutedEventArgs e)
    {
        var para = GetCurrentParagraph();
        if (para is not null) para.TextAlignment = TextAlignment.Center;
        AlignLeftToggle.IsChecked = false;
        AlignCenterToggle.IsChecked = true;
        AlignRightToggle.IsChecked = false;
        AlignJustifyToggle.IsChecked = false;
    }

    private void AlignRight_Click(object sender, RoutedEventArgs e)
    {
        var para = GetCurrentParagraph();
        if (para is not null) para.TextAlignment = TextAlignment.Right;
        AlignLeftToggle.IsChecked = false;
        AlignCenterToggle.IsChecked = false;
        AlignRightToggle.IsChecked = true;
        AlignJustifyToggle.IsChecked = false;
    }

    private void AlignJustify_Click(object sender, RoutedEventArgs e)
    {
        var para = GetCurrentParagraph();
        if (para is not null) para.TextAlignment = TextAlignment.Justify;
        AlignLeftToggle.IsChecked = false;
        AlignCenterToggle.IsChecked = false;
        AlignRightToggle.IsChecked = false;
        AlignJustifyToggle.IsChecked = true;
    }

    // ===== 剪贴板 =====

    private void Paste_Click(object sender, RoutedEventArgs e) => Editor.Paste();
    private void Cut_Click(object sender, RoutedEventArgs e) => Editor.Cut();
    private void Copy_Click(object sender, RoutedEventArgs e) => Editor.Copy();

    // ===== 字体格式 =====

    private void Strikethrough_Click(object sender, RoutedEventArgs e)
    {
        var sel = Editor.Selection;
        var td = sel.GetPropertyValue(Inline.TextDecorationsProperty);
        var hasStrike = td is TextDecorationCollection tdc && tdc.Contains(TextDecorations.Strikethrough[0]);

        var newDecorations = new TextDecorationCollection();
        if (td is TextDecorationCollection existing)
        {
            foreach (var dec in existing)
            {
                if (dec.Location != TextDecorationLocation.Strikethrough)
                    newDecorations.Add(dec);
            }
        }
        if (!hasStrike)
        {
            newDecorations.Add(TextDecorations.Strikethrough[0]);
        }
        sel.ApplyPropertyValue(Inline.TextDecorationsProperty, newDecorations);
    }

    private void FontColor_Click(object sender, RoutedEventArgs e)
    {
        var colors = new[] { Colors.Black, Colors.Red, Colors.DarkRed, Colors.Blue, Colors.DarkBlue,
                             Colors.Green, Colors.DarkGreen, Colors.Orange, Colors.Purple,
                             Colors.Brown, Colors.Gray, Colors.DarkCyan };
        var dialog = new Window
        {
            Title = "选择字体颜色",
            Width = 210,
            Height = 120,
            WindowStartupLocation = WindowStartupLocation.CenterOwner,
            Owner = this,
            ResizeMode = ResizeMode.NoResize
        };
        var panel = new WrapPanel { Margin = new Thickness(8) };
        foreach (var color in colors)
        {
            var brush = new SolidColorBrush(color);
            var btn = new Button
            {
                Width = 26, Height = 26,
                Background = brush,
                Margin = new Thickness(2),
                BorderBrush = GetThemeBrush("BorderSubtle", Brushes.Gray),
                BorderThickness = new Thickness(1),
                Tag = brush
            };
            btn.Click += (_, _) =>
            {
                Editor.Selection.ApplyPropertyValue(Inline.ForegroundProperty, brush);
                dialog.Close();
            };
            panel.Children.Add(btn);
        }
        dialog.Content = panel;
        dialog.ShowDialog();
    }

    private void ClearFormat_Click(object sender, RoutedEventArgs e)
    {
        var sel = Editor.Selection;
        sel.ApplyPropertyValue(Inline.FontWeightProperty, FontWeights.Normal);
        sel.ApplyPropertyValue(Inline.FontStyleProperty, FontStyles.Normal);
        sel.ApplyPropertyValue(Inline.TextDecorationsProperty, new TextDecorationCollection());
        sel.ApplyPropertyValue(Inline.FontFamilyProperty, new FontFamily("宋体"));
        sel.ApplyPropertyValue(Inline.FontSizeProperty, 16.0);
        // 清除显式前景色（ApplyPropertyValue(..., null) 会移除本地值），
        // 让文字回退到主题默认（TextPrimary），避免把黑色写死进选区、进而污染 EPUB 导出
        sel.ApplyPropertyValue(Inline.ForegroundProperty, null);
        var para = GetCurrentParagraph();
        if (para is not null)
        {
            para.TextAlignment = TextAlignment.Left;
            para.Margin = new Thickness(0);
            para.Padding = new Thickness(0);
        }
    }

    private void IncreaseFont_Click(object sender, RoutedEventArgs e)
    {
        var td = Editor.Selection.GetPropertyValue(Inline.FontSizeProperty);
        var current = td is double d ? d : 16.0;
        Editor.Selection.ApplyPropertyValue(Inline.FontSizeProperty, Math.Min(current + 2, 72));
    }

    private void DecreaseFont_Click(object sender, RoutedEventArgs e)
    {
        var td = Editor.Selection.GetPropertyValue(Inline.FontSizeProperty);
        var current = td is double d ? d : 16.0;
        Editor.Selection.ApplyPropertyValue(Inline.FontSizeProperty, Math.Max(current - 2, 8));
    }

    // ===== 段落格式 =====

    private void BulletList_Click(object sender, RoutedEventArgs e)
    {
        EditingCommands.ToggleBullets.Execute(null, Editor);
    }

    private void IncreaseIndent_Click(object sender, RoutedEventArgs e)
    {
        EditingCommands.IncreaseIndentation.Execute(null, Editor);
    }

    private void DecreaseIndent_Click(object sender, RoutedEventArgs e)
    {
        EditingCommands.DecreaseIndentation.Execute(null, Editor);
    }

    // ===== 插入元素 =====

    private void Hyperlink_Click(object sender, RoutedEventArgs e)
    {
        var url = PromptForInput("插入超链接", "请输入链接地址：", "https://");
        if (string.IsNullOrWhiteSpace(url)) return;

        var text = Editor.Selection.IsEmpty ? url : Editor.Selection.Text;
        var link = new Hyperlink(new Run(text)) { NavigateUri = new Uri(url) };

        if (!Editor.Selection.IsEmpty)
        {
            Editor.Selection.Text = "";
        }
        var caret = Editor.CaretPosition;
        var para = caret?.Paragraph;
        if (para is not null)
        {
            para.Inlines.Add(link);
        }
        ViewModel.StatusMessage = $"已插入超链接：{url}";
    }

    private void HorizontalLine_Click(object sender, RoutedEventArgs e)
    {
        var caret = Editor.CaretPosition;
        if (caret?.Paragraph is null) return;

        // 在当前段落后插入新段，设为水平线样式
        caret.Paragraph.ContentEnd.InsertParagraphBreak();
        var hrPara = Editor.CaretPosition.Paragraph;
        if (hrPara is not null)
        {
            hrPara.BorderBrush = GetThemeBrush("BorderSubtle", Brushes.Gray);
            hrPara.BorderThickness = new Thickness(0, 1, 0, 0);
            hrPara.Margin = new Thickness(0, 6, 0, 6);
            hrPara.FontSize = 1;
            hrPara.Inlines.Add(new Run(""));
        }
        // 再插入一个空段用于继续编辑
        hrPara?.ContentEnd.InsertParagraphBreak();
        Editor.CaretPosition = Editor.CaretPosition.Paragraph.ContentStart;
    }

    private void Table_Click(object sender, RoutedEventArgs e)
    {
        var rowsStr = PromptForInput("插入表格", "行数：", "3");
        if (!int.TryParse(rowsStr, out var rows) || rows < 1 || rows > 20) return;
        var colsStr = PromptForInput("插入表格", "列数：", "3");
        if (!int.TryParse(colsStr, out var cols) || cols < 1 || cols > 10) return;

        var table = new Table
        {
            CellSpacing = 0,
            BorderBrush = GetThemeBrush("BorderSubtle", Brushes.Gray),
            BorderThickness = new Thickness(0.5)
        };

        for (var i = 0; i < cols; i++)
            table.Columns.Add(new TableColumn { Width = new GridLength(120) });

        var rowGroup = new TableRowGroup();
        for (var r = 0; r < rows; r++)
        {
            var row = new TableRow();
            for (var c = 0; c < cols; c++)
            {
                var cell = new TableCell(new Paragraph(new Run(" ")))
                {
                    BorderBrush = GetThemeBrush("BorderSubtle", Brushes.Gray),
                    BorderThickness = new Thickness(0.5)
                };
                row.Cells.Add(cell);
            }
            rowGroup.Rows.Add(row);
        }
        table.RowGroups.Add(rowGroup);

        // 在当前段落后插入表格
        var para = Editor.CaretPosition?.Paragraph;
        if (para is null) return;

        var doc = Editor.Document;
        var idx = 0;
        foreach (var b in doc.Blocks)
        {
            if (b == para) break;
            idx++;
        }
        ((System.Collections.IList)doc.Blocks).Insert(idx + 1, table);

        ViewModel.StatusMessage = $"已插入 {rows}×{cols} 表格";
    }

    private void InsertImage_Click(object sender, RoutedEventArgs e)
    {
        var dialog = new OpenFileDialog
        {
            Filter = "图片文件|*.png;*.jpg;*.jpeg;*.gif;*.bmp|所有文件|*.*",
            Title = "选择图片"
        };

        if (dialog.ShowDialog() != true) return;

        var bitmap = new BitmapImage();
        bitmap.BeginInit();
        bitmap.UriSource = new Uri(dialog.FileName, UriKind.Absolute);
        bitmap.CacheOption = BitmapCacheOption.OnLoad;
        bitmap.EndInit();

        var image = new Image { Source = bitmap, Width = bitmap.Width, Stretch = Stretch.Uniform };
        if (image.Width > 500) image.Width = 500;

        var container = new InlineUIContainer { Child = image };
        var caret = Editor.CaretPosition;
        if (caret is not null)
        {
            var insertPara = caret.Paragraph ?? new Paragraph();
            insertPara.Inlines.Add(container);
        }

        // 把图片添加到 Book 资源
        var resource = new Epubra.Core.EpubResource
        {
            Kind = Epubra.Core.EpubResourceKind.Image,
            FileName = Path.GetFileName(dialog.FileName),
            MimeType = GetImageMimeType(dialog.FileName),
            Data = File.ReadAllBytes(dialog.FileName)
        };
        ViewModel.Book.Resources.Add(resource);

        // 更新 img src 为 epub 内部路径
        image.Tag = $"images/{resource.FileName}";

        ViewModel.StatusMessage = $"已插入图片：{resource.FileName}";
    }

    // ===== 插入音频 =====

    private void InsertAudio_Click(object sender, RoutedEventArgs e)
    {
        if (!Editor.IsEnabled)
        {
            ViewModel.StatusMessage = "请先选择一个章节";
            return;
        }

        var dialog = new OpenFileDialog
        {
            Filter = "音频文件|*.mp3;*.wav;*.m4a;*.ogg;*.aac;*.flac|所有文件|*.*",
            Title = "选择音频文件"
        };

        if (dialog.ShowDialog() != true) return;

        var filePath = dialog.FileName;
        var fileName = Path.GetFileName(filePath);
        var data = File.ReadAllBytes(filePath);
        var mimeType = GetAudioMimeType(filePath);

        // 添加音频资源到 Book
        var resource = new Epubra.Core.EpubResource
        {
            Kind = Epubra.Core.EpubResourceKind.Audio,
            FileName = fileName,
            MimeType = mimeType,
            Data = data,
            Role = "audio"
        };
        ViewModel.Book.Resources.Add(resource);

        // 在编辑器中插入音频占位符
        var audioHref = $"audio/{fileName}";
        var border = new Border
        {
            Background = GetThemeBrush("SurfaceAccentSubtle", Brushes.AliceBlue),
            BorderBrush = GetThemeBrush("BorderAccent", Brushes.SteelBlue),
            BorderThickness = new Thickness(1),
            Padding = new Thickness(12, 6, 12, 6),
            CornerRadius = new CornerRadius(4),
            Tag = $"audio:{audioHref}"
        };

        var panel = new StackPanel { Orientation = Orientation.Horizontal };
        panel.Children.Add(new TextBlock
        {
            Text = "\uD83C\uDFA7 ",
            FontSize = 18,
            VerticalAlignment = VerticalAlignment.Center
        });
        panel.Children.Add(new TextBlock
        {
            Text = fileName,
            FontSize = 14,
            Foreground = GetThemeBrush("TextAccent", Brushes.SteelBlue),
            VerticalAlignment = VerticalAlignment.Center
        });
        border.Child = panel;

        var container = new BlockUIContainer { Child = border };

        // 在当前段落后面插入
        var caret = Editor.CaretPosition;
        var para = caret?.Paragraph;
        if (para is null)
        {
            Editor.Document.Blocks.Add(container);
        }
        else
        {
            var doc = Editor.Document;
            var idx = 0;
            foreach (var b in doc.Blocks)
            {
                if (b == para) break;
                idx++;
            }
            ((System.Collections.IList)doc.Blocks).Insert(idx + 1, container);
        }

        ViewModel.StatusMessage = $"已插入音频：{fileName} ({data.Length / 1024.0:F1} KB)";
    }

    private static string GetAudioMimeType(string fileName)
    {
        var ext = Path.GetExtension(fileName).ToLowerInvariant();
        return ext switch
        {
            ".mp3" => "audio/mpeg",
            ".wav" => "audio/wav",
            ".m4a" => "audio/mp4",
            ".ogg" => "audio/ogg",
            ".aac" => "audio/aac",
            ".flac" => "audio/flac",
            _ => "application/octet-stream"
        };
    }

    // ===== 嵌入字体 =====

    private void EmbedFont_Click(object sender, RoutedEventArgs e)
    {
        var dialog = new OpenFileDialog
        {
            Filter = "字体文件|*.ttf;*.otf;*.woff;*.woff2|所有文件|*.*",
            Title = "选择要嵌入的字体文件"
        };

        if (dialog.ShowDialog() != true) return;

        var fileName = Path.GetFileName(dialog.FileName);
        var data = File.ReadAllBytes(dialog.FileName);

        var resource = new Epubra.Core.EpubResource
        {
            Kind = Epubra.Core.EpubResourceKind.Font,
            FileName = fileName,
            MimeType = GetFontMimeType(fileName),
            Data = data,
            Role = "font"
        };
        ViewModel.Book.Resources.Add(resource);

        // 添加到字体下拉框
        var fontName = Path.GetFileNameWithoutExtension(fileName);
        FontFamilyCombo.Items.Add(new ComboBoxItem { Content = fontName });

        ViewModel.StatusMessage = $"已嵌入字体：{fileName} ({data.Length / 1024.0:F1} KB)";
    }

    private static string GetFontMimeType(string fileName)
    {
        var ext = Path.GetExtension(fileName).ToLowerInvariant();
        return ext switch
        {
            ".ttf" => "font/ttf",
            ".otf" => "font/otf",
            ".woff" => "font/woff",
            ".woff2" => "font/woff2",
            _ => "application/octet-stream"
        };
    }

    // ===== 封面图片 =====

    private void ShowMetadata_Click(object sender, RoutedEventArgs e)
    {
        // 先保存当前编辑器内容
        if (ViewModel.SelectedChapter is not null)
        {
            SaveCurrentEditorContent(ViewModel.SelectedChapter);
        }

        var dialog = new MetadataDialog(ViewModel)
        {
            Owner = this
        };

        if (dialog.ShowDialog() == true)
        {
            ViewModel.StatusMessage = "书籍信息已更新";
        }
    }

    private void SetCover_Click(object sender, RoutedEventArgs e)
    {
        var dialog = new OpenFileDialog
        {
            Filter = "图片文件|*.png;*.jpg;*.jpeg;*.gif;*.bmp|所有文件|*.*",
            Title = "选择封面图片"
        };

        if (dialog.ShowDialog() != true) return;

        var filePath = dialog.FileName;
        var data = File.ReadAllBytes(filePath);
        var mimeType = GetImageMimeType(filePath);

        ViewModel.SetCoverImage(filePath, data, mimeType);
    }

    // ===== 一键排版 =====

    /// <summary>
    /// 对当前编辑器内容执行一键排版：
    /// 1. 提取所有段落文本 → 合并断行 → 清洗标点 → 重建段落
    /// 2. 清除格式（统一字体/字号/颜色）→ 设置首行缩进
    /// 3. 删除空段落
    /// 4. 保留图片/音频/表格等非文本块
    /// </summary>
    private void ApplyAutoFormat()
    {
        if (!Editor.IsEnabled || Editor.Document is null) return;

        var doc = Editor.Document;

        // Step 1: 收集所有段落文本，合并为整体文本进行排版
        var textBlocks = new List<(Block block, string text, bool isParagraph)> ();
        foreach (var block in doc.Blocks)
        {
            if (block is Paragraph para)
            {
                var text = GetParagraphPlainText(para);
                textBlocks.Add((block, text, true));
            }
            else
            {
                // 非段落块（图片/音频/表格/列表）保持原位
                textBlocks.Add((block, string.Empty, false));
            }
        }

        // Step 2: 将段落文本按顺序拼接，用换行分隔，交给 TextFormatter 整体格式化
        var sb = new StringBuilder();
        foreach (var (block, text, isParagraph) in textBlocks)
        {
            if (isParagraph)
            {
                if (sb.Length > 0) sb.Append('\n');
                sb.Append(text);
            }
            else
            {
                // 非文本块前后加空行作为段落分隔
                if (sb.Length > 0) sb.Append("\n\n");
                sb.Append("\u0001NONTEXT\u0001");  // 占位符
                sb.Append("\n\n");
            }
        }

        var formatted = Epubra.Epub.TextFormatter.Format(sb.ToString());

        // Step 3: 清空文档，用格式化后的文本重建段落
        doc.Blocks.Clear();

        // 保留非文本块的列表（按原始顺序）
        var nonTextBlocks = textBlocks
            .Where(t => !t.isParagraph)
            .Select(t => t.block)
            .ToList();
        var nonTextIdx = 0;

        var paragraphs = formatted.Split(new[] { "\n\n" }, StringSplitOptions.RemoveEmptyEntries);
        foreach (var paraText in paragraphs)
        {
            if (paraText.Trim() == "\u0001NONTEXT\u0001")
            {
                // 插入非文本块
                if (nonTextIdx < nonTextBlocks.Count)
                {
                    doc.Blocks.Add(nonTextBlocks[nonTextIdx++]);
                }
                continue;
            }

            var cleaned = paraText.Trim();
            if (string.IsNullOrWhiteSpace(cleaned)) continue;

            var para = new Paragraph
            {
                FontSize = 16,
                FontFamily = new FontFamily("宋体"),
                FontWeight = FontWeights.Normal,
                FontStyle = FontStyles.Normal,
                TextAlignment = TextAlignment.Left,
                TextIndent = 32  // 首行缩进 2 字符（16px × 2 = 32px）
            };
            para.Inlines.Add(new Run(cleaned));
            doc.Blocks.Add(para);
        }

        // 如果还有剩余的非文本块，追加到末尾
        while (nonTextIdx < nonTextBlocks.Count)
        {
            doc.Blocks.Add(nonTextBlocks[nonTextIdx++]);
        }

        Editor.Focus();
        UpdateWordCount();
    }

    /// <summary>提取段落中所有 Run 的纯文本。</summary>
    private static string GetParagraphPlainText(Paragraph para)
    {
        var sb = new StringBuilder();
        foreach (var inline in para.Inlines)
        {
            if (inline is Run run)
                sb.Append(run.Text);
            else if (inline is LineBreak)
                sb.Append('\n');
        }
        return sb.ToString();
    }

    // ===== 查找替换 =====

    private void ToggleFindReplace()
    {
        if (FindReplaceBar.Visibility == Visibility.Visible)
        {
            FindReplaceBar.Visibility = Visibility.Collapsed;
        }
        else
        {
            FindReplaceBar.Visibility = Visibility.Visible;
            FindTextBox.Focus();
            FindTextBox.SelectAll();
        }
    }

    private void FindReplace_Click(object sender, RoutedEventArgs e)
    {
        ToggleFindReplace();
    }

    private void CloseFindReplace_Click(object sender, RoutedEventArgs e)
    {
        FindReplaceBar.Visibility = Visibility.Collapsed;
        Editor.Focus();
    }

    private void FindTextBox_KeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Enter)
        {
            FindNext_Click(sender, e);
            e.Handled = true;
        }
        else if (e.Key == Key.Escape)
        {
            FindReplaceBar.Visibility = Visibility.Collapsed;
            Editor.Focus();
            e.Handled = true;
        }
    }

    private void FindNext_Click(object sender, RoutedEventArgs e)
    {
        var searchText = FindTextBox.Text;
        if (string.IsNullOrEmpty(searchText)) return;

        if (FindInEditor(searchText, forward: true))
        {
            ViewModel.StatusMessage = $"已找到：{searchText}";
        }
        else
        {
            ViewModel.StatusMessage = $"未找到：{searchText}";
        }
    }

    private void FindPrev_Click(object sender, RoutedEventArgs e)
    {
        var searchText = FindTextBox.Text;
        if (string.IsNullOrEmpty(searchText)) return;

        if (FindInEditor(searchText, forward: false))
        {
            ViewModel.StatusMessage = $"已找到：{searchText}";
        }
        else
        {
            ViewModel.StatusMessage = $"未找到：{searchText}";
        }
    }

    private void ReplaceOne_Click(object sender, RoutedEventArgs e)
    {
        var searchText = FindTextBox.Text;
        var replaceText = ReplaceTextBox.Text;
        if (string.IsNullOrEmpty(searchText)) return;

        var sel = Editor.Selection;
        if (!sel.IsEmpty && sel.Text == searchText)
        {
            sel.Text = replaceText;
            ViewModel.StatusMessage = "已替换";
        }

        // 查找下一个
        FindNext_Click(sender, e);
    }

    private void ReplaceAll_Click(object sender, RoutedEventArgs e)
    {
        var searchText = FindTextBox.Text;
        var replaceText = ReplaceTextBox.Text;
        if (string.IsNullOrEmpty(searchText)) return;

        var count = 0;
        var current = Editor.Document.ContentStart;
        var end = Editor.Document.ContentEnd;

        while (current is not null)
        {
            var result = FindTextRange(current, end, searchText);
            if (result is null) break;

            result.Text = replaceText;
            count++;
            current = result.End.GetPositionAtOffset(replaceText.Length + 1);
        }

        ViewModel.StatusMessage = count > 0 ? $"已替换 {count} 处" : "未找到匹配项";
        UpdateWordCount();
    }

    /// <summary>在编辑器中查找文本，选中并滚动到匹配位置。</summary>
    private bool FindInEditor(string searchText, bool forward)
    {
        var start = forward
            ? (Editor.Selection.End ?? Editor.Document.ContentStart)
            : (Editor.Selection.Start ?? Editor.Document.ContentEnd);

        var range = FindTextRange(start, Editor.Document.ContentEnd, searchText);
        if (range is null && forward)
        {
            // 从头搜索（环绕）
            range = FindTextRange(Editor.Document.ContentStart, Editor.Document.ContentEnd, searchText);
        }

        if (range is not null)
        {
            Editor.Selection.Select(range.Start, range.End);
            // 滚动到匹配位置
            var rect = range.Start.GetCharacterRect(LogicalDirection.Forward);
            Editor.ScrollToVerticalOffset(rect.Top - Editor.ViewportHeight / 2);
            Editor.Focus();
            return true;
        }

        return false;
    }

    /// <summary>在指定范围内查找文本，返回匹配的 TextRange。</summary>
    private TextRange? FindTextRange(TextPointer start, TextPointer end, string searchText)
    {
        var current = start;
        while (current is not null)
        {
            var next = current.GetPositionAtOffset(searchText.Length);
            if (next is null || next.CompareTo(end) > 0) break;

            var range = new TextRange(current, next);
            if (range.Text == searchText)
            {
                return range;
            }

            current = current.GetNextInsertionPosition(LogicalDirection.Forward);
        }

        return null;
    }

    // ===== 编辑器辅助 =====

    private Paragraph? GetCurrentParagraph()
    {
        var pos = Editor.CaretPosition;
        return pos?.Paragraph;
    }

    private void Editor_SelectionChanged(object sender, RoutedEventArgs e)
    {
        UpdateWordCount();
        UpdateToggleStates();
    }

    /// <summary>根据当前选区更新工具栏 Toggle 按钮状态。</summary>
    private void UpdateToggleStates()
    {
        if (Editor is null || !Editor.IsEnabled) return;

        var sel = Editor.Selection;

        // 加粗
        var fw = sel.GetPropertyValue(Inline.FontWeightProperty);
        BoldToggle.IsChecked = fw is FontWeight weight && weight == FontWeights.Bold;

        // 斜体
        var fs = sel.GetPropertyValue(Inline.FontStyleProperty);
        ItalicToggle.IsChecked = fs is FontStyle style && style == FontStyles.Italic;

        // 下划线 / 删除线
        var td = sel.GetPropertyValue(Inline.TextDecorationsProperty);
        if (td is TextDecorationCollection tdc)
        {
            UnderlineToggle.IsChecked = tdc.Contains(TextDecorations.Underline[0]);
            StrikeToggle.IsChecked = tdc.Contains(TextDecorations.Strikethrough[0]);
        }
        else
        {
            UnderlineToggle.IsChecked = false;
            StrikeToggle.IsChecked = false;
        }

        // 对齐
        var para = GetCurrentParagraph();
        if (para is not null)
        {
            AlignLeftToggle.IsChecked = para.TextAlignment == TextAlignment.Left;
            AlignCenterToggle.IsChecked = para.TextAlignment == TextAlignment.Center;
            AlignRightToggle.IsChecked = para.TextAlignment == TextAlignment.Right;
            AlignJustifyToggle.IsChecked = para.TextAlignment == TextAlignment.Justify;
        }
    }

    /// <summary>更新当前章节字数统计。</summary>
    private void UpdateWordCount()
    {
        if (!Editor.IsEnabled || Editor.Document is null)
        {
            ViewModel.UpdateWordCount(0, 0);
            return;
        }

        var text = new TextRange(Editor.Document.ContentStart, Editor.Document.ContentEnd).Text;
        var charCount = text.Length;
        var wordCount = CountWords(text);
        ViewModel.UpdateWordCount(charCount, wordCount);
    }

    /// <summary>统计文本字数：中文每字算一个，英文按单词算。</summary>
    private static int CountWords(string text)
    {
        if (string.IsNullOrEmpty(text)) return 0;

        var count = 0;
        var inWord = false;

        foreach (var c in text)
        {
            if (c >= '\u4e00' && c <= '\u9fff')
            {
                // 中文字符：每字算一个
                count++;
                inWord = false;
            }
            else if (char.IsLetterOrDigit(c))
            {
                if (!inWord)
                {
                    count++;
                    inWord = true;
                }
            }
            else
            {
                inWord = false;
            }
        }

        return count;
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

    // ===== 菜单事件 =====

    private void Save_Executed(object sender, ExecutedRoutedEventArgs e)
    {
        if (ViewModel.SelectedChapter is not null)
        {
            SaveCurrentEditorContent(ViewModel.SelectedChapter);
            ViewModel.StatusMessage = "已保存当前章节";
        }
    }

    private void Undo_Executed(object sender, ExecutedRoutedEventArgs e)
    {
        Editor.Undo();
    }

    private void Redo_Executed(object sender, ExecutedRoutedEventArgs e)
    {
        Editor.Redo();
    }

    private void Exit_Click(object sender, RoutedEventArgs e)
    {
        Close();
    }

    private void About_Click(object sender, RoutedEventArgs e)
    {
        MessageBox.Show(
            "Epubra - EPUB 编辑器\n\n" +
            "技术栈：WPF + .NET 8\n" +
            "P4 版本 - Ribbon 工具栏\n\n" +
            "Ribbon 功能区：\n" +
            "  开始：剪贴板、字体、段落、样式、编辑\n" +
            "  插入：图片、超链接、水平线、表格\n" +
            "  章节：章节管理、排序\n" +
            "  输出：EPUB/TXT/HTML 导出、项目文件\n" +
            "  视图：预览切换、撤销/重做\n\n" +
            "支持：EPUB 导入/导出、封面设置、查找替换、\n" +
            "字体嵌入、拖拽排序、字数统计",
            "关于 Epubra",
            MessageBoxButton.OK,
            MessageBoxImage.Information);
    }

    protected override void OnClosing(System.ComponentModel.CancelEventArgs e)
    {
        // 退订主题事件，避免窗口关闭后仍触发回调
        ThemeService.ThemeChanged -= OnThemeChanged;
        // 退出前保存当前章节
        if (ViewModel.SelectedChapter is not null)
        {
            SaveCurrentEditorContent(ViewModel.SelectedChapter);
        }
        base.OnClosing(e);
    }

    // ===== 自定义标题栏 / 窗口控制（WPS 风格） =====

    /// <summary>标题栏空白处拖动窗口；命中交互控件（按钮/下拉）时不触发，保证按钮可点。</summary>
    private void TitleBar_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        if (IsTitleBarInteractive(e.OriginalSource)) return;
        if (e.ChangedButton != MouseButton.Left) return;
        if (e.ClickCount == 2)
        {
            ToggleMaximizeRestore();
            return;
        }
        DragMove();
    }

    private static bool IsTitleBarInteractive(object source)
    {
        var d = source as DependencyObject;
        while (d != null)
        {
            if (d is Button or System.Windows.Controls.Primitives.ToggleButton or ComboBox) return true;
            d = VisualTreeHelper.GetParent(d);
        }
        return false;
    }

    private void Minimize_Click(object sender, RoutedEventArgs e) => SystemCommands.MinimizeWindow(this);
    private void Close_Click(object sender, RoutedEventArgs e) => SystemCommands.CloseWindow(this);
    private void MaximizeRestore_Click(object sender, RoutedEventArgs e) => ToggleMaximizeRestore();

    private void ToggleMaximizeRestore()
    {
        if (WindowState == WindowState.Maximized)
            SystemCommands.RestoreWindow(this);
        else
            SystemCommands.MaximizeWindow(this);
        SyncMaxRestoreIcon();
    }

    private void SyncMaxRestoreIcon()
    {
        if (MaxRestoreIcon == null) return;
        MaxRestoreIcon.Data = WindowState == WindowState.Maximized
            ? (Geometry)FindResource("IconRestore")
            : (Geometry)FindResource("IconMaximize");
        if (MaxRestoreBtn != null)
            MaxRestoreBtn.ToolTip = WindowState == WindowState.Maximized ? "还原" : "最大化";
    }

    /// <summary>标题栏主题按钮：在 Light / Dark / Sepia 之间循环切换。</summary>
    private void TitleBarTheme_Click(object sender, RoutedEventArgs e)
    {
        var next = ThemeService.Current switch
        {
            ThemeKind.Light => ThemeKind.Dark,
            ThemeKind.Dark => ThemeKind.Sepia,
            _ => ThemeKind.Light
        };
        ThemeService.Apply(next);
        SyncThemeCombo();
    }

    // ===== 视图缩放（WPS 风格状态栏） =====

    private const double ZoomMin = 0.5;
    private const double ZoomMax = 3.0;
    private double _zoom = 1.0;

    private void ApplyZoom()
    {
        _zoom = Math.Max(ZoomMin, Math.Min(ZoomMax, _zoom));
        Editor.LayoutTransform = new ScaleTransform(_zoom, _zoom);
        if (ZoomLabel != null)
            ZoomLabel.Text = $"{Math.Round(_zoom * 100)}%";
        // 编辑区缩放同步到预览区
        _ = ApplyPreviewZoom();
    }

    /// <summary>
    /// 将当前缩放应用到 WebView2 预览区（通过注入 CSS zoom 实现）。
    /// CoreWebView2 未就绪时静默跳过，待初始化或内容重新加载后再次应用。
    /// </summary>
    private async Task ApplyPreviewZoom()
    {
        if (!_webViewReady || PreviewViewer.CoreWebView2 is null) return;
        var pct = (_zoom * 100).ToString(System.Globalization.CultureInfo.InvariantCulture);
        try
        {
            await PreviewViewer.CoreWebView2.ExecuteScriptAsync(
                $"document.documentElement.style.zoom = '{pct}%'");
        }
        catch
        {
            // 预览脚本注入失败不影响编辑主流程
        }
    }

    private void ZoomIn_Click(object sender, RoutedEventArgs e)
    {
        _zoom += 0.1;
        ApplyZoom();
    }

    private void ZoomOut_Click(object sender, RoutedEventArgs e)
    {
        _zoom -= 0.1;
        ApplyZoom();
    }

    private void ZoomFit_Click(object sender, RoutedEventArgs e)
    {
        _zoom = 1.0;
        ApplyZoom();
    }

    // ===== 最大化约束（不覆盖任务栏） =====

    private void OnSourceInitialized(object? sender, EventArgs e)
    {
        if (PresentationSource.FromVisual(this) is HwndSource hwndSource)
        {
            hwndSource.AddHook(WndProc);
        }
    }

    private IntPtr WndProc(IntPtr hwnd, int msg, IntPtr wParam, IntPtr lParam, ref bool handled)
    {
        const int WM_GETMINMAXINFO = 0x0024;
        if (msg == WM_GETMINMAXINFO)
        {
            var mmi = Marshal.PtrToStructure<MINMAXINFO>(lParam);
            // 用工作区（排除任务栏）约束最大化后的位置与尺寸
            var work = SystemParameters.WorkArea;
            mmi.ptMaxPosition.X = (int)work.Left;
            mmi.ptMaxPosition.Y = (int)work.Top;
            mmi.ptMaxSize.X = (int)work.Width;
            mmi.ptMaxSize.Y = (int)work.Height;
            Marshal.StructureToPtr(mmi, lParam, true);
            handled = true;
        }
        return IntPtr.Zero;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct POINT
    {
        public int X;
        public int Y;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct MINMAXINFO
    {
        public POINT ptReserved;
        public POINT ptMaxSize;
        public POINT ptMaxPosition;
        public POINT ptMinTrackSize;
        public POINT ptMaxTrackSize;
    }
}
