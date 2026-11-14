using System.Collections.Generic;
using System.Threading.Tasks;

namespace Requestarr.Core.Qa
{
    // One item's result from Claude Code's automated run (docker/qa_checks.py record): passed or failing, what the check
    // read back, the part of Requestarr a failure is about, and the screenshots it took.
    public record QaAutomatedResult(bool Passed, string Evidence, string Component, IReadOnlyList<QaEvidenceFile> Screenshots);

    public enum QaAutomatedOutcome
    {
        Recorded,
        NotOnChecklist,
        NotClaudeCodes
    }

    public interface IQaAutomatedResults
    {
        Task<QaAutomatedOutcome> RecordAsync(string key, QaAutomatedResult result);
    }

    // Claude Code's results go on the checklist the way an admin's do: a pass is "Passed by Claude Code"; a failure files
    // a bug report like an admin's Failing (posted, filed on GitHub, and linked on the card), which blocks the gate the
    // same way. The evidence stays on the item's card so the owner can spot-check it. Only the items the checklist says
    // Claude Code checks take a result.
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

            if (!entry.Checker.ClaudeCode)
            {
                return QaAutomatedOutcome.NotClaudeCodes;
            }

            _checklist.SetEvidence(key, result.Evidence, result.Screenshots);

            if (result.Passed)
            {
                await _checklist.MarkPassedAsync(key, QaChecker.ClaudeCodeId, QaChecker.ClaudeCodeName);
            }
            else
            {
                // Submitting with the item's key marks it failing and links the report.
                await _reports.SubmitAsync(QaChecker.ClaudeCodeId, QaChecker.ClaudeCodeName, result.Component, $"Claude Code's {entry.Checker.Check} check failed: {result.Evidence}", entry.Text, key, limited: false);
            }

            return QaAutomatedOutcome.Recorded;
        }
    }
}
