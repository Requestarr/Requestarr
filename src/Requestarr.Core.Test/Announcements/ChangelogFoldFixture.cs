using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using FluentAssertions;
using NUnit.Framework;
using Requestarr.Core.Announcements;
using Requestarr.Core.Test.Discord.Harness;
using Requestarr.Test.Common;

namespace Requestarr.Core.Test.Announcements
{
    // What a server's readers are shown when one change replaces another: never posted there, the replacing entry
    // shows alone in its final form (New when the replaced one was New); posted there, it shows what changed. A chain
    // folds all the way, and a post that failed still counts as never posted.
    [TestFixture]
    public class ChangelogFoldFixture : DiscordScenario
    {
        private static readonly Version First = new(0, 2, 0);
        private static readonly Version Second = new(0, 3, 0);

        private static readonly ChangelogEntry A = Entry("a", ChangeKind.New, First, "**A:** first.", null);
        private static readonly ChangelogEntry B = Entry("b", ChangeKind.Changed, Second, "**B:** final.", "**B:** what changed.", "a");
        private static readonly ChangelogEntry C = Entry("c", ChangeKind.Fixed, null, "**C:** fixed for good.", "**C:** fixed since.", "b");

        private AnnouncementService Service => (AnnouncementService)Mocker.Resolve<IAnnouncementService>();

        private static ChangelogEntry Entry(string id, ChangeKind kind, Version build, string line, string seen, params string[] replaces) =>
            new(id, VersionAudience.Admins, kind, build, replaces, line, seen);

        private static ChangelogSource Source(params ChangelogEntry[] entries) =>
            new(entries, new Dictionary<Version, DateTime> { [First] = new(2026, 11, 1), [Second] = new(2026, 11, 8) });

        // Every line shown, as "version kind words", for the posted entries.
        private static List<string> Shown(ChangelogSource source, params string[] posted) =>
            source.Versions(posted.ToHashSet(), new Version(0, 4, 0))
                .SelectMany(v => v.Admins.Select(l => $"{v.Version} {l.Kind} {l.Text}"))
                .ToList();

        [Test]
        public void never_posted_the_replacing_entry_shows_alone_as_new()
        {
            Shown(Source(A, B)).Should().Equal("0.3.0 New **B:** final.");
        }

        [Test]
        public void posted_the_replaced_entry_stays_and_the_replacing_one_says_what_changed()
        {
            Shown(Source(A, B), "a").Should().Equal("0.3.0 Changed **B:** what changed.", "0.2.0 New **A:** first.");
        }

        [Test]
        public void a_partly_posted_chain_folds_all_the_way_to_what_the_readers_had()
        {
            Shown(Source(A, B, C)).Should().Equal("0.4.0 New **C:** fixed for good.");
            Shown(Source(A, B, C), "a").Should().Equal("0.4.0 Fixed **C:** fixed since.", "0.2.0 New **A:** first.");
        }

        [Test]
        public async Task a_post_that_failed_still_counts_as_never_posted()
        {
            Service.Changelog = () => Source(A, B);
            Service.CurrentVersion = () => new Version(0, 2, 0, 1);
            Discord.FailSends = true;
            await Service.PostVersionNotesAsync();
            ExceptionVerification.ExpectedWarns(1);

            Discord.FailSends = false;
            Service.CurrentVersion = () => new Version(0, 3, 0, 1);
            await Service.PostVersionNotesAsync();

            Service.VersionNotes().SelectMany(n => n.Version.Admins).Select(l => l.Text).Should().Equal("**B:** final.");
            Service.FullCard(VersionAudience.Admins, Second)[0].Cards[0].Text.Should().Contain("**New**\n- **B:** final.");
        }
    }
}
