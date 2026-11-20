using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;

namespace Requestarr.Core.Portal
{
    // One page of a list, as the web app's pager shows it: its number (from 1), how many a page, how many there are in
    // all, and the rows on it.
    public record PortalPage<T>(int Page, int PageSize, int TotalRecords, IReadOnlyList<T> Records);

    // The page asked for: its number, how many a page, and the order (a column's name, and which way).
    public record PortalPageRequest(int Page, int PageSize, string SortKey, bool Descending)
    {
        public const int DefaultSize = 25;

        // The most a page holds, so a page never becomes the whole list by accident.
        public const int MaxSize = 1000;
    }

    // Paging and sorting a portal list on the server, so the web app gets one page at a time.
    public static class PortalPaging
    {
        private static readonly IComparer<object> Ascending = new ValueComparer(false);
        private static readonly IComparer<object> Descending = new ValueComparer(true);

        // sorts: each column the list sorts by, by its name; a column it doesn't know keeps the list's own order.
        public static PortalPage<T> Of<T>(IEnumerable<T> rows, PortalPageRequest request, IReadOnlyDictionary<string, Func<T, object>> sorts)
        {
            var all = rows.ToList();
            var order = request.Descending ? Descending : Ascending;
            var sorted = request.SortKey != null && sorts.TryGetValue(request.SortKey, out var key) ? all.OrderBy(key, order).ToList() : all;
            var size = Math.Clamp(request.PageSize, 1, PortalPageRequest.MaxSize);
            var pages = Math.Max(1, (int)Math.Ceiling(all.Count / (double)size));
            var page = Math.Clamp(request.Page, 1, pages);

            return new PortalPage<T>(page, size, all.Count, sorted.Skip((page - 1) * size).Take(size).ToList());
        }

        // Words without regard to case; rows without a value go last whichever way the column is sorted, as in the web
        // app's own sorting.
        private sealed class ValueComparer : IComparer<object>
        {
            private readonly int _direction;

            public ValueComparer(bool descending)
            {
                _direction = descending ? -1 : 1;
            }

            public int Compare(object x, object y)
            {
                if (x == null || y == null)
                {
                    return (x == null ? 1 : 0) - (y == null ? 1 : 0);
                }

                var order = x is string a && y is string b ? StringComparer.OrdinalIgnoreCase.Compare(a, b) : Comparer.Default.Compare(x, y);

                return order * _direction;
            }
        }
    }
}
