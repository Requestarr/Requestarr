using System;
using System.Text.Json;
using System.Threading.Tasks;
using FluentAssertions;
using NUnit.Framework;

namespace Requestarr.Core.Test.Qa.Web
{
    public partial class QaWebChecksFixture
    {
        private const string Pager = "document.querySelector('[class*=\"TablePager-pager\"]')";

        // A fresh browser's page size, until Options changes it.
        private const int PageSize = 25;

        // The pager's Next: Servarr's pager draws first, previous, the page number, next and last, in that order.
        private const string NextPage = "document.querySelector('[class*=\"TablePager-controlsContainer\"]').firstElementChild.children[3]";

        // Long lists page through, in both themes, changing nothing. The Requests list shows a page of its requests with
        // Servarr's pager and goes to the next page; Options holds the page size; the Movies filter narrows all of them (the
        // pager's total says how many), and Select All then picks every match across the pages. The page of the member with most requests has each
        // section's total in its heading, a page of their requests with the pager, and no Requester column there.
        [Test]
        public Task paging() => CheckAsync(nameof(paging), PagingAsync, OwnersWidth);

        private static async Task PagingAsync(HeadlessBrowser browser, WebCheck check)
        {
            await SignInAsync(browser);

            foreach (var theme in Themes)
            {
                await browser.SetThemeAsync(theme);
                await RequestsPagedAsync(browser, check, theme);
                await MemberPagedAsync(browser, check, theme);
            }
        }

        private static Task<int> RowCountAsync(HeadlessBrowser browser, string within = "document") =>
            browser.EvalAsync<int>($"{within}.querySelectorAll('tbody tr').length");

        private static async Task RequestsPagedAsync(HeadlessBrowser browser, WebCheck check, string theme)
        {
            await OpenRequestsTableAsync(browser);
            var total = await PortalAsync<int>(browser, "requests", "r => r.length");
            var rows = await RowCountAsync(browser);
            var first = await browser.EvalAsync<string>("document.querySelector('tbody tr a[href*=\"/requests/\"]').innerText");
            check.Saw($"Requests ({theme}): {rows} rows drawn of {total}, {PageSize} a page; first: {first}");
            rows.Should().Be(Math.Min(total, PageSize), "only the page is drawn");

            if (total > PageSize)
            {
                await browser.RunAsync($"{NextPage}.click()");
                await browser.WaitForAsync($"document.querySelector('tbody tr a[href*=\"/requests/\"]').innerText !== {JsonSerializer.Serialize(first)}");
                check.Saw($"next page ({theme}): first now {await browser.EvalAsync<string>("document.querySelector('tbody tr a[href*=\"/requests/\"]').innerText")}");
                await check.ShotAsync(browser, $"requests-page-2-{theme}");
            }

            // The page size is under Options, as Servarr's tables have it.
            await PressToolbarAsync(browser, "Options");
            await browser.WaitForAsync("!!document.querySelector('input[name=pageSize]')");
            check.Saw($"Options ({theme}): page size {await browser.EvalAsync<string>("document.querySelector('input[name=pageSize]').value")}");
            await AnswerAsync(browser, "Close", true);

            // The Movies filter narrows every page, and Select All then picks every match across them.
            var movies = await PortalAsync<int>(browser, "requests", "r => r.filter(q => q.contentType === 'movie').length");
            await ChooseFilterAsync(browser, "Movies");
            await browser.WaitForAsync($"{Pager}?.innerText.includes('Total records: {movies}')");
            check.Saw($"the Movies filter ({theme}): {await browser.EvalAsync<string>($"{Pager}.innerText.replace(/\\s+/g, ' ')")}; {await RowCountAsync(browser)} rows drawn");
            const string Footer = "document.querySelector('[class*=\"IndexFooter-footer\"]')";
            // The legend is the footer's first block, one entry per colour.
            var legend = await browser.EvalAsync<int>($"{Footer}.firstElementChild.children.length");
            var totals = await browser.EvalAsync<string>($"{Footer}.querySelector('[class*=\"IndexFooter-statistics\"]').innerText.replace(/\\s+/g, ' ')");
            check.Saw($"the footer ({theme}): {legend} colours in the legend; totals: {totals}");
            legend.Should().Be(6, "one colour for each kind of status label");
            totals.Should().Contain($"Requests {movies} Movies {movies} TV 0", "the totals count what the filter shows");

            await PressToolbarAsync(browser, "Select Requests");
            await PressToolbarAsync(browser, "Select All");
            await browser.WaitForAsync($"{Bar}?.innerText.includes('{movies} request(s) selected')");
            check.Saw($"Select All with the filter ({theme}): \"{await BarTextAsync(browser)}\"");
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
