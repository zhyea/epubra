namespace Epubra.Export;

/// <summary>
/// 多格式导出器的入口点。
/// 目前支持 TXT 和 HTML 导出，未来可扩展 MOBI/PDF 等。
/// </summary>
public static class ExporterRegistry
{
    /// <summary>已注册的导出器实例。</summary>
    public static IReadOnlyDictionary<string, object> Exporters { get; } = new Dictionary<string, object>
    {
        ["txt"] = new TxtExporter(),
        ["html"] = new HtmlExporter()
    };

    /// <summary>支持的导出格式列表。</summary>
    public static IReadOnlyList<string> SupportedFormats { get; } = new[] { "txt", "html" };
}
