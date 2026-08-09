# Epubra UI 改造 · 参考 WPS 风格

## 改造概览

参考 WPS 的界面语言，对 Epubra 主窗口布局与视觉系统做了 4 项核心改造，整体更贴近 WPS 的「自定义标题栏 + 快速访问 + 白底 Ribbon + 状态栏缩放」结构。

## 改造要点

### 1. 自定义标题栏（最显著的 WPS 特征）
- 用 `WindowChrome` 替换系统标题栏（`WindowStyle=None`），窗口圆角与边缘缩放由 `WindowChrome` 接管。
- **左侧**：书形 Logo + 应用名「Epubra」+ 当前文档标题。
- **中部**：快速访问工具栏（新建 / 打开 / 保存 / 撤销 / 重做 / 一键排版 / 预览 / 主题切换），白字图标按钮，悬停浅蓝。
- **右侧**：窗口控制按钮（最小化 / 最大化·还原 / 关闭），关闭按钮悬停变红（WPS 风格）。
- 交互：标题栏空白处拖动窗口、双击最大化/还原，命中按钮时不触发拖动，保证按钮可点。

### 2. WPS 蓝配色
- 品牌靛蓝 `#2B5FB0` → WPS 经典蓝 `#2B7FFF`（更明亮、更现代）。
- 三套主题（Light / Dark / Sepia）统一调整 `AccentDefault/Hover/Pressed`、`BorderAccent`、`TextAccent`、`StateSelected`、`SurfaceAccentSubtle`、`FocusRing`，文字对比度仍满足 WCAG AA。
- 标题栏底色为固定 WPS 蓝（不随主题切换，与 WPS 顶栏一致）。

### 3. Ribbon 视觉精修
- Tab 头更紧凑；选中态背景与下方内容区同为白底，形成「上浮卡片」融合感，底部贯穿一条蓝色指示条。
- 新增控件样式：`QatButton`（快速访问图标按钮）、`QatToggleButton`（预览切换）、`TitleBarButton`、`TitleBarCloseButton`。

### 4. 状态栏缩放控件
- 状态栏右侧新增「缩小 / 百分比 / 放大 / 适应宽度」缩放区（WPS 标志性细节）。
- 通过编辑区 `LayoutTransform`（ScaleTransform）实现 50%–300% 缩放，百分比实时显示。

## 涉及文件

| 文件 | 改动 |
|---|---|
| `src/Epubra.App/Styles/DesignTokens.xaml` | `PaletteBrand` 改为 WPS 蓝阶；新增 `TitleBarBrush` / `TitleBarButtonHover` / `TitleBarButtonCloseHover` / `TitleBarForeground` |
| `src/Epubra.App/Styles/Themes/Theme.Light\|Dark\|Sepia.xaml` | 强调色系统一为 WPS 蓝 |
| `src/Epubra.App/Styles/Icons.xaml` | 新增窗口控制（最小化/最大化/还原）与缩放（放大/缩小）图标 |
| `src/Epubra.App/Styles/Controls.xaml` | 精修 `RibbonTab`；新增标题栏按钮样式 |
| `src/Epubra.App/Views/MainWindow.xaml` | 自定义标题栏 + 窗口控制 + 状态栏缩放 UI |
| `src/Epubra.App/Views/MainWindow.xaml.cs` | 窗口控制 / 拖动 / 主题循环切换 / 缩放逻辑 |

## 验证

- `dotnet build Epubra.sln -c Debug`：**0 错误**（仅有 WebView2 版本近似匹配的既有警告）。
- `dotnet test Epubra.sln -c Debug`：**103 / 103 通过，无回归**。

## 说明与可优化项

- 标题栏固定为 WPS 蓝，不随 Light / Dark / Sepia 切换（符合 WPS 顶栏特征；如需随主题变可改为绑定语义键）。
- 最大化时窗口覆盖任务栏（自定义无边框窗口的常见行为）。若要保留任务栏区域，可后续 hook `WM_GETMINMAXINFO`。
- 预览模式（WebView2）当前不跟随编辑区缩放，仅缩放编辑区。
