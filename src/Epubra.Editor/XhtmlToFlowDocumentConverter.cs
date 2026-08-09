using System.Xml.Linq;
using System.Windows;
using System.Windows.Documents;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.IO;

namespace Epubra.Editor;

/// <summary>
/// 把 XHTML 片段转换回 WPF FlowDocument。
/// 支持：段落、标题(h1-h4)、加粗/斜体/下划线、换行、列表、图片。
/// 用于从已保存的章节内容加载到编辑器。
/// </summary>
public static class XhtmlToFlowDocumentConverter
{
    /// <summary>
    /// 将 XHTML 片段转为 FlowDocument。
    /// 输入可以是完整 XHTML 文档或 body 内的片段。
    /// </summary>
    public static FlowDocument? Convert(string xhtml)
    {
        if (string.IsNullOrWhiteSpace(xhtml))
            return new FlowDocument();

        try
        {
            // 提取 body 内容（如果有完整文档包裹）
            var bodyContent = ExtractBodyContent(xhtml);
            if (string.IsNullOrWhiteSpace(bodyContent))
                return new FlowDocument();

            // 包装到根元素以便 XML 解析
            var wrapped = $"<root>{bodyContent}</root>";
            var doc = XDocument.Parse(wrapped, LoadOptions.PreserveWhitespace);

            var flowDoc = new FlowDocument();
            if (doc.Root is null) return flowDoc;

            foreach (var element in doc.Root.Elements())
            {
                var block = ConvertElementToBlock(element);
                if (block is not null)
                {
                    flowDoc.Blocks.Add(block);
                }
            }

            return flowDoc;
        }
        catch
        {
            // 解析失败时返回包含原始文本的文档
            var fallback = new FlowDocument();
            fallback.Blocks.Add(new Paragraph(new Run(xhtml)));
            return fallback;
        }
    }

    /// <summary>从 XHTML 文档中提取 body 标签内容。</summary>
    private static string ExtractBodyContent(string xhtml)
    {
        var trimmed = xhtml.TrimStart();
        if (!trimmed.StartsWith("<html", StringComparison.OrdinalIgnoreCase) &&
            !trimmed.StartsWith("<?xml", StringComparison.OrdinalIgnoreCase))
        {
            return xhtml; // 已经是片段
        }

        // 找 <body> 到 </body>
        var bodyStart = xhtml.IndexOf("<body", StringComparison.OrdinalIgnoreCase);
        if (bodyStart < 0) return xhtml;

        var contentStart = xhtml.IndexOf('>', bodyStart);
        if (contentStart < 0) return xhtml;
        contentStart++;

        var bodyEnd = xhtml.IndexOf("</body>", StringComparison.OrdinalIgnoreCase);
        if (bodyEnd < 0) return xhtml.Substring(contentStart);

        return xhtml.Substring(contentStart, bodyEnd - contentStart);
    }

    private static Block? ConvertElementToBlock(XElement element)
    {
        var tag = element.Name.LocalName.ToLowerInvariant();

        return tag switch
        {
            "p" => ConvertParagraph(element),
            "h1" or "h2" or "h3" or "h4" or "h5" or "h6" => ConvertHeading(element, tag),
            "ul" or "ol" => ConvertList(element, tag == "ol"),
            "table" => ConvertTable(element),
            "div" => ConvertDiv(element),
            "img" => ConvertImageBlock(element),
            "audio" => ConvertAudioBlock(element),
            _ => null
        };
    }

    private static Paragraph ConvertParagraph(XElement element)
    {
        var para = new Paragraph();

        // 对齐方式
        var style = element.Attribute("style")?.Value;
        if (style is not null)
        {
            if (style.Contains("text-align:center")) para.TextAlignment = TextAlignment.Center;
            else if (style.Contains("text-align:right")) para.TextAlignment = TextAlignment.Right;
            else if (style.Contains("text-align:justify")) para.TextAlignment = TextAlignment.Justify;
        }

        ConvertInlines(element, para.Inlines);
        return para;
    }

    private static Paragraph ConvertHeading(XElement element, string tag)
    {
        var para = new Paragraph();
        var (size, weight) = tag switch
        {
            "h1" => (28.0, FontWeights.Bold),
            "h2" => (24.0, FontWeights.Bold),
            "h3" => (20.0, FontWeights.Bold),
            "h4" => (18.0, FontWeights.Bold),
            "h5" => (16.0, FontWeights.Bold),
            _ => (16.0, FontWeights.Normal)
        };
        para.FontSize = size;
        para.FontWeight = weight;

        ConvertInlines(element, para.Inlines);
        return para;
    }

