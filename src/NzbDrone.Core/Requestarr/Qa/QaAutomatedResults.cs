using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;

namespace Requestarr.Core.Qa
{
    // One item's result from the automated run (docker/qa_checks.py record): passed or failing, what the check
    // read back, the part of Requestarr a failure is about, and the screenshots it took.
    public record QaAutomatedResult(bool Passed, string Evidence, string Component, IReadOnlyList<QaEvidenceFile> Screenshots);

    public enum QaAutomatedOutcome
    {
        Recorded,
        NotOnChecklist,
        NotAutomated,

        // An admin marked it failing: the evidence is kept, the failure stands.
        KeptAdminsFailing
    }

    public interface IQaAutomatedResults
    {
        Task<QaAutomatedOutcome> RecordAsync(string key, QaAutomatedResult result);
    }

    // The automated results go on the checklist the way an admin's do: a pass is "Passed automatically"; a failure files
    // a bug report like an admin's Failing (posted, filed on GitHub, and linked on the card), which blocks the gate the
    // same way. The evidence stays on the item's card so the owner can spot-check it. Only the items the checklist says
    // the automation checks take a result.
    public class QaAutomatedResults : IQaAutomatedResults
    {
        private readonly IQaChecklistService _checklist;
        private readonly IBugReportService _reports;

        public QaAutomatedResults(IQaChecklistService checklist, IBugReportService reports)
        {
            _checklist = checklist;
            _reports = reports;
        }

        public async Task<QaAutomatedOutcome> RecordAsync(string key, QaAutomatedResult result)
        {
            var entry = _checklist.Find(key);

            if (entry == null)
            {
                return QaAutomatedOutcome.NotOnChecklist;
            }

            if (!entry.Checker.Automated)
            {
                return QaAutomatedOutcome.NotAutomated;
            }

            var current = _checklist.State().Items.Single(i => i.Key == key);
            var failing = current.State == nameof(QaItemState.Failing);
            _checklist.SetEvidence(key, result.Evidence, result.Screenshots);

            // An admin's Failing stands until an admin passes the item; an automated failure already has its report.
            if (failing && (!current.ByAutomation || !result.Passed))
            {
                await _checklist.RefreshItemAsync(key);
                return current.ByAutomation ? QaAutomatedOutcome.Recorded : QaAutomatedOutcome.KeptAdminsFailing;
            }

            if (result.Passed)
            {
                await _checklist.MarkPassedAsync(key, QaChecker.AutomationId, QaChecker.AutomationName);
                return QaAutomatedOutcome.Recorded;
            }

            // Submitting with the item's key marks it failing and links the report. Without a bug reports channel there's
            // no report, and the item is failing all the same, so the gate stays blocked.
            var sent = await _reports.SubmitAsync(QaChecker.AutomationId, QaChecker.AutomationName, result.Component, $"The automated {entry.Checker.Check} check failed: {result.Evidence}", entry.Text, key, limited: false);

            if (sent.Report == null)
            {
                await _checklist.MarkFailingAsync(key, QaChecker.AutomationId, QaChecker.AutomationName, null);
            }

            return QaAutomatedOutcome.Recorded;
        }
    }
}
