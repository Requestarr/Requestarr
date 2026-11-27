using System;
using System.Linq;
using System.Text.Json;
using System.Threading.Tasks;
using FluentAssertions;
using NUnit.Framework;

namespace Requestarr.Core.Test.Qa.Web
{
    // The server cards on Import from Requestrr. The dev bot's real import-only server (the original server) is opened
    // and read, never left: taking the bot out would need the owner to add it back. A made-up server the run seeds among
    // the servers Requestarr remembers (qa_checks.py seed_check_rows) stands for one that removed the bot: Requestarr lists
    // it as not reachable, with its own Reconnect link, and deletes it for real. The bot joining a server can't be done by
    // a check without Discord's approval page, so a join is told to the page as the server tells it (the setup's server
    // list read again on its live update, with the made-up server in it as joined); Requestarr's own side of a join and a
    // leave is DiscordServersFixture's.
    public partial class QaWebChecksFixture
    {
        private const string MadeUpServer = "900000000000300001";
        private const string MadeUpName = "Automated check server";

        // The open windows, a confirmation on top of the window that asked for it.
        private const string Windows = "[...document.querySelectorAll('[class*=\"ModalContent-modalContent\"]')]";
        private const string WindowText = $"({Windows}.at(-1)?.innerText ?? '').replace(/\\s+/g, ' ').trim()";

