using System.Text.Json;
using System.Threading.Tasks;
using FluentAssertions;
using NUnit.Framework;

namespace Requestarr.Core.Test.Qa.Web
{
    public partial class QaWebChecksFixture
    {
        private const string IndexRows = "[...document.querySelectorAll('[class*=\"IndexTable-row\"]')]";
        private const string IndexBody = "document.querySelector('[class*=\"RequestsPage-contentBody\"]')";

        // Long lists, in both themes, changing nothing. The Requests list is one scroll, as Sonarr's series list: it
        // draws only the rows on screen and more as it scrolls, with no pager and no page size under Options. The Movies
        // filter narrows it (the footer counts only movies), and Select All then picks every match, ticked at the end of
        // the scroll too. The page of the member with most requests has each section's total in its heading, a page of
        // their requests with a pager, and no Requester column there.
        [Test]
        public Task paging() => CheckAsync(nameof(paging), PagingAsync, OwnersWidth);

        private static async Task PagingAsync(HeadlessBrowser browser, WebCheck check)
        {
            await SignInAsync(browser);

            foreach (var theme in Themes)
            {
                await browser.SetThemeAsync(theme);
                await RequestsScrollAsync(browser, check, theme);
                await MemberPagedAsync(browser, check, theme);
            }
        }

        private static Task<int> RowCountAsync(HeadlessBrowser browser, string within = "document") =>
            browser.EvalAsync<int>($"{within}.querySelectorAll('tbody tr').length");

        // Scrolls a list's page body (Requests or Members) until the row is drawn, as the list draws only the rows on
        // screen, then brings it to the middle.
        private static async Task ScrollToRowAsync(HeadlessBrowser browser, string row)
        {
            var found = await browser.EvalAsync<bool>(
                $"(async () => {{ const body = {IndexBody}; for (let top = 0; top <= body.scrollHeight; top += body.clientHeight / 2) {{ body.scrollTo(0, top); " +
                $"await new Promise(r => setTimeout(r, 150)); if ({row}) {{ {row}.scrollIntoView({{ block: 'center' }}); await new Promise(r => setTimeout(r, 150)); return true; }} }} return false; }})()");
            found.Should().BeTrue("the row is in the list");
        }

        // Scrolls the Requests list to its end and waits for its last rows to be drawn.
        private static async Task ScrollToEndAsync(HeadlessBrowser browser, int total)
        {
            await browser.RunAsync($"{IndexBody}.scrollTo(0, {IndexBody}.scrollHeight)");
            await browser.WaitForAsync($"{IndexRows}.length > 0 && Math.max(...{IndexRows}.map(r => parseInt(r.style.top))) >= ({total} - 1) * parseInt({IndexRows}[0].style.height)");
        }

