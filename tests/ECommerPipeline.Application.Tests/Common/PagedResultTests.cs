using ECommerPipeline.Application.Common.DTOs;
using FluentAssertions;
using Xunit;

namespace ECommerPipeline.Application.Tests.Common;

public class PagedResultTests
{
    [Theory]
    [InlineData(100, 20, 5)]   // 100 items / 20 per page = 5 pages
    [InlineData(101, 20, 6)]   // 101 -> 6 pages (ceiling)
    [InlineData(99, 20, 5)]    // 99 -> 5 pages
    [InlineData(0, 20, 0)]     // empty
    [InlineData(1, 20, 1)]     // 1 item -> 1 page
    [InlineData(20, 20, 1)]    // exactly 1 page
    public void TotalPages_computed_correctly(int total, int pageSize, int expectedPages)
    {
        var result = new PagedResult<string>(
            Items: new List<string>(), Page: 1, PageSize: pageSize, Total: total);

        result.TotalPages.Should().Be(expectedPages);
    }

    [Fact]
    public void PageSize_zero_returns_zero_pages_without_dividing()
    {
        var result = new PagedResult<string>(
            Items: new List<string>(), Page: 1, PageSize: 0, Total: 100);

        // Guards against divide-by-zero
        result.TotalPages.Should().Be(0);
    }

    [Fact]
    public void Items_are_preserved()
    {
        var items = new List<string> { "a", "b", "c" };
        var result = new PagedResult<string>(items, Page: 1, PageSize: 20, Total: 3);

        result.Items.Should().BeEquivalentTo(items);
        result.Total.Should().Be(3);
    }
}
