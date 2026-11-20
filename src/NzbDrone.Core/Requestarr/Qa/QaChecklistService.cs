using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using NLog;
using NzbDrone.Common.EnvironmentInfo;
using NzbDrone.Core.Messaging.Events;
using Requestarr.Core.Announcements;
using Requestarr.Core.Audit;
using Requestarr.Core.Discord;
using Requestarr.Core.Discord.Gateway;
using Requestarr.Core.Discord.Interactions;

namespace Requestarr.Core.Qa
{
    // CheckedBy: "automation" (with the Check that verifies it) or "owner" (with the Reason a person has to).
    public record QaChecklistItemState(string Key, string Version, string Name, string State, string By, DateTime? At, int? BugReportId, string IssueUrl, string CheckedBy, string Check, string Reason, string Evidence, IReadOnlyList<string> EvidenceFiles)
    {
        // Passed or failed by the automated checks, not an admin (QaChecker.IsAutomation).
        public bool ByAutomation { get; init; }
    }

    // What the automation reads from the QA stack before promoting to staging, and what the portal will show.
    // Carried: the items passed on an earlier build that kept their pass (their wording didn't change). New: the items
    // that first appeared on the running build (new, or reworded). Checkers: the automated items and how many passed,
    // and the owner's and how many are left.
    public record QaChecklistState(string Environment, IReadOnlyList<string> Versions, int Total, int Passed, int Failing, int Carried, int New, bool Complete, string CompletedBy, DateTime? CompletedAt, IReadOnlyList<QaChecklistItemState> Items)
    {
        public QaCheckers Checkers { get; init; }
    }

    public interface IQaChecklistService
    {
        // Posts the checklist in its channel, or brings it in line with QA-CHECKLIST.md and the record.
        Task EnsureAsync();

        QaChecklistEntry Find(string key);
        QaChecklistState State();

        // discordUserId QaChecker.AutomationId: the automated run (QaAutomatedResults).
        Task MarkPassedAsync(string key, ulong discordUserId, string name);
        Task MarkFailingAsync(string key, ulong discordUserId, string name, BugReport report);

        // What the automated check read back and its screenshots, kept with the item and shown on its card.
        void SetEvidence(string key, string evidence, IReadOnlyList<QaEvidenceFile> screenshots);

        // A bug report's issue link arrived: the failing item's card shows it.
        Task RefreshItemAsync(string key);
    }

    // The QA test checklist in Discord: a summary card, then one card per item with Passes and Failing, for every version
    // QA has that staging doesn't. The record is the truth; the cards show it. Dev keeps a record of the running
    // version's items, where the automated results are recorded before a promotion, and posts no cards (owner,
    // 2026-11-20: the checklist is the owner's QA work, on the QA server only). A new promotion to QA adds the new
    // versions' items (as not tested yet) and drops the versions staging now has; every pass or failure already recorded
    // stays.
    public class QaChecklistService : IQaChecklistService, IHandleAsync<DiscordReadyEvent>
    {
        private static readonly SemaphoreSlim Gate = new(1, 1);

        // Items whose screenshots changed since their card was last sent: the next edit uploads them.
        private readonly ConcurrentDictionary<string, bool> _newScreenshots = new();

        private readonly IDiscordGuildGateway _gateway;
        private readonly IGuildSetupService _guildSetup;
        private readonly IDiscordGuildConfigRepository _configs;
        private readonly IQaChecklistItemRepository _items;
        private readonly IBugReportRepository _reports;
        private readonly IReleaseEnvironment _environment;
        private readonly IAuditService _audit;
        private readonly IQaEvidenceStore _evidence;
        private readonly Logger _logger;

        public QaChecklistService(IDiscordGuildGateway gateway, IGuildSetupService guildSetup, IDiscordGuildConfigRepository configs, IQaChecklistItemRepository items, IBugReportRepository reports, IReleaseEnvironment environment, IAuditService audit, IQaEvidenceStore evidence, Logger logger)
        {
            _gateway = gateway;
            _guildSetup = guildSetup;
            _configs = configs;
            _items = items;
            _reports = reports;
            _environment = environment;
            _audit = audit;
            _evidence = evidence;
            _logger = logger;
        }

        // Settable so tests can use their own checklist and version.
        public Func<string> ChecklistText { get; set; } = QaChecklist.Embedded;
        public Func<Version> CurrentVersion { get; set; } = () => BuildInfo.Version;

        private bool OwnersOnly => _environment.VersionPosts == VersionPostMode.NotYetStaged;

