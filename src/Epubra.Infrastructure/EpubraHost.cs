using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace Epubra.Infrastructure;

/// <summary>
/// 应用依赖注入容器工厂。
/// P0 仅注册基础 logger，P1 起逐步加入各个 Service。
/// </summary>
public static class EpubraHost
{
    public static IServiceProvider Build()
    {
        var services = new ServiceCollection();
        services.AddLogging(b => b.AddConsole());
        return services.BuildServiceProvider();
    }
}