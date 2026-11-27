using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Linq.Expressions;
using System.Threading.Tasks;
using FluentAssertions;
using Moq;
using NUnit.Framework;
using Requestarr.Core.Announcements;
using Requestarr.Core.Discord;
using Requestarr.Core.Discord.Gateway;
using Requestarr.Core.Discord.Interactions;
using Requestarr.Core.Qa;
using Requestarr.Core.Test.Framework;

namespace Requestarr.Core.Test.Qa
{
    // The QA test checklist: QA-CHECKLIST.md, which versions each server tests, and passing, failing and completing it.
    [TestFixture]
    public class QaChecklistFixture : CoreTest<QaChecklistService>
    {
        private const string Text = @"# QA checklist

## 0.4.15 (2026-10-25)
- **Passing an item**
  - Steps:
    1. Press Passes.
  - Expect:
    - It passed.
- **Failing an item**
  - Steps:
    1. Press Failing.
    2. Send the report.
  - Expect:
    - It failing.

## 0.4.14 (2026-10-25)

## 0.4.13 (2026-10-25)
- **Sign-in logo**
  - Steps:
    1. Sign out.
  - Expect:
    - The logo.
    - The light arrow.
";

        private const string Passed = "    - It passed.";
        private const string PassingAutomated = Passed + "\n  - Checked automatically: the live passing check.";
        private const string NewThing = "- **New thing**\n  - Steps:\n    1. Do it.\n  - Expect:\n    - It.";

        private static readonly QaCheckers NoCheckers = new(0, 0, 0, 0, Array.Empty<string>());

        private readonly Dictionary<string, QaChecklistItem> _records = new();
        private readonly Dictionary<string, IReadOnlyList<QaEvidenceFile>> _screenshots = new();
        private int _nextId = 1;

        [SetUp]
        public void Setup()
        {
            _records.Clear();
            _screenshots.Clear();
            var evidence = Mocker.GetMock<IQaEvidenceStore>();
            evidence.Setup(e => e.Replace(It.IsAny<string>(), It.IsAny<IReadOnlyList<QaEvidenceFile>>())).Returns<string, IReadOnlyList<QaEvidenceFile>>((k, files) =>
            {
                _screenshots[k] = files;
                return files.Select(f => f.Name).ToList();
            });
            evidence.Setup(e => e.Read(It.IsAny<string>(), It.IsAny<IEnumerable<string>>())).Returns<string, IEnumerable<string>>((k, _) => _screenshots.TryGetValue(k, out var files) ? files : new List<QaEvidenceFile>());
            var items = Mocker.GetMock<IQaChecklistItemRepository>();
            items.Setup(r => r.FindByKey(It.IsAny<string>())).Returns<string>(k => _records.TryGetValue(k, out var r) ? r : null);
            items.Setup(r => r.Insert(It.IsAny<QaChecklistItem>())).Returns<QaChecklistItem>(r =>
            {
                r.Id = _nextId++;
                _records[r.ItemKey] = r;
                return r;
            });
            items.Setup(r => r.Update(It.IsAny<QaChecklistItem>())).Returns<QaChecklistItem>(r => _records[r.ItemKey] = r);
            items.Setup(r => r.SetFields(It.IsAny<QaChecklistItem>(), It.IsAny<Expression<Func<QaChecklistItem, object>>[]>())).Callback<QaChecklistItem, Expression<Func<QaChecklistItem, object>>[]>((r, _) =>
            {
                // A changed key moves the record, as the database would find it.
                foreach (var stale in _records.Where(e => e.Value == r && e.Key != r.ItemKey).Select(e => e.Key).ToList())
                {
                    _records.Remove(stale);
                }

                _records[r.ItemKey] = r;
            });

            Subject.ChecklistText = () => Text;
            Subject.CurrentVersion = () => new Version(0, 4, 15, 123);
            OnQa();
        }

