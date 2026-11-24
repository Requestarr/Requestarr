using System;
using System.Threading.Tasks;
using FluentAssertions;
using NUnit.Framework;

namespace Requestarr.Core.Test.Qa.Web
{
    public partial class QaWebChecksFixture
    {
        // Pages update by themselves, as Sonarr's do: with the Requests table open, the request the run seeded for this
        // check is denied from outside the page (the API, as Discord's buttons reach the same service), and its row's
        // status changes with no refresh, the page's own marker still there. Without it there's nothing to see change,
        // so the check fails rather than pass untested.
        [Test]
        public Task live_updates() => CheckAsync(nameof(live_updates), async (browser, check) =>
        {
            await SignInAsync(browser);
            var (ids, isMarked) = await RowsOfferingAsync(browser, "live update", "deny");

            check.Saw(isMarked ? $"live updates: \"{Marked}live update\" is waiting (request {ids[0]})" : $"live updates: no \"{Marked}live update\" request waiting");
            isMarked.Should().BeTrue("the run seeds a request for this check (docker/qa_checks.py), and without it nothing would change");

            await OpenRequestsTableAsync(browser);
            await ScrollToRowAsync(browser, Row(ids[0]));
            await browser.RunAsync("window.liveCheckMarker = true");
            var before = await browser.EvalAsync<string>($"{Row(ids[0])}.innerText.replace(/\\s+/g, ' ')");

            var status = await browser.EvalAsync<int>($"fetch(window.Requestarr.urlBase + '/api/v1/portal/requests/{ids[0]}/deny', {{ method: 'POST', headers: {{ 'X-Api-Key': window.Requestarr.apiKey, 'Content-Type': 'application/json' }}, body: JSON.stringify({{ reason: 'Automated check: live update' }}) }}).then(r => r.status)");

            // Denied, or closed as already available when the test media manager has it.
            await browser.WaitForAsync($"!{Row(ids[0])}?.innerText.includes('Waiting for approval')", TimeSpan.FromSeconds(30));
            var after = await browser.EvalAsync<string>($"{Row(ids[0])}.innerText.replace(/\\s+/g, ' ')");
            var kept = await browser.EvalAsync<bool>("window.liveCheckMarker === true");

            check.Saw($"live updates: request {ids[0]} denied from outside the page (HTTP {status}); its row went from \"{before}\" to \"{after}\", the page not reloaded: {kept}");
            await ShotBothAsync(browser, check, "live-update");
            status.Should().Be(200);
            kept.Should().BeTrue("the row changed by itself, without the page being loaded again");
        });
    }
}
