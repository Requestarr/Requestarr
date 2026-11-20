using System;
using System.Collections.Generic;
using System.Linq;
using FluentAssertions;
using NUnit.Framework;
using Requestarr.Core.Portal;

namespace Requestarr.Core.Test.Portal
{
    // The portal's long lists a page at a time, sorted on the server: the page asked for (kept within the list), its
    // size (kept within the limits), the total, and rows without a value last whichever way a column is sorted.
    [TestFixture]
    public class PortalPagingFixture
    {
        private record Row(string Name, int? Count);

        private static readonly IReadOnlyDictionary<string, Func<Row, object>> Sorts = new Dictionary<string, Func<Row, object>>
        {
            ["name"] = r => r.Name,
            ["count"] = r => r.Count
        };

        private static readonly List<Row> Rows = Enumerable.Range(1, 60).Select(i => new Row($"row {i:00}", i % 10 == 0 ? null : i)).ToList();

        [Test]
        public void a_page_holds_its_share_of_the_list_with_the_total()
        {
            var page = PortalPaging.Of(Rows, new PortalPageRequest(3, 25, null, false), Sorts);

            page.Page.Should().Be(3);
            page.TotalRecords.Should().Be(60);
            page.Records.Select(r => r.Name).Should().Equal(Rows.Skip(50).Select(r => r.Name));
        }

        [Test]
        public void a_page_past_the_end_is_the_last_and_the_size_stays_within_its_limits()
        {
            PortalPaging.Of(Rows, new PortalPageRequest(99, 25, null, false), Sorts).Page.Should().Be(3);
            PortalPaging.Of(Rows, new PortalPageRequest(0, 25, null, false), Sorts).Page.Should().Be(1);
            PortalPaging.Of(Rows, new PortalPageRequest(1, 0, null, false), Sorts).PageSize.Should().Be(1);
            PortalPaging.Of(Rows, new PortalPageRequest(1, 50000, null, false), Sorts).PageSize.Should().Be(PortalPageRequest.MaxSize);
            PortalPaging.Of(new List<Row>(), new PortalPageRequest(2, 25, null, false), Sorts).Should().BeEquivalentTo(new { Page = 1, TotalRecords = 0 });
        }

        [Test]
        public void rows_without_a_value_go_last_either_way_and_an_unknown_column_keeps_the_order()
        {
            var up = PortalPaging.Of(Rows, new PortalPageRequest(1, 60, "count", false), Sorts).Records;
            var down = PortalPaging.Of(Rows, new PortalPageRequest(1, 60, "count", true), Sorts).Records;

            up[0].Count.Should().Be(1);
            down[0].Count.Should().Be(59);
            up.TakeLast(6).Should().OnlyContain(r => r.Count == null);
            down.TakeLast(6).Should().OnlyContain(r => r.Count == null);
            PortalPaging.Of(Rows, new PortalPageRequest(1, 60, "nothing", true), Sorts).Records.Should().Equal(Rows);
        }
    }
}
