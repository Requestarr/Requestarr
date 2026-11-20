using System;
using System.Linq;
using System.Text.Json;
using System.Threading.Tasks;
using FluentAssertions;
using NUnit.Framework;

namespace Requestarr.Core.Test.Qa.Web
{
    public partial class QaWebChecksFixture
    {
        // Rows made up for the check (a run can seed them, and removes them afterwards): only these are approved or
        // deleted, never a real request. Without them the check opens the same confirmations on real rows and cancels.
        private const string Marked = "Automated check: ";

        private const int Picks = 3;

        // The owner's window, about this wide.
        private const int OwnersWidth = 1300;

        // The portal's answer for a path, read with the page's own sign-in, then picked from in the page.
        private const string Portal = "(path => fetch(window.Requestarr.urlBase + '/api/v1/portal/' + path, { headers: { 'X-Api-Key': window.Requestarr.apiKey } }).then(r => r.json()))";
        private const string Outcome = "document.querySelector('[class*=\"Alert-success\"], [class*=\"Alert-danger\"]')";
        private const string ModalButton = "[...document.querySelectorAll('[class*=\"ModalFooter\"] button')]";
        private const string Bar = "document.querySelector('[class*=\"SelectFooter-footer\"]')";
        private const string BarButtons = "[...document.querySelectorAll('[class*=\"SelectFooter-footer\"] button')]";

        // Servarr's messages in the sidebar, where a select-mode action reports what it did.
        private const string Message = "document.querySelector('[class*=\"Message-message\"]')";

        // The bar's actions for requests, by their name in the portal's answer, in the bar's order.
        private const string BulkLabels = "{ approve: 'Approve', deny: 'Deny', remove: 'Start removal', deleteRecord: 'Delete record' }";

        // Starting a removal from the search changes the dev server (a vote): only when the run asks, and cleans up.
        private static bool ActsOnLibrary => Environment.GetEnvironmentVariable("QA_CHECKS_ACT_ON_LIBRARY") == "1";

        // Requests' actions and picking, at the owner's window width, in both themes. Every row has its actions beside its
        // title and status; Select Requests shows Sonarr's select mode: three rows picked show the bar along the bottom
        // with the count and what all three allow; Select All picks every request; Unselect All empties it. A row's menu
        // lists what its page offers. Marked rows are approved and deleted and read back; real rows only get the
        // confirmations, cancelled. A library title nobody requested gets Start removal from the header search.
        [Test]
        public Task request_actions() => CheckAsync(nameof(request_actions), RequestActionsAsync, OwnersWidth);

        private static async Task RequestActionsAsync(HeadlessBrowser browser, WebCheck check)
        {
            await SignInAsync(browser);

            foreach (var theme in Themes)
            {
                await browser.SetThemeAsync(theme);
                await PickingAsync(browser, check, theme);
            }

            await RowMenuAsync(browser, check);
            await ApproveFromRowAsync(browser, check);
            await DeleteRecordsFromBarAsync(browser, check);
            await DeleteRecordFromRowAsync(browser, check);
            await RemovalFromSearchAsync(browser, check);
        }

        private static Task<T> PortalAsync<T>(HeadlessBrowser browser, string path, string pick) =>
            browser.EvalAsync<T>($"{Portal}({JsonSerializer.Serialize(path)}).then({pick})");

