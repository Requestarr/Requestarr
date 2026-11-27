using System;
using System.Diagnostics;
using System.Linq;
using System.Text.Json;
using System.Threading.Tasks;
using FluentAssertions;
using NUnit.Framework;

namespace Requestarr.Core.Test.Qa.Web
{
    // A Sonarr or Radarr's edit window, on the throwaway Requestarr the run starts beside the dev stack (dev's connections
    // come from its provisioning file and can't be changed): two Sonarrs, one for all other shows and one for anime, and a
    // Radarr, saved in its web app against the dev stack's test Sonarr and Radarr.
    public partial class QaWebChecksFixture
    {
        private const string ThrowawayContainer = "requestarr-dev-throwaway";
        private const string DevNetwork = "requestarr-dev_default";
        private const string ModalGroups = "[...document.querySelectorAll('[class*=\"ModalBody\"] [class*=\"FormGroup-group\"]')]";
        private const string FooterParts = "[...document.querySelector('[class*=\"ModalFooter-modalFooter\"]').children].map(e => e.innerText.trim() || (e.className.includes('AdvancedSettings') || e.querySelector('[class*=\"AdvancedSettings\"]') ? 'the Show Advanced gear' : e.title))";
        private const string AlertText = "[...document.querySelectorAll('[class*=\"PageContentBody\"] [class*=\"Alert-\"]')].map(a => a.innerText.trim()).join(' | ')";

        // Asked before regular show requests stop, in a window on top of the edit window.
        private const string AllOtherShowsWarning = "Regular show requests will stop until another Sonarr is set to take all other shows.";
        private const string WarningWindow = $"{Windows}.at(-1)";

        // The window's words as the owner reviewed them (the bot setup's before-and-after table, with each app's own name
        // where the window names it and the routing by content's purpose): each field's label and a part of its help.
        private static readonly (string Label, string Help)[] SonarrWords =
        {
            ("Name", "What admins see on cards and requests. No two share a name."),
            ("Use this Sonarr", "Off: no new requests are sent here. Requests it already handled keep their history."),
            ("What this Sonarr is for", "every request this Sonarr can take that no other Sonarr is for."),
            ("Address", "Not localhost: inside Requestarr's container, that means Requestarr itself."),
            ("Address for links", "Leave it empty if it's the same as the address above."),
            ("API key", "From Sonarr's Settings, then General."),
            ("Quality profile", "The profile new requests are added with. The list loads once the address and API key work."),
            ("Root folder", "Where new requests are saved. The list loads once the address and API key work."),
            ("Series type for new shows", "Anime is detected for you and always added as Anime."),
            ("Default access for new members", "What someone you add gets for this type. You can change it for each person later."),
            ("Ask members: subtitles or English dub", "Off: anime is added without asking, with your quality profile only."),
            ("If two people want different versions of the same show", "Which version Sonarr keeps for that show."),
            ("English dub: release must contain", "Words an English dub release needs."),
            ("English dub: release must not contain", "Words that rule out an English dub release."),
            ("Subtitled: release must contain", "Words a subtitled release needs."),
            ("Subtitled: release must not contain", "Words that rule out a subtitled release."),
        };

        private static readonly (string Label, string Help)[] RadarrWords =
        {
            ("Name", "What admins see on cards and requests. No two share a name."),
            ("Use this Radarr", "Off: no new requests are sent here. Requests it already handled keep their history."),
            ("Address", "Not localhost: inside Requestarr's container, that means Requestarr itself."),
            ("Address for links", "Leave it empty if it's the same as the address above."),
            ("API key", "From Radarr's Settings, then General."),
            ("Quality profile", "The profile new requests are added with. The list loads once the address and API key work."),
            ("Root folder", "Where new requests are saved. The list loads once the address and API key work."),
            ("Default access for new members", "What someone you add gets for this type. You can change it for each person later."),
        };

        private static string ThrowawayUrl => Environment.GetEnvironmentVariable("QA_CHECKS_THROWAWAY_URL")?.TrimEnd('/');