        public void HandleAsync(DiscordReadyEvent message)
        {
            EnsureAsync().GetAwaiter().GetResult();
        }

        public IReadOnlyList<QaChecklistVersion> Versions() =>
            QaChecklist.ForServer(QaChecklist.Parse(ChecklistText(), Changelog.Release(CurrentVersion())), _environment, CurrentVersion());

        // QA's checklist is the owner's: the automated items run on dev before a build is promoted and never show on QA.
        // Dev's record has them, with their automated results.
        public IReadOnlyList<QaChecklistEntry> Entries() => Versions().SelectMany(v => v.Items).Where(e => !(e.Checker.Automated && OwnersOnly)).ToList();

        public QaChecklistEntry Find(string key) => Entries().FirstOrDefault(e => e.Key == key);

        public QaChecklistState State()
        {
            var entries = Entries();
            var records = Records(entries);
            var reports = records.Values.Where(r => r.BugReportId.HasValue).Select(r => _reports.Find(r.BugReportId.Value)).Where(r => r != null).ToDictionary(r => r.Id);
            var passed = records.Values.Count(r => r.State == QaItemState.Passed);
            var failing = records.Values.Count(r => r.State == QaItemState.Failing);
            var complete = entries.Count > 0 && passed == entries.Count;
            var last = complete ? records.Values.OrderByDescending(r => r.At).First() : null;

            var items = entries.Select(e =>
            {
                var r = records[e.Key];
                var report = r.BugReportId.HasValue && reports.TryGetValue(r.BugReportId.Value, out var found) ? found : null;
                var checkedBy = e.Checker.Automated ? QaChecker.AutomationName : "owner";
                return new QaChecklistItemState(e.Key, Changelog.Display(e.Version), e.Name, r.State.ToString(), r.ByName, r.At, r.BugReportId, report?.IssueUrl, checkedBy, e.Checker.Check, e.Checker.Reason, r.Evidence, r.EvidenceFiles ?? new List<string>())
                {
                    ByAutomation = QaChecker.IsAutomation(r)
                };
            }).ToList();

            return new QaChecklistState(_environment.IconSet, Versions().Select(v => Changelog.Display(v.Version)).ToList(), entries.Count, passed, failing, Carried(records), New(records), complete, last?.ByName, last?.At, items)
            {
                Checkers = Checkers(entries, records)
            };
        }

        public async Task EnsureAsync()
        {
            // Only QA's guild setup makes a checklist channel (or a live check on dev, which removes it again).
            if (!ulong.TryParse(_guildSetup.GetConfig()?.QaChecklistChannelId, out var channel))
            {
                return;
            }

            await Gate.WaitAsync();

            try
            {
                ForgetAutomatedResultsOnQa();
                var entries = Entries();
                var records = Records(entries);
                var config = _guildSetup.GetConfig();

                // The cards, in order: the summary, then each item. Edited in place when they're all still there in
                // that order; otherwise the channel is cleared and posted again (nothing is lost: the record has it).
                var wanted = new List<string> { config.QaChecklistSummaryMessageId }.Concat(entries.Select(e => records[e.Key].MessageId)).ToList();
                var have = (await _gateway.GetOwnMessagesAsync(channel, 500)).Select(m => m.Id.ToString()).ToList();

                if (have.SequenceEqual(wanted))
                {
                    await _gateway.EditMessageAsync(channel, ulong.Parse(wanted[0]), SummaryCard(entries, records));

                    foreach (var entry in entries)
                    {
                        await EditItemCardAsync(channel, entry, records[entry.Key]);
                    }

                    return;
                }

                foreach (var id in have)
                {
                    await _gateway.DeleteMessageAsync(channel, ulong.Parse(id));
                }

                config.QaChecklistSummaryMessageId = (await _gateway.SendMessageAsync(channel, SummaryCard(entries, records))).ToString();
                _configs.SetFields(config, c => c.QaChecklistSummaryMessageId);

                foreach (var entry in entries)
                {
                    var record = records[entry.Key];
                    record.MessageId = (await _gateway.SendMessageAsync(channel, ItemCard(entry, record, CardFiles.Upload))).ToString();
                    _newScreenshots.TryRemove(entry.Key, out _);
                    _items.SetFields(record, r => r.MessageId);
                }

                _logger.Info("QA checklist posted: {0} item(s) for {1}", entries.Count, string.Join(", ", Versions().Select(v => Changelog.Display(v.Version))));
            }
            catch (Exception ex)
            {
                _logger.Warn(ex, "Could not post the QA checklist");
            }
            finally
            {
                Gate.Release();
            }
        }

