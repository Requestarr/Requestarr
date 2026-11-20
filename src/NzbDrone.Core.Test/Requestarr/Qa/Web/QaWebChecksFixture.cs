using System;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Text.Json;
using System.Threading.Tasks;
using FluentAssertions;
using NUnit.Framework;
using NzbDrone.Test.Common.Categories;

namespace Requestarr.Core.Test.Qa.Web
{
    // The automated web app checks for the QA checklist (docker/qa_checks.py run): a real browser against the dev stack
    // at the commit QA receives, signed in with a throwaway login, in the light and dark theme, never the QA server.
    // Each test is one check an item names ("the web app status page check") and writes what it saw and its screenshots
    // for the automation to look at against the item's Expect. The dev stack shows the dev logo where QA shows
    // QA's; the checks put QA's own logo files in the page where QA shows them (the logo is the only part that differs
    // between environments). Needs QA_CHECKS_URL, QA_CHECKS_USERNAME, QA_CHECKS_PASSWORD, QA_CHECKS_DIR and
    // QA_CHECKS_COMMIT (the run sets them).
    [TestFixture]
    [ManualTest]
    public class QaWebChecksFixture
    {
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
        private const string CompactPeople = "[...document.querySelectorAll('[class*=\"RequestViews-people\"], [class*=\"RequestViews-posterPeople\"], [class*=\"FollowerStack-stack\"], [class*=\"Person-small\"]')]";
        private const string FollowerStack = "document.querySelector('a[class*=\"FollowerStack-stack\"]')";

        private static readonly string[] Themes = { "light", "dark" };

        private static string Url => Environment.GetEnvironmentVariable("QA_CHECKS_URL")?.TrimEnd('/');
        private static string Commit => Environment.GetEnvironmentVariable("QA_CHECKS_COMMIT");

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
            check.Saw("On QA the same build says qa where the dev stack says dev; the number and the short code are the commit's.");

