using System.Linq;
using System.Text.Json;
using System.Threading.Tasks;
using FluentAssertions;
using NUnit.Framework;

namespace Requestarr.Core.Test.Qa.Web
{
    public partial class QaWebChecksFixture
    {
        // Rows made up for the check (a run can seed them): only these are approved or deleted, never a real request. Without
        // them the check opens the same confirmations on real rows and cancels.
        private const string Marked = "Automated check: ";

        private const int BulkRows = 3;

        // The portal's answer for a path, read with the page's own sign-in, then picked from in the page.
        private const string Portal = "(path => fetch(window.Requestarr.urlBase + '/api/v1/portal/' + path, { headers: { 'X-Api-Key': window.Requestarr.apiKey } }).then(r => r.json()))";
        private const string Outcome = "document.querySelector('[class*=\"Alert-success\"], [class*=\"Alert-danger\"]')";
        private const string ModalButton = "[...document.querySelectorAll('[class*=\"ModalFooter\"] button')]";

        // Requests' actions where titles are listed. Every row in the Table view has its actions, and a row's menu lists
        // what its page offers. Select mode picks three history rows and Delete record asks first, listing them. A history
        // row's trash button asks first, naming it. Marked rows are approved and deleted and read back. A library title
        // nobody requested gets Start removal from the header search, and the removal is dropped again afterwards. In both
        // themes.
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

