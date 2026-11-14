using System;
using System.Net;
using System.Net.Http;
using System.Text.Json;
using System.Threading.Tasks;
using FluentAssertions;
using NUnit.Framework;
using NzbDrone.Test.Common.Categories;

namespace Requestarr.Core.Test.Qa.Web
{
    // Claude Code's web app checks for the QA checklist (docker/qa_checks.py run): a real browser against the dev stack
    // at the commit QA receives, signed in with a throwaway login, in the light and dark theme, never the QA server.
    // Each test is one check an item names ("the web app status page check") and writes what it saw and its screenshots
    // for Claude Code to look at against the item's "You should see". The dev stack shows the dev logo where QA shows
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
        });

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
