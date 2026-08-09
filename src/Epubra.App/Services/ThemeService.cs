using System.Windows;
using Epubra.Infrastructure;

namespace Epubra.App.Services;

/// <summary>
/// 主题服务：负责在运行时热替换应用级 <see cref="Application.Resources"/>.MergedDictionaries
/// 中的主题字典，并持久化用户选择。
///
/// 主题字典统一以 <c>__ThemeDictionary__</c> 布尔标记键标识，ThemeService 借此在
/// MergedDictionaries 中定位并整体替换，从而实现 Light / Dark / Sepia 的实时切换。
/// </summary>
public static class ThemeService
{
    private const string ThemeMarkerKey = "__ThemeDictionary__";
    private const string ThemeResourceBase = "pack://application:,,,/Styles/Themes/Theme.";

    private static readonly object _sync = new();
    private static readonly SettingsService _settings = new();

    /// <summary>当前生效的主题。</summary>
    public static ThemeKind Current { get; private set; } = ThemeKind.Light;

    /// <summary>主题切换完成后触发，参数为新主题。</summary>
    public static event EventHandler<ThemeKind>? ThemeChanged;

    /// <summary>
    /// 应用启动时调用：读取持久化主题并应用（persist=false，避免重复写盘）。
    /// 必须在 <c>App.OnStartup</c> 中、<c>base.OnStartup(e)</c> 之前调用，
    /// 以确保首个窗口创建前即应用正确主题，避免浅色闪现。
    /// </summary>
    public static void Initialize()
    {
        var settings = _settings.Load();
        Apply(settings.Theme, persist: false);
    }

    /// <summary>
    /// 应用指定主题。界面引用语义键采用 <c>DynamicResource</c>，替换字典后自动刷新。
    /// </summary>
    /// <param name="kind">目标主题。</param>
    /// <param name="persist">true（用户手动切换）时写入设置文件；Initialize 内部调用传 false。</param>
    public static void Apply(ThemeKind kind, bool persist = true)
    {
        var dict = LoadThemeDictionary(kind);
        if (dict is null) return;

        var app = Application.Current;
        if (app is null) return;

        lock (_sync)
        {
            var dictionaries = app.Resources.MergedDictionaries;
            var index = FindThemeIndex(dictionaries);

            if (index >= 0)
            {
                // 在标记位置原地替换主题字典
                dictionaries.RemoveAt(index);
                dictionaries.Insert(index, dict);
            }
            else
            {
                // 极端情况：标记丢失时安全插到最前（Theme.Light 设计上恒在 [0]）
                dictionaries.Insert(0, dict);
            }

            Current = kind;
        }

        ThemeChanged?.Invoke(null, kind);

        if (persist)
        {
            _settings.Save(new AppSettings { Theme = kind });
        }
    }

    /// <summary>在 MergedDictionaries 中查找被 <c>__ThemeDictionary__</c> 标记的主题字典下标。</summary>
    private static int FindThemeIndex(IList<ResourceDictionary> dictionaries)
    {
        for (var i = 0; i < dictionaries.Count; i++)
        {
            var dict = dictionaries[i];
            if (dict.Contains(ThemeMarkerKey) && dict[ThemeMarkerKey] is true)
            {
                return i;
            }
        }

        return -1;
    }

    /// <summary>按主题种类从 pack URI 加载独立主题字典（自包含，可被整体热替换）。</summary>
    private static ResourceDictionary? LoadThemeDictionary(ThemeKind kind)
    {
        try
        {
            var uri = new Uri(ThemeResourceBase + kind + ".xaml", UriKind.Absolute);
            return new ResourceDictionary { Source = uri };
        }
        catch
        {
            return null;
        }
    }
}
