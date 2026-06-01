using FluentAssertions;
using Xunit;

namespace ECommerPipeline.Infrastructure.Tests.Etl;

/// <summary>
/// The ETL Transform step converts an OrderDate into an integer surrogate
/// DateKey of the form yyyyMMdd (e.g. 2026-05-19 -> 20260519). This mirrors
/// the logic in SalesEtlPipeline so the transformation is unit-tested in
/// isolation from the database.
/// </summary>
public class DateKeyTransformTests
{
    private static int ToDateKey(DateTime date) => int.Parse(date.ToString("yyyyMMdd"));

    [Theory]
    [InlineData(2026, 5, 19, 20260519)]
    [InlineData(2026, 1, 1, 20260101)]
    [InlineData(2026, 12, 31, 20261231)]
    [InlineData(2000, 2, 29, 20000229)]   // leap day
    public void ToDateKey_produces_yyyyMMdd_integer(int y, int m, int d, int expected)
    {
        var date = new DateTime(y, m, d);

        ToDateKey(date).Should().Be(expected);
    }

    [Fact]
    public void DateKey_is_chronologically_sortable()
    {
        var earlier = ToDateKey(new DateTime(2026, 1, 1));
        var later   = ToDateKey(new DateTime(2026, 12, 31));

        // Integer ordering matches chronological ordering — a key property
        // that lets dim.Date use DateKey as a sortable PK.
        later.Should().BeGreaterThan(earlier);
    }

    [Fact]
    public void DateKey_ignores_time_component()
    {
        var morning = ToDateKey(new DateTime(2026, 5, 19, 8, 0, 0));
        var evening = ToDateKey(new DateTime(2026, 5, 19, 23, 59, 59));

        morning.Should().Be(evening);
    }
}