    private static List ConvertList(XElement element, bool ordered)
    {
        var list = new List
        {
            MarkerStyle = ordered ? TextMarkerStyle.Decimal : TextMarkerStyle.Disc
        };

        foreach (var li in element.Elements().Where(e => e.Name.LocalName == "li"))
        {
            var listItem = new ListItem();
            var para = new Paragraph();
            ConvertInlines(li, para.Inlines);
            listItem.Blocks.Add(para);
            list.ListItems.Add(listItem);
        }

        return list;
    }

    private static Table? ConvertTable(XElement element)
    {
        var table = new Table();
        var rowGroup = new TableRowGroup();

        foreach (var tr in element.Elements().Where(e => e.Name.LocalName == "tr"))
        {
            var row = new TableRow();
            foreach (var cell in tr.Elements().Where(e => e.Name.LocalName is "td" or "th"))
            {
                var tableCell = new TableCell();
                var para = new Paragraph();
                ConvertInlines(cell, para.Inlines);
                tableCell.Blocks.Add(para);
                row.Cells.Add(tableCell);
            }
            rowGroup.Rows.Add(row);
        }

        table.RowGroups.Add(rowGroup);
        return table;
    }

    private static Block? ConvertDiv(XElement element)
    {
        // div 作为容器，把子元素转成 block
        var section = new Section();
        foreach (var child in element.Elements())
        {
            var block = ConvertElementToBlock(child);
            if (block is not null) section.Blocks.Add(block);
        }
        return section.Blocks.Count > 0 ? section : null;
    }

    private static Block ConvertImageBlock(XElement element)
    {
        var para = new Paragraph();
        var inline = CreateImageInline(element);
        if (inline is not null) para.Inlines.Add(inline);
        return para;
    }

    /// <summary>把 &lt;audio&gt; 标签转换为 FlowDocument 中的可视化占位符。</summary>
    private static Block ConvertAudioBlock(XElement element)
    {
        // 优先取 src 属性，其次取 <source> 子元素的 src
        var src = element.Attribute("src")?.Value;
        if (string.IsNullOrEmpty(src))
        {
            var sourceEl = element.Elements().FirstOrDefault(e => e.Name.LocalName == "source");
            src = sourceEl?.Attribute("src")?.Value;
        }

        var displayName = string.IsNullOrEmpty(src) ? "音频" : Path.GetFileName(src);

        var border = new System.Windows.Controls.Border
        {
            Background = GetThemeBrush("SurfaceAccentSubtle", System.Windows.Media.Brushes.AliceBlue),
            BorderBrush = GetThemeBrush("BorderAccent", System.Windows.Media.Brushes.SteelBlue),
            BorderThickness = new Thickness(1),
            Padding = new Thickness(12, 6, 12, 6),
            CornerRadius = new CornerRadius(4),
            Tag = $"audio:{src}"
        };

        var panel = new System.Windows.Controls.StackPanel
        {
            Orientation = System.Windows.Controls.Orientation.Horizontal
        };
        panel.Children.Add(new System.Windows.Controls.TextBlock
        {
            Text = "\uD83C\uDFA7 ",  // 🔇 emoji
            FontSize = 18,
            VerticalAlignment = VerticalAlignment.Center
        });
        panel.Children.Add(new System.Windows.Controls.TextBlock
        {
            Text = displayName,
            FontSize = 14,
            Foreground = GetThemeBrush("TextAccent", System.Windows.Media.Brushes.SteelBlue),
            VerticalAlignment = VerticalAlignment.Center
        });
        border.Child = panel;

        var container = new BlockUIContainer { Child = border };
        return container;
    }

    private static void ConvertInlines(XElement element, InlineCollection inlines)
    {
        foreach (var node in element.Nodes())
        {
            switch (node)
            {
                case XText text:
                    inlines.Add(new Run(text.Value));
                    break;
                case XElement child:
                    var inline = ConvertInlineElement(child);
                    if (inline is not null) inlines.Add(inline);
                    break;
            }
        }
    }

    private static Inline? ConvertInlineElement(XElement element)
    {
        var tag = element.Name.LocalName.ToLowerInvariant();

        return tag switch
        {
            "strong" or "b" => CreateBold(element),
            "em" or "i" => CreateItalic(element),
            "u" => CreateUnderline(element),
            "br" => new LineBreak(),
            "a" => CreateHyperlink(element),
            "img" => CreateImageInline(element),
            "audio" => CreateAudioInline(element),
            "span" => CreateSpan(element),
            _ => CreateTextRun(element)
        };
    }