        [Test]
        public void the_shipped_checklist_is_embedded_and_starts_at_the_changelogs_newest_version()
        {
            var file = File.ReadAllText(RepoFiles.Path("QA-CHECKLIST.md"));
            var changelog = File.ReadAllText(RepoFiles.Path("CHANGELOG.md"));
            var running = new Version(9, 9, 9);
            var versions = QaChecklist.Parse(file, running);

            QaChecklist.Embedded().Replace("\r\n", "\n").Should().Be(file.Replace("\r\n", "\n"));
            QaChecklist.Parse(file).Select(v => v.Version).Should().Equal(Changelog.Parse(changelog).Select(v => v.Version).Where(v => v >= new Version(0, 4, 3)), "the staging promotion numbers both files together");
            versions.SelectMany(v => v.Items).Select(i => i.Key).Should().OnlyHaveUniqueItems();
            versions.SelectMany(v => v.Items).Should().OnlyContain(i => i.Steps.Count > 0 && i.Expect.Count > 0);
        }

        [Test]
        public void items_are_keyed_by_their_wording_and_a_version_with_nothing_to_test_has_none()
        {
            var versions = QaChecklist.Parse(Text);

            versions.Select(v => v.Version.ToString()).Should().Equal("0.4.15", "0.4.14", "0.4.13");
            versions[0].Items.Select(i => i.Key).Should().OnlyContain(k => k.Length < 60).And.SatisfyRespectively(
                k => k.Should().MatchRegex(@"^passing-an-item\.[0-9a-f]{8}$"),
                k => k.Should().MatchRegex(@"^failing-an-item\.[0-9a-f]{8}$"));
            versions[1].Items.Should().BeEmpty();
            versions[2].Items.Single().Name.Should().Be("Sign-in logo");
        }

        [Test]
        public void an_item_is_its_numbered_steps_and_what_to_expect_and_its_wording_is_both()
        {
            var item = QaChecklist.Parse(Text)[0].Items[1];

            item.Steps.Should().Equal("Press Failing.", "Send the report.");
            item.Expect.Should().Equal("It failing.");
            item.Text.Should().Be("Steps:\n1. Press Failing.\n2. Send the report.\nExpect:\n- It failing.", "docker/qa_checks.py keys a result by the same words");
        }

        [Test]
        public void a_card_shows_the_steps_as_a_numbered_list_and_what_to_expect_as_bullets_one_per_line()
        {
            var item = QaChecklist.Parse(Text)[2].Items.Single() with { Checker = new QaChecker(false, null, "needs human eyes", "how it looks") };

            QaText.Item(item, null, null).Should().Be(
                "**Sign-in logo** · 0.4.13\n**Steps**\n1. Sign out.\n\n**Expect**\n- The logo.\n- The light arrow.\n\n-# 👤 For you to check, needs human eyes: how it looks\n⏳ Not tested yet");
        }

        [Test]
        public void the_same_words_under_another_heading_are_the_same_item_and_new_words_are_a_new_one()
        {
            var moved = QaChecklist.Parse(Text.Replace("## 0.4.15 (2026-10-25)", "## Unreleased"), new Version(0, 7, 4));
            var reworded = QaChecklist.Parse(Text.Replace(Passed, "    - A green tick."));

            moved[0].Items.Select(i => i.Key).Should().Equal(Keys("Passing an item", "Failing an item"), "a fold into Unreleased or a build number keeps the result");
            reworded[0].Items[0].Key.Should().NotBe(Key("Passing an item"), "changed words are a changed test");
            reworded[0].Items[1].Key.Should().Be(Key("Failing an item"));
            QaChecklist.Parse(Text.Replace("Press Passes.", "Press   Passes.")).SelectMany(v => v.Items).Select(i => i.Key).Should().Equal(Keys("Passing an item", "Failing an item", "Sign-in logo"), "spacing isn't wording");
        }

        [Test]
        public void unreleased_items_are_the_build_qa_runs()
        {
            const string Pending = "# QA checklist\n\n## Unreleased\n- **Build number**\n  - Steps:\n    1. Open System, then Status.\n  - Expect:\n    - The build's number.\n\n## 0.6.0 (2026-11-13)\n";
            var running = new Version(0, 7, 5);

            var unreleased = QaChecklist.Parse(Pending, running);

            unreleased.Select(v => v.Version).Should().Equal(running, new Version(0, 6, 0));
            QaChecklist.Parse(Pending, new Version(0, 8, 5))[0].Items.Single().Key.Should().Be(unreleased[0].Items.Single().Key, "shipping 0.7.0004 moves QA's minor, and a pass recorded in QA is kept");
            QaChecklist.Parse(Pending).Select(v => v.Version).Should().Equal(new[] { new Version(0, 6, 0) }, "without a running build Unreleased is left out");
        }

