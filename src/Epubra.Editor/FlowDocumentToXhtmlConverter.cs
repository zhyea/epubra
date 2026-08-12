using System.Globalization;
using System.Text;
using System.Windows;
using System.Windows.Documents;
using System.Windows.Media;

namespace Epubra.Editor;

/// <summary>
/// 把 WPF RichTextBox 的 FlowDocument 转换为 EPUB 用的 XHTML 片段。
/// 支持：段落、标题层级（Heading 1-6）、加粗/斜体/下划线、字体、字号、颜色、
///       图片（InlineUIContainer）、列表、引用、超链接、换行。
/// </summary>
public static class FlowDocumentToXhtmlConverter
{
    /// <summary>
    /// 将 FlowDocument 转为 XHTML 片段（不含 html/body 包裹，由 EpubWriter 负责包裹）。
    /// </summary>
    public static string Convert(FlowDocument document)
    {
        ArgumentNullException.ThrowIfNull(document);

        var sb = new StringBuilder();
        foreach (var block in document.Blocks)
        {
            ConvertBlock(block, sb, 0);
        }

        // 清理多余空行
        return sb.ToString().TrimEnd('\n', '\r', ' ', '\t');
    }

    private static void ConvertBlock(Block block, StringBuilder sb, int indent)
    {
        switch (block)
        {
            case Paragraph para:
                ConvertParagraph(para, sb, indent);
                break;

            case Section section:
                foreach (var child in section.Blocks)
                {
                    ConvertBlock(child, sb, indent);
                }
                break;

            case List list:
                ConvertList(list, sb, indent);
                break;

            case Table table:
                ConvertTable(table, sb, indent);
                break;

            case BlockUIContainer uiContainer:
                // 块级 UI 容器（图片或音频占位符）
                if (uiContainer.Child is System.Windows.Controls.Image img && img.Source is System.Windows.Media.Imaging.BitmapSource bmp)
                {
                    var src = img.GetValue(System.Windows.Controls.Image.SourceProperty)?.ToString() ?? string.Empty;
                    sb.Append("<img src=\"").Append(EscapeAttribute(src)).Append("\"");
                    if (img.Width > 0) sb.Append(" width=\"").Append((int)img.Width).Append("\"");
                    if (img.Height > 0) sb.Append(" height=\"").Append((int)img.Height).Append("\"");
                    sb.Append(" alt=\"\"/>\n");
                }
                else if (uiContainer.Child is System.Windows.Controls.Border border && border.Tag is string tag && tag.StartsWith("audio:"))
                {
                    var audioSrc = tag["audio:".Length..];
                    sb.Append("<audio src=\"").Append(EscapeAttribute(audioSrc)).Append("\" controls=\"controls\"/>\n");
                }
                else if (uiContainer.Child is System.Windows.Controls.Border border2 && border2.Tag is string vtag && vtag.StartsWith("video:"))
                {
                    var videoSrc = vtag["video:".Length..];
                    sb.Append("<video src=\"").Append(EscapeAttribute(videoSrc)).Append("\" controls=\"controls\"/>\n");
                }
                break;
        }
    }

    private static void ConvertParagraph(Paragraph para, StringBuilder sb, int indent)
    {
        var tagName = GetHeadingTagName(para);
        var indentStr = new string(' ', indent * 2);

        if (tagName is not null)
        {
            // 标题：h1-h6
            sb.Append(indentStr).Append('<').Append(tagName).Append('>');
            ConvertInlines(para.Inlines, sb);
            sb.Append("</").Append(tagName).Append(">\n");
        }
        else
        {
            // 普通段落
            var styleAttr = BuildParagraphStyle(para);
            if (styleAttr.Length > 0)
            {
                sb.Append(indentStr).Append("<p").Append(styleAttr).Append('>');
            }
            else
            {
                sb.Append(indentStr).Append("<p>");
            }

            ConvertInlines(para.Inlines, sb);
            sb.Append("</p>\n");
        }
    }

