using System.Linq;
using System.Text.Json;
using System.Threading.Tasks;
using FluentAssertions;
using NUnit.Framework;

namespace Requestarr.Core.Test.Qa.Web
{
    public partial class QaWebChecksFixture
    {
        // The rows the check acts on: made up for it (the proof seeds them), so no real request is ever changed.
        private const string Marked = "Automated check: ";

        private const int BulkRows = 3;

        // The portal's answer for a path, read with the page's own sign-in, then picked from in the page.
        private const string Portal = "(path => fetch(window.Requestarr.urlBase + '/api/v1/portal/' + path, { headers: { 'X-Api-Key': window.Requestarr.apiKey } }).then(r => r.json()))";
        private const string Outcome = "document.querySelector('[class*=\"Alert-success\"], [class*=\"Alert-danger\"]')";

        // Requests' actions where titles are listed. Every row in the Table view has its actions. A row's Approve, select
        // mode's Delete record on three history rows and a history row's trash button each act and are read back. A
        // library title nobody requested gets Start removal from the header search, and the removal is dropped again
        // afterwards. In both themes. Only rows titled with the marker are acted on, and a server without them passes.
        [Test]
        public Task request_actions() => CheckAsync(nameof(request_actions), async (browser, check) =>
        {
            await SignInAsync(browser);

            foreach (var theme in Themes)
            {
                await browser.SetThemeAsync(theme);
                await OpenRequestsTableAsync(browser);
                var menus = await browser.EvalAsync<int>("document.querySelectorAll('tbody [class*=\"RequestRows-actions\"]').length");
                var expected = await PortalAsync<int>(browser, "requests", "r => r.filter(q => q.actions.length || q.media?.openUrl).length");
                check.Saw($"requests, Table ({theme}): {menus} rows with actions; the portal says {expected} have some");
                menus.Should().Be(expected, "every row offers what its page offers");
                await check.ShotAsync(browser, $"table-{theme}");
            }

            await ApproveFromRowAsync(browser, check);
            await DeleteRecordsInSelectModeAsync(browser, check);
            await DeleteRecordFromRowAsync(browser, check);
            await RemovalFromSearchAsync(browser, check);
        });

        private static Task<T> PortalAsync<T>(HeadlessBrowser browser, string path, string pick) =>
            browser.EvalAsync<T>($"{Portal}({JsonSerializer.Serialize(path)}).then({pick})");

        private static Task<int[]> MarkedAsync(HeadlessBrowser browser, string name, string action) =>
            PortalAsync<int[]>(browser, "requests", $"r => r.filter(q => q.title === '{Marked}{name}' && q.actions.includes('{action}')).map(q => q.id)");

        private static async Task OpenRequestsTableAsync(HeadlessBrowser browser)
        {
            await browser.GoAsync($"{Url}/requests");
            await browser.WaitForAsync("!!document.querySelector('[class*=\"RequestViews-\"], table, [class*=\"Portal-empty\"]')");
            await browser.RunAsync("[...document.querySelectorAll('button')].find(b => b.innerText.trim() === 'View').click()");
            await browser.RunAsync("[...document.querySelectorAll('button, a, div[role=\"button\"]')].find(e => e.innerText.trim() === 'Table' && e.closest('[class*=\"MenuContent\"]')).click()");
            await browser.WaitForAsync("!!document.querySelector('tbody tr')");
        }

        private static string Row(int id) => $"[...document.querySelectorAll('tbody tr')].find(r => r.querySelector('a[href$=\"/requests/{id}\"]'))";

        // The result in both themes, light last.
        private static async Task ShotBothAsync(HeadlessBrowser browser, WebCheck check, string label)
        {
            foreach (var theme in Themes.AsEnumerable().Reverse())
            {
                await browser.SetThemeAsync(theme);
                await check.ShotAsync(browser, $"{label}-{theme}");
            }
        }

        private static async Task<string> OutcomeAsync(HeadlessBrowser browser)
        {
            await browser.WaitForAsync($"!!{Outcome}");
            return await browser.EvalAsync<string>($"{Outcome}.innerText.trim()");
        }

        private static async Task ApproveFromRowAsync(HeadlessBrowser browser, WebCheck check)
        {
            var ids = await MarkedAsync(browser, "approve", "approve");

            if (ids.Length == 0)
            {
                check.Saw($"approve: no \"{Marked}approve\" request waiting, so nothing approved");
                return;
            }

            await OpenRequestsTableAsync(browser);
            await browser.RunAsync($"{Row(ids[0])}.querySelector('[title=\"Actions\"]').click()");
            await browser.RunAsync("[...document.querySelectorAll('[class*=\"MenuItem-menuItem\"]')].find(e => e.innerText.trim() === 'Approve').click()");
            var said = await OutcomeAsync(browser);
            var status = await PortalAsync<string>(browser, $"requests/{ids[0]}", "d => d.request.status");
            check.Saw($"approve from its row's menu: \"{said}\"; the request is now {status}");
            status.Should().NotBe("pending", "the row's Approve acted on it");
            await ShotBothAsync(browser, check, "approved");
        }