        // The page's own requests answer as the server would once Discord told it the bot joined the made-up server
        // (window.__qaServer set), and the page reads the list again, as its live update does.
        private const string FakeServer = @"(() => {
            if (!window.__qaPatched) {
                window.__qaPatched = true;
                const open = XMLHttpRequest.prototype.open;
                XMLHttpRequest.prototype.open = function (method, url, ...rest) {
                    if (/\/requestarr\/setup(\?|$)/.test(url)) {
                        this.addEventListener('readystatechange', () => {
                            if (this.readyState !== 4 || !window.__qaServer) { return; }
                            const body = JSON.parse(this.responseText);
                            body.servers = body.servers.filter(s => s.id !== window.__qaServer.id).concat([window.__qaServer]);
                            const text = JSON.stringify(body);
                            Object.defineProperty(this, 'responseText', { value: text });
                            Object.defineProperty(this, 'response', { value: text });
                        });
                    }
                    return open.call(this, method, url, ...rest);
                };
            }
            const root = document.getElementById('root');
            const key = Object.keys(root).find(k => k.startsWith('__reactContainer'));
            const stack = [root[key]];
            while (stack.length) {
                const fiber = stack.pop();
                if (!fiber) { continue; }
                if (fiber.memoizedProps?.store?.dispatch) { window.__qaStore = fiber.memoizedProps.store; break; }
                stack.push(fiber.sibling, fiber.child);
            }
            return !!window.__qaStore;
        })()";

        // The setup as the server gives it now, and its server list.
        private const string Setup = "(await (await fetch('/api/v1/requestarr/setup', { headers: { 'X-Api-Key': window.Requestarr.apiKey } })).json())";
        private const string SetupServers = $"(async () => {Setup}.servers)()";

        private const string ReadAgain = "window.__qaStore.dispatch({ type: 'app/setAppValue', payload: { setupChanged: Date.now() } })";

        [Test]
        public Task server_cards() => CheckAsync(nameof(server_cards), async (browser, check) =>
        {
            await SignInAsync(browser);
            await OpenImportServersAsync(browser);
            var original = await OriginalServerAsync(browser, check);
            await OriginalServerWindowAsync(browser, check, original);
            await AddedServerAsync(browser, check);
            await RemovedServerAsync(browser, check);
            await ReconnectedServerAsync(browser, check);
            await DeletedServerAsync(browser, check);

            // The bot still in the original server, read back.
            var id = original.GetProperty("id").GetString();
            var still = (await browser.EvalAsync<JsonElement>(SetupServers)).EnumerateArray().First(s => s.GetProperty("id").GetString() == id);
            check.Saw($"afterwards, the original server: the bot is in it {still.GetProperty("joined").GetBoolean()}, reachable {still.GetProperty("reachable").GetBoolean()}");
            still.GetProperty("joined").GetBoolean().Should().BeTrue("the bot is still in the original server");
        });

        private static async Task OpenImportServersAsync(HeadlessBrowser browser)
        {
            await browser.GoAsync($"{Url}/settings/import");
            await browser.WaitForAsync($"{Cards}.length > 0");
            (await browser.EvalAsync<bool>(FakeServer)).Should().BeTrue("the page's store is found");
        }

        // The dev bot's import-only server: on the list and connected (a server the bot is in has no Reconnect).
        private static async Task<JsonElement> OriginalServerAsync(HeadlessBrowser browser, WebCheck check)
        {
            var servers = await browser.EvalAsync<JsonElement>(SetupServers);
            var original = servers.EnumerateArray().Where(s => !s.GetProperty("isOurs").GetBoolean() && s.GetProperty("joined").GetBoolean()).Select(s => s.Clone()).ToList();
            original.Should().ContainSingle("the dev bot is in the original server, import only");
            var cards = await browser.EvalAsync<string[]>($"{Cards}.map(c => c.innerText.replace(/\\s+/g, ' ').trim())");
            check.Saw($"Import's server cards: {string.Join(" | ", cards)}");
            await check.ShotAsync(browser, "server-cards");
            cards.Should().Contain(c => c.StartsWith(original[0].GetProperty("name").GetString() + " Import only", StringComparison.Ordinal) && c.Contains("Connected"));

            return original[0];
        }

        // Its window: what the bot does there, its channels only, and taking the bot out asked first (cancelled here).
        private static async Task OriginalServerWindowAsync(HeadlessBrowser browser, WebCheck check, JsonElement original)
        {
            var name = original.GetProperty("name").GetString();
            await browser.RunAsync($"{CardNamed(name)}.click()");
            await browser.WaitForAsync($"{WindowText}.startsWith({JsonSerializer.Serialize(name)}) && !!document.querySelector('[class*=\"ModalFooter\"]')");
            check.Saw($"its window: {await browser.EvalAsync<string>(WindowText)}");
            await check.ShotAsync(browser, "original-server-window");

            await AnswerAsync(browser, "Remove the bot from this server", true);
            await browser.WaitForAsync($"{WindowText}.includes('Remove the bot from this server') && {Windows}.length === 2");
            var confirm = await browser.EvalAsync<string>(WindowText);
            check.Saw($"Remove the bot from this server asks first: {confirm}");
            await check.ShotAsync(browser, "remove-bot-confirm");
            confirm.Should().Contain(name);
            await CancelConfirmAsync(browser);

            await browser.RunAsync("[...document.querySelectorAll('[class*=\"ModalFooter\"] a, [class*=\"ModalFooter\"] button')].find(b => b.innerText.trim().includes('channels')).click()");
            await browser.WaitForAsync($"location.search.includes('server={original.GetProperty("id").GetString()}') && !!document.querySelector('[class*=\"ImportChannels-serverName\"]')");
            var shown = await browser.EvalAsync<string[]>("[...new Set([...document.querySelectorAll('[class*=\"ImportChannels-serverName\"]')].map(e => e.innerText.trim()))]");
            check.Saw($"its channels: the list shows {string.Join(", ", shown)}");
            await check.ShotAsync(browser, "original-server-channels");
            shown.Should().Equal(name);
        }

        // The + card's invite opens, and the window closes once the bot is in: the made-up server joins.
        private static async Task AddedServerAsync(HeadlessBrowser browser, WebCheck check)
        {
            const string InvitePage = "Open Discord's invite page";
            await OpenImportServersAsync(browser);
            await browser.RunAsync("window.open = (url) => { window.invited = url; }; document.querySelector('[class*=\"ConnectionCard-addConnection\"]').click()");
            await browser.WaitForAsync($"[...document.querySelectorAll('button')].some(b => b.innerText.trim() === \"{InvitePage}\")");
            await browser.RunAsync($"[...document.querySelectorAll('button')].find(b => b.innerText.trim() === \"{InvitePage}\").click()");
            await browser.WaitForAsync("document.body.innerText.includes('Waiting for the bot to join')");
            await check.ShotAsync(browser, "add-waiting");

            await JoinedAsync(browser, true);
            await browser.WaitForAsync($"!document.body.innerText.includes('Waiting for the bot to join') && {Windows}.length === 0");
            var card = await browser.EvalAsync<string>($"{CardNamed(MadeUpName)}?.innerText.replace(/\\s+/g, ' ').trim() ?? ''");
            check.Saw($"the bot joins the made-up server: the Add window closed; its card: {card}");
            await check.ShotAsync(browser, "add-joined");
            card.Should().Contain("Connected");
        }

        // Removed from it (Requestarr remembers it, the bot isn't in it): the card stays and says Not reachable, and its
        // window offers Reconnect and Delete.
        private static async Task RemovedServerAsync(HeadlessBrowser browser, WebCheck check)
        {
            await JoinedAsync(browser, false);
            await browser.WaitForAsync($"{CardNamed(MadeUpName)}?.innerText.includes('Not reachable')");
            var card = await browser.EvalAsync<string>($"{CardNamed(MadeUpName)}.innerText.replace(/\\s+/g, ' ').trim()");
            check.Saw($"the made-up server removes the bot: its card stays: {card}");
            await check.ShotAsync(browser, "removed-card");

            await browser.RunAsync($"{CardNamed(MadeUpName)}.click()");
            await browser.WaitForAsync($"{ModalButton}.some(b => b.innerText.trim() === 'Delete')");
            var window = await browser.EvalAsync<string>(WindowText);
            check.Saw($"its window: {window}");
            window.Should().Contain("Reconnect");
        }

        // Reconnect opens Requestarr's own link for it, Discord's invite with that server picked, and the window closes
        // once the bot is back.
        private static async Task ReconnectedServerAsync(HeadlessBrowser browser, WebCheck check)
        {
            var link = (await browser.EvalAsync<JsonElement>(SetupServers)).EnumerateArray().Single(s => s.GetProperty("id").GetString() == MadeUpServer).GetProperty("reconnectUrl").GetString();
            await browser.RunAsync("window.invited = undefined; [...document.querySelectorAll('[class*=\"ModalBody\"] button')].find(b => b.innerText.trim() === 'Reconnect').click()");
            await browser.WaitForAsync("!!window.invited && document.body.innerText.includes('Waiting for the bot to join')");
            var invite = await browser.EvalAsync<string>("window.invited");
            check.Saw($"Reconnect opens Requestarr's link for it, Discord's invite ending {invite[invite.IndexOf("&scope=", StringComparison.Ordinal)..]}");
            await check.ShotAsync(browser, "reconnect-waiting");
            invite.Should().Be(link).And.Contain($"guild_id={MadeUpServer}").And.Contain("disable_guild_select=true");

            await JoinedAsync(browser, true);
            await browser.WaitForAsync($"{Windows}.length === 0");
            var card = await browser.EvalAsync<string>($"{CardNamed(MadeUpName)}.innerText.replace(/\\s+/g, ' ').trim()");
            check.Saw($"the bot is back: the window closed; its card: {card}");
            await check.ShotAsync(browser, "rejoined-card");
            card.Should().Contain("Connected");
        }

        // Removed again, its card is deleted: asked first, then gone from Requestarr's list.
        private static async Task DeletedServerAsync(HeadlessBrowser browser, WebCheck check)
        {
            await JoinedAsync(browser, false);
            await browser.WaitForAsync($"{CardNamed(MadeUpName)}?.innerText.includes('Not reachable')");
            await browser.RunAsync($"{CardNamed(MadeUpName)}.click()");
            await browser.WaitForAsync($"{ModalButton}.some(b => b.innerText.trim() === 'Delete')");
            await AnswerAsync(browser, "Delete", true);
            await browser.WaitForAsync($"{Windows}.length === 2");
            var confirm = await browser.EvalAsync<string>(WindowText);
            check.Saw($"Delete asks first: {confirm}");
            await check.ShotAsync(browser, "removed-delete-confirm");
            confirm.Should().Contain(MadeUpName);
            await browser.RunAsync($"[...{Windows}.at(-1).querySelectorAll('button')].find(b => b.innerText.trim() === 'Delete').click()");
            await browser.WaitForAsync($"!{CardNamed(MadeUpName)}");
            var listed = (await browser.EvalAsync<JsonElement>(SetupServers)).EnumerateArray().Any(s => s.GetProperty("id").GetString() == MadeUpServer);
            check.Saw($"deleted: its card is gone, and Requestarr lists it {(listed ? "still" : "no more")}");
            await check.ShotAsync(browser, "deleted");
            listed.Should().BeFalse("Delete forgets the server");
        }

        // The made-up server as the page next reads it: joined (the bot in it), or as Requestarr remembers it (removed).
        private static Task JoinedAsync(HeadlessBrowser browser, bool joined) =>
            browser.RunAsync(joined
                ? $"window.__qaServer = {{ id: '{MadeUpServer}', name: '{MadeUpName}', isOurs: false, reachable: true, joined: true }}; {ReadAgain}"
                : $"window.__qaServer = undefined; {ReadAgain}");

        // The confirmation on top cancelled, back to the window that asked for it.
        private static async Task CancelConfirmAsync(HeadlessBrowser browser)
        {
            await browser.RunAsync($"[...{Windows}.at(-1).querySelectorAll('button')].find(b => b.innerText.trim() === 'Cancel').click()");
            await browser.WaitForAsync($"{Windows}.length === 1");
        }
    }
}
