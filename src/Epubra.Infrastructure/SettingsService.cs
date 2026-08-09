using System.Text.Json;
using System.Text.Json.Serialization;

namespace Epubra.Infrastructure;

/// <summary>
/// 全局用户设置读写服务。
/// 设置持久化在 %LocalAppData%/Epubra/settings.json。
/// 写入采用「临时文件 + 原子 Move」策略，保证断电或异常时不会留下半截文件。
/// </summary>
public sealed class SettingsService
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        Converters = { new JsonStringEnumConverter() }
    };

    /// <summary>
    /// 测试/可重定向：设置文件所在目录。为 null（默认）时使用 %LocalAppData%/Epubra。
    /// 单元测试会临时指向临时目录，避免污染真实用户数据；生产代码无需设置。
    /// </summary>
    public static string? StorageDirectory { get; set; }

    private static string FilePath
    {
        get
        {
            var baseDir = StorageDirectory
                ?? Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
            return Path.Combine(baseDir, "Epubra", "settings.json");
        }
    }

    private readonly object _sync = new();

    /// <summary>
    /// 读取设置。文件不存在或解析失败时返回默认设置（主题 Light），
    /// 保证损坏的设置不会阻断应用启动。
    /// </summary>
    public AppSettings Load()
    {
        try
        {
            if (!File.Exists(FilePath)) return new AppSettings();

            var json = File.ReadAllText(FilePath);
            return JsonSerializer.Deserialize<AppSettings>(json, JsonOptions) ?? new AppSettings();
        }
        catch
        {
            // 设置损坏 → 回退默认，不抛异常
            return new AppSettings();
        }
    }

    /// <summary>
    /// 保存设置（原子写）。任何异常都内部吞掉，持久化失败不应影响主流程。
    /// </summary>
    public void Save(AppSettings settings)
    {
        ArgumentNullException.ThrowIfNull(settings);

        try
        {
            var dir = Path.GetDirectoryName(FilePath);
            if (!string.IsNullOrEmpty(dir) && !Directory.Exists(dir))
            {
                Directory.CreateDirectory(dir);
            }

            var json = JsonSerializer.Serialize(settings, JsonOptions);
            var tmp = FilePath + ".tmp";
            File.WriteAllText(tmp, json);
            File.Move(tmp, FilePath, overwrite: true);
        }
        catch
        {
            // 最佳努力持久化：失败即忽略
        }
    }
}

/// <summary>用户级设置（可序列化）。</summary>
public sealed class AppSettings
{
    /// <summary>界面主题，默认浅色。</summary>
    public ThemeKind Theme { get; set; } = ThemeKind.Light;
}