            await RowMenuAsync(browser, check);
            await ApproveFromRowAsync(browser, check);
            await DeleteRecordsInSelectModeAsync(browser, check);
            await DeleteRecordFromRowAsync(browser, check);
            await RemovalFromSearchAsync(browser, check);
        });

        private static Task<T> PortalAsync<T>(HeadlessBrowser browser, string path, string pick) =>
            browser.EvalAsync<T>($"{Portal}({JsonSerializer.Serialize(path)}).then({pick})");

        // Rows offering an action: the marked ones named so when there are any (isMarked), otherwise any.
        private static async Task<(int[] Ids, bool IsMarked)> RowsOfferingAsync(HeadlessBrowser browser, string name, string action)
        {
            var offering = $"r.filter(q => q.actions.includes('{action}'))";
            var marked = await PortalAsync<int[]>(browser, "requests", $"r => {offering}.filter(q => q.title === '{Marked}{name}').map(q => q.id)");

            return marked.Length > 0 ? (marked, true) : (await PortalAsync<int[]>(browser, "requests", $"r => {offering}.map(q => q.id)"), false);
        }

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

        // A confirmation's button by its words: pressed (confirm) or Cancel.
        private static Task AnswerAsync(HeadlessBrowser browser, string words, bool confirm) =>
            browser.RunAsync($"{ModalButton}.find(b => b.innerText.trim() === '{(confirm ? words : "Cancel")}').click()");

        // The first row's menu lists as many actions as its page offers, plus Open in Sonarr or Radarr where it has that.
        private static async Task RowMenuAsync(HeadlessBrowser browser, WebCheck check)
        {
            var id = await PortalAsync<int>(browser, "requests", "r => r.find(q => q.actions.length)?.id ?? 0");
            var offers = await PortalAsync<int>(browser, "requests", $"r => (q => q.actions.length + (q.media?.openUrl ? 1 : 0))(r.find(q => q.id === {id}))");
            await OpenRequestsTableAsync(browser);
            await browser.RunAsync($"{Row(id)}.querySelector('[title=\"Actions\"]').click()");
            await browser.WaitForAsync("!!document.querySelector('[class*=\"MenuItem-menuItem\"]')");
            var items = await browser.EvalAsync<string[]>("[...document.querySelectorAll('[class*=\"MenuItem-menuItem\"]')].map(e => e.innerText.trim())");
            check.Saw($"request {id}'s menu: {string.Join(", ", items)}; its page offers {offers}");
            items.Should().HaveCount(offers, "a row's menu has exactly what its page offers");
            await ShotBothAsync(browser, check, "row-menu");
        }

        private static async Task ApproveFromRowAsync(HeadlessBrowser browser, WebCheck check)
        {
            var (ids, isMarked) = await RowsOfferingAsync(browser, "approve", "approve");

            if (!isMarked)
            {
                check.Saw($"approve: no \"{Marked}approve\" request waiting, so nothing approved (the menu is checked above)");
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
            var (found, isMarked) = await RowsOfferingAsync(browser, "history", "deleteRecord");
            var ids = found.Take(BulkRows).ToArray();

            if (ids.Length < BulkRows)
            {
                check.Saw($"select mode: fewer than {BulkRows} history rows, so nothing to pick");
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
            await browser.WaitForAsync($"document.querySelectorAll('[class*=\"SelectFooter-titles\"] li').length === {BulkRows}");
            var listed = await browser.EvalAsync<string>("[...document.querySelectorAll('[class*=\"SelectFooter-titles\"] li')].map(l => l.innerText).join(', ')");
            check.Saw($"select mode, {BulkRows} picked, Delete record asks first, listing: {listed}");
            await ShotBothAsync(browser, check, "select-confirm");
            await AnswerAsync(browser, "Delete record", isMarked);

            if (isMarked)
            {
                var said = await OutcomeAsync(browser);
                var left = await PortalAsync<int>(browser, "requests", $"r => r.filter(q => {JsonSerializer.Serialize(ids)}.includes(q.id)).length");
                check.Saw($"select mode's Delete record: \"{said}\"; {left} of the {BulkRows} left");
                left.Should().Be(0, "select mode deleted every record picked");
                await ShotBothAsync(browser, check, "select-done");
            }

            await browser.RunAsync("[...document.querySelectorAll('button')].find(b => b.innerText.trim() === 'Stop selecting').click()");
        }

        private static async Task DeleteRecordFromRowAsync(HeadlessBrowser browser, WebCheck check)
        {
            var (ids, isMarked) = await RowsOfferingAsync(browser, "delete one", "deleteRecord");

            if (ids.Length == 0)
            {
                check.Saw("Delete record: no history row");
                return;
            }

            await OpenRequestsTableAsync(browser);
            await browser.RunAsync($"{Row(ids[0])}.querySelector('[title=\"Delete record\"]').click()");
            await browser.WaitForAsync($"{ModalButton}.some(b => b.innerText.trim() === 'Delete record')");
            var asked = await browser.EvalAsync<string>("document.querySelector('[class*=\"ModalBody\"]').innerText.trim()");
            var title = await PortalAsync<string>(browser, "requests", $"r => r.find(q => q.id === {ids[0]}).title");
            check.Saw($"a history row's trash button asks first: \"{asked}\"");
            asked.Should().Contain(title, "the confirmation names the title");
            await ShotBothAsync(browser, check, "delete-confirm");
            await AnswerAsync(browser, "Delete record", isMarked);

            if (isMarked)
            {
                var said = await OutcomeAsync(browser);
                var gone = await PortalAsync<bool>(browser, "requests", $"r => !r.some(q => q.id === {ids[0]})");
                check.Saw($"then: \"{said}\"; gone from the list: {gone}");
                gone.Should().BeTrue("Delete record removed the record");
                await ShotBothAsync(browser, check, "deleted");
            }
        }

        private static async Task RemovalFromSearchAsync(HeadlessBrowser browser, WebCheck check)
        {
            var found = await PortalAsync<string[]>(browser, "library?term=", "t => t.length ? [t[0].title, t[0].label] : ['', '']");
            var (title, label) = (found[0], found[1]);

            if (title.Length == 0)
            {
                check.Saw("search: every library title has a request here, so there's nothing unrequested to remove");
                return;
            }

            const string Results = "[...document.querySelectorAll('[class*=\"PortalSearch-resultRow\"]')]";
            var ours = $"{Results}.find(r => r.innerText.includes({JsonSerializer.Serialize(label)}))";
            await browser.GoAsync(Url + "/system/status");
            await browser.WaitForAsync("!!document.querySelector('[class*=\"PortalSearch-input\"]')");
            await browser.RunAsync($"const e = document.querySelector('[class*=\"PortalSearch-input\"]'); e.focus(); Object.getOwnPropertyDescriptor(HTMLInputElement.prototype, 'value').set.call(e, {JsonSerializer.Serialize(title)}); e.dispatchEvent(new Event('input', {{ bubbles: true }}))");
            await browser.WaitForAsync($"document.querySelector('[class*=\"PortalSearch-results\"]')?.innerText.includes('In your library, not requested') && !!{ours}");
            check.Saw($"search for \"{title}\": {await browser.EvalAsync<string>("document.querySelector('[class*=\"PortalSearch-results\"]').innerText.replace(/\\s+/g, ' ')")}");
            await ShotBothAsync(browser, check, "search-library");
            await browser.RunAsync($"[...{ours}.querySelectorAll('[class*=\"PortalSearch-removal\"]')].find(l => l.innerText.trim() === 'Start removal').click()");
            await browser.WaitForAsync("[...document.querySelectorAll('[class*=\"ModalBody\"] button')].some(b => b.innerText.trim() === 'Start removal')");
            await browser.RunAsync("[...document.querySelectorAll('[class*=\"ModalBody\"] button')].find(b => b.innerText.trim() === 'Start removal').click()");
            var said = await OutcomeAsync(browser);
            var removal = await PortalAsync<int>(browser, "title-removals", $"r => r.find(x => x.title === {JsonSerializer.Serialize(label)})?.removal.id ?? 0");
            check.Saw($"Start removal on {label} from the search: \"{said}\"; its open removal: {removal}");
            await ShotBothAsync(browser, check, "search-removal");

            if (removal != 0)
            {
                // Dropped again, so the library and the dev server's votes are as they were.
                await browser.EvalAsync<bool>($"fetch(window.Requestarr.urlBase + '/api/v1/portal/removals/{removal}/drop', {{ method: 'POST', headers: {{ 'X-Api-Key': window.Requestarr.apiKey, 'Content-Type': 'application/json' }}, body: '{{}}' }}).then(r => r.ok)");
                check.Saw($"removal {removal} dropped again");
            }

            removal.Should().NotBe(0, "the search's Start removal started one on that title");
        }
    }
}
