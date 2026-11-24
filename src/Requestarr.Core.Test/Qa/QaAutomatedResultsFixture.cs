using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Discord;
using FluentAssertions;
using Moq;
using NUnit.Framework;
using NzbDrone.Common.EnvironmentInfo;
using Requestarr.Core.Discord;
using Requestarr.Core.Discord.Gateway;
using Requestarr.Core.Discord.Interactions;
using Requestarr.Core.Qa;
using Requestarr.Core.Test.Framework;

namespace Requestarr.Core.Test.Qa
{
    // The automated results on the QA checklist: a pass is recorded as the automation's, a failure files a bug report like
    // an admin's Failing, and only the items the automation checks take a result.
    [TestFixture]
    public class QaAutomatedResultsFixture : CoreTest<QaAutomatedResults>
    {
        private static readonly QaChecklistEntry AutomatedItem = new(new Version(0, 7, 5), "build-number.1a2b3c4d", "Build number", "Steps:\n1. Open Status.\nExpect:\n- The number.")
        {
            Checker = new QaChecker(true, "web app status page", null, null)
        };

        private static readonly QaChecklistEntry Owners = new(new Version(0, 7, 5), "bot-picture.5e6f7a8b", "Bot picture", "Steps:\n1. Open the profile.\nExpect:\n- The picture.")
        {
            Checker = new QaChecker(false, null, "needs human eyes", "only in the Discord app")
        };

        private static readonly QaEvidenceFile Screenshot = new("status-page-light.jpg", new byte[] { 1, 2, 3 });

        [SetUp]
        public void Setup()
        {
            Mocker.GetMock<IQaChecklistService>().Setup(c => c.Find(AutomatedItem.Key)).Returns(AutomatedItem);
            Mocker.GetMock<IQaChecklistService>().Setup(c => c.Find(Owners.Key)).Returns(Owners);
            ItemIs("Pending", null);
            Mocker.GetMock<IBugReportService>().Setup(r => r.SubmitAsync(It.IsAny<ulong>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<bool>()))
                .ReturnsAsync(new BugReportResult(new BugReport { Id = 5 }, "Thanks"));
        }

        [Test]
        public async Task an_admins_failing_stands_and_keeps_the_automated_evidence()
        {
            ItemIs("Failing", "Alex");

            var outcome = await Subject.RecordAsync(AutomatedItem.Key, new QaAutomatedResult(true, "Saw it.", "web-app", Array.Empty<QaEvidenceFile>()));

            outcome.Should().Be(QaAutomatedOutcome.KeptAdminsFailing);
            Mocker.GetMock<IQaChecklistService>().Verify(c => c.SetEvidence(AutomatedItem.Key, "Saw it.", It.IsAny<IReadOnlyList<QaEvidenceFile>>()), Times.Once());
            Mocker.GetMock<IQaChecklistService>().Verify(c => c.MarkPassedAsync(It.IsAny<string>(), It.IsAny<ulong>(), It.IsAny<string>()), Times.Never());
        }

        [Test]
        public async Task failing_again_files_no_second_report_and_a_pass_clears_its_own_automated_failure()
        {
            ItemIs("Failing", "automation");

            await Subject.RecordAsync(AutomatedItem.Key, new QaAutomatedResult(false, "Still wrong.", "web-app", Array.Empty<QaEvidenceFile>()));
            await Subject.RecordAsync(AutomatedItem.Key, new QaAutomatedResult(true, "Fixed.", "web-app", Array.Empty<QaEvidenceFile>()));

            Mocker.GetMock<IBugReportService>().Verify(r => r.SubmitAsync(It.IsAny<ulong>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<bool>()), Times.Never());
            Mocker.GetMock<IQaChecklistService>().Verify(c => c.MarkPassedAsync(AutomatedItem.Key, QaChecker.AutomationId, "automation"), Times.Once());
        }

        [Test]
        public async Task a_failure_with_no_bug_reports_channel_still_marks_the_item_failing()
        {
            Mocker.GetMock<IBugReportService>().Setup(r => r.SubmitAsync(It.IsAny<ulong>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<bool>()))
                .ReturnsAsync(new BugReportResult(null, QaText.NoChannel));

            await Subject.RecordAsync(AutomatedItem.Key, new QaAutomatedResult(false, "Wrong.", "web-app", Array.Empty<QaEvidenceFile>()));

            Mocker.GetMock<IQaChecklistService>().Verify(c => c.MarkFailingAsync(AutomatedItem.Key, QaChecker.AutomationId, "automation", null), Times.Once());
        }

        private void ItemIs(string state, string by)
        {
            var items = new[] { AutomatedItem, Owners }
                .Select(e => new QaChecklistItemState(e.Key, "0.7.0005", e.Name, state, by, null, null, null, "owner", null, null, null, new List<string>()) { ByAutomation = by == QaChecker.AutomationName })
                .ToList();
            Mocker.GetMock<IQaChecklistService>().Setup(c => c.State()).Returns(new QaChecklistState("qa", new[] { "0.7.0005" }, 2, 0, 0, 0, 0, false, null, null, items));
        }