        // Each window's labels and help, footer and Updates line; Test All Connections when every connection passes and
        // when they can't be reached; and the warning before regular show requests stop, on Save and on Delete.
        [Test]
        public Task connection_windows() => CheckAsync(nameof(connection_windows), async (browser, check) =>
        {
            ThrowawayUrl.Should().NotBeNullOrEmpty("the run starts a throwaway Requestarr for this check");
            await browser.GoAsync($"{ThrowawayUrl}/settings/connections");
            await browser.WaitForAsync("!!window.Requestarr?.apiKey");
            await AddConnectionsAsync(browser, check);

            foreach (var theme in Themes)
            {
                await browser.SetThemeAsync(theme);
                await OpenConnectionsAsync(browser);
                await EditWindowWordsAsync(browser, check, theme, "Sonarr", SonarrWords);
                await EditWindowWordsAsync(browser, check, theme, "Radarr", RadarrWords);
            }

            await TestAllAsync(browser, check);
            await AllOtherShowsWarningAsync(browser, check);
        });

        // Saved through the web app's own API, as its Save does, with the test Sonarr's and Radarr's keys (never written
        // to the evidence).
        private static async Task AddConnectionsAsync(HeadlessBrowser browser, WebCheck check)
        {
            var sonarrKey = JsonSerializer.Serialize(Environment.GetEnvironmentVariable("SONARR_TEST_API_KEY"));
            var radarrKey = JsonSerializer.Serialize(Environment.GetEnvironmentVariable("RADARR_TEST_API_KEY"));
            var added = await browser.EvalAsync<string[]>(
                "(async () => { const h = { 'X-Api-Key': window.Requestarr.apiKey, 'Content-Type': 'application/json' }; " +
                "const schema = await (await fetch('/api/v1/mediamanager/schema', { headers: h })).json(); " +
                "const add = async (app, name, url, key, purpose) => { const s = structuredClone(schema.find(x => x.implementation === app)); s.name = name; s.enable = true; " +
                "const set = (n, v) => { s.fields.find(f => f.name === n).value = v; }; set('url', url); set('apiKey', key); set('purposeChoice', purpose); " +
                "const r = await fetch('/api/v1/mediamanager', { method: 'POST', headers: h, body: JSON.stringify(s) }); return `${name}: ${r.status}${r.ok ? '' : ' ' + (await r.json()).map(e => e.errorMessage).join('; ')}`; }; " +
                $"return [await add('Sonarr', 'Sonarr', 'http://sonarr-test:8989', {sonarrKey}, 0), await add('Sonarr', 'Sonarr (Anime)', 'http://sonarr-test:8989', {sonarrKey}, 1), " +
                $"await add('Radarr', 'Radarr', 'http://radarr-test:7878', {radarrKey}, 0)]; }})()");
            check.Saw($"throwaway Requestarr: saved {string.Join(", ", added)}");
            added.Should().OnlyContain(a => a.EndsWith(": 201", StringComparison.Ordinal), "each connection saves against the test Sonarr and Radarr");
        }

        private static async Task OpenConnectionsAsync(HeadlessBrowser browser)
        {
            await browser.GoAsync($"{ThrowawayUrl}/settings/connections");
            await browser.WaitForAsync($"{Cards}.filter(c => c.innerText.includes('Sonarr') || c.innerText.includes('Radarr')).length >= 3");
        }

        private static string CardNamed(string name) =>
            $"{Cards}.find(c => c.querySelector('[class*=\"ConnectionCard-name\"]')?.innerText.trim() === '{name}')";

        private static async Task OpenCardAsync(HeadlessBrowser browser, string name)
        {
            await browser.RunAsync($"{CardNamed(name)}.click()");
            await browser.WaitForAsync("!!document.querySelector('[class*=\"ModalFooter-modalFooter\"]')");
        }

