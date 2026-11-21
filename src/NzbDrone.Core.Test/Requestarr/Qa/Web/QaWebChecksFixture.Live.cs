using System;
using System.Threading.Tasks;
using FluentAssertions;
using NUnit.Framework;

namespace Requestarr.Core.Test.Qa.Web
{
    public partial class QaWebChecksFixture
    {
        // Pages update by themselves, as Sonarr's do: with the Requests table open, a request a run seeded is denied from
        // outside the page (the API, as Discord's buttons reach the same service), and its row changes to Denied with no
        // refresh, the page's own marker still there. Without a seeded request waiting, the check changes nothing.
        [Test]
        public Task live_updates() => CheckAsync(nameof(live_updates), async (browser, check) =>
        {
            await SignInAsync(browser);
            var (ids, isMarked) = await RowsOfferingAsync(browser, "approve", "deny");

            if (!isMarked)
            {
                check.Saw($"live updates: no \"{Marked}approve\" request waiting, so nothing changed");
                return;
            }

            await OpenRequestsTableAsync(browser);
            await ScrollToRowAsync(browser, Row(ids[0]));
            await browser.RunAsync("window.liveCheckMarker = true");
            var before = await browser.EvalAsync<string>($"{Row(ids[0])}.innerText.replace(/\\s+/g, ' ')");

            var status = await browser.EvalAsync<int>($"fetch(window.Requestarr.urlBase + '/api/v1/portal/requests/{ids[0]}/deny', {{ method: 'POST', headers: {{ 'X-Api-Key': window.Requestarr.apiKey, 'Content-Type': 'application/json' }}, body: JSON.stringify({{ reason: 'Automated check: live update' }}) }}).then(r => r.status)");
            await browser.WaitForAsync($"{Row(ids[0])}?.innerText.includes('Denied')", TimeSpan.FromSeconds(30));
            var after = await browser.EvalAsync<string>($"{Row(ids[0])}.innerText.replace(/\\s+/g, ' ')");
            var kept = await browser.EvalAsync<bool>("window.liveCheckMarker === true");

            check.Saw($"live updates: request {ids[0]} denied from outside the page (HTTP {status}); its row went from \"{before}\" to \"{after}\", the page not reloaded: {kept}");
            await ShotBothAsync(browser, check, "live-update");
            status.Should().Be(200);
            kept.Should().BeTrue("the row changed by itself, without the page being loaded again");
        });
    }
}
