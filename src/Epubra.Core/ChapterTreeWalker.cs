namespace Epubra.Core;

/// <summary>
/// 章节层级遍历器：对扁平的 <see cref="Chapter"/> 列表按 <see cref="Chapter.ParentId"/> 和 <see cref="Chapter.Order"/> 排序后输出。
/// </summary>
public static class ChapterTreeWalker
{
    /// <summary>
    /// 返回按文档顺序排列的章节序列：父在前、子在后（深度优先）。
    /// </summary>
    public static IEnumerable<Chapter> WalkInDocumentOrder(IEnumerable<Chapter> chapters)
    {
        ArgumentNullException.ThrowIfNull(chapters);

        var list = chapters as IList<Chapter> ?? chapters.ToList();
        // 用 Guid.Empty 表示顶级章节（ParentId == null 的归到一组）
        var byParent = list
            .GroupBy(c => c.ParentId ?? Guid.Empty)
            .ToDictionary(g => g.Key, g => g.OrderBy(c => c.Order).ThenBy(c => c.Title, StringComparer.Ordinal).ToList());

        return WalkInternal(Guid.Empty, byParent);
    }

    private static IEnumerable<Chapter> WalkInternal(Guid parentId, IReadOnlyDictionary<Guid, List<Chapter>> byParent)
    {
        if (!byParent.TryGetValue(parentId, out var children))
        {
            yield break;
        }

        foreach (var child in children)
        {
            yield return child;
            foreach (var grandChild in WalkInternal(child.Id, byParent))
            {
                yield return grandChild;
            }
        }
    }

    /// <summary>顶级章节（ParentId 为 null），按 Order 排序。</summary>
    public static IEnumerable<Chapter> GetTopLevel(IEnumerable<Chapter> chapters)
    {
        ArgumentNullException.ThrowIfNull(chapters);
        return chapters
            .Where(c => c.ParentId is null)
            .OrderBy(c => c.Order)
            .ThenBy(c => c.Title, StringComparer.Ordinal);
    }

    /// <summary>指定章节的直属子章节。</summary>
    public static IEnumerable<Chapter> GetChildren(IEnumerable<Chapter> chapters, Guid parentId)
    {
        ArgumentNullException.ThrowIfNull(chapters);
        return chapters
            .Where(c => c.ParentId == parentId)
            .OrderBy(c => c.Order)
            .ThenBy(c => c.Title, StringComparer.Ordinal);
    }
}