        private static async Task ShowAdvancedAsync(HeadlessBrowser browser)
        {
            const string Gear = "document.querySelector('[class*=\"ModalFooter-modalFooter\"] [class*=\"AdvancedSettingsButton\"]')";

            if (!await browser.EvalAsync<bool>($"{ModalText}.includes('English dub: release must not contain') || {ModalText}.includes('Address for links')"))
            {
                await browser.RunAsync($"({Gear}.closest('button, a') ?? {Gear}).click()");
                await browser.WaitForAsync($"{ModalText}.includes('Address for links')");
            }
        }

        // With Show Advanced on: every field's label and help as reviewed, no other app named, the footer's order and
        // the Updates line.
        private static async Task EditWindowWordsAsync(HeadlessBrowser browser, WebCheck check, string theme, string name, (string Label, string Help)[] words)
        {
            await OpenCardAsync(browser, name);
            await ShowAdvancedAsync(browser);
            var groups = await browser.EvalAsync<string[]>($"{ModalGroups}.map(g => g.innerText.replace(/\\s+/g, ' ').trim())");
            var footer = await browser.EvalAsync<string[]>(FooterParts);
            var updates = await browser.EvalAsync<string>("[...document.querySelectorAll('[class*=\"ModalBody\"] *')].map(e => e.innerText?.trim() ?? '').find(t => t.startsWith('Updates:') && !t.includes('\\n')) ?? ''");
            var all = await browser.EvalAsync<string>(ModalText);
            check.Saw($"{name} window ({theme}), Show Advanced on: {string.Join(" | ", groups)}");
            check.Saw($"{name} window ({theme}): footer {string.Join(", ", footer)}; {updates}");
            await check.ShotAsync(browser, $"{name.ToLowerInvariant()}-window-{theme}");

            foreach (var (label, help) in words)
            {
                groups.Should().Contain(g => g.StartsWith(label + " ", StringComparison.Ordinal) && g.Contains(help), $"the {name} window's {label} reads as reviewed");
            }

            var other = name == "Sonarr" ? "Radarr" : "Sonarr";
            all.Should().NotContain(other, "a window names only its own app").And.NotContain("Sonarr and Radarr").And.NotContain("Sonarr or Radarr");
            footer.Should().Equal("Delete", "the Show Advanced gear", "Test", "Cancel", "Save");
            updates.Should().MatchRegex(@"^Updates: (right away|every \d+ minutes, (because|until|as) .+)$", "it says right away or on a timer, with its reason");

            await AnswerAsync(browser, "Cancel", true);
        }

        // Every connection passes; then, with the throwaway cut off from the test Sonarr and Radarr, each is named with
        // why it failed, and no app is named where the card's own name isn't.
        private static async Task TestAllAsync(HeadlessBrowser browser, WebCheck check)
        {
            const string Result = "[...document.querySelectorAll('[class*=\"PageContentBody\"] [class*=\"Alert-\"]')].find(a => /passed its test|: /.test(a.innerText))";
            await OpenConnectionsAsync(browser);
            await PressToolbarAsync(browser, "Test All Connections");
            await browser.WaitForAsync($"{AlertText}.includes('Every connection passed its test.')");
            var passed = await browser.EvalAsync<string>(AlertText);
            check.Saw($"Test All Connections: {passed}");
            await check.ShotAsync(browser, "test-all-passed");
            passed.Should().NotContain("Sonarr").And.NotContain("Radarr");

            Docker("network", "disconnect", DevNetwork, ThrowawayContainer);

            try
            {
                await OpenConnectionsAsync(browser);
                await PressToolbarAsync(browser, "Test All Connections");
                await browser.WaitForAsync($"!!{Result} && !{Result}.innerText.includes('passed its test')", TimeSpan.FromMinutes(3));
                var failed = await browser.EvalAsync<string[]>($"[...{Result}.querySelectorAll('li')].map(l => l.innerText.trim())");
                check.Saw($"Test All Connections, cut off: {string.Join(" | ", failed)}");
                await check.ShotAsync(browser, "test-all-failed");
                failed.Should().NotBeEmpty();
                failed.Should().OnlyContain(f => new[] { "Sonarr: ", "Sonarr (Anime): ", "Radarr: " }.Any(n => f.StartsWith(n, StringComparison.Ordinal)) && f.Length > f.IndexOf(": ", StringComparison.Ordinal) + 2,
                    "each failing connection is named by its card, with its reason");
            }
            finally
            {
                Docker("network", "connect", DevNetwork, ThrowawayContainer);
            }
        }