    private static void ConvertInlines(InlineCollection inlines, StringBuilder sb)
    {
        foreach (var inline in inlines)
        {
            ConvertInline(inline, sb);
        }
    }

    private static void ConvertInline(Inline inline, StringBuilder sb)
    {
        switch (inline)
        {
            case Run run:
                ConvertRun(run, sb);
                break;

            case Bold bold:
                sb.Append("<strong>");
                ConvertInlines(bold.Inlines, sb);
                sb.Append("</strong>");
                break;

            case Italic italic:
                sb.Append("<em>");
                ConvertInlines(italic.Inlines, sb);
                sb.Append("</em>");
                break;

            case Underline underline:
                sb.Append("<u>");
                ConvertInlines(underline.Inlines, sb);
                sb.Append("</u>");
                break;

            case Hyperlink link:
                var href = link.NavigateUri?.ToString() ?? "#";
                sb.Append("<a href=\"").Append(EscapeAttribute(href)).Append("\">");
                ConvertInlines(link.Inlines, sb);
                sb.Append("</a>");
                break;

            case LineBreak:
                sb.Append("<br/>");
                break;

            case InlineUIContainer uiContainer:
                ConvertInlineUIContainer(uiContainer, sb, 0);
                break;

            case Span span:
                ConvertSpan(span, sb);
                break;
        }
    }

    private static void ConvertRun(Run run, StringBuilder sb)
    {
        if (string.IsNullOrEmpty(run.Text))
        {
            return;
        }

        // 收集 Run 的样式属性
        var style = BuildInlineStyle(run);
        if (style.Length > 0)
        {
            sb.Append("<span").Append(style).Append('>');
            sb.Append(EscapeText(run.Text));
            sb.Append("</span>");
        }
        else
        {
            sb.Append(EscapeText(run.Text));
        }
    }

    private static void ConvertSpan(Span span, StringBuilder sb)
    {
        var style = BuildInlineStyle(span);
        if (style.Length > 0)
        {
            sb.Append("<span").Append(style).Append('>');
            ConvertInlines(span.Inlines, sb);
            sb.Append("</span>");
        }
        else
        {
            ConvertInlines(span.Inlines, sb);
        }
    }

    private static void ConvertInlineUIContainer(InlineUIContainer container, StringBuilder sb, int indent)
    {
        // 如果包含 Image，提取 Source 和尺寸
        if (container.Child is System.Windows.Controls.Image image && image.Source is System.Windows.Media.Imaging.BitmapSource bitmap)
        {
            var src = image.GetValue(System.Windows.Controls.Image.SourceProperty)?.ToString() ?? string.Empty;
            var width = (int)image.Width;
            var height = (int)image.Height;

            sb.Append("<img src=\"").Append(EscapeAttribute(src)).Append("\"");
            if (width > 0) sb.Append(" width=\"").Append(width).Append("\"");
            if (height > 0) sb.Append(" height=\"").Append(height).Append("\"");
            sb.Append(" alt=\"\"/>");
        }
    }

    private static void ConvertList(List list, StringBuilder sb, int indent)
    {
        var indentStr = new string(' ', indent * 2);
        var tag = list.MarkerStyle == TextMarkerStyle.Disc || list.MarkerStyle == TextMarkerStyle.Circle || list.MarkerStyle == TextMarkerStyle.Square ? "ul" : "ol";

        sb.Append(indentStr).Append('<').Append(tag).Append(">\n");

        foreach (var item in list.ListItems)
        {
            sb.Append(indentStr).Append("  <li>");
            foreach (var block in item.Blocks)
            {
                if (block is Paragraph p)
                {
                    ConvertInlines(p.Inlines, sb);
                }
                else
                {
                    ConvertBlock(block, sb, indent + 1);
                }
            }
            sb.Append("</li>\n");
        }

        sb.Append(indentStr).Append("</").Append(tag).Append(">\n");
    }