            shown.Should().Be(built, "the label the image was built with");
            shown.Should().EndWith("." + Commit, "the build is the commit QA receives");
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
            const string Loaded = "document.querySelector('table, fieldset, [class*=\"Portal-empty\"], [class*=\"Alert-\"], [class*=\"RequestViews-\"]')";
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
                    var rows = await browser.EvalAsync<int>("document.querySelectorAll('tbody tr').length");
                    check.Saw($"{page} ({theme}): {rows} row(s)");
                    await check.ShotAsync(browser, $"{page.Replace('/', '-')}-{theme}");
                    await OpenFirstAsync(browser, check, page, theme);
                }
            }
        });

        // The Requests page's three views, as Sonarr's and Radarr's lists have them, in both themes: Overview (the
        // default) with posters and what each title is, Posters, and Table with a poster column, each naming its people
        // on one line however many follow a title: the requester small, then at most five follower pictures and a count.
        // A follower stack opens its request's page at the Followers section, which lists them all. Then an "Open in
        // Sonarr" link is followed to the title's page in that Sonarr. A server with no requests says so and passes.
        [Test]
        public Task request_views() => CheckAsync(nameof(request_views), async (browser, check) =>
        {
            const string Shown = "document.querySelector('[class*=\"RequestViews-\"], table, [class*=\"Portal-empty\"]')";
            await SignInAsync(browser);

            foreach (var theme in Themes)
            {
                await browser.SetThemeAsync(theme);
                await browser.GoAsync($"{Url}/requests");
                await browser.WaitForAsync(Shown);

                if (await browser.EvalAsync<bool>("!!document.querySelector('[class*=\"Portal-empty\"]')"))
                {
                    check.Saw($"requests ({theme}): no requests on this server");
                    return;
                }

                foreach (var (view, shows) in new[] { ("Overview", "[class*=\"RequestViews-overviewRow\"]"), ("Posters", "[class*=\"RequestViews-posters\"]"), ("Table", "table") })
                {
                    await browser.RunAsync("[...document.querySelectorAll('button')].find(b => b.innerText.trim() === 'View').click()");
                    await browser.RunAsync($"[...document.querySelectorAll('button, a, div[role=\"button\"]')].find(e => e.innerText.trim() === '{view}' && e.closest('[class*=\"MenuContent\"]')).click()");
                    await browser.WaitForAsync($"!!document.querySelector('{shows}')");
                    await browser.WaitForAsync("[...document.querySelectorAll('[class*=\"PageContentBody\"] img')].filter(i => i.getBoundingClientRect().top < innerHeight).every(i => i.complete)");
                    var posters = await browser.EvalAsync<int>("[...document.querySelectorAll('[class*=\"PageContentBody\"] img')].filter(i => i.complete && i.naturalWidth > 0).length");
                    var opens = await browser.EvalAsync<int>("[...document.querySelectorAll('a')].filter(a => a.innerText.startsWith('Open in ')).length");
                    check.Saw($"requests, {view} ({theme}): {posters} posters shown, {opens} Open in links");
                    await check.ShotAsync(browser, $"requests-{view.ToLowerInvariant()}-{theme}");
                    await PeopleOnOneLineAsync(browser, check, $"{view} ({theme})");
                }

                await FollowersSectionAsync(browser, check, theme);
            }

            await OpenInSonarrAsync(browser, check);
        });

        private static async Task PeopleOnOneLineAsync(HeadlessBrowser browser, WebCheck check, string where)
        {
            var tallest = await browser.EvalAsync<double>($"Math.max(0, ...{CompactPeople}.map(e => e.getBoundingClientRect().height))");
            var pictures = await browser.EvalAsync<int>("Math.max(0, ...[...document.querySelectorAll('[class*=\"FollowerStack-pictures\"]')].map(p => p.children.length))");
            var most = await browser.EvalAsync<string>("[...document.querySelectorAll('[class*=\"FollowerStack-count\"], [class*=\"FollowerStack-badge\"]')].map(e => e.innerText.trim()).sort((a, b) => b.localeCompare(a, undefined, { numeric: true }))[0] ?? 'none'");
            check.Saw($"requests, {where}: people lines at most {tallest}px tall, at most {pictures} follower pictures, the most followers shown as {most}");
            tallest.Should().BeLessOrEqualTo(OneLine, "a title's people stay on one line however many follow it");
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

        // From the Overview view, the first "Open in Sonarr" link lands on that series' page in the Sonarr it names.
        private static async Task OpenInSonarrAsync(HeadlessBrowser browser, WebCheck check)
        {
            await browser.GoAsync($"{Url}/requests");
            await browser.WaitForAsync("!!document.querySelector('[class*=\"RequestViews-\"], table')");
            await browser.RunAsync("[...document.querySelectorAll('button')].find(b => b.innerText.trim() === 'View').click()");
            await browser.RunAsync("[...document.querySelectorAll('button, a, div[role=\"button\"]')].find(e => e.innerText.trim() === 'Overview' && e.closest('[class*=\"MenuContent\"]')).click()");
            await browser.WaitForAsync("!!document.querySelector('[class*=\"RequestViews-overviewRow\"]')");
            var link = await browser.EvalAsync<string>("[...document.querySelectorAll('[class*=\"RequestViews-overviewRow\"] a')].find(a => a.innerText === 'Open in Sonarr')?.href ?? ''");

            if (string.IsNullOrEmpty(link))
            {
                check.Saw("no request in a Sonarr a browser can reach: nothing to open");
                return;
            }

            var title = await browser.EvalAsync<string>("[...document.querySelectorAll('[class*=\"RequestViews-overviewRow\"]')].find(r => [...r.querySelectorAll('a')].some(a => a.innerText === 'Open in Sonarr')).querySelector('a').innerText");
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
            await browser.WaitForAsync("document.querySelector('table, [class*=\"Portal-empty\"], [class*=\"RequestViews-\"]')");
            var title = await browser.EvalAsync<string>("document.querySelector('[class*=\"PageContentBody\"] a[href*=\"/requests/\"]:not(:has(img))')?.textContent ?? ''");
            await browser.GoAsync(Url + "/members");
            await browser.WaitForAsync("document.querySelector('table, [class*=\"Portal-empty\"]')");
            var member = await browser.EvalAsync<string>("document.querySelector('tbody a[href*=\"/members/\"]')?.textContent ?? ''");

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
        // the read-only invite link, both channels found and readable, the one list of what would be imported, then,
        // once confirmed, the Requests and Members pages with the imported statuses, in both themes; a rerun finds
        // nothing new. A stack without recordings says so and passes: reading a real server is the owner's run, on beta.
        [Test]
        public Task import_preview() => CheckAsync(nameof(import_preview), async (browser, check) =>
        {
            const string ReadInvite = "Invite the bot to read another server";
            await SignInAsync(browser);
            var recorded = false;

            foreach (var theme in Themes)
            {
                await browser.SetThemeAsync(theme);
                await browser.GoAsync($"{Url}/settings/import");
                await browser.WaitForAsync("document.body.innerText.includes('Channel IDs')");

                // The invite for another server asks only to read, and adds no commands.
                await browser.WaitForAsync($"document.body.innerText.includes('{ReadInvite}')");
                var invite = await browser.EvalAsync<string>($"[...document.querySelectorAll('a')].find(a => a.innerText.includes('{ReadInvite}')).href");
                var asks = invite.Substring(invite.IndexOf("&permissions=", StringComparison.Ordinal) + 1);
                check.Saw($"import ({theme}): {ReadInvite}, asking {asks}");
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

        // The recorded channels' IDs typed in, checked, and previewed: the preview's summary line.
        private static async Task<string> PreviewImportAsync(HeadlessBrowser browser, WebCheck check, string theme)
        {
            const string Channels = "900000000000100001\n900000000000100002";

            await browser.RunAsync($"const e = document.querySelector('textarea[name=\"channelIds\"]'); Object.getOwnPropertyDescriptor(HTMLTextAreaElement.prototype, 'value').set.call(e, {JsonSerializer.Serialize(Channels)}); e.dispatchEvent(new Event('input', {{ bubbles: true }}))");
            await browser.WaitForAsync("document.body.innerText.split('the bot can read it').length > 2");
            await check.ShotAsync(browser, $"import-channels-{theme}");
            await browser.RunAsync("[...document.querySelectorAll('button')].find(b => b.innerText.trim() === 'Preview').click()");
            await browser.WaitForAsync("document.body.innerText.includes('Requests to import')");
            var summary = await browser.EvalAsync<string>("[...document.querySelectorAll('div')].map(d => d.innerText).find(t => t.startsWith('Read ')) ?? ''");
            check.Saw($"import ({theme}): {summary}");

            return summary;
        }

        // Confirms the last preview, then the Requests and Members pages show the imported statuses in both themes, and
        // previewing again finds nothing new.
        private async Task ConfirmImportAsync(HeadlessBrowser browser, WebCheck check)
        {
            await browser.RunAsync("[...document.querySelectorAll('button')].find(b => b.innerText.trim() === 'Confirm and import').click()");
            await browser.WaitForAsync("document.body.innerText.includes('Imported ')");
            check.Saw("import: " + await browser.EvalAsync<string>("[...document.querySelectorAll('div')].map(d => d.innerText).find(t => t.startsWith('Imported ')) ?? ''"));

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
                await browser.WaitForAsync("document.querySelector('table, [class*=\"Portal-empty\"]')");

                var statuses = await browser.EvalAsync<string>("[...new Set([...document.querySelectorAll('tr')].map(r => r.cells[1]?.innerText).filter(t => t?.startsWith('Imported, ')))].join(' | ')");

                if (string.IsNullOrEmpty(statuses))
                {
                    check.Saw($"members ({theme}): nobody imported on this stack");
                    continue;
                }

                check.Saw($"members ({theme}): {statuses}");
                await check.ShotAsync(browser, $"imported-members-{theme}");
                await MemberRowsAsync(browser, check, theme);

                await browser.RunAsync("[...document.querySelectorAll('tr')].find(r => r.cells[1]?.innerText.startsWith('Imported, ')).querySelector('a').click()");
                await browser.WaitForAsync("document.body.innerText.includes('Imported from')");
                var source = await browser.EvalAsync<string>("[...document.querySelectorAll('dt')].find(t => t.innerText === 'Imported from')?.nextElementSibling?.innerText ?? ''");
                check.Saw($"member page ({theme}): Imported from {source}");
                await check.ShotAsync(browser, $"imported-member-page-{theme}");
                source.Should().NotBeEmpty();
            }
        });

        // Every Members row has a picture and an @handle, and someone imported is first seen (Added) with their first
        // request; the list's end is photographed too, so every row is on a screenshot.
        private static async Task MemberRowsAsync(HeadlessBrowser browser, WebCheck check, string theme)
        {
            const string Rows = "[...document.querySelectorAll('tbody tr')]";
            const string Column = "(name => [...document.querySelectorAll('thead th')].findIndex(t => t.innerText.trim().startsWith(name)))";
            var rows = await browser.EvalAsync<int>($"{Rows}.length");
            var pictures = await browser.EvalAsync<int>($"{Rows}.filter(r => r.cells[0].querySelector('img')).length");
            var handles = await browser.EvalAsync<int>($"{Rows}.filter(r => r.cells[0].innerText.includes('@')).length");
            var notFirstSeen = await browser.EvalAsync<string>(
                $"(() => {{ const first = {Column}('First request'), added = {Column}('Added'); " +
                $"return {Rows}.filter(r => r.cells[1].innerText.startsWith('Imported, ') && r.cells[first].innerText !== r.cells[added].innerText)" +
                ".map(r => r.cells[0].querySelector('a').innerText).join(', '); })()");
            check.Saw($"members ({theme}): {rows} rows, {pictures} with a picture, {handles} with an @handle; imported rows whose Added isn't their first request: {(notFirstSeen.Length == 0 ? "none" : notFirstSeen)}");

            await browser.RunAsync($"{Rows}.pop().scrollIntoView()");
            await check.ShotAsync(browser, $"imported-members-end-{theme}");
            await browser.RunAsync($"{Rows}[0].scrollIntoView()");

            rows.Should().BePositive();
            pictures.Should().Be(rows, "everyone has a picture, Discord's default when they have none");
            handles.Should().Be(rows, "everyone shows their @handle");
            notFirstSeen.Should().BeEmpty();
        }

        // Requestarr's own settings pages load in both themes. QA and dev take their settings from the provisioning file,
        // so each page says so and its fields can't be changed. Connections is built like Sonarr's Indexers page: a card
        // for each connection under Discord, Media managers, Metadata and Bug reports, the "+" card opening the grouped Add
        // modal, a card opening its edit modal, and Options below; the old Discord and Sonarr and Radarr addresses land on
        // their sections.
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
            await browser.WaitForAsync("!!document.querySelector('[class*=\"AddConnectionModal-choice\"]')");
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
            await check.ShotAsync(browser, $"settings-connections-edit-{theme}");
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
            await browser.WaitForAsync("document.querySelectorAll('legend').length > 1");
            var sections = await browser.EvalAsync<string>("[...document.querySelectorAll('legend')].map(l => l.textContent.trim()).join(', ')");
            check.Saw($"{page} detail ({theme}): {sections}");
            sections.Should().Contain(page == "requests" ? "Timeline" : "History");
            await check.ShotAsync(browser, $"{page}-detail-{theme}");
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

        private static async Task CheckAsync(string name, Func<HeadlessBrowser, WebCheck, Task> body)
        {
            if (string.IsNullOrWhiteSpace(Url))
            {
                Assert.Ignore("QA_CHECKS_URL not set: run through docker/qa_checks.py run");
            }

            var check = new WebCheck(name, Environment.GetEnvironmentVariable("QA_CHECKS_DIR"));
            Exception failure = null;

            try
            {
                await using var browser = await HeadlessBrowser.StartAsync();
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