        [Test]
        public async Task a_promotion_keeps_unchanged_passes_and_a_reworded_or_new_item_comes_back_untested()
        {
            Mocker.GetMock<IReleaseEnvironment>().Setup(e => e.Shown(It.IsAny<Version>())).Returns("0.7.0004-qa.1111111");
            await Subject.MarkPassedAsync(Key("Passing an item"), 1, "Alex");
            await Subject.MarkPassedAsync(Key("Failing an item"), 1, "Alex");
            await Subject.MarkPassedAsync(Key("Sign-in logo"), 1, "Alex");

            // The next promotion: one item reworded, one new, all of it folded under Unreleased.
            Mocker.GetMock<IReleaseEnvironment>().Setup(e => e.Shown(It.IsAny<Version>())).Returns("0.7.0004-qa.2222222");
            Subject.ChecklistText = () => Text.Replace("## 0.4.15 (2026-10-25)", "## Unreleased\n" + NewThing)
                .Replace("    - The logo.", "    - The new logo.");
            Subject.CurrentVersion = () => new Version(0, 7, 4, 1);
            var state = Subject.State();

            state.Items.Select(i => (i.Name, i.State, i.By)).Should().Equal(
                ("New thing", "Pending", null), ("Passing an item", "Passed", "Alex"), ("Failing an item", "Passed", "Alex"), ("Sign-in logo", "Pending", null));
            state.Should().BeEquivalentTo(new { Total = 4, Passed = 2, Carried = 2, New = 2, Complete = false });
        }

        [TestCase(0, 0, "⏳ **1 left to test.**")]
        [TestCase(2, 1, "🔁 **2 carried over** with their pass from an earlier build; **1 new or changed** in this one.\n⏳ **1 left to test.**")]
        public void the_summary_says_how_many_passes_were_carried_over_and_how_many_items_are_new(int carried, int added, string ending)
        {
            QaText.Summary(new[] { new Version(0, 7, 4) }, 3, 2, 0, new QaCarriedOver(carried, added), NoCheckers, null, null).Should().EndWith(ending);
        }

        [Test]
        public void qa_tests_what_staging_doesnt_have_dev_records_the_running_version_and_only_qa_has_a_channel()
        {
            var versions = QaChecklist.Parse(Text);
            var running = new Version(0, 4, 15, 7);

            QaChecklist.ForServer(versions, Environment("qa", "0.4.13"), running).Select(v => v.Version.ToString()).Should().Equal("0.4.15", "0.4.14");
            QaChecklist.ForServer(versions, Environment("dev"), running).Select(v => v.Version.ToString()).Should().Equal("0.4.15");
            QaChecklist.ForServer(versions, Environment("staging"), running).Should().BeEmpty();
            QaChecklist.ForServer(versions, Environment(null), running).Should().BeEmpty();
            QaChecklist.HasChecklist(Environment("qa")).Should().BeTrue();
            QaChecklist.HasChecklist(Environment("dev")).Should().BeFalse("the checklist is the owner's QA work, on the QA server only");
            QaChecklist.HasChecklist(Environment(null)).Should().BeFalse();
        }

        [Test]
        public async Task the_checklist_completes_when_every_item_has_passed_and_records_who_and_when()
        {
            Subject.State().Should().BeEquivalentTo(new { Total = 3, Passed = 0, Failing = 0, Complete = false });

            await Subject.MarkPassedAsync(Key("Passing an item"), 1, "Alex");
            await Subject.MarkPassedAsync(Key("Failing an item"), 1, "Alex");
            Subject.State().Complete.Should().BeFalse();

            await Subject.MarkPassedAsync(Key("Sign-in logo"), 2, "Sam");
            var state = Subject.State();

            state.Should().BeEquivalentTo(new { Total = 3, Passed = 3, Failing = 0, Complete = true, CompletedBy = "Sam" });
            state.Versions.Should().Equal("0.4.15", "0.4.14", "0.4.13");
            _records[Key("Sign-in logo")].ByDiscordUserId.Should().Be("2");
            _records[Key("Sign-in logo")].At.Should().NotBeNull();
        }