    private static void ConvertTable(Table table, StringBuilder sb, int indent)
    {
        var indentStr = new string(' ', indent * 2);
        sb.Append(indentStr).Append("<table>\n");

        foreach (var rowGroup in table.RowGroups)
        foreach (var row in rowGroup.Rows)
        {
            sb.Append(indentStr).Append("  <tr>\n");
            foreach (var cell in row.Cells)
            {
                var tag = cell.Tag?.ToString() == "header" ? "th" : "td";
                sb.Append(indentStr).Append("    <").Append(tag).Append('>');
                foreach (var block in cell.Blocks)
                {
                    if (block is Paragraph p)
                    {
                        ConvertInlines(p.Inlines, sb);
                    }
                }
                sb.Append("</").Append(tag).Append(">\n");
            }
            sb.Append(indentStr).Append("  </tr>\n");
        }

        sb.Append(indentStr).Append("</table>\n");
    }

    // ===== 辅助方法 =====

    private static string? GetHeadingTagName(Paragraph para)
    {
        // 通过 FontSize 推断标题层级（WPF FlowDocument 没有内置 Heading 级别属性）
        if (para.FontSize >= 28) return "h1";
        if (para.FontSize >= 24) return "h2";
        if (para.FontSize >= 20) return "h3";
        if (para.FontSize >= 18) return "h4";
        return null;
    }

    private static string BuildParagraphStyle(Paragraph para)
    {
        var parts = new List<string>();

        if (para.TextAlignment == TextAlignment.Center)
            parts.Add("text-align:center");
        else if (para.TextAlignment == TextAlignment.Right)
            parts.Add("text-align:right");
        else if (para.TextAlignment == TextAlignment.Justify)
            parts.Add("text-align:justify");

        // 首行缩进
        if (para.TextIndent > 0)
        {
            var emWidth = para.TextIndent / 16.0;
            parts.Add($"text-indent:{emWidth.ToString("F1", CultureInfo.InvariantCulture)}em");
        }

        return parts.Count > 0 ? $" style=\"{string.Join(";", parts)}\"" : string.Empty;
    }

    private static string BuildInlineStyle(Inline inline)
    {
        var parts = new List<string>();

        // 字体
        if (!string.IsNullOrEmpty(inline.FontFamily?.Source) && inline.FontFamily.Source != "宋体")
        {
            parts.Add($"font-family:{EscapeCss(inline.FontFamily.Source)}");
        }

        // 字号
        if (inline.FontSize > 0 && Math.Abs(inline.FontSize - 16) > 0.1)
        {
            var pxSize = inline.FontSize;
            parts.Add($"font-size:{pxSize.ToString("F0", CultureInfo.InvariantCulture)}px");
        }

        // 颜色：仅当用户显式设置了前景色才写入导出，避免把主题默认文字色写死进 EPUB
        var fgLocal = inline.ReadLocalValue(Inline.ForegroundProperty);
        if (fgLocal is SolidColorBrush brush && brush.Color != Colors.Black)
        {
            parts.Add($"color:#{brush.Color.R:X2}{brush.Color.G:X2}{brush.Color.B:X2}");
        }

        // 背景色：同理，仅显式设置且非白色才写入
        var bgLocal = inline.ReadLocalValue(Inline.BackgroundProperty);
        if (bgLocal is SolidColorBrush bgBrush && bgBrush.Color != Colors.White)
        {
            parts.Add($"background-color:#{bgBrush.Color.R:X2}{bgBrush.Color.G:X2}{bgBrush.Color.B:X2}");
        }

        return parts.Count > 0 ? $" style=\"{string.Join(";", parts)}\"" : string.Empty;
    }

    private static string EscapeText(string text)
    {
        return text
            .Replace("&", "&amp;")
            .Replace("<", "&lt;")
            .Replace(">", "&gt;");
    }

    private static string EscapeAttribute(string value)
    {
        return value
            .Replace("&", "&amp;")
            .Replace("\"", "&quot;")
            .Replace("<", "&lt;")
            .Replace(">", "&gt;");
    }

    private static string EscapeCss(string value)
    {
        return value.Replace("\"", "\\\"").Replace(";", "\\;");
    }
}