        private static async Task DeleteRecordsInSelectModeAsync(HeadlessBrowser browser, WebCheck check)
        {
            var ids = (await MarkedAsync(browser, "history", "deleteRecord")).Take(BulkRows).ToArray();

            if (ids.Length < BulkRows)
            {
                check.Saw($"select mode: fewer than {BulkRows} \"{Marked}history\" rows, so nothing deleted");
                return;
            }

            await OpenRequestsTableAsync(browser);
            await browser.RunAsync("[...document.querySelectorAll('button')].find(b => b.innerText.trim() === 'Select').click()");

            foreach (var id in ids)
            {
                await browser.WaitForAsync($"!!{Row(id)}?.querySelector('input[type=checkbox]')");
                await browser.RunAsync($"{Row(id)}.querySelector('input[type=checkbox]').click()");
            }

            await browser.WaitForAsync($"document.querySelector('[class*=\"SelectFooter-count\"]')?.innerText.trim() === '{BulkRows} selected'");
            await browser.RunAsync("[...document.querySelectorAll('[class*=\"SelectFooter-actions\"] button')].find(b => b.innerText.trim() === 'Delete record').click()");
            await browser.WaitForAsync("document.querySelectorAll('[class*=\"SelectFooter-titles\"] li').length > 0");
            var listed = await browser.EvalAsync<string>("[...document.querySelectorAll('[class*=\"SelectFooter-titles\"] li')].map(l => l.innerText).join(', ')");
            check.Saw($"select mode, {BulkRows} picked, Delete record asks first, listing: {listed}");
            await ShotBothAsync(browser, check, "select-confirm");
            await browser.RunAsync("[...document.querySelectorAll('[class*=\"ModalFooter\"] button')].find(b => b.innerText.trim() === 'Delete record').click()");
            var said = await OutcomeAsync(browser);
            var left = await PortalAsync<int>(browser, "requests", $"r => r.filter(q => {JsonSerializer.Serialize(ids)}.includes(q.id)).length");
            check.Saw($"select mode's Delete record: \"{said}\"; {left} of the {BulkRows} left");
            left.Should().Be(0, "select mode deleted every record picked");
            await ShotBothAsync(browser, check, "select-done");
            await browser.RunAsync("[...document.querySelectorAll('button')].find(b => b.innerText.trim() === 'Stop selecting').click()");
        }

        private static async Task DeleteRecordFromRowAsync(HeadlessBrowser browser, WebCheck check)
        {
            var ids = await MarkedAsync(browser, "delete one", "deleteRecord");

            if (ids.Length == 0)
            {
                check.Saw($"Delete record: no \"{Marked}delete one\" row, so nothing deleted");
                return;
            }

            await OpenRequestsTableAsync(browser);
            await browser.RunAsync($"{Row(ids[0])}.querySelector('[title=\"Delete record\"]').click()");
            await browser.WaitForAsync("[...document.querySelectorAll('[class*=\"ModalFooter\"] button')].some(b => b.innerText.trim() === 'Delete record')");
            var asked = await browser.EvalAsync<string>("document.querySelector('[class*=\"ModalBody\"]').innerText.trim()");
            await browser.RunAsync("[...document.querySelectorAll('[class*=\"ModalFooter\"] button')].find(b => b.innerText.trim() === 'Delete record').click()");
            var said = await OutcomeAsync(browser);
            var gone = await PortalAsync<bool>(browser, "requests", $"r => !r.some(q => q.id === {ids[0]})");
            check.Saw($"a history row's trash button asked \"{asked}\", then: \"{said}\"; gone from the list: {gone}");
            gone.Should().BeTrue("Delete record removed the record");
            await ShotBothAsync(browser, check, "deleted");
        }

        private static async Task RemovalFromSearchAsync(HeadlessBrowser browser, WebCheck check)
        {
            var title = await PortalAsync<string>(browser, "library?term=", "t => t.find(x => !x.requested)?.title ?? ''");

            if (title.Length == 0)
            {
                check.Saw("search: every library title has a request here, so there's nothing unrequested to remove");
                return;
            }

            await browser.GoAsync(Url + "/system/status");
            await browser.WaitForAsync("!!document.querySelector('[class*=\"PortalSearch-input\"]')");
            await browser.RunAsync($"const e = document.querySelector('[class*=\"PortalSearch-input\"]'); e.focus(); Object.getOwnPropertyDescriptor(HTMLInputElement.prototype, 'value').set.call(e, {JsonSerializer.Serialize(title)}); e.dispatchEvent(new Event('input', {{ bubbles: true }}))");
            await browser.WaitForAsync("document.querySelector('[class*=\"PortalSearch-results\"]')?.innerText.includes('In your library, not requested')");
            check.Saw($"search for \"{title}\": {await browser.EvalAsync<string>("document.querySelector('[class*=\"PortalSearch-results\"]').innerText.replace(/\\s+/g, ' ')")}");
            await ShotBothAsync(browser, check, "search-library");
            await browser.RunAsync("[...document.querySelectorAll('[class*=\"PortalSearch-removal\"]')].find(l => l.innerText.trim() === 'Start removal').click()");
            await browser.WaitForAsync("[...document.querySelectorAll('[class*=\"ModalBody\"] button')].some(b => b.innerText.trim() === 'Start removal')");
            await browser.RunAsync("[...document.querySelectorAll('[class*=\"ModalBody\"] button')].find(b => b.innerText.trim() === 'Start removal').click()");
            var said = await OutcomeAsync(browser);
            var removal = await PortalAsync<int>(browser, "title-removals", $"r => r.find(x => x.title.startsWith({JsonSerializer.Serialize(title)}))?.removal.id ?? 0");
            check.Saw($"Start removal from the search: \"{said}\"; its open removal: {removal}");
            removal.Should().NotBe(0, "the search's Start removal started one");
            await ShotBothAsync(browser, check, "search-removal");

            // Dropped again, so the library and the dev server's votes are as they were.
            await browser.EvalAsync<bool>($"fetch(window.Requestarr.urlBase + '/api/v1/portal/removals/{removal}/drop', {{ method: 'POST', headers: {{ 'X-Api-Key': window.Requestarr.apiKey, 'Content-Type': 'application/json' }}, body: '{{}}' }}).then(r => r.ok)");
            check.Saw($"removal {removal} dropped again");
        }
    }
}