        // Rows offering an action: the marked ones named so when there are any (isMarked), otherwise any.
        private static async Task<(int[] Ids, bool IsMarked)> RowsOfferingAsync(HeadlessBrowser browser, string name, string action)
        {
            var offering = $"r.filter(q => q.actions.includes('{action}'))";
            var marked = await PortalAsync<int[]>(browser, "requests", $"r => {offering}.filter(q => q.title === '{Marked}{name}').map(q => q.id)");

            if (marked.Length > 0)
            {
                return (marked, true);
            }

            return (await PortalAsync<int[]>(browser, "requests", $"r => {offering}.map(q => q.id)"), false);
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

        // A toolbar button by its words.
        private static Task PressToolbarAsync(HeadlessBrowser browser, string label) =>
            browser.RunAsync($"[...document.querySelectorAll('[class*=\"PageToolbar\"] button')].find(b => b.innerText.trim() === {JsonSerializer.Serialize(label)}).click()");

        private static async Task<string> MessageAsync(HeadlessBrowser browser)
        {
            await browser.WaitForAsync($"!!{Message}");
            return await browser.EvalAsync<string>($"{Message}.innerText.trim()");
        }

        private static Task<string> BarTextAsync(HeadlessBrowser browser) => browser.EvalAsync<string>($"{Bar}?.innerText.replace(/\\s+/g, ' ').trim() ?? ''");

        // No boxes until Select Requests; then a box on every row and in the header; three picked, then Select All (every
        // request the filter shows, across the pages), then Unselect All and Stop Selecting; and each row's own action
        // button in full view.
        private static async Task PickingAsync(HeadlessBrowser browser, WebCheck check, string theme)
        {
            await OpenRequestsTableAsync(browser);
            var before = await browser.EvalAsync<int>("document.querySelectorAll('tbody tr input[type=checkbox]').length");
            check.Saw($"requests, Table ({theme}, {OwnersWidth}px): {before} boxes before Select Requests");
            before.Should().Be(0, "boxes show only in select mode, as Sonarr's do");

            var buttons = await browser.EvalAsync<string>("[...document.querySelectorAll('tbody [class*=\"RequestRows-actions\"] button')].filter(b => b.innerText.trim()).slice(0, 3).map(b => `${b.innerText.trim()} at ${Math.round(b.getBoundingClientRect().right)}px`).join(', ')");
            var offscreen = await browser.EvalAsync<int>("[...document.querySelectorAll('tbody [class*=\"RequestRows-actions\"]')].filter(a => a.getBoundingClientRect().right > innerWidth).length");
            check.Saw($"row buttons ({theme}): {buttons}; {offscreen} past the window's edge at {OwnersWidth}px");
            offscreen.Should().Be(0, "each row's actions show at the owner's width");

            await PressToolbarAsync(browser, "Select Requests");
            await browser.WaitForAsync("!!document.querySelector('thead input[type=checkbox]')");
            var rows = await browser.EvalAsync<int>("document.querySelectorAll('tbody tr').length");
            var boxes = await browser.EvalAsync<int>("document.querySelectorAll('tbody tr input[type=checkbox]').length");
            check.Saw($"Select Requests ({theme}): {boxes} boxes on {rows} rows, and one in the header");
            boxes.Should().Be(rows, "every row has its box in select mode");

            await browser.RunAsync($"[...document.querySelectorAll('tbody tr')].slice(0, {Picks}).forEach(r => r.querySelector('input[type=checkbox]').click())");
            await browser.WaitForAsync($"{Bar}?.innerText.includes('{Picks} request(s) selected')");

            // Only what every one of the three offers is shown.
            var ids = await browser.EvalAsync<int[]>($"[...document.querySelectorAll('tbody tr')].slice(0, {Picks}).map(r => Number(r.querySelector('a[href*=\"/requests/\"]').getAttribute('href').split('/').pop()))");
            var expected = await PortalAsync<string[]>(browser, "requests", $"r => (picked => Object.entries({BulkLabels}).filter(([a]) => picked.every(q => q.actions.includes(a))).map(([, label]) => label))(r.filter(q => {JsonSerializer.Serialize(ids)}.includes(q.id)))");
            var shown = await browser.EvalAsync<string[]>($"{BarButtons}.map(b => b.innerText.trim())");
            check.Saw($"{Picks} picked ({theme}): the bar says \"{await BarTextAsync(browser)}\"; all three offer: {string.Join(", ", expected)}");
            shown.Should().Equal(expected, "the bar shows only what every pick offers");
            (await browser.EvalAsync<double>($"{Bar}.getBoundingClientRect().top - document.querySelector('table').getBoundingClientRect().top")).Should().BeGreaterThan(0, "the bar sits along the bottom, as Sonarr's does");
            await check.ShotAsync(browser, $"picked-{theme}");

            await PressToolbarAsync(browser, "Select All");
            var matching = await PortalAsync<int>(browser, "requests", "r => r.length");
            await browser.WaitForAsync($"{Bar}?.innerText.includes('{matching} request(s) selected')");
            check.Saw($"Select All ({theme}): \"{await BarTextAsync(browser)}\" of {matching} requests, across the pages");
            await check.ShotAsync(browser, $"all-selected-{theme}");

            await PressToolbarAsync(browser, "Unselect All");
            await browser.WaitForAsync($"{Bar}?.innerText.includes('0 request(s) selected')");
            var ticked = await browser.EvalAsync<int>("document.querySelectorAll('tbody input[type=checkbox]:checked').length");
            check.Saw($"Unselect All ({theme}): the bar says \"{await BarTextAsync(browser)}\", {ticked} boxes ticked");
            ticked.Should().Be(0);

            await PressToolbarAsync(browser, "Stop Selecting");
            await browser.WaitForAsync($"!{Bar} && !document.querySelector('thead input[type=checkbox]')");
        }

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
            await browser.RunAsync($"[...{Row(ids[0])}.querySelectorAll('button')].find(b => b.innerText.trim() === 'Approve').click()");
            var said = await OutcomeAsync(browser);
            var status = await PortalAsync<string>(browser, $"requests/{ids[0]}", "d => d.request.status");
            check.Saw($"Approve, the row's own button: \"{said}\"; the request is now {status}");
            status.Should().NotBe("pending", "the row's Approve acted on it");
            await ShotBothAsync(browser, check, "approved");
        }

