using System;
using System.Text.Json;
using System.Threading.Tasks;
using FluentAssertions;
using NUnit.Framework;

namespace Requestarr.Core.Test.Qa.Web
{
    public partial class QaWebChecksFixture
    {
        private const string Pager = "document.querySelector('[class*=\"PortalPager-pager\"]')";

        // The pager's Next: Servarr's pager draws first, previous, the page number, next and last, in that order.
        private const string NextPage = "document.querySelector('[class*=\"TablePager-controlsContainer\"]').firstElementChild.children[3]";

        // Long lists page through, in both themes, changing nothing. The Requests list shows a page of its requests with the
        // pager and goes to the next page; the Member filter narrows all of them (the count says how many of all), and the
        // header's box then offers every match across the pages. The page of the member with most requests has each
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
            var size = await browser.EvalAsync<int>($"Number({Pager}?.querySelector('select[name=pageSize]')?.value ?? {total})");
            var first = await browser.EvalAsync<string>("document.querySelector('tbody tr a[href*=\"/requests/\"]').innerText");
            check.Saw($"Requests ({theme}): {rows} rows drawn of {total}, {size} a page; first: {first}");
            rows.Should().Be(Math.Min(total, size), "only the page is drawn");

            if (total > size)
            {
                await browser.RunAsync($"{NextPage}.click()");
                await browser.WaitForAsync($"document.querySelector('tbody tr a[href*=\"/requests/\"]').innerText !== {JsonSerializer.Serialize(first)}");
                check.Saw($"next page ({theme}): first now {await browser.EvalAsync<string>("document.querySelector('tbody tr a[href*=\"/requests/\"]').innerText")}");
                await check.ShotAsync(browser, $"requests-page-2-{theme}");
            }

            var (member, name) = await TopMemberAsync(browser);
            var theirs = await PortalAsync<int>(browser, "requests", $"r => r.filter(q => q.requester.id === {member} || q.followers.some(f => f.id === {member})).length");
            await browser.RunAsync($"const s = document.querySelector('select[name=member]'); s.value = '{member}'; s.dispatchEvent(new Event('change', {{ bubbles: true }}))");
            await browser.WaitForAsync($"{Bar}?.innerText.includes('Showing {theirs} of {total}')");
            check.Saw($"Member filter set to {name} ({theme}): \"{await BarTextAsync(browser)}\", {await RowCountAsync(browser)} rows drawn");

            await browser.RunAsync("document.querySelector('thead input[type=checkbox]').click()");
            await browser.WaitForAsync($"{Bar}?.innerText.includes('selected')");
            var offer = await BarTextAsync(browser);
            check.Saw($"the page picked ({theme}): \"{offer}\"");

            if (theirs > size)
            {
                offer.Should().Contain($"Select all {theirs} matching");
                await browser.RunAsync($"[...{Bar}.querySelectorAll('a, button')].find(e => e.innerText.startsWith('Select all')).click()");
                await browser.WaitForAsync($"{Bar}?.innerText.includes('All {theirs} matching are selected')");
                check.Saw($"every match picked across the pages ({theme}): \"{await BarTextAsync(browser)}\"");
            }

            await check.ShotAsync(browser, $"requests-filtered-{theme}");
            await browser.RunAsync($"[...{Bar}.querySelectorAll('a, button')].find(e => e.innerText.trim() === 'Clear selection').click()");
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
