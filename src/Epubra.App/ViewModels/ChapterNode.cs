using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using Epubra.Core;

namespace Epubra.App.ViewModels;

/// <summary>
/// 章节树的 UI 节点，包装 <see cref="Chapter"/> 领域模型。
/// 维护父子关系和 Children 集合，供 TreeView 绑定。
/// </summary>
public partial class ChapterNode : ObservableObject
{
    /// <summary>底层领域模型。</summary>
    public Chapter Chapter { get; }

    /// <summary>父节点（顶级章节为 null）。</summary>
    public ChapterNode? Parent { get; set; }

    /// <summary>子节点集合。</summary>
    public ObservableCollection<ChapterNode> Children { get; } = new();

    [ObservableProperty]
    private string _title = string.Empty;

    [ObservableProperty]
    private bool _isSelected;

    [ObservableProperty]
    private bool _isExpanded = true;

    public ChapterNode(Chapter chapter, ChapterNode? parent = null)
    {
        Chapter = chapter ?? throw new ArgumentNullException(nameof(chapter));
        Parent = parent;
        _title = chapter.Title;
    }

    /// <summary>把 Title 同步回底层 Chapter。</summary>
    partial void OnTitleChanged(string value)
    {
        Chapter.Title = value;
    }

    /// <summary>深度优先遍历自身和所有后代。</summary>
    public IEnumerable<ChapterNode> DescendAndSelf()
    {
        yield return this;
        foreach (var child in Children)
        foreach (var d in child.DescendAndSelf())
            yield return d;
    }
}