        public async Task MarkPassedAsync(string key, ulong discordUserId, string name)
        {
            if (Find(key) == null)
            {
                return;
            }

            var wasComplete = State().Complete;
            var record = _items.FindByKey(key);

            record.State = QaItemState.Passed;
            record.ByDiscordUserId = MemberId(discordUserId);
            record.ByName = name;
            record.At = DateTime.UtcNow;
            record.RecordedOn = RunningBuild();
            _items.Update(record);
            _audit.Record(ActorFor(discordUserId, name), "qa.checklist.pass", "qa-item", key);

            await RefreshItemAsync(key);

            var state = State();

            if (state.Complete && !wasComplete)
            {
                _audit.Record(ActorFor(discordUserId, name), "qa.checklist.complete", "qa-checklist", string.Join(",", state.Versions));
                _logger.Info("QA checklist complete for {0}, the last item passed by {1}", string.Join(", ", state.Versions), name);
            }
        }

        public async Task MarkFailingAsync(string key, ulong discordUserId, string name, BugReport report)
        {
            if (Find(key) == null)
            {
                return;
            }

            // Every item on the checklist has a record, even before the cards were posted.
            var record = Records(Entries())[key];

            record.State = QaItemState.Failing;
            record.ByDiscordUserId = MemberId(discordUserId);
            record.ByName = name;
            record.At = DateTime.UtcNow;
            record.RecordedOn = RunningBuild();
            record.BugReportId = report?.Id;
            _items.Update(record);
            _audit.Record(ActorFor(discordUserId, name), "qa.checklist.fail", "qa-item", key, report != null ? $"bug report {report.Id}" : null);

            await RefreshItemAsync(key);
        }

        public void SetEvidence(string key, string evidence, IReadOnlyList<QaEvidenceFile> screenshots)
        {
            if (Find(key) == null)
            {
                return;
            }

            var record = Records(Entries())[key];
            record.Evidence = evidence?.Trim();
            record.EvidenceFiles = _evidence.Replace(key, screenshots).ToList();
            _items.SetFields(record, r => r.Evidence, r => r.EvidenceFiles);
            _newScreenshots[key] = true;
        }

        public async Task RefreshItemAsync(string key)
        {
            var config = _guildSetup.GetConfig();
            var entry = Find(key);
            var record = _items.FindByKey(key);

            if (entry == null || record?.MessageId == null || !ulong.TryParse(config?.QaChecklistChannelId, out var channel))
            {
                return;
            }

            var entries = Entries();
            await EditItemCardAsync(channel, entry, record);

            if (ulong.TryParse(config.QaChecklistSummaryMessageId, out var summary))
            {
                await _gateway.EditMessageAsync(channel, summary, SummaryCard(entries, Records(entries)));
            }
        }

        // Every item has a record; new ones start as not tested yet.
        private Dictionary<string, QaChecklistItem> Records(IReadOnlyList<QaChecklistEntry> entries)
        {
            var records = new Dictionary<string, QaChecklistItem>();

            foreach (var entry in entries)
            {
                records[entry.Key] = _items.FindByKey(entry.Key) ??
                    _items.Insert(new QaChecklistItem { ItemKey = entry.Key, Version = Changelog.Display(entry.Version), Name = entry.Name, State = QaItemState.Pending, AddedOn = RunningBuild() });
            }

            return records;
        }

        // Results the automation recorded on QA before its items left QA's checklist (November 15, 2026) go, with their
        // screenshots; the owner's own results stay.
        private void ForgetAutomatedResultsOnQa()
        {
            if (!OwnersOnly)
            {
                return;
            }

            foreach (var record in _items.All().Where(r => r.ByName == QaChecker.AutomationName && r.ByDiscordUserId == null).ToList())
            {
                _evidence.Replace(record.ItemKey, Array.Empty<QaEvidenceFile>());
                _items.Delete(record);
                _logger.Info("QA checklist: removed the automated result on {0}; its checks run before a promotion now", record.Name);
            }
        }

        private static string MemberId(ulong discordUserId) => discordUserId == QaChecker.AutomationId ? null : discordUserId.ToString();

        private static Actor ActorFor(ulong discordUserId, string name) => discordUserId == QaChecker.AutomationId ? Actor.Automation : Actor.Discord(discordUserId, name);

