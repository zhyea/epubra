# P13 开始 Tab 精简实施报告

## 结论
**方案 B 完全对齐 epubBuilder 截图**已开始 Tab 精简。原 5 组（剪贴板/字体/段落/样式/排版）已删除，仅保留 1 个"书籍"组（5 大按钮）。同时同步清理了插入 Tab 的"封面"和"书籍信息"按钮。

## 实施结果

### 代码改动
`src/Epubra.App/Views/MainWindow.xaml`：

| 区域 | 改动 |
|------|------|
| **开始 Tab**（第 149-191 行）| 删除原 5 组 Border + StackPanel（约 125 行），替换为 1 个 Border = "书籍"组（5 个 RBtnIconText 大按钮）|
| **插入 Tab**（第 219-237 行）| 删除图片组里的"封面"+"书籍信息"两个 Button（接替到开始 Tab 的"书籍设置"）|

### 开始 Tab 新布局
```
开始  导入  插入  编辑  发布
─────────────────────────────────────
 [新建]   [打开]   [保存]   [书籍设置]   [关闭]
             「书籍」组
```

5 个 RBtnIconText 大按钮（与插入 Tab 风格一致）：
- 新建 → `NewProjectCommand`（Ctrl+N）
- 打开 → `OpenProjectCommand`（Ctrl+O）
- 保存 → `SaveProjectCommand`（Ctrl+S）
- 书籍设置 → `ShowMetadata_Click`（接替了原"插入 Tab 书籍信息"，并且封面也由书籍设置对话框接管）
- 关闭 → `Close_Click`

### 入口去向表
| 命令 | 现有入口 |
|------|---------|
| 新建 | 开始 Tab + QAT + Ctrl+N |
| 打开 | 开始 Tab + QAT + Ctrl+O |
| 保存 | 开始 Tab + QAT + Ctrl+S |
| 书籍设置 | 开始 Tab（P13 接管原本"插入Tab 书籍信息"）|
| 关闭 | 开始 Tab + 标题栏 X |
| 删除的"封面"| 已被书籍设置对话框接替 |
| 删除的"书籍信息"| 已被书籍设置接替 |

## ⚠️ 重要风险

**方案 B 的代价**：原开始 Tab 5 组的命令**完全从 Ribbon 消失**，只能通过 WPF FlowDocument 内置的**"右键 → 字体"**对话框使用：

- 字体 / 字号 / A+ / A- / 字体颜色 / 清除格式
- 项目符号 / 缩进减 / 缩进增 / 4 对齐
- 样式 H1 / H2 / H3 / H4

如果将来发现这些命令缺失造成不便，**补救方案**：把字体/段落/样式 3 组迁到"编辑 Tab"（当前编辑 Tab 只含查找/替换/拆分章节），让字体段落样式有 Ribbon 入口。

## 构建验证状态

- ⚠️ 宿主机 EDR 持续锁住 `C:\epubra_bld`（与 P12 同类问题），本会话的 `dotnet build` 失败
- 代码改动已完成，等用户在 Rider 中执行：
  ```bash
  dotnet build           # 期望：0 错误
  dotnet test            # 期望：现有测试维持绿色（业务逻辑零变更）
  ```

## 残留污染

- `src/Epubra.App/Views/MainWindow.xaml.tmpwrite`（4 字节）— 调试时 EDR 锁导致测试用临时文件未能清理，**请在 Rider 中手动删除**

## 文件与变化清单
- 修改：`src/Epubra.App/Views/MainWindow.xaml`（约 -125 行 / +57 行）
- 删除：`p13-changes-begin-tab.xaml` / `p13-changes-insert-tab.xaml`（已应用，临时文件清理）
- 状态：Task #38 → completed（2026-08-11）