        private static async Task DeleteRecordsFromBarAsync(HeadlessBrowser browser, WebCheck check)
        {
            var (found, isMarked) = await RowsOfferingAsync(browser, "history", "deleteRecord");
            var ids = found.Take(Picks).ToArray();

            if (ids.Length < Picks)
            {
                check.Saw($"the bar's Delete record: fewer than {Picks} history rows, so nothing to pick");
                return;
            }

            await OpenRequestsTableAsync(browser);
            await PressToolbarAsync(browser, "Select Requests");
            await browser.WaitForAsync("!!document.querySelector('tbody tr input[type=checkbox]')");

            foreach (var id in ids)
            {
                await browser.RunAsync($"{Row(id)}.querySelector('input[type=checkbox]').click()");
            }

            await browser.WaitForAsync($"{Bar}?.innerText.includes('{Picks} request(s) selected')");
            await browser.RunAsync($"{BarButtons}.find(b => b.innerText.trim() === 'Delete record').click()");
            await browser.WaitForAsync($"document.querySelectorAll('[class*=\"BulkActionModal-titles\"] li').length === {Picks}");
            var listed = await browser.EvalAsync<string>("[...document.querySelectorAll('[class*=\"BulkActionModal-titles\"] li')].map(l => l.innerText).join(', ')");
            check.Saw($"{Picks} history rows picked; the bar's Delete record asks first, listing: {listed}");
            await ShotBothAsync(browser, check, "bulk-confirm");
            await AnswerAsync(browser, "Delete record", isMarked);

            if (isMarked)
            {
                var said = await MessageAsync(browser);
                var left = await PortalAsync<int>(browser, "requests", $"r => r.filter(q => {JsonSerializer.Serialize(ids)}.includes(q.id)).length");
                check.Saw($"the bar's Delete record: \"{said}\"; {left} of the {Picks} left");
                left.Should().Be(0, "every record picked was deleted");
                await ShotBothAsync(browser, check, "bulk-done");
            }
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
            await browser.RunAsync($"[...{Row(ids[0])}.querySelectorAll('button')].find(b => b.innerText.trim() === 'Delete record').click()");
            await browser.WaitForAsync($"{ModalButton}.some(b => b.innerText.trim() === 'Delete record')");
            var asked = await browser.EvalAsync<string>("document.querySelector('[class*=\"ModalBody\"]').innerText.trim()");
            var title = await PortalAsync<string>(browser, "requests", $"r => r.find(q => q.id === {ids[0]}).title");
            check.Saw($"a history row's own Delete record button asks first: \"{asked}\"");
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
            // A title without an open removal, so the check never touches one it didn't start.
            var open = await PortalAsync<int[]>(browser, "title-removals", "r => r.map(x => x.removal.id)");
            var openTitles = await PortalAsync<string[]>(browser, "title-removals", "r => r.map(x => x.title)");
            var found = await PortalAsync<string[]>(browser, "library?term=", $"t => (x => x ? [x.title, x.label] : ['', ''])(t.find(x => !{JsonSerializer.Serialize(openTitles)}.includes(x.label)))");
            var (title, label) = (found[0], found[1]);

            if (title.Length == 0)
            {
                check.Saw("search: every library title has a request here, so there's nothing unrequested to remove");
                return;
            }

            const string Results = "[...document.querySelectorAll('[class*=\"PortalSearch-resultRow\"]')]";
            var ours = $"{Results}.find(r => r.textContent.includes({JsonSerializer.Serialize(label)}))";
            await browser.GoAsync(Url + "/system/status");
            await browser.WaitForAsync("!!document.querySelector('[class*=\"PortalSearch-input\"]')");
            await browser.RunAsync($"const e = document.querySelector('[class*=\"PortalSearch-input\"]'); e.focus(); Object.getOwnPropertyDescriptor(HTMLInputElement.prototype, 'value').set.call(e, {JsonSerializer.Serialize(title)}); e.dispatchEvent(new Event('input', {{ bubbles: true }}))");
            await browser.WaitForAsync($"document.querySelector('[class*=\"PortalSearch-results\"]')?.textContent.includes('In your library, not requested') && !!{ours}");
            check.Saw($"search for \"{title}\": {await browser.EvalAsync<string>("document.querySelector('[class*=\"PortalSearch-results\"]').innerText.replace(/\\s+/g, ' ')")}");
            await ShotBothAsync(browser, check, "search-library");
            await browser.RunAsync($"[...{ours}.querySelectorAll('[class*=\"PortalSearch-removal\"]')].find(l => l.innerText.trim() === 'Start removal').click()");
            await browser.WaitForAsync("[...document.querySelectorAll('[class*=\"ModalBody\"] button')].some(b => b.innerText.trim() === 'Start removal')");
            await ShotBothAsync(browser, check, "search-removal-ask");

            if (!ActsOnLibrary)
            {
                check.Saw($"Start removal on {label} opens its dialog; not started, since this run doesn't change the library");
                return;
            }

            var started = $"r => r.find(x => x.title === {JsonSerializer.Serialize(label)} && !{JsonSerializer.Serialize(open)}.includes(x.removal.id))?.removal.id ?? 0";
            var removal = 0;

            try
            {
                await browser.RunAsync("[...document.querySelectorAll('[class*=\"ModalBody\"] button')].find(b => b.innerText.trim() === 'Start removal').click()");
                var said = await OutcomeAsync(browser);
                var worked = await browser.EvalAsync<bool>("!!document.querySelector('[class*=\"ModalBody\"] [class*=\"Alert-success\"]')");
                removal = await PortalAsync<int>(browser, "title-removals", started);
                check.Saw($"Start removal on {label} from the search: \"{said}\" (done: {worked}); the removal it started: {removal}");
                await ShotBothAsync(browser, check, "search-removal");
                worked.Should().BeTrue("the search's Start removal started one");
                removal.Should().NotBe(0, "the removal it started is open on that title");
            }
            finally
            {
                // Dropped again, whatever happened; the run that asked for it removes the record afterwards.
                removal = removal != 0 ? removal : await PortalAsync<int>(browser, "title-removals", started);

                if (removal != 0)
                {
                    await browser.EvalAsync<bool>($"fetch(window.Requestarr.urlBase + '/api/v1/portal/removals/{removal}/drop', {{ method: 'POST', headers: {{ 'X-Api-Key': window.Requestarr.apiKey, 'Content-Type': 'application/json' }}, body: '{{}}' }}).then(r => r.ok)");
                    check.Saw($"removal {removal} dropped again");
                }
            }
        }
    }
}
