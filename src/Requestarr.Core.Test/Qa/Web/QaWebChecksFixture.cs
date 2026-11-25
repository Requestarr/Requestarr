using System;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Text.Json;
using System.Threading.Tasks;
using FluentAssertions;
using NUnit.Framework;
using Requestarr.Test.Common.Categories;

namespace Requestarr.Core.Test.Qa.Web
{
    // The automated web app checks for the QA checklist (docker/qa_checks.py run): a real browser against the dev stack
    // at the commit QA receives, signed in with a throwaway login, in the light and dark theme, never the QA server.
    // Each test is one check an item names ("the web app status page check") and writes what it saw and its screenshots
    // for the automation to look at against the item's Expect. The dev stack shows the dev logo where QA shows
    // QA's; the checks put QA's own logo files in the page where QA shows them (the logo is the only part that differs
    // between environments). Needs QA_CHECKS_URL, QA_CHECKS_USERNAME, QA_CHECKS_PASSWORD, QA_CHECKS_DIR and
    // QA_CHECKS_VERSION (the run sets them).
    [TestFixture]
    [ManualTest]
    public partial class QaWebChecksFixture
    {
        private const string FilterChoices = "[...document.querySelectorAll('[class*=\"MenuContent\"] [class*=\"MenuItem-menuItem\"]')].map(e => e.innerText.trim())";
        private const string HeaderLogo = "img[class*='PageHeader-logo']";
        private const string ProvisionedNote = "Set by your provisioning file";

        // Empty rows the header logo's image may leave above and below the R (antialiasing), in all.
        private const int MaxLogoPadding = 4;

        // A colour whose brightest channel is at most this reads as black.
        private const int NotBlack = 20;