        private static QaCheckers Checkers(IReadOnlyList<QaChecklistEntry> entries, Dictionary<string, QaChecklistItem> records)
        {
            var automated = entries.Where(e => e.Checker.Automated).Select(e => records[e.Key]).ToList();
            var ownerLeft = entries.Where(e => !e.Checker.Automated && records[e.Key].State != QaItemState.Passed).Select(e => e.Name).ToList();

            return new QaCheckers(automated.Count, automated.Count(r => r.State == QaItemState.Passed), automated.Count(r => r.State == QaItemState.Failing), entries.Count - automated.Count, ownerLeft);
        }

        private string RunningBuild() => _environment.Shown(CurrentVersion());

        private int Carried(Dictionary<string, QaChecklistItem> records)
        {
            var running = RunningBuild();

            return records.Values.Count(r => r.State == QaItemState.Passed && r.RecordedOn != running);
        }

        private int New(Dictionary<string, QaChecklistItem> records)
        {
            var running = RunningBuild();

            return records.Values.Count(r => r.AddedOn == running);
        }

        private InteractionReply SummaryCard(IReadOnlyList<QaChecklistEntry> entries, Dictionary<string, QaChecklistItem> records)
        {
            var passed = records.Values.Count(r => r.State == QaItemState.Passed);
            var failing = records.Values.Count(r => r.State == QaItemState.Failing);
            var last = entries.Count > 0 && passed == entries.Count ? records.Values.OrderByDescending(r => r.At).First() : null;

            return new InteractionReply
            {
                Quiet = true,
                Cards = { new InteractionCard { Color = SummaryColor(failing, complete: last != null), Text = QaText.Summary(Versions().Select(v => v.Version).ToList(), entries.Count, passed, failing, new QaCarriedOver(Carried(records), New(records)), Checkers(entries, records), last == null ? null : QaText.By(last), last?.At) } }
            };
        }

        private static uint SummaryColor(int failing, bool complete)
        {
            if (failing > 0)
            {
                return QaText.FailingColor;
            }

            return complete ? QaText.PassedColor : QaText.ChecklistColor;
        }

        // A card's screenshots are uploaded once per result: later edits (a press, a restart) keep the files the message
        // has. If Discord won't keep them, they're sent again.
        private async Task EditItemCardAsync(ulong channel, QaChecklistEntry entry, QaChecklistItem record)
        {
            var message = ulong.Parse(record.MessageId);

            if (!_newScreenshots.ContainsKey(entry.Key) && await _gateway.EditMessageAsync(channel, message, ItemCard(entry, record, CardFiles.Keep)))
            {
                return;
            }

            if (await _gateway.EditMessageAsync(channel, message, ItemCard(entry, record, CardFiles.Upload)))
            {
                _newScreenshots.TryRemove(entry.Key, out _);
            }
        }

        // The automated check's screenshots, if any, show under the item's text: sent with the card, or kept from when they were.
        private InteractionReply ItemCard(QaChecklistEntry entry, QaChecklistItem record, CardFiles files)
        {
            var report = record.BugReportId.HasValue ? _reports.Find(record.BugReportId.Value) : null;
            var screenshots = files == CardFiles.Upload ? _evidence.Read(entry.Key, record.EvidenceFiles) : new List<QaEvidenceFile>();
            var names = files == CardFiles.Upload ? screenshots.Select(s => s.Name).ToList() : (record.EvidenceFiles ?? new List<string>()).ToList();

            return new InteractionReply
            {
                Quiet = true,
                Files = screenshots.Select(s => new InteractionFile(s.Name, s.Bytes)).ToList(),
                KeepFiles = files == CardFiles.Keep,
                Cards =
                {
                    new InteractionCard
                    {
                        Color = record.State switch { QaItemState.Passed => QaText.PassedColor, QaItemState.Failing => QaText.FailingColor, _ => QaText.ChecklistColor },
                        Text = QaText.Item(entry, record, report),
                        Images = names,
                        Buttons = new List<InteractionButton>
                        {
                            new() { CustomId = $"{QaRoutes.Check}:pass:{entry.Key}", Label = QaText.PassLabel, Style = ButtonStyle.Success },
                            new() { CustomId = $"{QaRoutes.Check}:fail:{entry.Key}", Label = QaText.FailLabel, Style = ButtonStyle.Danger }
                        }
                    }
                }
            };
        }

        private enum CardFiles
        {
            Upload,
            Keep
        }
    }
}
