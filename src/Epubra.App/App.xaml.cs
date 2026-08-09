using System.Windows;
using Epubra.App.Services;

namespace Epubra.App;

public partial class App : Application
{
    /// <summary>
    /// 重写启动逻辑：在窗口创建之前应用持久化主题，避免浅色闪现，
    /// 随后交给基类完成正常启动流程。
    /// </summary>
    protected override void OnStartup(StartupEventArgs e)
    {
        ThemeService.Initialize();
        base.OnStartup(e);
    }
}