        // As QA: the environment's logo files are QA's (the dev stack's are dev's).
        private const string ShowAsQa = @"(() => {
            document.querySelectorAll('img, link[rel*=icon]').forEach(e => {
                const attribute = e.tagName === 'IMG' ? 'src' : 'href';
                const value = e.getAttribute(attribute);
                if (value && value.includes('/dev/')) { e.setAttribute(attribute, value.replace('/dev/', '/qa/')); }
            });
            return true;
        })()";

        private const string ImagesLoaded = "[...document.images].every(i => i.complete && i.naturalWidth > 0)";

        // QA's tab icons as a browser's tab strip shows them: on a light strip and on a dark one.
        private const string TabIconsPage = @"(() => {
            const base = window.Requestarr.urlBase + '/Content/Images/Icons/';
            const strip = (shade, color) => `<div style=""background:${color};padding:24px;display:flex;gap:24px;align-items:center;font:16px sans-serif;color:${shade === 'dark' ? '#eee' : '#222'}"">
                <img src=""${base}${shade}/qa/favicon-32x32.png"" width=""64"" height=""64""><img src=""${base}${shade}/qa/favicon-16x16.png"" width=""32"" height=""32"">
                QA's tab icon, ${shade} theme</div>`;
            document.body.innerHTML = strip('light', '#f1f3f4') + strip('dark', '#35363a');
        })()";

        // The Connections settings page: its section headings, its connection cards, and an open modal's words.
        private const string Legends = "[...document.querySelectorAll('legend')].map(l => l.innerText.trim())";
        private const string Cards = "[...document.querySelectorAll('[class*=\"ConnectionCard-connection\"]:not([class*=\"ConnectionCard-addConnection\"])')]";
        private const string ModalText = "document.querySelector('[class*=\"ModalContent\"]').innerText.replace(/\\s+/g, ' ')";

        // A list view names its people on one line: the small picture is 20px, so anything taller has wrapped.
        private const int OneLine = 24;
        private const int MaxFollowerPictures = 5;
        private const string CompactPeople = "[...document.querySelectorAll('[class*=\"RequestIndexPoster-title\"], [class*=\"RequestIndexOverviewInfo-infoRow\"], [class*=\"FollowerStack-stack\"], [class*=\"Person-small\"]')]";
        private const string FollowerStack = "document.querySelector('a[class*=\"FollowerStack-stack\"]')";

        private static readonly string[] Themes = { "light", "dark" };

        private static string Url => Environment.GetEnvironmentVariable("QA_CHECKS_URL")?.TrimEnd('/');

        [Test]
        public Task status_page() => CheckAsync(nameof(status_page), async (browser, check) =>
        {
            await SignInAsync(browser);
            string shown = null;

            foreach (var theme in Themes)
            {
                await browser.SetThemeAsync(theme);
                await browser.GoAsync(Url + "/system/status");
                await browser.WaitForAsync("[...document.querySelectorAll('dt')].some(d => d.textContent.trim() === 'Version')");
                shown = await browser.EvalAsync<string>("[...document.querySelectorAll('dt')].find(d => d.textContent.trim() === 'Version').nextElementSibling.textContent.trim()");
                await QaLogoAsync(browser);
                await check.ShotAsync(browser, theme);
            }

            var built = Environment.GetEnvironmentVariable("QA_CHECKS_VERSION");
            var logoTitle = await browser.EvalAsync<string>($"document.querySelector(\"{HeaderLogo}\").title");
            check.Saw($"Status shows version {shown}; the build is {built}; the logo's own label says \"{logoTitle}\".");
            check.Saw("On QA the same build says qa where the dev stack says dev, with the same number.");

            shown.Should().Be(built, "the label the image was built with");
            shown.Should().MatchRegex(@"^\d+\.\d+\.\d{4,}-dev(\.[0-9a-f]{7})?$", "a goal's numbered build, or the next number and the commit between goals");
            logoTitle.Should().Contain(shown);

            var copyright = await browser.EvalAsync<string>("[...document.querySelectorAll('dt')].find(d => d.textContent.trim() === 'Copyright').nextElementSibling.textContent.trim()");
            check.Saw($"About says: {copyright}");
            copyright.Should().MatchRegex(@"^Copyright 2026(-20\d\d)? ").And.Contain("Requestarr contributors").And.NotContain("Runemark")
                .And.EndWith("Portions copyright 2014-2026 prowlarr.com and the Servarr team (GNU General Public v3)");

            // The Movie Database's terms ask every app using its API to credit it.
            var credit = await browser.EvalAsync<string>("[...document.querySelectorAll('dt')].find(d => d.textContent.trim() === 'The Movie Database')?.nextElementSibling.textContent.trim() ?? ''");
            check.Saw($"About credits The Movie Database: {credit}");
            credit.Should().Be("This product uses the TMDB API but is not endorsed or certified by TMDB.");
        });

        // The admin portal's pages: Requests, Members, Queue, Access requests, Outages, the Audit log and System, What's
        // new load in both themes
        // with their tables or their "nothing yet" lines, and a request's and a member's own page open from the lists
        // when there are any, with what an admin can do there.
        [Test]
        public Task portal_pages() => CheckAsync(nameof(portal_pages), async (browser, check) =>
        {
            const string Loaded = "document.querySelector('table, fieldset, [class*=\"Portal-empty\"], [class*=\"Alert-\"], [class*=\"RequestIndex\"], [class*=\"IndexTable-row\"]')";
            const string Failed = "!!document.querySelector('[class*=\"Alert-danger\"], [class*=\"Alert-warning\"]')";
            await SignInAsync(browser);

            foreach (var theme in Themes)
            {
                await browser.SetThemeAsync(theme);

                foreach (var page in new[] { "requests", "members", "queue", "access-requests", "outages", "audit", "system/whats-new" })
                {
                    await browser.GoAsync($"{Url}/{page}");
                    await browser.WaitForAsync(Loaded);
                    (await browser.EvalAsync<bool>(Failed)).Should().BeFalse($"the {page} page loads");
                    var rows = await browser.EvalAsync<int>("document.querySelectorAll('tbody tr, [class*=\"IndexTable-row\"]').length");
                    check.Saw($"{page} ({theme}): {rows} row(s)");
                    await check.ShotAsync(browser, $"{page.Replace('/', '-')}-{theme}");
                    await OpenFirstAsync(browser, check, page, theme);
                }
            }

            await browser.GoAsync($"{Url}/system/whats-new");
            await browser.WaitForAsync("document.body.innerText.includes('0.7.0010')");
            var legend = await browser.EvalAsync<string>("[...document.querySelectorAll('legend')].find(l => l.innerText.startsWith('0.7.0010')).innerText");
            var hasMarks = await browser.EvalAsync<bool>("document.querySelector('[class*=\"PageContentBody\"]').innerText.includes('**')");
            check.Saw($"What's new: \"{legend}\"; asterisks left in its lines: {hasMarks}");
            legend.Should().EndWith("November 22, 2026", "the build's own day, wherever the browser is");
            hasMarks.Should().BeFalse();

            foreach (var page in new[] { "members/add", "queue", "access-requests", "outages", "audit", "settings/changes" })
            {
                await browser.GoAsync($"{Url}/{page}");
                await browser.WaitForAsync("[...document.querySelectorAll('[class*=\"PageToolbar\"] button')].some(b => b.innerText.trim() === 'Refresh')");
                check.Saw($"{page}: a toolbar with {string.Join(", ", await browser.EvalAsync<string[]>("[...new Set([...document.querySelectorAll('[class*=\"PageToolbar\"] button')].map(b => b.innerText.trim()).filter(t => t))]"))}");
            }
        });

        // The Requests page's three views, as Sonarr's and Radarr's lists have them, in both themes: Overview (the
        // default) with posters and what each title is, Posters, and Table with a poster column, each naming its people
        // on one line however many follow a title: the requester small, then at most five follower pictures and a count.
        // A follower stack opens its request's page at the Followers section, which lists them all. No list links to an
        // app; a request's own page does, in its toolbar, and "Open in Sonarr" there is followed to the title's page in
        // that Sonarr. The Filter menu has the states, on a member's page too. A server with no requests says so and passes.
        [Test]
        public Task request_views() => CheckAsync(nameof(request_views), async (browser, check) =>
        {
            const string Shown = "document.querySelector('[class*=\"RequestIndex\"], [class*=\"IndexTable-row\"], [class*=\"Alert-info\"]')";
            await SignInAsync(browser);

            foreach (var theme in Themes)
            {
                await browser.SetThemeAsync(theme);
                await browser.GoAsync($"{Url}/requests");
                await browser.WaitForAsync(Shown);

                if (await browser.EvalAsync<bool>("!!document.querySelector('[class*=\"Alert-info\"]')"))
                {
                    check.Saw($"requests ({theme}): no requests on this server");
                    return;
                }

                foreach (var (view, shows) in new[] { ("Overview", "[class*=\"RequestIndexOverview-content\"]"), ("Posters", "[class*=\"RequestIndexPoster-content\"]"), ("Table", "[class*=\"IndexTable-row\"]") })
                {
                    await browser.RunAsync("[...document.querySelectorAll('button')].find(b => b.innerText.trim() === 'View').click()");
                    await browser.RunAsync($"[...document.querySelectorAll('button, a, div[role=\"button\"]')].find(e => e.innerText.trim() === '{view}' && e.closest('[class*=\"MenuContent\"]')).click()");
                    await browser.WaitForAsync($"!!document.querySelector('{shows}')");
                    await browser.WaitForAsync("[...document.querySelectorAll('[class*=\"PageContentBody\"] img')].filter(i => i.getBoundingClientRect().top < innerHeight).every(i => i.complete)");
                    var posters = await browser.EvalAsync<int>("[...document.querySelectorAll('[class*=\"PageContentBody\"] img')].filter(i => i.complete && i.naturalWidth > 0).length");
                    var opens = await browser.EvalAsync<int>("[...document.querySelectorAll('a')].filter(a => a.innerText.startsWith('Open in ')).length");
                    check.Saw($"requests, {view} ({theme}): {posters} posters shown, {opens} Open in links");
                    opens.Should().Be(0, "the app opens from the request's own page");
                    await check.ShotAsync(browser, $"requests-{view.ToLowerInvariant()}-{theme}");
                    await PeopleOnOneLineAsync(browser, check, $"{view} ({theme})");
                }

                await FollowersSectionAsync(browser, check, theme);
            }

            await PosterOptionsAsync(browser, check);
            await JumpAndReturnAsync(browser, check);
            await FilterStatesAsync(browser, check);
            await OpenInSonarrAsync(browser, check);
        });

        // The Requests page's filters, and the same on the page of the member with the most requests.
        private static async Task FilterStatesAsync(HeadlessBrowser browser, WebCheck check)
        {
            var states = new[] { "All", "Waiting for approval", "Denied", "Approved", "Not available yet", "Downloading", "Stuck", "Available", "Removed", "Not in library", "Movies", "TV" };
            await browser.GoAsync($"{Url}/requests");
            await browser.WaitForAsync($"!!{IndexBody}");
            await PressToolbarAsync(browser, "Filter");
            var onRequests = await browser.EvalAsync<string[]>(FilterChoices);
            var member = await browser.EvalAsync<int>("fetch(`${window.Requestarr.urlBase}/api/v1/portal/members`, { headers: { 'X-Api-Key': window.Requestarr.apiKey } }).then(r => r.json()).then(all => all.sort((a, b) => b.requests - a.requests)[0].id)");
            await browser.GoAsync($"{Url}/members/{member}");
            await browser.WaitForAsync("[...document.querySelectorAll('[class*=\"PageToolbar\"] button')].some(b => b.innerText.trim() === 'Filter')");
            await PressToolbarAsync(browser, "Filter");
            var onMember = await browser.EvalAsync<string[]>(FilterChoices);
            check.Saw($"Filter on Requests: {string.Join(", ", onRequests)}; on a member's page: {string.Join(", ", onMember)}");
            await check.ShotAsync(browser, "member-filter");
            onRequests.Should().ContainInOrder(states);
            onMember.Should().Equal(states);
        }

        private static async Task ChooseViewAsync(HeadlessBrowser browser, string view, string shows)
        {
            await browser.RunAsync("[...document.querySelectorAll('button')].find(b => b.innerText.trim() === 'View').click()");
            await browser.RunAsync($"[...document.querySelectorAll('button, a, div[role=\"button\"]')].find(e => e.innerText.trim() === '{view}' && e.closest('[class*=\"MenuContent\"]')).click()");
            await browser.WaitForAsync($"!!document.querySelector('{shows}')");
        }

        // Posters' Options, as Sonarr's: a smaller poster size draws smaller posters, and Close keeps the choice.
        private static async Task PosterOptionsAsync(HeadlessBrowser browser, WebCheck check)
        {
            const string Poster = "document.querySelector('[class*=\"RequestIndexPoster-content\"]').getBoundingClientRect().width";
            await browser.GoAsync($"{Url}/requests");
            await ChooseViewAsync(browser, "Posters", "[class*=\"RequestIndexPoster-content\"]");
            var before = await browser.EvalAsync<double>(Poster);
            await PressToolbarAsync(browser, "Options");
            await browser.WaitForAsync("!!document.querySelector('[class*=\"ModalBody\"] [class*=\"EnhancedSelectInput-enhancedSelect\"]')");
            await ChoosePosterSizeAsync(browser, "Small");
            await browser.WaitForAsync($"{Poster} < {before.ToString(System.Globalization.CultureInfo.InvariantCulture)}");
            var after = await browser.EvalAsync<double>(Poster);
            var options = await browser.EvalAsync<string>("[...document.querySelectorAll('[class*=\"ModalBody\"] label')].map(l => l.innerText.trim()).filter(Boolean).join(', ')");
            check.Saw($"Posters' Options: {options}; Poster Size Small draws posters {after:0}px wide, down from {before:0}px");
            await check.ShotAsync(browser, "poster-options");
            await ChoosePosterSizeAsync(browser, "Large");
            await AnswerAsync(browser, "Close", true);
            after.Should().BeLessThan(before);
        }

        // The Poster Size choice in the open Options, as a person picks it from Servarr's drop-down.
        private static async Task ChoosePosterSizeAsync(HeadlessBrowser browser, string size)
        {
            const string Options = "[...document.querySelectorAll('[class*=\"EnhancedSelectInputOption-option\"]')]";
            await browser.RunAsync("document.querySelector('[class*=\"ModalBody\"] [class*=\"EnhancedSelectInput-enhancedSelect\"]').click()");
            await browser.WaitForAsync($"{Options}.some(o => o.innerText.trim() === '{size}')");
            await browser.RunAsync($"{Options}.find(o => o.innerText.trim() === '{size}').click()");
        }

        // Sorted by title, the letter bar beside the list jumps to a letter; opening a request and coming back returns to
        // where the list was scrolled, as Sonarr's series list does.
        private static async Task JumpAndReturnAsync(HeadlessBrowser browser, WebCheck check)
        {
            const string Letters = "[...document.querySelectorAll('[class*=\"PageJumpBarItem-jumpBarItem\"]')]";
            const string FirstShown = "(b => [...document.querySelectorAll('[class*=\"RequestIndexPoster-posterContainer\"]')].find(t => t.getBoundingClientRect().top >= b.getBoundingClientRect().top))(document.querySelector('[class*=\"RequestsPage-contentBody\"]'))";
            await PressToolbarAsync(browser, "Sort");
            await browser.RunAsync("[...document.querySelectorAll('[class*=\"MenuContent\"] [class*=\"MenuItem-menuItem\"]')].find(e => e.innerText.trim() === 'Title').click()");
            await browser.WaitForAsync($"{Letters}.length > 1");

            // The bar measures which letters fit before it settles; then the last letter is pressed by its text.
            await browser.EvalAsync<bool>($"(async () => {{ let last = -1; while ({Letters}.length !== last) {{ last = {Letters}.length; await new Promise(r => setTimeout(r, 400)); }} return true; }})()");
            var letter = await browser.EvalAsync<string>($"{Letters}.map(l => l.innerText.trim()).filter(l => /^[A-Z]$/.test(l)).at(-1)");
            await browser.RunAsync($"{Letters}.find(l => l.innerText.trim() === '{letter}').click()");
            await browser.WaitForAsync($"{IndexBody}.scrollTop > 0 && !!{FirstShown}");
            await browser.EvalAsync<bool>($"(async () => {{ const b = {IndexBody}; let last = -1; while (b.scrollTop !== last) {{ last = b.scrollTop; await new Promise(r => setTimeout(r, 400)); }} return true; }})()");
            var where = await browser.EvalAsync<string>($"(b => b.scrollTop + ' of ' + b.scrollHeight)({IndexBody})");

            // The poster grid's top row: the letter's first title shares it with the titles before it.
            var topRow = await browser.EvalAsync<string[]>($"(f => [...document.querySelectorAll('[class*=\"RequestIndexPoster-posterContainer\"]')].filter(t => t.getBoundingClientRect().top === f.getBoundingClientRect().top).map(t => t.title))({FirstShown})");
            check.Saw($"sorted by title, the jump bar's {letter} scrolls the list to {where}px, the row of {string.Join(", ", topRow)}");
            await check.ShotAsync(browser, "jump-bar");
            topRow.Should().Contain(t => t.ToUpperInvariant().StartsWith(letter, StringComparison.Ordinal));

            var scrolled = await browser.EvalAsync<double>($"{IndexBody}.scrollTop");
            await browser.RunAsync($"{FirstShown}.closest('[class*=\"RequestIndexPoster-content\"]').querySelector('a[href*=\"/requests/\"]').click()");
            await browser.WaitForAsync("/\\/requests\\/\\d+/.test(location.pathname)");
            await browser.RunAsync("history.back()");
            await browser.WaitForAsync($"!!{IndexBody} && Math.abs({IndexBody}.scrollTop - {scrolled.ToString(System.Globalization.CultureInfo.InvariantCulture)}) < 50");
            check.Saw($"opening a request and going back returns the list to {await browser.EvalAsync<double>($"{IndexBody}.scrollTop"):0}px down (it was {scrolled:0})");
        }

        private static async Task PeopleOnOneLineAsync(HeadlessBrowser browser, WebCheck check, string where)
        {
            var tallest = await browser.EvalAsync<double>($"Math.max(0, ...{CompactPeople}.map(e => e.getBoundingClientRect().height))");
            var pictures = await browser.EvalAsync<int>("Math.max(0, ...[...document.querySelectorAll('[class*=\"FollowerStack-pictures\"]')].map(p => p.children.length))");
            var most = await browser.EvalAsync<int>("Math.max(0, ...[...document.querySelectorAll('[class*=\"FollowerStack-count\"], [class*=\"FollowerStack-badge\"]')].map(e => Number(e.innerText.match(/\\d+/)?.[0] ?? 0)))");
            var overflowing = await browser.EvalAsync<int>($"{CompactPeople}.filter(e => e.getBoundingClientRect().right > e.parentElement.getBoundingClientRect().right + 1).length");
            check.Saw($"requests, {where}: people lines at most {tallest}px tall, {overflowing} wider than their row, at most {pictures} follower pictures, the largest count +{most}");
            tallest.Should().BeLessOrEqualTo(OneLine, "a title's people stay on one line however many follow it");
            overflowing.Should().Be(0, "a long name ends in an ellipsis instead of running out of its row");
            pictures.Should().BeLessOrEqualTo(MaxFollowerPictures);
        }

        private static async Task FollowersSectionAsync(HeadlessBrowser browser, WebCheck check, string theme)
        {
            var link = await browser.EvalAsync<string>($"{FollowerStack}?.href ?? ''");

            if (string.IsNullOrEmpty(link))
            {
                check.Saw($"followers ({theme}): no request has followers");
                return;
            }

            // The stack's pictures and its "+37 following": how many follow the title.
            var following = await browser.EvalAsync<int>($"(a => a.querySelectorAll('img').length + Number((a.innerText.match(/\\+(\\d+)/) ?? [0, 0])[1]))({FollowerStack})");
            await browser.GoAsync(link);
            await browser.WaitForAsync("!!document.querySelector('#followers tbody tr')");
            var rows = await browser.EvalAsync<int>("document.querySelectorAll('#followers tbody tr').length");
            var inView = await browser.EvalAsync<bool>("(r => r.top < innerHeight && r.bottom > 0)(document.getElementById('followers').getBoundingClientRect())");
            check.Saw($"followers ({theme}): the stack says {following} following; {link} opens at Followers (in view: {inView}) listing {rows}");
            rows.Should().Be(following, "the request's page lists every follower");
            inView.Should().BeTrue("the page opens at its Followers section");
            await check.ShotAsync(browser, $"request-followers-{theme}");
        }

        // From a request's page, the toolbar's "Open in Sonarr" lands on that series' page in the Sonarr it names.
        private static async Task OpenInSonarrAsync(HeadlessBrowser browser, WebCheck check)
        {
            await browser.GoAsync($"{Url}/requests");
            await browser.WaitForAsync($"!!{IndexBody}");
            var id = await browser.EvalAsync<int>("fetch(`${window.Requestarr.urlBase}/api/v1/portal/requests`, { headers: { 'X-Api-Key': window.Requestarr.apiKey } }).then(r => r.json()).then(all => all.find(r => r.media?.openIn === 'Sonarr' && r.media.openUrl)?.id ?? 0)");

            if (id == 0)
            {
                check.Saw("no request in a Sonarr a browser can reach: nothing to open");
                return;
            }

            const string Button = "[...document.querySelectorAll('[class*=\"PageToolbar\"] a')].find(a => a.innerText.trim() === 'Open in Sonarr')";
            await browser.GoAsync($"{Url}/requests/{id}");
            await browser.WaitForAsync($"!!{Button}");
            await check.ShotAsync(browser, "request-toolbar");
            var link = await browser.EvalAsync<string>($"{Button}.href");
            var title = await browser.EvalAsync<string>("document.querySelector('[class*=\"DetailsHeader-title\"]').innerText");
            await browser.GoAsync(link);
            var series = title.Contains(" (", StringComparison.Ordinal) ? title[..title.LastIndexOf(" (", StringComparison.Ordinal)] : title;
            await browser.WaitForAsync($"document.title.includes({JsonSerializer.Serialize(series)}) || document.body.innerText.includes({JsonSerializer.Serialize(series)})", TimeSpan.FromSeconds(30));
            check.Saw($"Open in Sonarr for {title}: {link} shows \"{await browser.EvalAsync<string>("document.title")}\"");
            await check.ShotAsync(browser, "open-in-sonarr");
        }

        // The header search, as in Sonarr: "/" puts the cursor in it; part of a request's title and of a member's name
        // each find it, grouped, and the result opens its page. Light and dark.
        [Test]
        public Task header_search() => CheckAsync(nameof(header_search), async (browser, check) =>
        {
            await SignInAsync(browser);
            await browser.GoAsync(Url + "/requests");
            await browser.WaitForAsync("document.querySelector('[class*=\"RequestIndex\"], [class*=\"IndexTable-row\"], [class*=\"Alert-info\"]')");
            var title = await PortalAsync<string>(browser, "requests", "r => r[0]?.title ?? ''");
            await browser.GoAsync(Url + "/members");
            await browser.WaitForAsync("document.querySelector('[class*=\"IndexTable-row\"], [class*=\"Alert-info\"]')");
            var member = await browser.EvalAsync<string>("document.querySelector('[class*=\"IndexTable-row\"] a[href*=\"/members/\"]')?.textContent ?? ''");

            if (title.Length == 0 || member.Length == 0)
            {
                check.Saw("No request or member to search for on this server.");
                return;
            }

            // The first few letters of each (a name may start with emoji, which aren't typed).
            var searches = new[] { ("request", FirstLetters(title, 4), "/requests/"), ("member", FirstLetters(member, 3), "/members/") };

            foreach (var (kind, words, opens) in searches)
            {
                foreach (var theme in Themes)
                {
                    await browser.SetThemeAsync(theme);
                    await browser.GoAsync(Url + "/system/status");
                    await browser.WaitForAsync("!!document.querySelector('[class*=\"PortalSearch-input\"]')");
                    await browser.RunAsync("document.body.dispatchEvent(new KeyboardEvent('keydown', { key: '/', bubbles: true }))");
                    (await browser.EvalAsync<bool>("document.activeElement?.className.includes('PortalSearch-input')")).Should().BeTrue("\"/\" puts the cursor in the search");
                    await browser.RunAsync($"const e = document.activeElement; Object.getOwnPropertyDescriptor(HTMLInputElement.prototype, 'value').set.call(e, {JsonSerializer.Serialize(words)}); e.dispatchEvent(new Event('input', {{ bubbles: true }}))");
                    await browser.WaitForAsync($"[...document.querySelectorAll('[class*=\"PortalSearch-result\"]')].some(r => r.getAttribute('href')?.includes('{opens}'))");
                    var shown = await browser.EvalAsync<string>("[...document.querySelectorAll('[class*=\"PortalSearch-results\"] > *')].map(e => e.textContent.trim()).join(' | ')");
                    check.Saw($"{kind} \"{words}\" ({theme}): {shown}");
                    await check.ShotAsync(browser, $"{kind}-{theme}");
                }

                await browser.RunAsync($"[...document.querySelectorAll('[class*=\"PortalSearch-result\"]')].find(r => r.getAttribute('href')?.includes('{opens}')).click()");
                await browser.WaitForAsync($"location.pathname.includes('{opens}')");
                check.Saw($"the {kind} result opened {await browser.EvalAsync<string>("location.pathname")}");
            }
        });

        // Import from Requestrr on the dev stack's recorded channels (copied into its recordings folder for a check):
        // The + card's invite asks only to read, ticking both channels lists what would be imported, then, once
        // Import is pressed, the import is listed as a command in System, then Tasks, and the Requests and Members pages
        // show the imported statuses, in both themes; a rerun finds nothing new. A stack without recordings says so and
        // passes: reading a real server is the owner's run, on beta.
        [Test]
        public Task import_preview() => CheckAsync(nameof(import_preview), async (browser, check) =>
        {
            const string Add = "document.querySelector('[class*=\"ConnectionCard-addConnection\"]')";
            const string InvitePage = "Open Discord's invite page";
            await SignInAsync(browser);
            var recorded = false;

            foreach (var theme in Themes)
            {
                await browser.SetThemeAsync(theme);
                await browser.GoAsync($"{Url}/settings/import");
                await browser.WaitForAsync("[...document.querySelectorAll('legend')].some(l => l.innerText.trim() === 'Channels')");

                // The invite for another server asks only to read, and adds no commands. The window it opens is caught.
                await browser.WaitForAsync($"!!{Add}");
                await browser.RunAsync($"window.open = (url) => {{ window.invited = url; }}; {Add}.click()");
                await browser.WaitForAsync($"[...document.querySelectorAll('button')].some(b => b.innerText.trim() === \"{InvitePage}\")");
                await browser.RunAsync($"[...document.querySelectorAll('button')].find(b => b.innerText.trim() === \"{InvitePage}\").click()");
                await browser.WaitForAsync("document.body.innerText.includes('Waiting for the bot to join')");
                await check.ShotAsync(browser, $"import-invite-{theme}");
                var invite = await browser.EvalAsync<string>("window.invited ?? ''");
                var asks = invite.Substring(invite.IndexOf("&permissions=", StringComparison.Ordinal) + 1);
                check.Saw($"import ({theme}): the + card's invite, asking {asks}");
                asks.Should().Be("permissions=66560&integration_type=0&scope=bot");

                try
                {
                    await browser.WaitForAsync("document.body.innerText.includes('Recorded channels (dev)')", TimeSpan.FromSeconds(10));
                }
                catch (TimeoutException)
                {
                    check.Saw($"import ({theme}): no recorded channels on this stack, nothing to preview");
                    continue;
                }

                recorded = true;
                var summary = await PreviewImportAsync(browser, check, theme);
                await browser.RunAsync("[...document.querySelectorAll('legend')].find(l => l.innerText.includes('Requests to import')).scrollIntoView()");
                await check.ShotAsync(browser, $"import-preview-{theme}");
                summary.Should().Contain("Requests found:");
            }

            if (recorded)
            {
                await ConfirmImportAsync(browser, check);
            }
        });

        // Both recorded channels, listed as ones Requestrr answered in, ticked, which reads them and lists what would be
        // imported: the list's summary line.
        private static async Task<string> PreviewImportAsync(HeadlessBrowser browser, WebCheck check, string theme)
        {
            // Each channel's box is named by its ID; the recorded channels are 900000000000100001 and ...02.
            const string Recorded = "[...document.querySelectorAll('input[type=checkbox][name^=\"90000000000010000\"]')]";

            await browser.WaitForAsync($"{Recorded}.length === 2");
            await browser.RunAsync($"{Recorded}.forEach(box => box.click())");
            await browser.WaitForAsync("document.body.innerText.includes('Requests to import')");
            await check.ShotAsync(browser, $"import-channels-{theme}");
            var summary = await browser.EvalAsync<string>("[...document.querySelectorAll('div')].map(d => d.innerText).find(t => t.startsWith('Read ')) ?? ''");
            check.Saw($"import ({theme}): {summary}");

            return summary;
        }

        // Confirms the last preview, then the Requests and Members pages show the imported statuses in both themes, and
        // previewing again finds nothing new.
        private async Task ConfirmImportAsync(HeadlessBrowser browser, WebCheck check)
        {
            await browser.RunAsync("[...document.querySelectorAll('button')].find(b => b.innerText.trim() === 'Import').click()");
            await browser.WaitForAsync("document.body.innerText.includes('Imported ')");
            check.Saw("import: " + await browser.EvalAsync<string>("[...document.querySelectorAll('div')].map(d => d.innerText).find(t => t.startsWith('Imported ')) ?? ''"));

            // It ran as a Servarr command: System, then Tasks lists it in the queue, as Sonarr lists its long jobs.
            await browser.GoAsync($"{Url}/system/tasks");
            await browser.WaitForAsync("[...document.querySelectorAll('tr')].some(r => r.innerText.includes('Import Requestrr'))");
            var task = await browser.EvalAsync<string>("[...document.querySelectorAll('tr')].find(r => r.innerText.includes('Import Requestrr')).innerText.replace(/\\s+/g, ' ').trim()");
            check.Saw($"System, then Tasks: {task}");

            // Posting its history in the admin feed (on to start) is listed there too, while it runs and after.
            await browser.WaitForAsync("[...document.querySelectorAll('tr')].some(r => r.innerText.includes('Post Imported History'))");
            var posting = await browser.EvalAsync<string>("[...document.querySelectorAll('tr')].find(r => r.innerText.includes('Post Imported History')).innerText.replace(/\\s+/g, ' ').trim()");
            check.Saw($"System, then Tasks: {posting}");
            await check.ShotAsync(browser, "import-task");

            foreach (var theme in Themes)
            {
                await browser.SetThemeAsync(theme);

                // Imported people are "Imported, not onboarded", or "Imported, left the server" when they aren't in it.
                foreach (var (page, status) in new[] { ("requests", "Not in library"), ("members", "Imported, ") })
                {
                    await browser.GoAsync($"{Url}/{page}");
                    await browser.WaitForAsync($"document.body.innerText.includes('{status}')");
                    var lines = await browser.EvalAsync<string>($"[...document.querySelectorAll('tr')].map(r => r.innerText.replace(/\\s+/g, ' ')).filter(t => t.includes('{status}')).join(' | ')");
                    check.Saw($"import ({theme}): {page}: {lines}");
                    await check.ShotAsync(browser, $"import-{page}-{theme}");
                }
            }

            await browser.GoAsync($"{Url}/settings/import");
            await browser.WaitForAsync("document.body.innerText.includes('Recorded channels (dev)')");
            (await PreviewImportAsync(browser, check, "rerun")).Should().Contain("Requests found: 0");
        }

        // People known from an import, on the Members page and an imported person's page, in both themes: each status as
        // the page shows it, and where their history came from. A stack with nobody imported says so and passes.
        [Test]
        public Task imported_members() => CheckAsync(nameof(imported_members), async (browser, check) =>
        {
            await SignInAsync(browser);

            foreach (var theme in Themes)
            {
                await browser.SetThemeAsync(theme);
                await browser.GoAsync($"{Url}/members");
                await browser.WaitForAsync("document.querySelector('[class*=\"IndexTable-row\"], [class*=\"Alert-info\"]')");

                var statuses = await browser.EvalAsync<string>("[...new Set([...document.querySelectorAll('[class*=\"IndexTable-row\"]')].map(r => r.children[1]?.innerText).filter(t => t?.startsWith('Imported, ')))].join(' | ')");

                if (string.IsNullOrEmpty(statuses))
                {
                    check.Saw($"members ({theme}): nobody imported on this stack");
                    continue;
                }

                check.Saw($"members ({theme}): {statuses}");
                await check.ShotAsync(browser, $"imported-members-{theme}");
                await MemberRowsAsync(browser, check, theme);

                await browser.RunAsync("[...document.querySelectorAll('[class*=\"IndexTable-row\"]')].find(r => r.children[1]?.innerText.startsWith('Imported, ')).querySelector('a').click()");
                await browser.WaitForAsync("document.body.innerText.includes('Imported from')");
                var source = await browser.EvalAsync<string>("[...document.querySelectorAll('dt')].find(t => t.innerText === 'Imported from')?.nextElementSibling?.innerText ?? ''");
                check.Saw($"member page ({theme}): Imported from {source}");
                await check.ShotAsync(browser, $"imported-member-page-{theme}");
                source.Should().NotBeEmpty();
            }
        });

        // Every Members row has a picture and an @handle, and someone imported is first seen (Added) with their first
        // request. The list draws only the rows on screen, so the check scrolls through it, reading each row as it's
        // drawn (leaving out the person a check run seeds), and photographs its end.
        private static async Task MemberRowsAsync(HeadlessBrowser browser, WebCheck check, string theme)
        {
            const string Rows = "[...document.querySelectorAll('[class*=\"IndexTable-row\"]')]";
            var seen = await browser.EvalAsync<JsonElement>(
                $"(async () => {{ const body = {IndexBody}; const header = [...document.querySelector('[class*=\"VirtualTableHeader-header\"]').children]; " +
                "const column = name => header.findIndex(t => t.innerText.trim().startsWith(name)); const first = column('First request'), added = column('Added'); " +
                "const rows = new Map(); for (let top = 0; ; top += body.clientHeight / 2) { body.scrollTo(0, top); await new Promise(r => setTimeout(r, 150)); " +
                $"for (const r of {Rows}) {{ const c = r.children; if (c[0].innerText.startsWith('Automated check')) continue; rows.set(c[0].querySelector('a').innerText, {{ picture: !!c[0].querySelector('img'), handle: c[0].innerText.includes('@'), " +
                "notFirstSeen: first >= 0 && c[1].innerText.startsWith('Imported, ') && c[first].innerText !== c[added].innerText }); } if (top >= body.scrollHeight) break; } " +
                "const all = [...rows.entries()]; return { rows: all.length, pictures: all.filter(([, r]) => r.picture).length, handles: all.filter(([, r]) => r.handle).length, " +
                "notFirstSeen: all.filter(([, r]) => r.notFirstSeen).map(([n]) => n).join(', ') }; })()");
            var rows = seen.GetProperty("rows").GetInt32();
            var pictures = seen.GetProperty("pictures").GetInt32();
            var handles = seen.GetProperty("handles").GetInt32();
            var notFirstSeen = seen.GetProperty("notFirstSeen").GetString();
            check.Saw($"members ({theme}): {rows} rows read through the whole list, {pictures} with a picture, {handles} with an @handle; imported rows whose Added isn't their first request: {(notFirstSeen.Length == 0 ? "none" : notFirstSeen)}");

            await check.ShotAsync(browser, $"imported-members-end-{theme}");
            await browser.RunAsync($"{IndexBody}.scrollTo(0, 0)");

            rows.Should().BePositive();
            pictures.Should().Be(rows, "everyone has a picture, Discord's default when they have none");
            handles.Should().Be(rows, "everyone shows their @handle");
            notFirstSeen.Should().BeEmpty();
        }

        // Requestarr's own settings pages load in both themes. QA and dev take their settings from the provisioning file,
        // so each page says so and its fields can't be changed. Connections is built like Sonarr's Indexers page: a card
        // for each connection under Discord, Media managers, Metadata and Bug reports, the "+" card opening the grouped Add
        // modal, a card opening its edit modal (with its default access for new members), and Options below; the old
        // Discord and Sonarr and Radarr addresses land on their sections.
        [Test]
        public Task settings_pages() => CheckAsync(nameof(settings_pages), async (browser, check) =>
        {
            const string Loaded = "document.querySelector('fieldset, table, [class*=\"Portal-empty\"], [class*=\"Alert-danger\"]')";
            await SignInAsync(browser);

            foreach (var theme in Themes)
            {
                await browser.SetThemeAsync(theme);

                foreach (var page in new[] { "connections", "requests", "changes" })
                {
                    await browser.GoAsync($"{Url}/settings/{page}");
                    await browser.WaitForAsync(Loaded);
                    var notice = await browser.EvalAsync<bool>("document.body.innerText.includes('takes these settings from its provisioning file')");
                    var editable = await browser.EvalAsync<int>("[...document.querySelectorAll('main input, [class*=\"PageContentBody\"] input')].filter(i => !i.disabled && !i.readOnly).length");
                    check.Saw($"{page} ({theme}): provisioning note {(notice ? "shown" : "not shown")}, {editable} field(s) that can be changed");
                    await check.ShotAsync(browser, $"settings-{page}-{theme}");

                    (await browser.EvalAsync<bool>("!!document.querySelector('[class*=\"PageContentBody\"] [class*=\"Alert-danger\"]')")).Should().BeFalse($"the {page} settings page loads");

                    if (page != "changes")
                    {
                        notice.Should().BeTrue("the provisioning file sets these on QA and dev");
                        editable.Should().Be(0, "a change would be undone on the next start");
                    }

                    if (page == "connections")
                    {
                        await ConnectionsAsync(browser, check, theme);
                    }

                    if (page == "requests")
                    {
                        await AuditLogKeptAsync(browser, check, theme);
                    }
                }
            }

            await OldAddressesAsync(browser, check);
        });

        // The sections in order, a card for the bot and one per Sonarr or Radarr (The Movie Database and GitHub keys come
        // from the server's environment, so they have none), each "+" card's Add modal, and a card's edit modal.
        private static async Task ConnectionsAsync(HeadlessBrowser browser, WebCheck check, string theme)
        {
            await browser.WaitForAsync($"{Cards}.length >= 3");
            var legends = await browser.EvalAsync<string[]>(Legends);
            var cards = await browser.EvalAsync<string[]>($"{Cards}.map(c => c.innerText.replace(/\\s+/g, ' ').trim())");
            var adds = await browser.EvalAsync<int>("document.querySelectorAll('[class*=\"ConnectionCard-addConnection\"]').length");
            var toolbar = await browser.EvalAsync<string>("document.querySelector('[class*=\"PageToolbar-toolbar\"]').innerText.replace(/\\s+/g, ' ')");
            check.Saw($"connections ({theme}): sections {string.Join(", ", legends)}; cards {string.Join(" | ", cards)}; {adds} \"+\" card; toolbar: {toolbar}");

            legends.Should().ContainInOrder("Discord", "Media managers", "Media servers", "Options");
            cards.Should().HaveCountGreaterThanOrEqualTo(3, "the bot and both test instances");
            string.Join(" ", cards).Should().NotContain("GitHub").And.NotContain("The Movie Database");
            cards.Count(c => c.Contains("Connected")).Should().BeGreaterThanOrEqualTo(2, "the bot and the test instances are connected");
            adds.Should().Be(2, "Media managers and Media servers each add through a \"+\" card");
            toolbar.Should().Contain("Show Advanced").And.Contain("No Changes").And.Contain("Test All Connections").And.NotContain("Add Sonarr");

            await browser.RunAsync("document.querySelector('[class*=\"ConnectionCard-addConnection\"]').click()");
            await browser.WaitForAsync("[...document.querySelectorAll('[class*=\"AddConnectionModal-name\"]')].some(n => n.innerText === 'Sonarr')");
            var choices = await browser.EvalAsync<string>(ModalText);
            check.Saw($"add modal ({theme}): {choices}");
            choices.Should().Contain("Media managers").And.Contain("Sonarr").And.Contain("Radarr");
            await check.ShotAsync(browser, $"settings-connections-add-{theme}");
            await browser.RunAsync("[...document.querySelectorAll('[class*=\"ModalFooter\"] button')].find(b => b.innerText.includes('Close')).click()");
            await browser.WaitForAsync("!document.querySelector('[class*=\"ModalContent\"]')");

            await browser.RunAsync("document.querySelectorAll('[class*=\"ConnectionCard-addConnection\"]')[1].click()");
            await browser.WaitForAsync("!!document.querySelector('[class*=\"ModalContent\"]')");
            var servers = await browser.EvalAsync<string>(ModalText);
            check.Saw($"media servers add modal ({theme}): {servers}");
            servers.Should().Contain("Media servers").And.Contain("aren't built yet");
            await check.ShotAsync(browser, $"settings-connections-media-servers-{theme}");
            await browser.RunAsync("[...document.querySelectorAll('[class*=\"ModalFooter\"] button')].find(b => b.innerText.includes('Close')).click()");
            await browser.WaitForAsync("!document.querySelector('[class*=\"ModalContent\"]')");

            await browser.RunAsync($"{Cards}.find(c => c.innerText.includes('Sonarr')).click()");
            await browser.WaitForAsync("!!document.querySelector('[class*=\"ModalFooter\"]')");
            var modal = await browser.EvalAsync<string>(ModalText);
            check.Saw($"edit modal ({theme}): {modal}");
            modal.Should().Contain("Test").And.Contain("Cancel").And.Contain("Save");

            // Servarr's provider form, from the media managers' schema: Sonarr's own fields and its anime ones.
            modal.Should().Contain("Quality profile").And.Contain("Series type").And.Contain("Anime").And.Contain("Turned on");

            // Its choices come from the test Sonarr through the provider's actions; the saved profile shows once they do.
            await browser.WaitForAsync($"{ModalText}.includes('HD-1080p') && {ModalText}.includes('/data/tv')");
            check.Saw($"edit modal ({theme}): quality profile and root folder loaded from Sonarr (HD-1080p, /data/tv)");
            await check.ShotAsync(browser, $"settings-connections-edit-{theme}");

            // The test stacks' provisioning file sets no default access, so their instances give new members what a new one
            // does. The chosen one is read from Servarr's drop-down (or a plain select's selected option).
            var defaultAccess = await browser.EvalAsync<string>(
                "(() => { const g = [...document.querySelectorAll('[class*=\"ModalBody\"] [class*=\"FormGroup-group\"]')].find(e => e.innerText.startsWith('Default access for new members')); " +
                "g?.scrollIntoView(); const chosen = g?.querySelector('select')?.selectedOptions[0]?.text ?? g?.querySelector('[class*=\"EnhancedSelectInputSelectedValue-selectedValue\"]')?.innerText.trim() ?? 'nothing chosen'; " +
                "return g ? `${chosen}: ${g.querySelector('[class*=\"FormInputHelpText\"]')?.innerText ?? ''}` : ''; })()");
            check.Saw($"edit modal ({theme}): default access for new members {defaultAccess}");
            defaultAccess.Should().StartWith("Approved automatically:").And.Contain("for each person");
            await check.ShotAsync(browser, $"settings-connections-default-access-{theme}");
            await browser.RunAsync("[...document.querySelectorAll('[class*=\"ModalFooter\"] button')].find(b => b.innerText.includes('Cancel')).click()");
        }

        private static async Task AuditLogKeptAsync(HeadlessBrowser browser, WebCheck check, string theme)
        {
            var keepFor = await browser.EvalAsync<bool>("document.body.innerText.includes('Keep the audit log for') && document.body.innerText.includes('1 year')");
            var locked = await browser.EvalAsync<bool>("!!document.querySelector('[class*=\"EnhancedSelectInput-isDisabled\"]')");
            check.Saw($"requests ({theme}): Keep the audit log for {(keepFor ? "shown, 1 year" : "not shown")}, {(locked ? "locked" : "can be changed")}");
            keepFor.Should().BeTrue("the provisioning file keeps the audit log for 1 year");
            locked.Should().BeTrue("a change would be undone on the next start");
            (await browser.EvalAsync<bool>("(() => { const l = [...document.querySelectorAll('legend')].find(e => e.innerText.includes('Audit log')); l?.scrollIntoView(); return !!l; })()"))
                .Should().BeTrue("the Audit log section is on the Requests page");
            await check.ShotAsync(browser, $"settings-requests-audit-log-{theme}");
        }

        // Links in the docs, the setup guide and Discord messages still name the pages Connections replaced.
        private static async Task OldAddressesAsync(HeadlessBrowser browser, WebCheck check)
        {
            foreach (var (old, section) in new[] { ("discord", "discord"), ("instances", "media-managers") })
            {
                await browser.GoAsync($"{Url}/settings/{old}");
                await browser.WaitForAsync($"!!document.getElementById('{section}')");
                var landed = await browser.EvalAsync<string>("location.pathname + location.hash");
                check.Saw($"/settings/{old} landed on {landed}");
                landed.Should().EndWith($"/settings/connections#{section}");
            }
        }

        // A list's first title or name opens its own page, which shows what it is and its history.
        private static string FirstLetters(string text, int count)
        {
            var start = text.TakeWhile(c => !char.IsLetterOrDigit(c)).Count();

            return text.Substring(start, Math.Min(count, text.Length - start));
        }

        private static async Task OpenFirstAsync(HeadlessBrowser browser, WebCheck check, string page, string theme)
        {
            var link = await browser.EvalAsync<string>($"document.querySelector('[class*=\"PageContentBody\"] a[href*=\"/{page}/\"]')?.getAttribute('href') ?? ''");

            if (page == "queue" || string.IsNullOrEmpty(link))
            {
                return;
            }

            await browser.GoAsync(Url + link[link.IndexOf("/" + page + "/", StringComparison.Ordinal)..]);
            await browser.WaitForAsync("!!document.querySelector('[class*=\"DetailsHeader-header\"]') && document.querySelectorAll('legend').length > 0");
            var sections = await browser.EvalAsync<string>("[...document.querySelectorAll('legend')].map(l => l.textContent.trim()).join(', ')");
            check.Saw($"{page} detail ({theme}): {sections}");
            sections.Should().Contain(page == "requests" ? "Timeline" : "History");

            // Sonarr's series page header (backdrop, picture, title, label chips) and the actions in the toolbar.
            var header = await browser.EvalAsync<string>("document.querySelector('[class*=\"DetailsHeader-header\"]')?.innerText.replace(/\\s+/g, ' ').trim() ?? ''");
            var actions = await browser.EvalAsync<string>("[...document.querySelectorAll('[class*=\"PageToolbar\"] button')].map(b => b.innerText.trim()).join(', ')");
            check.Saw($"{page} detail ({theme}): header \"{header}\"; toolbar: {actions}");
            header.Should().NotBeEmpty("the page opens with Sonarr's details header");

            // The poster or picture stays in its place at the left, as Sonarr's poster does, never over the title.
            var imageWidth = await browser.EvalAsync<double>("document.querySelector('[class*=\"DetailsHeader-poster\"] img')?.getBoundingClientRect().width ?? 0");
            check.Saw($"{page} detail ({theme}): its poster or picture is {imageWidth:0}px wide");
            imageWidth.Should().BeLessThanOrEqualTo(170, "the header's poster space is 170px wide");
            await check.ShotAsync(browser, $"{page}-detail-{theme}");

            // A timeline or history entry's details button opens it in full, as Sonarr's History does.
            if (await browser.EvalAsync<bool>("!!document.querySelector('fieldset tbody tr button')"))
            {
                await browser.RunAsync("document.querySelector('fieldset tbody tr button').click()");
                await browser.WaitForAsync("!!document.querySelector('[class*=\"ModalBody\"] dl')");
                check.Saw($"an entry's details ({theme}): {await browser.EvalAsync<string>("document.querySelector('[class*=\"ModalContent\"]').innerText.replace(/\\s+/g, ' ').trim()")}");
                await check.ShotAsync(browser, $"{page}-entry-details-{theme}");
                await AnswerAsync(browser, "Close", true);
            }
        }

        [Test]
        public Task header_logo() => CheckAsync(nameof(header_logo), async (browser, check) =>
        {
            await SignInAsync(browser);

            foreach (var theme in Themes)
            {
                await browser.SetThemeAsync(theme);
                await browser.GoAsync(Url + "/system/status");
                await browser.WaitForAsync($"document.querySelector(\"{HeaderLogo}\")");
                await QaLogoAsync(browser);

                // The R's own rows in the image (right of the bar): full height when it leaves no empty rows above or below.
                var fit = await browser.EvalAsync<JsonElement>($@"(() => {{
                    const logo = document.querySelector(""{HeaderLogo}"");
                    const box = getComputedStyle(logo.parentElement);
                    const canvas = document.createElement('canvas');
                    canvas.width = logo.naturalWidth; canvas.height = logo.naturalHeight;
                    const context = canvas.getContext('2d');
                    context.drawImage(logo, 0, 0);
                    const pixels = context.getImageData(0, 0, canvas.width, canvas.height).data;
                    const rows = [];
                    for (let y = 0; y < canvas.height; y++) {{
                        for (let x = Math.floor(canvas.width * 0.3); x < canvas.width; x++) {{
                            if (pixels[(y * canvas.width + x) * 4 + 3] > 16) {{ rows.push(y); break; }}
                        }}
                    }}
                    return {{ height: canvas.height, top: rows[0], bottom: rows[rows.length - 1], shown: Math.round(logo.getBoundingClientRect().height),
                              border: box.borderTopWidth, behind: box.backgroundColor, header: getComputedStyle(logo.closest('header, [class*=""PageHeader-header""]')).backgroundColor,
                              src: logo.getAttribute('src') }};
                }})()");
                var (height, top, bottom) = (fit.GetProperty("height").GetInt32(), fit.GetProperty("top").GetInt32(), fit.GetProperty("bottom").GetInt32());
                var header = fit.GetProperty("header").GetString();
                check.Saw($"{theme}: logo {fit.GetProperty("src").GetString()}, shown {fit.GetProperty("shown").GetInt32()}px tall; the R fills rows {top} to {bottom} of {height}; around it a {fit.GetProperty("border").GetString()} border on {fit.GetProperty("behind").GetString()}, in a header of {header}.");
                (top + (height - 1 - bottom)).Should().BeLessOrEqualTo(MaxLogoPadding, "the R is full height");
                fit.GetProperty("border").GetString().Should().Be("0px", "no box around the logo");

                if (theme == "dark")
                {
                    header.Should().NotBe("rgb(0, 0, 0)", "the dark theme's header is charcoal gray, never black");
                }

                await check.ShotAsync(browser, theme);
            }

            // The darkest solid pixel in each of QA's logo and tab icon files: the arrow, a soft charcoal, never black.
            var samples = await browser.EvalAsync<JsonElement>(@"Promise.all(['light-purple/qa/logo-header.png', 'light/qa/favicon-32x32.png', 'dark/qa/logo-header.png', 'dark/qa/favicon-32x32.png'].map(file => new Promise(done => {
                const image = new Image();
                image.onload = () => {
                    const canvas = document.createElement('canvas');
                    canvas.width = image.naturalWidth; canvas.height = image.naturalHeight;
                    const context = canvas.getContext('2d');
                    context.drawImage(image, 0, 0);
                    const pixels = context.getImageData(0, 0, canvas.width, canvas.height).data;
                    let darkest = [255, 255, 255];
                    for (let i = 0; i < pixels.length; i += 4) {
                        if (pixels[i + 3] > 200 && pixels[i] + pixels[i + 1] + pixels[i + 2] < darkest[0] + darkest[1] + darkest[2]) { darkest = [pixels[i], pixels[i + 1], pixels[i + 2]]; }
                    }
                    done({ file, r: darkest[0], g: darkest[1], b: darkest[2] });
                };
                image.src = window.Requestarr.urlBase + '/Content/Images/Icons/' + file;
            })))");

            foreach (var sample in samples.EnumerateArray())
            {
                var (r, g, b) = (sample.GetProperty("r").GetInt32(), sample.GetProperty("g").GetInt32(), sample.GetProperty("b").GetInt32());
                check.Saw($"{sample.GetProperty("file").GetString()}: darkest solid colour rgb({r}, {g}, {b}).");
                Math.Max(r, Math.Max(g, b)).Should().BeGreaterThan(NotBlack, "a soft charcoal, never black");
            }

            await ShowTabIconsAsync(browser);
            await check.ShotAsync(browser, "tab-icons");
        });

        [Test]
        public Task sign_in_and_out() => CheckAsync(nameof(sign_in_and_out), async (browser, check) =>
        {
            using var http = new HttpClient(new HttpClientHandler { AllowAutoRedirect = false, CheckCertificateRevocationList = true });
            var signedOut = await http.GetAsync(Url + "/settings/general");
            check.Saw($"Signed out, the settings page answers {(int)signedOut.StatusCode} to {signedOut.Headers.Location}.");
            signedOut.StatusCode.Should().Be(HttpStatusCode.Redirect, "never a bare browser error");

            await browser.GoAsync(Url + "/settings/general");
            await browser.WaitForAsync("document.querySelector('form input[name=username]')");
            check.Saw($"The browser lands on {await browser.EvalAsync<string>("location.pathname + location.search")} with the sign-in form.");
            await QaLogoAsync(browser);
            await check.ShotAsync(browser, "signed-out");

            await SubmitLoginAsync(browser);
            var landed = await browser.EvalAsync<string>("location.pathname");
            check.Saw($"Signed in, it goes on to {landed}.");
            landed.Should().EndWith("/settings/general", "signing in goes back to where you were going");

            await browser.WaitForAsync("document.querySelector(\"[class*='PageHeaderActionsMenu-menuButton']\")");
            await browser.RunAsync("document.querySelector(\"[class*='PageHeaderActionsMenu-menuButton']\").click()");
            await browser.WaitForAsync("[...document.querySelectorAll('a')].some(a => a.textContent.trim() === 'Logout')");
            await QaLogoAsync(browser);
            await check.ShotAsync(browser, "account-menu");
            await browser.LeaveAsync("[...document.querySelectorAll('a')].find(a => a.textContent.trim() === 'Logout').click()");
            check.Saw($"After Logout: {await browser.EvalAsync<string>("location.pathname")}, sign-in form shown {await browser.EvalAsync<bool>("!!document.querySelector('form input[name=username]')")}.");
            await QaLogoAsync(browser);
            await check.ShotAsync(browser, "after-logout");

            await browser.GoAsync(Url + "/");
            var again = await browser.EvalAsync<bool>("!!document.querySelector('form input[name=username]')");
            check.Saw($"Opening the address again shows the sign-in form: {again}.");
            again.Should().BeTrue();
        });

        [Test]
        public Task general_settings() => CheckAsync(nameof(general_settings), async (browser, check) =>
        {
            await SignInAsync(browser);
            await browser.SetThemeAsync("light");
            await browser.GoAsync(Url + "/settings/general");
            await browser.WaitForAsync("document.querySelector('input[name=port]')");
            await QaLogoAsync(browser);

            foreach (var label in new[] { "Port Number", "Allowed Hosts", "Authentication", "Authentication Required", "Username" })
            {
                var group = await GroupAsync(browser, label);
                check.Saw($"{label}: read-only {group.ReadOnly}; note \"{group.Help}\".");
                (label == "Port Number" ? group.Help.Contains("compose file") : group.Help.Contains(ProvisionedNote)).Should().BeTrue($"{label} explains itself");
                group.ReadOnly.Should().BeTrue($"{label} is set where it can't be changed here");
            }

            await check.ShotAsync(browser, "light");
            await ToolbarAsync(browser, "Show Advanced");
            var before = await browser.EvalAsync<string>("document.querySelector('input[name=backupRetention]').value");
            var changed = (int.Parse(before) % 90) + 1;
            await SaveBackupRetentionAsync(browser, changed);
            var saved = await browser.EvalAsync<int>("fetch(window.Requestarr.urlBase + '/api/v1/config/host', { headers: { 'X-Api-Key': window.Requestarr.apiKey } }).then(r => r.json()).then(h => h.backupRetention)");
            check.Saw($"Backup Retention {before} changed to {changed} and saved: no error, and the server now has {saved}.");
            await browser.RunAsync("document.querySelector('input[name=backupRetention]').scrollIntoView({ block: 'center' })");
            await check.ShotAsync(browser, "saved");
            saved.Should().Be(changed);

            await SaveBackupRetentionAsync(browser, int.Parse(before));
            check.Saw($"Set back to {before}.");
            await browser.SetThemeAsync("dark");
            await browser.GoAsync(Url + "/settings/general");
            await browser.WaitForAsync("document.querySelector('input[name=port]')");
            await QaLogoAsync(browser);
            await check.ShotAsync(browser, "dark");
        });

        [Test]
        public Task sign_in_page() => CheckAsync(nameof(sign_in_page), async (browser, check) =>
        {
            foreach (var theme in Themes)
            {
                await browser.SetThemeAsync(theme);
                await browser.GoAsync(Url + "/login");
                await QaLogoAsync(browser);
                var page = await browser.EvalAsync<JsonElement>(@"(() => {
                    const logo = document.querySelector('img.logo');
                    return { src: logo.getAttribute('src'), panel: getComputedStyle(logo.closest('.panel-header') || logo.parentElement).backgroundColor };
                })()");
                check.Saw($"{theme}: logo {page.GetProperty("src").GetString()} on {page.GetProperty("panel").GetString()}.");
                page.GetProperty("src").GetString().Should().Contain("/dark/", "the gray box takes the light-arrow logo in both themes");
                await check.ShotAsync(browser, theme);
            }
        });

        [Test]
        public Task loading_screen() => CheckAsync(nameof(loading_screen), async (browser, check) =>
        {
            await SignInAsync(browser);

            foreach (var theme in Themes)
            {
                await browser.SetThemeAsync(theme);
                await browser.HoldRequestsAsync("*/api/v1/*");
                await browser.GoAsync(Url + "/");
                await browser.WaitForAsync("document.querySelector(\"[class*='LoadingPage-page'] img\")");
                await QaLogoAsync(browser);
                var screen = await browser.EvalAsync<JsonElement>(@"(() => {
                    const page = document.querySelector(""[class*='LoadingPage-page']"");
                    const logo = page.querySelector('img');
                    return { src: logo.getAttribute('src'), behind: getComputedStyle(logo).backgroundColor, page: getComputedStyle(page).backgroundColor };
                })()");
                check.Saw($"{theme}: loading screen logo {screen.GetProperty("src").GetString()} (the header's), behind it {screen.GetProperty("behind").GetString()} on {screen.GetProperty("page").GetString()}.");
                screen.GetProperty("src").GetString().Should().Contain("/qa/logo-header.png");
                await check.ShotAsync(browser, theme);
                await browser.ReleaseRequestsAsync();
            }
        });

        [Test]
        public Task project_links() => CheckAsync(nameof(project_links), async (browser, check) =>
        {
            await SignInAsync(browser);
            await browser.GoAsync(Url + "/system/status");
            await browser.WaitForAsync("[...document.querySelectorAll('a')].some(a => a.href.includes('github.com'))");
            var links = await browser.EvalAsync<string[]>("[...document.querySelectorAll('a')].filter(a => a.href.includes('github.com')).map(a => a.closest('dd').previousElementSibling.textContent.trim() + ': ' + a.href)");
            await browser.RunAsync("[...document.querySelectorAll('a')].find(a => a.href.includes('github.com')).scrollIntoView()");

            foreach (var link in links)
            {
                check.Saw(link);
            }

            await QaLogoAsync(browser);
            await check.ShotAsync(browser, "more-info");
            links.Should().Contain(l => l.EndsWith("https://github.com/Requestarr/Requestarr")).And.Contain(l => l.EndsWith("https://github.com/Requestarr/Requestarr/issues"));
        });

        // width: the browser window's, in pixels.
        private static async Task CheckAsync(string name, Func<HeadlessBrowser, WebCheck, Task> body, int width = 1366)
        {
            if (string.IsNullOrWhiteSpace(Url))
            {
                Assert.Ignore("QA_CHECKS_URL not set: run through docker/qa_checks.py run");
            }

            var check = new WebCheck(name, Environment.GetEnvironmentVariable("QA_CHECKS_DIR"));
            Exception failure = null;

            try
            {
                await using var browser = await HeadlessBrowser.StartAsync(width);
                await body(browser, check);
            }
            catch (Exception ex)
            {
                failure = ex;
                throw;
            }
            finally
            {
                check.Write(failure);
            }
        }

        private static async Task SignInAsync(HeadlessBrowser browser)
        {
            await browser.GoAsync(Url + "/login");
            await SubmitLoginAsync(browser);
            (await browser.EvalAsync<string>("location.pathname")).Should().NotEndWith("/login", "the throwaway login signs in");
        }

        private static Task SubmitLoginAsync(HeadlessBrowser browser)
        {
            var username = JsonSerializer.Serialize(Environment.GetEnvironmentVariable("QA_CHECKS_USERNAME"));
            var password = JsonSerializer.Serialize(Environment.GetEnvironmentVariable("QA_CHECKS_PASSWORD"));

            return browser.LeaveAsync($"document.querySelector('input[name=username]').value = {username}; document.querySelector('input[name=password]').value = {password}; document.querySelector('form').submit()");
        }

        private static async Task QaLogoAsync(HeadlessBrowser browser)
        {
            await browser.EvalAsync<bool>(ShowAsQa);
            await browser.WaitForAsync(ImagesLoaded);
        }

        private static async Task ShowTabIconsAsync(HeadlessBrowser browser)
        {
            await browser.RunAsync(TabIconsPage);
            await browser.WaitForAsync(ImagesLoaded);
        }

        private static async Task<(bool ReadOnly, string Help)> GroupAsync(HeadlessBrowser browser, string label)
        {
            var group = await browser.EvalAsync<JsonElement>($@"(() => {{
                const label = [...document.querySelectorAll(""[class*='FormLabel']"")].find(l => l.textContent.trim() === {JsonSerializer.Serialize(label)});
                const group = label.closest(""[class*='FormGroup-group']"");
                const input = group.querySelector('input, button, [class*=""EnhancedSelectInput""]');
                const locked = !!input && (input.readOnly === true || input.disabled === true || /isDisabled|readOnly/i.test(input.className));
                return {{ locked, help: [...group.querySelectorAll(""[class*='FormInputHelpText']"")].map(h => h.textContent.trim()).join(' ') }};
            }})()");

            return (group.GetProperty("locked").GetBoolean(), group.GetProperty("help").GetString());
        }

        private static Task ToolbarAsync(HeadlessBrowser browser, string text) =>
            browser.RunAsync($"[...document.querySelectorAll(\"[class*='PageToolbarButton-toolbarButton']\")].find(b => b.textContent.includes({JsonSerializer.Serialize(text)})).click()");

        // Typed the way React sees typing, then saved; the toolbar says No Changes again once it's saved.
        private static async Task SaveBackupRetentionAsync(HeadlessBrowser browser, int days)
        {
            await browser.EvalAsync<bool>($@"(() => {{
                const input = document.querySelector('input[name=backupRetention]');
                Object.getOwnPropertyDescriptor(HTMLInputElement.prototype, 'value').set.call(input, '{days}');
                ['input', 'change', 'blur'].forEach(type => input.dispatchEvent(new Event(type, {{ bubbles: true }})));
                return true;
            }})()");
            await browser.WaitForAsync("[...document.querySelectorAll(\"[class*='PageToolbarButton-toolbarButton']\")].some(b => b.textContent.includes('Save Changes'))");
            await ToolbarAsync(browser, "Save Changes");
            await browser.WaitForAsync("[...document.querySelectorAll(\"[class*='PageToolbarButton-toolbarButton']\")].some(b => b.textContent.includes('No Changes'))");
            (await browser.EvalAsync<int>("document.querySelectorAll(\"[class*='Alert-danger'], [class*='validationError']\").length")).Should().Be(0, "it saves with no error");
        }
    }
}
