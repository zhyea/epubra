# Epubra UI 设计系统重构

> 系统性视觉升级：从无设计令牌的默认 WPF 外观，升级为令牌驱动、矢量图标、四态交互、无障碍合规的统一设计语言。

## 设计令牌分层（三层模型）

| 层 | 文件 | 职责 |
|---|---|---|
| Palette | `DesignTokens.xaml` | 原始色阶（品牌靛蓝 50–800、暖调中性灰 0–900、语义色），界面不直接引用 |
| Semantic | `DesignTokens.xaml` | 语义画刷（Surface / Border / Text / Accent / State / Feedback），界面唯一引用层 |
| Metrics | `DesignTokens.xaml` | 字族、字号阶梯、4px 间距栅格、圆角分级（3/4/6/8/Pill）、控件尺寸、动效时长、阴影 |

- **品牌色**：书卷靛蓝 Ink Indigo（Brand500 `#2B5FB0`）
- **对比度**：所有前景/背景组合通过 WCAG AA 4.5:1 校验
- **图标**：零字体依赖矢量库 `Icons.xaml`（`StreamGeometry`，16×16 画布 + 32×32 空状态插画），规避跨 Windows 版本缺字"豆腐块"

## 控件模板库 `Controls.xaml`

覆盖全部交互控件，统一实现 **Rest / Hover / Pressed / Disabled** 四态 + 独立焦点环（WCAG 2.4.7）：

- Ribbon：`RibbonTab`（2px 收窄指示条）、`RibbonGroup`、`RBtnS/M`、`RBtnIconText`、`RBtnAccent`、`RToggleS/M`
- 通用：`PrimaryButton` / `SecondaryButton` / `GhostIconButton` / `GhostDangerButton`
- 输入：扁平 `TextBox`（焦点边框变 Accent + 1.5px）、`ComboBox`（自定义箭头 + 阴影浮层）
- 结构：`TreeView`（30px 行高、左侧 3px 强调条、圆角选中态）、`Menu` / `ContextMenu`、`ScrollBar`（细滚动条 hover 加宽）、`GridSplitter`（视觉 1px / 命中 5px）、`StatusBar`、`ToolTip`
- 复合：`PanelHeader` / `PanelTitle` / `CountBadge` / `FieldLabel` / `Card`

## 应用落地

- `App.xaml`：合并引入三本字典（生效前提）
- `MainWindow.xaml`：移除 7 个内联样式改用令牌；Ribbon 按钮矢量图标化；章节树头部 `CountBadge` + 图标按钮组；元数据栏改单行 `WrapPanel` 紧凑型；状态栏分区带图标；**新增空状态引导卡片**（`CountToVisibilityConverter` 控制 TreeView/EmptyState 互斥）；编辑区标题栏 `PanelHeader` 精致化
- `MetadataDialog.xaml`：封面预览卡片化、表单网格化、底部 Primary/Secondary 主从按钮层级
- `CountToVisibilityConverter.cs`：集合数量 → Visibility（支持 `Inverse` 参数）

## 验证结果

- 全方案构建 **0 错误**（仅 WebView2 版本 NuGet 提示，无影响）
- **99/99 测试通过**，UI 重构零回归
- 所有 code-behind 控件名（`x:Name`）与事件处理器完整保留

## 已知次要项（可后续优化）

- 顶级菜单项未配置图标，子菜单模板预留 26px 图标列显示为左留白（不影响功能，后续可补菜单图标）