        private static async Task RequestsScrollAsync(HeadlessBrowser browser, WebCheck check, string theme)
        {
            await OpenRequestsTableAsync(browser);
            var total = await PortalAsync<int>(browser, "requests", "r => r.length");
            var drawn = await browser.EvalAsync<int>($"{IndexRows}.length");
            var pager = await browser.EvalAsync<bool>("!!document.querySelector('[class*=\"TablePager-pager\"]')");
            check.Saw($"Requests ({theme}): {drawn} rows drawn of {total}, a pager: {pager}");
            pager.Should().BeFalse("the list is one scroll, as Sonarr's");
            drawn.Should().BePositive().And.BeLessOrEqualTo(total);

            await ScrollToEndAsync(browser, total);
            var last = await browser.EvalAsync<string>($"{IndexRows}.sort((a, b) => parseInt(b.style.top) - parseInt(a.style.top))[0].querySelector('a[href*=\"/requests/\"]').innerText");
            check.Saw($"scrolled to the end ({theme}): the last row drawn is {last}");
            await check.ShotAsync(browser, $"requests-end-{theme}");
            await browser.RunAsync($"{IndexBody}.scrollTo(0, 0)");

            // Options holds the columns, with no page size, as Sonarr's series table.
            await PressToolbarAsync(browser, "Options");
            await browser.WaitForAsync("!!document.querySelector('[class*=\"ModalBody\"]')");
            var pageSize = await browser.EvalAsync<bool>("!!document.querySelector('input[name=pageSize]')");
            check.Saw($"Options ({theme}): a page size: {pageSize}");
            pageSize.Should().BeFalse();
            await AnswerAsync(browser, "Close", true);

            // The Movies filter narrows the list, and Select All then picks every match.
            var movies = await PortalAsync<int>(browser, "requests", "r => r.filter(q => q.contentType === 'movie').length");
            await ChooseFilterAsync(browser, "Movies");
            const string Footer = "document.querySelector('[class*=\"IndexFooter-footer\"]')";
            await browser.WaitForAsync($"{Footer}?.innerText.replace(/\\s+/g, ' ').includes('Requests {movies} Movies {movies} TV 0')");

            // The legend is the footer's first block, one entry per colour.
            var legend = await browser.EvalAsync<int>($"{Footer}.firstElementChild.children.length");
            var totals = await browser.EvalAsync<string>($"{Footer}.querySelector('[class*=\"IndexFooter-statistics\"]').innerText.replace(/\\s+/g, ' ')");
            check.Saw($"the Movies filter and the footer ({theme}): {legend} colours in the legend; totals: {totals}");
            legend.Should().Be(6, "one colour for each kind of status label");
            totals.Should().Contain($"Requests {movies} Movies {movies} TV 0", "the totals count what the filter shows");

            await PressToolbarAsync(browser, "Select Requests");
            await PressToolbarAsync(browser, "Select All");
            await browser.WaitForAsync($"{IndexRows}.some(r => r.querySelector('input[type=checkbox]')) && {IndexRows}.every(r => r.querySelector('input[type=checkbox]').checked)");

            // Every match is picked, so the rows drawn at the end of the scroll come ticked too.
            await ScrollToEndAsync(browser, movies);
            var unticked = await browser.EvalAsync<int>($"{IndexRows}.filter(r => !r.querySelector('input[type=checkbox]').checked).length");
            check.Saw($"Select All with the filter ({theme}): {unticked} rows unticked at the end of the scroll");
            unticked.Should().Be(0, "Select All picks every match, not only the rows drawn");
            await check.ShotAsync(browser, $"requests-filtered-{theme}");
            await PressToolbarAsync(browser, "Stop Selecting");
            await ChooseFilterAsync(browser, "All");
        }

        // A choice in the toolbar's Filter menu, by its words.
        private static async Task ChooseFilterAsync(HeadlessBrowser browser, string label)
        {
            await PressToolbarAsync(browser, "Filter");
            await browser.WaitForAsync("!!document.querySelector('[class*=\"MenuContent\"]')");
            await browser.RunAsync($"[...document.querySelectorAll('[class*=\"MenuContent\"] [class*=\"MenuItem-menuItem\"]')].find(e => e.innerText.trim() === {JsonSerializer.Serialize(label)}).click()");
        }

        // The member with most requests, by their ID and name.
        private static async Task<(int Id, string Name)> TopMemberAsync(HeadlessBrowser browser)
        {
            var top = await PortalAsync<string[]>(browser, "members", "m => (x => [String(x.id), x.name])([...m].sort((a, b) => b.requests - a.requests)[0])");

            return (int.Parse(top[0]), top[1]);
        }

        private static async Task MemberPagedAsync(HeadlessBrowser browser, WebCheck check, string theme)
        {
            var (member, name) = await TopMemberAsync(browser);
            await browser.GoAsync($"{Url}/members/{member}");
            await browser.WaitForAsync("[...document.querySelectorAll('legend')].some(l => /\\(\\d+\\)$/.test(l.innerText.trim()))");
            await browser.WaitForAsync("document.querySelectorAll('fieldset tbody tr').length > 0");
            var legends = await browser.EvalAsync<string>("[...document.querySelectorAll('legend')].map(l => l.innerText.trim()).join(' | ')");
            const string Theirs = "[...document.querySelectorAll('fieldset')].find(f => f.querySelector('legend').innerText.startsWith('Their requests'))";
            var rows = await RowCountAsync(browser, Theirs);
            var requester = await browser.EvalAsync<bool>($"[...{Theirs}.querySelectorAll('th')].some(h => h.innerText.trim() === 'Requester')");
            var paged = await browser.EvalAsync<bool>($"!!{Theirs}.querySelector('[class*=\"PortalPager-pager\"]')");
            check.Saw($"{name}'s page ({theme}): {legends}; their requests: {rows} rows drawn, a pager: {paged}, a Requester column: {requester}");
            legends.Should().MatchRegex(@"Their requests \(\d+\)");
            requester.Should().BeFalse("every row would name them");
            await check.ShotAsync(browser, $"member-{theme}");
        }
    }
}
