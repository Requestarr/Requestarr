using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Mvc;
using Requestarr.Core.Qa;

namespace Requestarr.Http.Qa
{
    // What Claude Code reads from the QA stack at the start of every goal (docker/qa-gate.py): the QA checklist's state
    // and the bug reports this server sent. Needs the API key, like the rest of the API.
    public class QaChecklistController : Controller
    {
        // Four screenshots of up to 2 MB each, in base64, with room for the rest.
        private const long MaxResultBytes = 12 * 1024 * 1024;

        private readonly IQaChecklistService _checklist;
        private readonly IBugReportRepository _reports;
        private readonly IQaAutomatedResults _automated;

        public QaChecklistController(IQaChecklistService checklist, IBugReportRepository reports, IQaAutomatedResults automated)
        {
            _checklist = checklist;
            _reports = reports;
            _automated = automated;
        }

        // Claude Code's result for one item it checks (docker/qa_checks.py record), with its evidence: passed, or failing
        // (which files a bug report like an admin's Failing). 404: no such item here; 409: the owner checks that item.
        [HttpPost("/api/v1/qa/checklist/{key}/result")]
        [Produces("application/json")]
        [RequestSizeLimit(MaxResultBytes)]
        public async Task<IActionResult> RecordResult(string key, [FromBody] QaResultResource result)
        {
            if (result == null || string.IsNullOrWhiteSpace(result.Evidence))
            {
                return BadRequest(new { message = "A result needs its evidence: what the check read back." });
            }

            var screenshots = (result.Screenshots ?? new List<QaScreenshotResource>()).Select(s => new QaEvidenceFile(s.Name, Decode(s.Data))).ToList();
            var outcome = await _automated.RecordAsync(key, new QaAutomatedResult(result.Passed, result.Evidence, result.Component, screenshots));

            return outcome switch
            {
                QaAutomatedOutcome.NotOnChecklist => NotFound(new { message = $"{key} isn't on this server's checklist." }),
                QaAutomatedOutcome.NotClaudeCodes => Conflict(new { message = $"{key} is the owner's to check, not Claude Code's." }),
                _ => Json(_checklist.State().Items.Single(i => i.Key == key))
            };
        }

        [HttpGet("/api/v1/qa/checklist")]
        [Produces("application/json")]
        public IActionResult Get()
        {
            var state = _checklist.State();
            var all = _reports.All().OrderBy(r => r.Id).ToList();
            var reports = all
                .Where(r => state.Versions.Contains(r.Version))
                .Select(r => new { r.Id, r.Component, r.Version, r.Environment, r.Created, r.ChecklistItemKey, Filing = r.Filing.ToString(), r.FilingProblem, r.IssueNumber, r.IssueUrl })
                .ToList();

            // Every report from this server that isn't on GitHub, whatever its version: each is an open bug the gate
            // can't see on GitHub, so it blocks until it's filed (and then closed there).
            var unfiled = all
                .Where(r => r.Filing != BugReportFiling.Filed)
                .Select(r => new { r.Id, r.Component, r.Version, r.Created, Filing = r.Filing.ToString(), r.FilingProblem, What = r.WhatHappened })
                .ToList();

            return Json(new { checklist = state, bugReports = reports, unfiledReports = unfiled });
        }

        private static byte[] Decode(string data)
        {
            try
            {
                return Convert.FromBase64String(data ?? string.Empty);
            }
            catch (FormatException)
            {
                return Array.Empty<byte>();
            }
        }
    }

    public class QaResultResource
    {
        public bool Passed { get; set; }
        public string Evidence { get; set; }

        // The part of Requestarr a failure is about (a bug report part: web-app, admin, access...).
        public string Component { get; set; }
        public List<QaScreenshotResource> Screenshots { get; set; }
    }

    // A screenshot: its file name (lower case, .jpg or .png) and its bytes in base64.
    public class QaScreenshotResource
    {
        public string Name { get; set; }
        public string Data { get; set; }
    }
}
