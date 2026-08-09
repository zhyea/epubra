namespace Epubra.Infrastructure;

/// <summary>
/// 界面主题种类。
/// 定义在 Infrastructure 层，供 Epubra.App（ThemeService）与 Infrastructure（SettingsService）
/// 共享，避免 Epubra.App → Epubra.Infrastructure → Epubra.App 的循环引用。
/// </summary>
public enum ThemeKind
{
    /// <summary>浅色（默认）。</summary>
    Light,

    /// <summary>深色。</summary>
    Dark,

    /// <summary>护眼（暖纸色）。</summary>
    Sepia,
}