        [Test]
        public async Task a_failing_item_links_its_bug_report_and_blocks_completion_until_it_passes()
        {
            Mocker.GetMock<IBugReportRepository>().Setup(r => r.Find(9)).Returns(new BugReport { Id = 9, IssueUrl = "https://github.com/x/y/issues/1" });

            foreach (var key in Keys("Passing an item", "Sign-in logo"))
            {
                await Subject.MarkPassedAsync(key, 1, "Alex");
            }

            await Subject.MarkFailingAsync(Key("Failing an item"), 1, "Alex", new BugReport { Id = 9 });
            var state = Subject.State();

            state.Should().BeEquivalentTo(new { Passed = 2, Failing = 1, Complete = false });
            state.Items.Single(i => i.Key == Key("Failing an item")).Should().BeEquivalentTo(new { State = "Failing", BugReportId = 9, IssueUrl = "https://github.com/x/y/issues/1" });

            await Subject.MarkPassedAsync(Key("Failing an item"), 2, "Sam");
            Subject.State().Should().BeEquivalentTo(new { Passed = 3, Failing = 0, Complete = true });
        }

        [Test]
        public async Task a_new_promotion_keeps_what_was_recorded_adds_new_items_and_drops_what_staging_has()
        {
            await Subject.MarkPassedAsync(Key("Sign-in logo"), 1, "Alex");
            await Subject.MarkPassedAsync(Key("Passing an item"), 1, "Alex");

            // Staging got 0.4.13, and QA got 0.4.16 with one new item.
            OnQa("0.4.13");
            Subject.ChecklistText = () => Text.Replace("# QA checklist", "# QA checklist\n\n## 0.4.16 (2026-10-26)\n" + NewThing);
            Subject.CurrentVersion = () => new Version(0, 4, 16, 1);
            var state = Subject.State();

            state.Versions.Should().Equal("0.4.16", "0.4.15", "0.4.14");
            state.Items.Select(i => (i.Name, i.State)).Should().Equal(("New thing", "Pending"), ("Passing an item", "Passed"), ("Failing an item", "Pending"));
        }

        [Test]
        public void the_line_under_an_item_says_who_checks_it_and_isnt_part_of_its_wording()
        {
            var checkedText = Text
                .Replace(Passed, PassingAutomated)
                .Replace("    - The light arrow.", "    - The light arrow.\n  - Checked by the owner, needs human eyes: how it looks in the Discord app.");

            var items = QaChecklist.Parse(checkedText).SelectMany(v => v.Items).ToList();

            items.Select(i => i.Checker).Should().Equal(
                new QaChecker(true, "live passing", null, null), QaChecker.Unclassified, new QaChecker(false, null, "needs human eyes", "how it looks in the Discord app"));
            items.Select(i => i.Key).Should().Equal(Keys("Passing an item", "Failing an item", "Sign-in logo"), "a result stays with its item when it's classified");
        }

        [Test]
        public void every_unreleased_item_in_the_shipped_checklist_says_who_checks_it()
        {
            var unreleased = QaChecklist.Parse(QaChecklist.Embedded(), new Version(9, 9, 9999))[0].Items;

            unreleased.Should().OnlyContain(i => i.Checker != QaChecker.Unclassified);
            unreleased.Where(i => !i.Checker.Automated).Select(i => i.Name).Should().BeEmpty("everything in it is checked on dev; only a click only a person can make is the owner's");
        }

        [Test]
        public void the_summary_shows_the_automated_results_apart_from_the_owners_items_left()
        {
            var summary = QaText.Summary(new[] { new Version(0, 7, 5) }, 19, 16, 1, new QaCarriedOver(0, 0), new QaCheckers(17, 16, 1, 2, new[] { "Who checks each item", "Bot picture and banner" }), null, null);

            summary.Should().Contain("🤖 **Automated checks:** 16 of 17 passed, 1 failing, each with what it checked on the card.\n")
                .And.Contain("👤 **For you:** 2 of 2 left: Who checks each item, Bot picture and banner.\n");
            QaText.Summary(new[] { new Version(0, 7, 5) }, 2, 2, 0, new QaCarriedOver(0, 0), new QaCheckers(0, 0, 0, 2, Array.Empty<string>()), "by Alex", null)
                .Should().Contain("👤 **For you:** all 2 passed.").And.NotContain("Automated checks");
        }