        [Test]
        public async Task a_pass_keeps_its_evidence_and_is_recorded_as_automated()
        {
            var outcome = await Subject.RecordAsync(AutomatedItem.Key, new QaAutomatedResult(true, "Status shows 0.7.0005-qa.abc1234.", "web-app", new[] { Screenshot }));

            outcome.Should().Be(QaAutomatedOutcome.Recorded);
            Mocker.GetMock<IQaChecklistService>().Verify(c => c.SetEvidence(AutomatedItem.Key, "Status shows 0.7.0005-qa.abc1234.", It.Is<IReadOnlyList<QaEvidenceFile>>(f => f.Single() == Screenshot)), Times.Once());
            Mocker.GetMock<IQaChecklistService>().Verify(c => c.MarkPassedAsync(AutomatedItem.Key, QaChecker.AutomationId, "automation"), Times.Once());
            Mocker.GetMock<IBugReportService>().VerifyNoOtherCalls();
        }

        [Test]
        public async Task a_failure_files_a_bug_report_on_the_item_without_the_report_limit()
        {
            await Subject.RecordAsync(AutomatedItem.Key, new QaAutomatedResult(false, "Status shows 0.7.0004-qa.0000000.", "web-app", Array.Empty<QaEvidenceFile>()));

            const string What = "The automated web app status page check failed: Status shows 0.7.0004-qa.0000000.";
            Mocker.GetMock<IBugReportService>().Verify(r => r.SubmitAsync(QaChecker.AutomationId, "automation", "web-app", What, AutomatedItem.Text, AutomatedItem.Key, false), Times.Once());
            Mocker.GetMock<IQaChecklistService>().Verify(c => c.MarkPassedAsync(It.IsAny<string>(), It.IsAny<ulong>(), It.IsAny<string>()), Times.Never());
        }

        [Test]
        public async Task the_owners_items_and_items_not_on_the_checklist_take_no_result()
        {
            var result = new QaAutomatedResult(true, "Saw it.", "other", Array.Empty<QaEvidenceFile>());

            (await Subject.RecordAsync(Owners.Key, result)).Should().Be(QaAutomatedOutcome.NotAutomated);
            (await Subject.RecordAsync("gone.00000000", result)).Should().Be(QaAutomatedOutcome.NotOnChecklist);
            Mocker.GetMock<IQaChecklistService>().Verify(c => c.SetEvidence(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<IReadOnlyList<QaEvidenceFile>>()), Times.Never());
            Mocker.GetMock<IBugReportService>().VerifyNoOtherCalls();
        }
    }

    // The screenshots kept for a checklist item, in the app data folder.
    [TestFixture]
    public class QaEvidenceStoreFixture : CoreTest<QaEvidenceStore>
    {
        [SetUp]
        public void Setup()
        {
            Mocker.GetMock<IAppFolderInfo>().SetupGet(f => f.AppDataFolder).Returns(TempFolder);
        }

        [Test]
        public void a_new_result_replaces_the_items_screenshots_keeping_only_safe_names_and_sizes()
        {
            Subject.Replace("web-app-logo.3f9a2c1d", new[] { new QaEvidenceFile("old.jpg", new byte[] { 9 }) });

            var kept = Subject.Replace("web-app-logo.3f9a2c1d", new[]
            {
                new QaEvidenceFile("header-light.jpg", new byte[] { 1 }),
                new QaEvidenceFile("../escape.jpg", new byte[] { 2 }),
                new QaEvidenceFile("Upper.JPG", new byte[] { 3 }),
                new QaEvidenceFile("empty.png", Array.Empty<byte>()),
                new QaEvidenceFile("too-big.png", new byte[QaEvidenceStore.MaxBytes + 1]),
                new QaEvidenceFile("header-dark.png", new byte[] { 4 })
            });

            kept.Should().Equal("header-light.jpg", "header-dark.png");
            Subject.Read("web-app-logo.3f9a2c1d", new[] { "old.jpg", "header-light.jpg", "header-dark.png" }).Select(f => (f.Name, f.Bytes.Single()))
                .Should().Equal(("header-light.jpg", (byte)1), ("header-dark.png", (byte)4));
            Directory.GetFiles(Path.Combine(TempFolder, "qa-evidence", "web-app-logo.3f9a2c1d")).Should().HaveCount(2);
        }

        [Test]
        public void at_most_four_screenshots_are_kept_and_none_leaves_no_folder()
        {
            Subject.Replace("a.1", Enumerable.Range(1, 6).Select(n => new QaEvidenceFile($"shot-{n}.jpg", new byte[] { 1 })).ToList()).Should().HaveCount(QaEvidenceStore.MaxFiles);
            Subject.Replace("a.1", Array.Empty<QaEvidenceFile>()).Should().BeEmpty();
            Directory.Exists(Path.Combine(TempFolder, "qa-evidence", "a.1")).Should().BeFalse();
        }
    }

    // A card's screenshots go with its message and show under its text.
    [TestFixture]
    public class QaEvidenceCardFixture
    {
        [Test]
        public void a_cards_images_are_a_gallery_of_the_files_the_message_brings()
        {
            var reply = new InteractionReply
            {
                Files = new List<InteractionFile> { new("status-page-light.jpg", new byte[] { 1 }) },
                Cards = { new InteractionCard { Text = "**Build number**", Images = new List<string> { "status-page-light.jpg" } } }
            };

            var gallery = DiscordNetResponder.Components(reply).Components.OfType<ContainerComponent>().Single().Components.OfType<MediaGalleryComponent>().Single();

            gallery.Items.Select(i => i.Media.Url).Should().Equal("attachment://status-page-light.jpg");
            DiscordAssets.For(reply).Select(f => f.Name).Should().Equal("status-page-light.jpg");
        }
    }
}
