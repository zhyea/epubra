# Epubra

EPUB 编辑器 —— Windows 桌面应用，支持章节管理、富文本编辑、多格式导出。

## 技术栈

- **语言/运行时**：C# 12 + .NET 8
- **UI 框架**：WPF（Windows）
- **MVVM**：CommunityToolkit.Mvvm（Source Generator）
- **富文本编辑**：RichTextBox (FlowDocument) → XHTML 转换
- **EPUB 打包**：System.IO.Compression.ZipArchive（零第三方依赖）
- **预览引擎**：WebView2（内嵌 Edge Chromium）
- **测试**：xUnit + FluentAssertions

## 项目结构

```
epubra/
├── src/
│   ├── Epubra.App/              # WPF 启动项目（菜单+工具栏+章节树+编辑区）
│   ├── Epubra.Core/             # 领域模型（Book, Chapter, EpubResource）
│   ├── Epubra.Editor/           # FlowDocument ↔ XHTML 转换器
│   ├── Epubra.Epub/             # EPUB 3.0 解析 + 打包
│   ├── Epubra.Export/           # 多格式导出（txt/html，未来扩展 mobi/snb）
│   └── Epubra.Infrastructure/   # 文件 IO、日志、依赖注入
├── tests/
│   └── Epubra.Tests/            # xUnit 单元测试
├── samples/
│   └── Epubra.Sample/           # P0 验证程序（构造 Book → 生成 EPUB）
└── samples/output/              # 生成的示例 EPUB
```

## 构建

```bash
# 需要 .NET 8 SDK
dotnet build Epubra.sln -c Debug

# 运行 EPUB 生成验证
dotnet run --project samples/Epubra.Sample

# 运行测试
dotnet test tests/Epubra.Tests
```

## P0 验证结果

运行 `dotnet run --project samples/Epubra.Sample` 后，在 `samples/output/` 生成 `sample.epub`：

- **文件大小**：3,697 bytes | **生成耗时**：~30ms
- **内部结构**（9 个 zip 条目）：mimetype(Stored) + container.xml + content.opf + nav.xhtml + toc.ncx + 4 个章节 XHTML
- mimetype 是第一个 entry 且未压缩 ✅
- 包含 EPUB 3 nav.xhtml 和 EPUB 2 兼容 toc.ncx ✅
- 中文章节标题正确保留 ✅

## P1 MVP 功能

| 功能 | 说明 |
|---|---|
| **章节树 CRUD** | 添加/删除/重命名(F2/双击)/上移/下移/缩进/取消缩进，支持嵌套 |
| **富文本编辑** | 加粗/斜体/下划线、标题层级(H1-H4)、字体切换、字号、对齐方式、插入图片 |
| **元数据编辑** | 书名、作者实时同步到 Book 模型 |
| **EPUB 导出** | 编辑器内容 → FlowDocumentToXhtmlConverter → EpubWriter → 完整 EPUB 3.0 文件 |
| **编辑器双向转换** | FlowDocument → XHTML（导出）+ XHTML → FlowDocument（加载已保存内容） |
| **图片资源管理** | 插入图片自动注册到 Book.Resources，导出时嵌入 EPUB |
| **快捷键** | Ctrl+E 导出 EPUB，Ctrl+S 保存章节，F2 重命名 |

## P2 进阶功能

| 功能 | 说明 |
|---|---|
| **编辑/预览切换** | 工具栏「预览」按钮切换到 WebView2 渲染模式，所见即所得查看章节在阅读器中的效果 |
| **章节拖拽排序** | TreeView 支持拖拽章节到其他章节下，自动维护父子关系和排序，禁止拖到自身后代（防环） |
| **嵌入字体** | 工具栏「嵌入字体」按钮选择 .ttf/.otf/.woff 文件，自动注册到 Book.Resources 并在 OPF manifest 中声明 |
| **项目保存/加载** | .epubra 项目文件（JSON 格式）保存完整状态（元数据+章节树+资源二进制），关闭后可重新打开继续编辑 |
| **撤销/重做** | Ctrl+Z 撤销、Ctrl+Y 重做，绑定到 RichTextBox 编辑器 |
| **窗口标题** | 标题栏显示当前项目文件名，保存后自动更新 |

### 新增文件（P2）

| 文件 | 说明 |
|---|---|
| `Infrastructure/ProjectService.cs` | 项目文件序列化/反序列化（JSON + Base64 资源） |
| `App/Converters/InverseBoolToVisibilityConverter.cs` | 反转布尔→可见性转换器（编辑/预览切换） |

### 测试覆盖（17/17 通过）

| 测试文件 | 测试数 | 覆盖范围 |
|---|---|---|
| `EpubWriterTests` | 6 | mimetype 合规、最小打包、中文标题、图片/字体资源、嵌套章节 spine |
| `ChapterTreeWalkerTests` | 3 | 深度优先遍历、顶层筛选、同序排序 |
| `EpubExportIntegrationTests` | 4 | 完整流程(多章节+富文本)、章节顺序(嵌套)、图片资源、空内容章节 |
| `ProjectServiceTests` | 4 | 项目保存/加载往返（元数据+章节、图片资源、字体资源、无效文件） |

## 开发路线图

| 阶段 | 状态 | 目标 |
|---|---|---|
| **P0 技术验证** | ✅ 完成 | 项目骨架 + EPUB 打包闭环 + WPF UI 骨架 |
| **P1 MVP** | ✅ 完成 | 章节树 CRUD + 富文本编辑 + EPUB 导出闭环 + 元数据编辑 |
| **P2 进阶** | ✅ 完成 | WebView2 预览 + 拖拽排序 + 嵌入字体 + 项目保存/加载 + 撤销/重做 |
| **P3 拓展导出** | 待定 | TXT/HTML 直出，MOBI/SNB 评估第三方转换器 |

## EPUB 结构说明

生成的 EPUB 遵循 EPUB 3.0 规范：

```
sample.epub (ZIP)
├── mimetype                          # application/epub+zip（不压缩）
├── META-INF/
│   └── container.xml                 # 指向 OEBPS/content.opf
└── OEBPS/
    ├── content.opf                   # 包络文件（metadata + manifest + spine）
    ├── nav.xhtml                     # EPUB 3 导航
    ├── toc.ncx                       # EPUB 2 兼容导航
    ├── chapter_001.xhtml             # 章节内容
    ├── images/                       # 图片资源
    └── fonts/                        # 嵌入字体
```

## License

MIT