        [Test]
        public async Task an_automated_pass_is_recorded_under_no_member_and_its_card_shows_what_it_saw_and_its_screenshots()
        {
            OnDev();
            Subject.ChecklistText = () => Text.Replace(Passed, PassingAutomated);
            Mocker.GetMock<IGuildSetupService>().Setup(g => g.GetConfig()).Returns(new DiscordGuildConfig { QaChecklistChannelId = "100", QaChecklistSummaryMessageId = "200" });
            var key = Subject.State().Items[0].Key;
            _records[key].MessageId = "300";
            var edits = new List<InteractionReply>();
            Mocker.GetMock<IDiscordGuildGateway>().Setup(g => g.EditMessageAsync(100, 300, It.IsAny<InteractionReply>())).Callback<ulong, ulong, InteractionReply>((_, _, r) => edits.Add(r)).ReturnsAsync(true);

            Subject.SetEvidence(key, "Pressed Passes twice; the card says passed.", new[] { new QaEvidenceFile("passing-light.jpg", new byte[] { 7 }) });
            await Subject.MarkPassedAsync(key, QaChecker.AutomationId, QaChecker.AutomationName);
            var state = Subject.State();

            _records[key].Should().BeEquivalentTo(new { ByDiscordUserId = (string)null, ByName = "automation", Evidence = "Pressed Passes twice; the card says passed.", EvidenceFiles = new[] { "passing-light.jpg" } });
            state.Items[0].Should().BeEquivalentTo(new { CheckedBy = "automation", Check = "live passing", State = "Passed", By = "automation" });
            state.Items[1].Should().BeEquivalentTo(new { CheckedBy = "owner", Reason = "not classified yet" });
            state.Checkers.Should().BeEquivalentTo(new QaCheckers(1, 1, 0, 1, new[] { "Failing an item" }));
            var card = edits[^1];
            card.Cards.Single().Text.Should().Contain("🤖 Checked automatically by the live passing check.").And.Contain("✅ Passed automatically").And.Contain("**What the check saw:** Pressed Passes twice");
            card.Cards.Single().Images.Should().Equal("passing-light.jpg");
            card.Files.Select(f => f.Name).Should().Equal("passing-light.jpg");
            card.KeepFiles.Should().BeFalse();
            Mocker.GetMock<Requestarr.Core.Audit.IAuditService>().Verify(a => a.Record(Requestarr.Core.Audit.Actor.Automation, "qa.checklist.pass", "qa-item", key, null), Times.Once());

            // The next edit (a press, a restart) keeps the screenshots the message has: nothing is uploaded again.
            await Subject.RefreshItemAsync(key);
            edits[^1].Should().BeEquivalentTo(new { KeepFiles = true, Files = new List<InteractionFile>() }, o => o.Including(r => r.KeepFiles).Including(r => r.Files));
            edits[^1].Cards.Single().Images.Should().Equal("passing-light.jpg");
        }

        [Test]
        public async Task a_card_discord_wont_edit_keeping_its_screenshots_sends_them_again()
        {
            OnDev();
            Subject.ChecklistText = () => Text.Replace(Passed, PassingAutomated);
            Mocker.GetMock<IGuildSetupService>().Setup(g => g.GetConfig()).Returns(new DiscordGuildConfig { QaChecklistChannelId = "100" });
            var key = Subject.State().Items[0].Key;
            _records[key].MessageId = "300";
            _records[key].EvidenceFiles = new List<string> { "passing-light.jpg" };
            _screenshots[key] = new[] { new QaEvidenceFile("passing-light.jpg", new byte[] { 7 }) };
            var edits = new List<InteractionReply>();
            Mocker.GetMock<IDiscordGuildGateway>().Setup(g => g.EditMessageAsync(100, 300, It.IsAny<InteractionReply>())).Callback<ulong, ulong, InteractionReply>((_, _, r) => edits.Add(r)).ReturnsAsync((ulong _, ulong _, InteractionReply r) => !r.KeepFiles);

            await Subject.RefreshItemAsync(key);

            edits.Select(e => (e.KeepFiles, e.Files.Count)).Should().Equal((true, 0), (false, 1));
        }

