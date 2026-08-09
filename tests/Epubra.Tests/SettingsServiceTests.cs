using System.IO;
using System.Text.Json;
using System.Text.Json.Serialization;
using Epubra.Infrastructure;
using FluentAssertions;
using Xunit;

namespace Epubra.Tests;

/// <summary>
/// SettingsService 持久化测试：主题设置往返、缺失/损坏文件回退默认、
/// 以及原子写后文件存在且可解析。通过 SettingsService.StorageDirectory
/// 把写入重定向到临时目录，避免污染真实 %LocalAppData%/Epubra 数据。
/// </summary>
public class SettingsServiceTests : IDisposable
{
    private readonly string _tempDir;
    private readonly string? _originalDir;

    public SettingsServiceTests()
    {
        _tempDir = Path.Combine(Path.GetTempPath(), $"epubra_settings_{Guid.NewGuid():N}");
        Directory.CreateDirectory(_tempDir);
        _originalDir = SettingsService.StorageDirectory;
        SettingsService.StorageDirectory = _tempDir;
    }

    public void Dispose()
    {
        SettingsService.StorageDirectory = _originalDir;
        try
        {
            if (Directory.Exists(_tempDir))
            {
                Directory.Delete(_tempDir, recursive: true);
            }
        }
        catch
        {
            // 最佳努力清理，忽略删除失败
        }

        GC.SuppressFinalize(this);
    }

    /// <summary>
    /// 用例 (a)：Save(Dark) → Load → Theme 应为 Dark，设置完整往返。
    /// </summary>
    [Fact]
    public void 保存并加载_主题设置完整往返()
    {
        var service = new SettingsService();
        service.Save(new AppSettings { Theme = ThemeKind.Dark });

        var loaded = service.Load();
        loaded.Theme.Should().Be(ThemeKind.Dark);
    }

    /// <summary>
    /// 用例 (b)：设置文件不存在时，Load 返回默认浅色且不抛异常。
    /// </summary>
    [Fact]
    public void 加载_文件不存在_回退默认浅色且不抛异常()
    {
        var service = new SettingsService();

        var loaded = service.Load();

        loaded.Theme.Should().Be(ThemeKind.Light);
    }

    /// <summary>
    /// 用例 (c)：设置文件内容损坏（非法 JSON）时，Load 返回默认浅色且不抛异常。
    /// </summary>
    [Fact]
    public void 加载_文件损坏_回退默认浅色且不抛异常()
    {
        var path = Path.Combine(_tempDir, "Epubra", "settings.json");
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllText(path, "{ 这不是合法的 json ,,, ");

        var service = new SettingsService();
        var act = () => service.Load();

        act.Should().NotThrow();
        service.Load().Theme.Should().Be(ThemeKind.Light);
    }

    /// <summary>
    /// 用例 (d)：原子写后，settings.json 存在且内容可解析回原主题。
    /// </summary>
    [Fact]
    public void 保存_原子写_文件存在且可解析()
    {
        var service = new SettingsService();
        service.Save(new AppSettings { Theme = ThemeKind.Sepia });

        var path = Path.Combine(_tempDir, "Epubra", "settings.json");
        File.Exists(path).Should().BeTrue();

        var json = File.ReadAllText(path);
        // 与 SettingsService.JsonOptions 保持一致：枚举按字符串序列化。
        var opts = new JsonSerializerOptions { Converters = { new JsonStringEnumConverter() } };
        var parsed = JsonSerializer.Deserialize<AppSettings>(json, opts);
        parsed.Should().NotBeNull();
        parsed!.Theme.Should().Be(ThemeKind.Sepia);
    }
}