        // Turning off the Sonarr for all other shows warns first, on Save and on Delete; confirming it pauses regular
        // show requests (the queue says so).
        private static async Task AllOtherShowsWarningAsync(HeadlessBrowser browser, WebCheck check)
        {
            const string UseThis = "document.querySelector('[class*=\"ModalBody\"] input[name=\"enable\"]')";
            await OpenConnectionsAsync(browser);
            await OpenCardAsync(browser, "Sonarr");
            await browser.RunAsync($"{UseThis}.click()");
            await browser.WaitForAsync($"!{UseThis}.checked");
            await WarnsThenCancelAsync(browser, check, "Save", "Use this Sonarr off, then Save", "save-warning");
            await WarnsThenCancelAsync(browser, check, "Delete", "Delete", "delete-warning");

            // Saved after all: the confirm's Save turns it off, and regular show requests wait.
            await AnswerAsync(browser, "Save", true);
            await browser.WaitForAsync($"{WarningWindow}.innerText.includes('{AllOtherShowsWarning}')");
            await PressInWarningAsync(browser, "Save");
            await browser.WaitForAsync("!document.querySelector('[class*=\"ModalFooter-modalFooter\"]')", TimeSpan.FromMinutes(1));
            await browser.GoAsync($"{ThrowawayUrl}/queue");
            await browser.WaitForAsync("document.body.innerText.includes('Requests paused')");
            var queue = await browser.EvalAsync<string>("[...document.querySelectorAll('tr')].map(r => r.innerText.replace(/\\s+/g, ' ').trim()).find(t => t.includes('Requests paused')) ?? ''");
            check.Saw($"confirmed, the queue: {queue}");
            await check.ShotAsync(browser, "requests-paused");
            queue.Should().NotBeEmpty("regular show requests are paused until another Sonarr takes all other shows");
        }

        // The window's button warns before regular show requests stop; the warning is read, then cancelled.
        private static async Task WarnsThenCancelAsync(HeadlessBrowser browser, WebCheck check, string button, string pressed, string shot)
        {
            await AnswerAsync(browser, button, true);
            await browser.WaitForAsync($"{WarningWindow}.innerText.includes('{AllOtherShowsWarning}')");
            check.Saw($"{pressed}: {await browser.EvalAsync<string>($"{WarningWindow}.innerText.replace(/\\s+/g, ' ')")}");
            await check.ShotAsync(browser, shot);
            await PressInWarningAsync(browser, "Cancel");
            await browser.WaitForAsync($"!{WarningWindow}.innerText.includes('{AllOtherShowsWarning}')");
        }

        private static Task PressInWarningAsync(HeadlessBrowser browser, string label) =>
            browser.RunAsync($"[...{WarningWindow}.querySelectorAll('button')].find(b => b.innerText.trim() === '{label}').click()");

        // A docker command the check needs (cutting the throwaway off its network and back): it fails the check when docker
        // does.
        private static void Docker(params string[] args)
        {
            var start = new ProcessStartInfo("docker") { RedirectStandardError = true, RedirectStandardOutput = true };

            foreach (var arg in args)
            {
                start.ArgumentList.Add(arg);
            }

            using var process = Process.Start(start);
            process.WaitForExit();
            process.ExitCode.Should().Be(0, $"docker {string.Join(" ", args)}: {process.StandardError.ReadToEnd()}");
        }
    }
}