        [Test]
        public async Task qa_shows_only_the_owners_items_and_forgets_the_automated_results_keeping_the_owners()
        {
            Subject.ChecklistText = () => Text.Replace(Passed, PassingAutomated);
            var automatedKey = QaChecklist.Parse(Subject.ChecklistText()).SelectMany(v => v.Items).Single(i => i.Name == "Passing an item").Key;
            _records[automatedKey] = new QaChecklistItem { Id = 70, ItemKey = automatedKey, Name = "Passing an item", State = QaItemState.Passed, ByName = "automation", EvidenceFiles = new List<string> { "a.jpg" } };
            _records[Key("Sign-in logo")] = new QaChecklistItem { Id = 71, ItemKey = Key("Sign-in logo"), Name = "Sign-in logo", State = QaItemState.Passed, ByDiscordUserId = "1", ByName = "EaZyCheaZ" };
            Mocker.GetMock<IQaChecklistItemRepository>().Setup(r => r.All()).Returns(() => _records.Values.ToList());
            Mocker.GetMock<IQaChecklistItemRepository>().Setup(r => r.Delete(It.IsAny<QaChecklistItem>())).Callback<QaChecklistItem>(r => _records.Remove(r.ItemKey));
            Mocker.GetMock<IGuildSetupService>().Setup(g => g.GetConfig()).Returns(new DiscordGuildConfig { QaChecklistChannelId = "100" });
            Mocker.GetMock<IDiscordGuildGateway>().Setup(g => g.GetOwnMessagesAsync(100, It.IsAny<int>())).ReturnsAsync(new List<ChannelMessage>());
            Mocker.GetMock<IDiscordGuildGateway>().Setup(g => g.SendMessageAsync(100, It.IsAny<InteractionReply>())).ReturnsAsync(500UL);

            await Subject.EnsureAsync();
            var state = Subject.State();

            state.Items.Select(i => i.Name).Should().Equal("Failing an item", "Sign-in logo");
            state.Should().BeEquivalentTo(new { Total = 2, Passed = 1 });
            _records.Should().NotContainKey(automatedKey);
            _records[Key("Sign-in logo")].ByName.Should().Be("EaZyCheaZ");
            Mocker.GetMock<IQaEvidenceStore>().Verify(e => e.Replace(automatedKey, It.Is<IReadOnlyList<QaEvidenceFile>>(f => f.Count == 0)), Times.Once());
            Mocker.GetMock<IDiscordGuildGateway>().Verify(g => g.SendMessageAsync(100, It.IsAny<InteractionReply>()), Times.Exactly(3), "the summary and the owner's two cards");
            Subject.Find(automatedKey).Should().BeNull("a result for it is refused on QA");
        }

        [Test]
        public void dev_shows_the_automated_items_with_their_results()
        {
            OnDev();
            Subject.ChecklistText = () => Text.Replace(Passed, PassingAutomated);

            Subject.State().Items.Select(i => (i.Name, i.CheckedBy)).Should().Equal(("Passing an item", "automation"), ("Failing an item", "owner"));
        }

        [Test]
        public async Task an_item_no_longer_on_the_checklist_cant_be_marked()
        {
            await Subject.MarkPassedAsync("0.3.0/old", 1, "Alex");

            _records.Should().NotContainKey("0.3.0/old");
        }

        // An item's key in the fixture's checklist, found by its name.
        private static string Key(string name) => QaChecklist.Parse(Text).SelectMany(v => v.Items).Single(i => i.Name == name).Key;

        private static string[] Keys(params string[] names) => names.Select(Key).ToArray();

        private void OnDev()
        {
            var environment = Environment("dev");
            Mocker.GetMock<IReleaseEnvironment>().SetupGet(e => e.VersionPosts).Returns(environment.VersionPosts);
            Mocker.GetMock<IReleaseEnvironment>().SetupGet(e => e.IconSet).Returns(environment.IconSet);
        }

        private void OnQa(string staged = "")
        {
            var environment = Environment("qa", staged);
            Mocker.GetMock<IReleaseEnvironment>().SetupGet(e => e.VersionPosts).Returns(environment.VersionPosts);
            Mocker.GetMock<IReleaseEnvironment>().SetupGet(e => e.Staged).Returns(environment.Staged);
            Mocker.GetMock<IReleaseEnvironment>().SetupGet(e => e.IconSet).Returns(environment.IconSet);
            Mocker.GetMock<IGuildSetupService>().Setup(g => g.GetConfig()).Returns((DiscordGuildConfig)null);
        }

        private static ReleaseEnvironment Environment(string name, string staged = "") => new(Moq.Mock.Of<NzbDrone.Core.Configuration.IConfigService>())
        {
            Variable = v => v switch
            {
                ReleaseEnvironment.EnvironmentVariable => name,
                ReleaseEnvironment.StagedVersionsVariable => staged,
                _ => null
            }
        };
    }
}