    private static Inline CreateBold(XElement element)
    {
        var bold = new Bold();
        ConvertInlines(element, bold.Inlines);
        return bold;
    }

    private static Inline CreateItalic(XElement element)
    {
        var italic = new Italic();
        ConvertInlines(element, italic.Inlines);
        return italic;
    }

    private static Inline CreateUnderline(XElement element)
    {
        var underline = new Underline();
        ConvertInlines(element, underline.Inlines);
        return underline;
    }

    private static Inline CreateHyperlink(XElement element)
    {
        var href = element.Attribute("href")?.Value ?? "#";
        var link = new Hyperlink { NavigateUri = new Uri(href, UriKind.RelativeOrAbsolute) };
        ConvertInlines(element, link.Inlines);
        return link;
    }

    private static Inline CreateSpan(XElement element)
    {
        var span = new Span();
        // 解析 style 属性
        var style = element.Attribute("style")?.Value;
        if (style is not null)
        {
            if (style.Contains("font-family:"))
            {
                var family = ExtractCssValue(style, "font-family");
                if (family is not null) span.FontFamily = new FontFamily(family);
            }
            if (style.Contains("font-size:"))
            {
                var sizeStr = ExtractCssValue(style, "font-size");
                if (sizeStr is not null && double.TryParse(sizeStr.Replace("px", "").Trim(), out var size))
                    span.FontSize = size;
            }
            if (style.Contains("color:#"))
            {
                var colorStr = ExtractCssValue(style, "color");
                if (colorStr is not null && colorStr.StartsWith("#"))
                {
                    try
                    {
                        var color = (Color)ColorConverter.ConvertFromString(colorStr);
                        span.Foreground = new SolidColorBrush(color);
                    }
                    catch { }
                }
            }
        }
        ConvertInlines(element, span.Inlines);
        return span;
    }

    private static Inline CreateTextRun(XElement element)
    {
        var text = element.Value;
        return new Run(text);
    }

    private static Inline? CreateImageInline(XElement element)
    {
        var src = element.Attribute("src")?.Value;
        if (string.IsNullOrEmpty(src)) return null;

        // 尝试加载图片（仅本地文件路径）
        BitmapSource? bitmap = null;
        try
        {
            if (File.Exists(src))
            {
                var bmp = new BitmapImage();
                bmp.BeginInit();
                bmp.UriSource = new Uri(src, UriKind.Absolute);
                bmp.CacheOption = BitmapCacheOption.OnLoad;
                bmp.EndInit();
                bitmap = bmp;
            }
        }
        catch
        {
            // 图片加载失败，显示占位符
        }

        if (bitmap is not null)
        {
            var image = new System.Windows.Controls.Image
            {
                Source = bitmap,
                Stretch = System.Windows.Media.Stretch.Uniform
            };

            var widthAttr = element.Attribute("width")?.Value;
            if (widthAttr is not null && double.TryParse(widthAttr, out var w) && w > 0)
                image.Width = w;
            else
                image.Width = Math.Min(bitmap.Width, 400);

            return new InlineUIContainer { Child = image };
        }

        // 占位符
        return new Run($"[图片: {src}]");
    }

    /// <summary>为内联 &lt;audio&gt; 标签创建占位符 Run。</summary>
    private static Inline CreateAudioInline(XElement element)
    {
        var src = element.Attribute("src")?.Value;
        if (string.IsNullOrEmpty(src))
        {
            var sourceEl = element.Elements().FirstOrDefault(e => e.Name.LocalName == "source");
            src = sourceEl?.Attribute("src")?.Value;
        }
        var name = string.IsNullOrEmpty(src) ? "音频" : Path.GetFileName(src);
        return new Run($"[音频: {name}]");
    }

    private static string? ExtractCssValue(string css, string property)
    {
        var idx = css.IndexOf(property + ":", StringComparison.OrdinalIgnoreCase);
        if (idx < 0) return null;
        var start = idx + property.Length + 1;
        var end = css.IndexOf(';', start);
        if (end < 0) end = css.Length;
        return css.Substring(start, end - start).Trim();
    }

    /// <summary>
    /// 从应用资源中按 key 取主题语义画刷；找不到或资源系统未初始化时回退到 fallback，
    /// 保证在非 WPF 宿主（如单元测试）中也不会抛空引用。
    /// </summary>
    private static System.Windows.Media.Brush GetThemeBrush(string key, System.Windows.Media.Brush fallback)
    {
        var brush = System.Windows.Application.Current?.TryFindResource(key) as System.Windows.Media.Brush;
        return brush ?? fallback;
    }
}
