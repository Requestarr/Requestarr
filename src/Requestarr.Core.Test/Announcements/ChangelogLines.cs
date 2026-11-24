using System.Collections.Generic;
using System.Linq;
using Requestarr.Core.Announcements;

namespace Requestarr.Core.Test.Announcements
{
    // A version's lines written out for a test, each its own Changed entry.
    public static class ChangelogLines
    {
        public static IReadOnlyList<ChangelogLine> Of(params string[] texts) =>
            texts.Select((text, i) => new ChangelogLine($"line-{i:D3}", ChangeKind.Changed, text)).ToList();
    }
}
