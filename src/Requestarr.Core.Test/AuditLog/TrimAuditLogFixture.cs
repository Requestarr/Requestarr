using System;
using System.Linq;
using FluentAssertions;
using NUnit.Framework;
using NzbDrone.Core.Configuration;
using Requestarr.Core.Audit;
using Requestarr.Core.Provisioning;
using Requestarr.Core.Test.Framework;
using Requestarr.Core.Users;

namespace Requestarr.Core.Test.AuditLog
{
    // The daily trim on a real database: entries older than "Keep the audit log for" go, the newer ones stay, and the
    // trim is recorded by the automation.
    [TestFixture]
    public class TrimAuditLogFixture : DbTest<TrimAuditLog, AuditEntry>
    {
        private IConfigService Config => Mocker.GetMock<IConfigService>().Object;

        [SetUp]
        public void Setup()
        {
            Mocker.GetMock<IConfigService>().SetupAllProperties();
            Mocker.SetConstant<IAuditRepository>(Mocker.Resolve<AuditRepository>());
            Mocker.SetConstant<IAuditService>(Mocker.Resolve<AuditService>());
            Mocker.SetConstant<IAuditSettings>(Mocker.Resolve<AuditSettings>());
        }

        private void EntryDaysAgo(int days, string action) =>
            Storage.Insert(new AuditEntry { Actor = "system", Action = action, Timestamp = DateTime.UtcNow.AddDays(-days), Recorded = DateTime.UtcNow.AddDays(-days) });

        // The owner's dev data, 2026-11-20: the import's entries are dated in the old channel, back to 2024, and a 1-year
        // trim deleted them. The trim goes by when an entry was written.
        [Test]
        public void entries_an_import_wrote_today_about_long_ago_stay()
        {
            Storage.Insert(new AuditEntry { Actor = "discord:1 (someone)", Action = "request.imported", Timestamp = DateTime.UtcNow.AddDays(-700) });
            EntryDaysAgo(400, "old");

            Subject.Clean();

            AllStoredModels.Select(e => e.Action).Should().BeEquivalentTo("request.imported", TrimAuditLog.TrimAction);
        }

        [Test]
        public void by_default_a_year_is_kept_and_the_trim_is_recorded()
        {
            EntryDaysAgo(400, "old.one");
            EntryDaysAgo(366, "old.two");
            EntryDaysAgo(364, "kept.one");
            EntryDaysAgo(10, "kept.two");

            Subject.Clean();

            AllStoredModels.Select(e => e.Action).Should().BeEquivalentTo("kept.one", "kept.two", TrimAuditLog.TrimAction);
            var trim = AllStoredModels.Single(e => e.Action == TrimAuditLog.TrimAction);
            trim.Actor.Should().Be(Actor.Automation.Description);
            trim.Detail.Should().Be("Trimmed 2 entries older than 1 year");
            Mocker.Resolve<AuditSettings>().TrimmedBefore.Should().BeCloseTo(DateTime.UtcNow.AddYears(-1), TimeSpan.FromMinutes(1));
        }

        [Test]
        public void renames_are_kept_so_a_former_name_still_finds_its_member()
        {
            EntryDaysAgo(800, MemberNameService.RenameAction);
            EntryDaysAgo(800, "old");

            Subject.Clean();

            AllStoredModels.Select(e => e.Action).Should().BeEquivalentTo(MemberNameService.RenameAction, TrimAuditLog.TrimAction);
        }

        [Test]
        public void a_shorter_choice_trims_more()
        {
            Config.AuditLogKeepFor = AuditRetention.NinetyDays;
            EntryDaysAgo(91, "old");
            EntryDaysAgo(89, "kept");

            Subject.Clean();

            AllStoredModels.Select(e => e.Action).Should().BeEquivalentTo("kept", TrimAuditLog.TrimAction);
            AllStoredModels.Single(e => e.Action == TrimAuditLog.TrimAction).Detail.Should().Be("Trimmed 1 entry older than 90 days");
        }

        [Test]
        public void forever_keeps_everything()
        {
            Config.AuditLogKeepFor = AuditRetention.Forever;
            EntryDaysAgo(3000, "ancient");

            Subject.Clean();

            AllStoredModels.Select(e => e.Action).Should().Equal("ancient");
        }

        [Test]
        public void nothing_old_enough_records_nothing()
        {
            EntryDaysAgo(10, "recent");

            Subject.Clean();

            AllStoredModels.Select(e => e.Action).Should().Equal("recent");
            Mocker.Resolve<AuditSettings>().TrimmedBefore.Should().BeNull("nothing was trimmed");
        }

        [TestCase(AuditRetention.NinetyDays, -90, 0)]
        [TestCase(AuditRetention.SixMonths, 0, -6)]
        [TestCase(AuditRetention.OneYear, 0, -12)]
        [TestCase(AuditRetention.TwoYears, 0, -24)]
        public void each_choice_has_its_cutoff(string keepFor, int days, int months)
        {
            var now = new DateTime(2026, 11, 18, 12, 0, 0, DateTimeKind.Utc);

            AuditRetention.Cutoff(keepFor, now).Should().Be(now.AddDays(days).AddMonths(months));
        }

        [Test]
        public void a_stored_value_that_isnt_a_choice_reads_as_the_default()
        {
            Config.AuditLogKeepFor = "3 weeks";

            Mocker.Resolve<AuditSettings>().KeepFor.Should().Be(AuditRetention.Default);
        }

        [Test]
        public void the_provisioning_file_sets_a_choice_and_refuses_anything_else()
        {
            var settings = Mocker.Resolve<AuditSettings>();

            settings.Apply(new AuditLogProvisioning { KeepFor = AuditRetention.TwoYears });
            settings.KeepFor.Should().Be(AuditRetention.TwoYears);

            settings.Invoking(s => s.Apply(new AuditLogProvisioning { KeepFor = "3 weeks" })).Should().Throw<ProvisioningException>().WithMessage("*90 days, 6 months, 1 year, 2 years, forever*");
            settings.KeepFor.Should().Be(AuditRetention.TwoYears, "a refused value changes nothing");
        }
    }
}
