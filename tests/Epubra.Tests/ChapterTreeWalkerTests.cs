using Epubra.Core;
using FluentAssertions;
using Xunit;

namespace Epubra.Tests;

public class ChapterTreeWalkerTests
{
    [Fact]
    public void WalkInDocumentOrder_顶层章节应排在子章节之前()
    {
        var top1 = new Chapter { Id = Guid.NewGuid(), Title = "第一章", Order = 0 };
        var top2 = new Chapter { Id = Guid.NewGuid(), Title = "第二章", Order = 1 };
        var sub1 = new Chapter { Id = Guid.NewGuid(), Title = "1.1", ParentId = top1.Id, Order = 0 };
        var sub2 = new Chapter { Id = Guid.NewGuid(), Title = "1.2", ParentId = top1.Id, Order = 1 };
        var subSub = new Chapter { Id = Guid.NewGuid(), Title = "1.1.1", ParentId = sub1.Id, Order = 0 };

        var chapters = new[] { top1, top2, sub1, sub2, subSub };

        var walked = ChapterTreeWalker.WalkInDocumentOrder(chapters).Select(c => c.Title).ToList();

        walked.Should().Equal("第一章", "1.1", "1.1.1", "1.2", "第二章");
    }

    [Fact]
    public void GetTopLevel_返回所有ParentId为null的章节()
    {
        var top = new Chapter { Id = Guid.NewGuid(), Title = "T", Order = 0 };
        var sub = new Chapter { Id = Guid.NewGuid(), Title = "S", ParentId = top.Id, Order = 0 };

        var tops = ChapterTreeWalker.GetTopLevel(new[] { top, sub }).ToList();

        tops.Should().HaveCount(1);
        tops[0].Title.Should().Be("T");
    }

    [Fact]
    public void 同Order按标题排序()
    {
        var a = new Chapter { Id = Guid.NewGuid(), Title = "Bravo", Order = 0 };
        var b = new Chapter { Id = Guid.NewGuid(), Title = "Alpha", Order = 0 };

        var result = ChapterTreeWalker.WalkInDocumentOrder(new[] { a, b }).ToList();

        result[0].Title.Should().Be("Alpha");
        result[1].Title.Should().Be("Bravo");
    }
}