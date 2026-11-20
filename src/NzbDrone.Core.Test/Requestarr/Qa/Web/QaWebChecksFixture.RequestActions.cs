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

        // The group's actions for requests, by their name in the portal's answer, in the toolbar's order.
        private const string GroupLabels = "{ approve: 'Approve', deny: 'Deny', remove: 'Start removal' }";

        // The toolbar's actions on what's picked: its buttons left of the page's first own button (Refresh).
        private const string ToolbarActions = "(b => b.slice(0, b.findIndex(x => x.innerText.trim() === 'Refresh')))([...document.querySelectorAll('[class*=\"PageToolbar\"] button')])";

        // Starting a removal from the search changes the dev server (a vote): only when the run asks, and cleans up.
        private static bool ActsOnLibrary => Environment.GetEnvironmentVariable("QA_CHECKS_ACT_ON_LIBRARY") == "1";

        // Requests' actions and picking, at the owner's window width, in both themes. Rows have no buttons of their own.
        // Select Requests shows Sonarr's select mode with the actions on what's picked in the toolbar, left of Refresh.
        // Three rows picked leave live what any of them allows, Select All picks every request and Unselect All empties
        // it. One request picked adds everything its page offers. Marked rows are approved and removed (a vote starts) and read back,
        // while real rows only get the confirmations, cancelled. A library title nobody requested gets Start removal from the
        // header search.
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

            await OneRequestAsync(browser, check);
            await ApproveOneAsync(browser, check);
            await RemoveHistoryAsync(browser, check);
            await RemoveOneHistoryAsync(browser, check);
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
            await browser.WaitForAsync("!!document.querySelector('[class*=\"RequestIndex\"], [class*=\"IndexTable-row\"], [class*=\"Alert-info\"]')");
            await browser.RunAsync("[...document.querySelectorAll('button')].find(b => b.innerText.trim() === 'View').click()");
            await browser.RunAsync("[...document.querySelectorAll('button, a, div[role=\"button\"]')].find(e => e.innerText.trim() === 'Table' && e.closest('[class*=\"MenuContent\"]')).click()");
            await browser.WaitForAsync("!!document.querySelector('[class*=\"IndexTable-row\"]')");
        }

        private static string Row(int id) => $"[...document.querySelectorAll('[class*=\"IndexTable-row\"]')].find(r => r.querySelector('a[href$=\"/requests/{id}\"]'))";

        // The Requests table in select mode with these rows picked.
        private static async Task PickRequestsAsync(HeadlessBrowser browser, int[] ids)
        {
            await OpenRequestsTableAsync(browser);
            await PressToolbarAsync(browser, "Select Requests");
            await browser.WaitForAsync("!!document.querySelector('[class*=\"IndexTable-row\"] input[type=checkbox]')");

            foreach (var id in ids)
            {
                await browser.RunAsync($"{Row(id)}.querySelector('label').click()");
            }

            await browser.WaitForAsync($"document.querySelectorAll('[class*=\"IndexTable-row\"] input[type=checkbox]:checked').length === {ids.Length}");
        }

        // The toolbar's actions on the picks: every one shown, and the live ones (not greyed out).
        private static async Task<(string[] Shown, string[] Live)> ToolbarActionsAsync(HeadlessBrowser browser) =>
            (await browser.EvalAsync<string[]>($"{ToolbarActions}.map(b => b.innerText.trim())"),
             await browser.EvalAsync<string[]>($"{ToolbarActions}.filter(b => !b.disabled).map(b => b.innerText.trim())"));

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

        // The sidebar message that starts with an action's words, such as "Start removal:".
        private static async Task<string> MessageAsync(HeadlessBrowser browser, string start)
        {
            var mine = $"[...document.querySelectorAll('[class*=\"Message-message\"]')].find(m => m.innerText.trim().startsWith({JsonSerializer.Serialize(start)}))";
            await browser.WaitForAsync($"!!{mine}");
            return await browser.EvalAsync<string>($"{mine}.innerText.trim()");
        }

        private static Task<string> BarTextAsync(HeadlessBrowser browser) => browser.EvalAsync<string>($"{Bar}?.innerText.replace(/\\s+/g, ' ').trim() ?? ''");

        // No buttons on rows; Select Requests, three picked (only what all three allow is live, nothing below the
        // list), Select All (every request, across the pages), Unselect All, Stop Selecting.
        private static async Task PickingAsync(HeadlessBrowser browser, WebCheck check, string theme)
        {
            await OpenRequestsTableAsync(browser);
            var rowButtons = await browser.EvalAsync<int>("document.querySelectorAll('[class*=\"IndexTable-row\"] button').length");
            check.Saw($"requests, Table ({theme}, {OwnersWidth}px): {rowButtons} buttons on the rows");
            rowButtons.Should().Be(0, "actions are in the toolbar, not on rows");

            await PressToolbarAsync(browser, "Select Requests");
            await browser.WaitForAsync("!!document.querySelector('[class*=\"VirtualTableHeader-header\"] input[type=checkbox]')");
            var rows = await browser.EvalAsync<int>("document.querySelectorAll('[class*=\"IndexTable-row\"]').length");
            var boxes = await browser.EvalAsync<int>("document.querySelectorAll('[class*=\"IndexTable-row\"] input[type=checkbox]').length");
            var (none, live) = await ToolbarActionsAsync(browser);
            check.Saw($"Select Requests ({theme}): {boxes} boxes on {rows} rows; toolbar: {string.Join(", ", none)}; live with nothing picked: {live.Length}");
            boxes.Should().Be(rows, "every row has its box in select mode");
            live.Should().BeEmpty("nothing picked, so every action is greyed out");

            await browser.RunAsync($"[...document.querySelectorAll('[class*=\"IndexTable-row\"]')].slice(0, {Picks}).forEach(r => r.querySelector('label').click())");
            await browser.WaitForAsync($"document.querySelectorAll('[class*=\"IndexTable-row\"] input[type=checkbox]:checked').length === {Picks}");

            // Only what every one of the three offers is live.
            var ids = await browser.EvalAsync<int[]>($"[...document.querySelectorAll('[class*=\"IndexTable-row\"]')].slice(0, {Picks}).map(r => Number(r.querySelector('a[href*=\"/requests/\"]').getAttribute('href').split('/').pop()))");
            var expected = await PortalAsync<string[]>(browser, "requests", $"r => (picked => Object.entries({GroupLabels}).filter(([a]) => picked.some(q => q.actions.includes(a))).map(([, label]) => label))(r.filter(q => {JsonSerializer.Serialize(ids)}.includes(q.id)))");
            var (shown, allowed) = await ToolbarActionsAsync(browser);
            check.Saw($"{Picks} picked ({theme}): toolbar {string.Join(", ", shown)}; live: {string.Join(", ", allowed)}; any of the three offers: {string.Join(", ", expected)}");
            shown.Should().Equal("Approve", "Deny", "Start removal");
            allowed.Should().Equal(expected, "an action is live when any pick offers it");
            (await browser.EvalAsync<bool>($"!{Bar}")).Should().BeTrue("there's no bar along the bottom");
            await check.ShotAsync(browser, $"picked-{theme}");

            await PressToolbarAsync(browser, "Select All");
            await browser.WaitForAsync($"[...document.querySelectorAll('[class*=\"PageToolbar\"] button')].some(b => b.innerText.trim() === 'Unselect All') && document.querySelectorAll('[class*=\"IndexTable-row\"] input[type=checkbox]:not(:checked)').length === 0");
            check.Saw($"Select All ({theme}): every row ticked, and the button now says Unselect All (every request picked, across the pages)");
            await check.ShotAsync(browser, $"all-selected-{theme}");

            await PressToolbarAsync(browser, "Unselect All");
            await browser.WaitForAsync("document.querySelectorAll('[class*=\"IndexTable-row\"] input[type=checkbox]:checked').length === 0");
            check.Saw($"Unselect All ({theme}): no boxes ticked");

            await PressToolbarAsync(browser, "Stop Selecting");
            await browser.WaitForAsync("!document.querySelector('[class*=\"VirtualTableHeader-header\"] input[type=checkbox]')");
            var left = await browser.EvalAsync<int>($"{ToolbarActions}.length");
            check.Saw($"Stop Selecting ({theme}): {left} actions left in the toolbar");
            left.Should().Be(0);
        }

        // One request picked: the toolbar has the group's actions and everything else its page offers, and exactly its
        // page's actions are live.
        private static async Task OneRequestAsync(HeadlessBrowser browser, WebCheck check)
        {
            var id = await PortalAsync<int>(browser, "requests", "r => r.find(q => q.actions.length)?.id ?? 0");
            var offers = await PortalAsync<int>(browser, "requests", $"r => r.find(q => q.id === {id}).actions.length");
            await PickRequestsAsync(browser, new[] { id });
            var (shown, live) = await ToolbarActionsAsync(browser);
            check.Saw($"request {id} picked alone: toolbar {string.Join(", ", shown)}; live: {string.Join(", ", live)}; its page offers {offers}");
            live.Should().HaveCount(offers, "one pick has exactly what its page offers");
            await ShotBothAsync(browser, check, "one-picked");
        }

        private static async Task ApproveOneAsync(HeadlessBrowser browser, WebCheck check)
        {
            var (ids, isMarked) = await RowsOfferingAsync(browser, "approve", "approve");

            if (!isMarked)
            {
                check.Saw($"approve: no \"{Marked}approve\" request waiting, so nothing approved (the toolbar is checked above)");
                return;
            }

            await PickRequestsAsync(browser, new[] { ids[0] });
            await PressToolbarAsync(browser, "Approve");
            var said = await OutcomeAsync(browser);
            var status = await PortalAsync<string>(browser, $"requests/{ids[0]}", "d => d.request.status");
            check.Saw($"Approve in the toolbar, one picked: \"{said}\"; the request is now {status}");
            status.Should().NotBe("pending", "Approve acted on it");
            await ShotBothAsync(browser, check, "approved");
        }

        // History rows (not in the library), the marked ones when a run seeded them, otherwise any.
        private static async Task<(int[] Ids, bool IsMarked)> HistoryRowsAsync(HeadlessBrowser browser, string name)
        {
            var history = "r.filter(q => q.status === 'notInLibrary' && q.actions.includes('remove'))";
            var marked = await PortalAsync<int[]>(browser, "requests", $"r => {history}.filter(q => q.title === '{Marked}{name}').map(q => q.id)");

            return marked.Length > 0 ? (marked, true) : (await PortalAsync<int[]>(browser, "requests", $"r => {history}.map(q => q.id)"), false);
        }

        // The removals the check started, dropped again, so no vote it opened stays open on the dev server.
        private static async Task DropRemovalsAsync(HeadlessBrowser browser, WebCheck check, int[] ids)
        {
            var removals = await PortalAsync<int[]>(browser, "requests", $"r => r.filter(q => {JsonSerializer.Serialize(ids)}.includes(q.id) && q.removalId !== null).map(q => q.removalId)");

            foreach (var removal in removals)
            {
                await browser.EvalAsync<int>($"fetch(window.Requestarr.urlBase + '/api/v1/portal/removals/{removal}/drop', {{ method: 'POST', headers: {{ 'X-Api-Key': window.Requestarr.apiKey, 'Content-Type': 'application/json' }}, body: '{{}}' }}).then(r => r.status)");
            }

            check.Saw($"the {removals.Length} removals the check started, dropped again");
        }

        // Three history rows picked: the toolbar's Start removal asks first with their titles and says nothing is deleted.
        // Marked rows then get their removal votes (read back, then dropped); real rows only get the question, cancelled.
        private static async Task RemoveHistoryAsync(HeadlessBrowser browser, WebCheck check)
        {
            var (found, isMarked) = await HistoryRowsAsync(browser, "history");
            var ids = found.Take(Picks).ToArray();

            if (ids.Length < Picks)
            {
                check.Saw($"Start removal on history rows: fewer than {Picks} history rows");
                return;
            }

            await PickRequestsAsync(browser, ids);
            await PressToolbarAsync(browser, "Start removal");
            await browser.WaitForAsync($"document.querySelectorAll('[class*=\"BulkActionModal-titles\"] li').length === {Picks}");
            var listed = await browser.EvalAsync<string>("[...document.querySelectorAll('[class*=\"BulkActionModal-titles\"] li')].map(l => l.innerText).join(', ')");
            var note = await browser.EvalAsync<string>("document.querySelector('[class*=\"ModalBody\"] [class*=\"Alert-\"]')?.innerText.trim() ?? ''");
            check.Saw($"{Picks} history rows picked; Start removal asks first, listing: {listed}; note: \"{note}\"");
            note.Should().Contain("nothing is deleted", "Sonarr and Radarr don't have these titles");
            await ShotBothAsync(browser, check, "bulk-confirm");
            await AnswerAsync(browser, "Start removal", isMarked);

            if (!isMarked)
            {
                return;
            }

            var said = await MessageAsync(browser, "Start removal:");
            var open = await PortalAsync<int>(browser, "requests", $"r => r.filter(q => {JsonSerializer.Serialize(ids)}.includes(q.id) && q.removalId !== null).length");
            check.Saw($"the toolbar's Start removal: \"{said}\"; {open} of the {Picks} have a removal vote");
            open.Should().Be(Picks, "each history row follows the removal flow");
            await ShotBothAsync(browser, check, "bulk-done");
            await DropRemovalsAsync(browser, check, ids);
        }

        // One history row picked: Start removal opens the removal dialog for its title with no seasons to pick. A marked
        // row gets its vote (read back, then dropped); a real row only gets the dialog, closed.
        private static async Task RemoveOneHistoryAsync(HeadlessBrowser browser, WebCheck check)
        {
            var (ids, isMarked) = await HistoryRowsAsync(browser, "delete one");

            if (ids.Length == 0)
            {
                check.Saw("Start removal on one history row: no history row");
                return;
            }

            await PickRequestsAsync(browser, new[] { ids[0] });
            var (_, live) = await ToolbarActionsAsync(browser);
            check.Saw($"one history row picked: live {string.Join(", ", live)}");
            live.Should().Contain("Start removal").And.NotContain("Delete record");
            await PressToolbarAsync(browser, "Start removal");
            await browser.WaitForAsync("[...document.querySelectorAll('[class*=\"ModalBody\"] button')].some(b => b.innerText.trim() === 'Start removal')");
            var asked = await browser.EvalAsync<string>("document.querySelector('[class*=\"ModalBody\"]').innerText.replace(/\\s+/g, ' ').trim()");
            var seasons = await browser.EvalAsync<bool>("!!document.querySelector('[class*=\"ModalBody\"] input[name=seasons]')");
            check.Saw($"its Start removal dialog: \"{asked}\"; a seasons field: {seasons}");
            seasons.Should().BeFalse("the whole request goes; there are no seasons to pick");
            await ShotBothAsync(browser, check, "remove-one");

            if (!isMarked)
            {
                await browser.RunAsync("document.querySelector('[class*=\"ModalContent-closeButton\"]').click()");
                return;
            }

            await browser.RunAsync("[...document.querySelectorAll('[class*=\"ModalBody\"] button')].find(b => b.innerText.trim() === 'Start removal').click()");
            await browser.WaitForAsync($"!!{Outcome}");
            var open = await PortalAsync<bool>(browser, "requests", $"r => r.some(q => q.id === {ids[0]} && q.removalId !== null)");
            check.Saw($"then: \"{await OutcomeAsync(browser)}\"; a removal vote is open: {open}");
            open.Should().BeTrue("the history row follows the removal flow");
            await DropRemovalsAsync(browser, check, new[] { ids[0] });
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
