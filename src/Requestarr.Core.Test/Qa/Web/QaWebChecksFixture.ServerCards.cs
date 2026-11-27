using System;
using System.Linq;
using System.Text.Json;
using System.Threading.Tasks;
using FluentAssertions;
using NUnit.Framework;

namespace Requestarr.Core.Test.Qa.Web
{
    // The server cards on Import from Requestrr. The dev bot's real import-only server (the original server) is opened
    // and read, never left: taking the bot out would need the owner to add it back. Adding the bot to a server, and that
    // server removing it and taking it back, happen to a made-up server: the page is told of it as the server tells it
    // after Discord's join or leave (the setup's server list read again on its live update), so only Discord's own
    // approval page, where a person adds a bot, isn't exercised. Requestarr's own side of a join and a leave is
    // DiscordServersFixture's.
    public partial class QaWebChecksFixture
    {
        private const string MadeUpServer = "900000000000300001";
        private const string MadeUpName = "Automated check server";
        private const string ServerCards = "[...document.querySelectorAll('[class*=\"ConnectionCard-connection\"]:not([class*=\"ConnectionCard-addConnection\"])')]";
        private const string WindowText = "([...document.querySelectorAll('[class*=\"ModalContent-modalContent\"]')].at(-1)?.innerText ?? '').replace(/\\s+/g, ' ').trim()";

        // The page's own requests answer as the server would once Discord told it of the made-up server's join or leave
        // (window.__qaServer: undefined, joined or removed), and the page reads the list again, as its live update does.
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

        // The server list as the server gives it now.
        private const string SetupServers = "(async () => (await (await fetch('/api/v1/requestarr/setup', { headers: { 'X-Api-Key': window.Requestarr.apiKey } })).json()).servers)()";

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
            await RejoinedServerAsync(browser, check);

            // The made-up server gone again, and the bot still in the original server, read back.
            await browser.RunAsync($"window.__qaServer = undefined; {ReadAgain}");
            await browser.WaitForAsync($"!{ServerCards}.some(c => c.innerText.includes('{MadeUpName}'))");
            var id = original.GetProperty("id").GetString();
            var still = (await browser.EvalAsync<JsonElement>(SetupServers)).EnumerateArray().First(s => s.GetProperty("id").GetString() == id);
            check.Saw($"afterwards, the original server: the bot is in it {still.GetProperty("joined").GetBoolean()}, reachable {still.GetProperty("reachable").GetBoolean()}");
            still.GetProperty("joined").GetBoolean().Should().BeTrue("the bot is still in the original server");
        });

        private static async Task OpenImportServersAsync(HeadlessBrowser browser)
        {
            await browser.GoAsync($"{Url}/settings/import");
            await browser.WaitForAsync($"{ServerCards}.length > 0");
            (await browser.EvalAsync<bool>(FakeServer)).Should().BeTrue("the page's store is found");
        }

        // The dev bot's import-only server: on the list and connected (a server the bot is in has no Reconnect).
        private static async Task<JsonElement> OriginalServerAsync(HeadlessBrowser browser, WebCheck check)
        {
            var servers = await browser.EvalAsync<JsonElement>(SetupServers);
            JsonElement? original = null;

            foreach (var server in servers.EnumerateArray())
            {
                if (!server.GetProperty("isOurs").GetBoolean() && server.GetProperty("joined").GetBoolean())
                {
                    original = server.Clone();
                }
            }

            original.Should().NotBeNull("the dev bot is in the original server, import only");
            var cards = await browser.EvalAsync<string[]>($"{ServerCards}.map(c => c.innerText.replace(/\\s+/g, ' ').trim())");
            check.Saw($"Import's server cards: {string.Join(" | ", cards)}");
            await check.ShotAsync(browser, "server-cards");
            cards.Should().Contain(c => c.StartsWith(original.Value.GetProperty("name").GetString() + " Import only", StringComparison.Ordinal) && c.Contains("Connected"));

            return original.Value;
        }

        // Its window: what the bot does there, its channels only, and taking the bot out asked first (cancelled here).
        private static async Task OriginalServerWindowAsync(HeadlessBrowser browser, WebCheck check, JsonElement original)
        {
            var name = original.GetProperty("name").GetString();
            await browser.RunAsync($"{ServerCards}.find(c => c.innerText.includes({JsonSerializer.Serialize(name)})).click()");
            await browser.WaitForAsync("!!document.querySelector('[class*=\"ModalFooter\"]')");
            check.Saw($"its window: {await browser.EvalAsync<string>(WindowText)}");
            await check.ShotAsync(browser, "original-server-window");

            await browser.RunAsync("[...document.querySelectorAll('[class*=\"ModalFooter\"] button')].find(b => b.innerText.trim() === 'Remove the bot from this server').click()");
            await browser.WaitForAsync($"{WindowText}.includes('Remove the bot from this server') && [...document.querySelectorAll('[class*=\"ModalContent-modalContent\"]')].length === 2");
            var confirm = await browser.EvalAsync<string>(WindowText);
            check.Saw($"Remove the bot from this server asks first: {confirm}");
            await check.ShotAsync(browser, "remove-bot-confirm");
            confirm.Should().Contain(name);
            await browser.RunAsync("[...[...document.querySelectorAll('[class*=\"ModalContent-modalContent\"]')].at(-1).querySelectorAll('button')].find(b => b.innerText.trim() === 'Cancel').click()");
            await browser.WaitForAsync("[...document.querySelectorAll('[class*=\"ModalContent-modalContent\"]')].length === 1");

            await browser.RunAsync("[...document.querySelectorAll('[class*=\"ModalFooter\"] a, [class*=\"ModalFooter\"] button')].find(b => b.innerText.trim().includes('channels')).click()");
            await browser.WaitForAsync($"location.search.includes('server={original.GetProperty("id").GetString()}') && !!document.querySelector('[class*=\"ImportChannels-serverName\"]')");
            var shown = await browser.EvalAsync<string[]>("[...new Set([...document.querySelectorAll('[class*=\"ImportChannels-serverName\"]')].map(e => e.innerText.trim()))]");
            check.Saw($"its channels: the list shows {string.Join(", ", shown)}");
            await check.ShotAsync(browser, "original-server-channels");
            shown.Should().Equal(name);
        }

        // The + card's invite opens, and the window closes once the bot is in (the made-up server joins).
        private static async Task AddedServerAsync(HeadlessBrowser browser, WebCheck check)
        {
            const string InvitePage = "Open Discord's invite page";
            await OpenImportServersAsync(browser);
            await browser.RunAsync("window.open = (url) => { window.invited = url; }; document.querySelector('[class*=\"ConnectionCard-addConnection\"]').click()");
            await browser.WaitForAsync($"[...document.querySelectorAll('button')].some(b => b.innerText.trim() === \"{InvitePage}\")");
            await browser.RunAsync($"[...document.querySelectorAll('button')].find(b => b.innerText.trim() === \"{InvitePage}\").click()");
            await browser.WaitForAsync("document.body.innerText.includes('Waiting for the bot to join')");
            await check.ShotAsync(browser, "add-waiting");

            // Its Reconnect link as the server builds one (Setup.ReconnectUrl): the read-only invite with the server picked.
            var readOnly = await browser.EvalAsync<string>("(async () => (await (await fetch('/api/v1/requestarr/setup', { headers: { 'X-Api-Key': window.Requestarr.apiKey } })).json()).readOnlyInviteUrl)()");
            var reconnect = $"{readOnly}&guild_id={MadeUpServer}&disable_guild_select=true";
            await browser.RunAsync($"window.__qaServer = {{ id: '{MadeUpServer}', name: '{MadeUpName}', isOurs: false, reachable: true, joined: true, reconnectUrl: {JsonSerializer.Serialize(reconnect)} }}; {ReadAgain}");
            await browser.WaitForAsync("!document.body.innerText.includes('Waiting for the bot to join') && !document.querySelector('[class*=\"ModalContent-modalContent\"]')");
            var card = await browser.EvalAsync<string>($"{ServerCards}.find(c => c.innerText.includes('{MadeUpName}'))?.innerText.replace(/\\s+/g, ' ').trim() ?? ''");
            check.Saw($"the bot joins the made-up server: the Add window closed; its card: {card}");
            await check.ShotAsync(browser, "add-joined");
            card.Should().Contain("Connected");
        }

        // Removed from it: the card stays, says Not reachable, and its window offers Reconnect and Delete (asked first).
        private static async Task RemovedServerAsync(HeadlessBrowser browser, WebCheck check)
        {
            await browser.RunAsync($"Object.assign(window.__qaServer, {{ reachable: false, joined: false }}); {ReadAgain}");
            await browser.WaitForAsync($"{ServerCards}.find(c => c.innerText.includes('{MadeUpName}'))?.innerText.includes('Not reachable')");
            var card = await browser.EvalAsync<string>($"{ServerCards}.find(c => c.innerText.includes('{MadeUpName}')).innerText.replace(/\\s+/g, ' ').trim()");
            check.Saw($"the made-up server removes the bot: its card stays: {card}");
            await check.ShotAsync(browser, "removed-card");

            await browser.RunAsync($"{ServerCards}.find(c => c.innerText.includes('{MadeUpName}')).click()");
            await browser.WaitForAsync("[...document.querySelectorAll('[class*=\"ModalFooter\"] button')].some(b => b.innerText.trim() === 'Delete')");
            var window = await browser.EvalAsync<string>(WindowText);
            check.Saw($"its window: {window}");
            window.Should().Contain("Reconnect");
            await browser.RunAsync("[...document.querySelectorAll('[class*=\"ModalFooter\"] button')].find(b => b.innerText.trim() === 'Delete').click()");
            await browser.WaitForAsync("[...document.querySelectorAll('[class*=\"ModalContent-modalContent\"]')].length === 2");
            var confirm = await browser.EvalAsync<string>(WindowText);
            check.Saw($"Delete asks first: {confirm}");
            await check.ShotAsync(browser, "removed-delete-confirm");
            confirm.Should().Contain(MadeUpName);
            await browser.RunAsync("[...[...document.querySelectorAll('[class*=\"ModalContent-modalContent\"]')].at(-1).querySelectorAll('button')].find(b => b.innerText.trim() === 'Cancel').click()");
            await browser.WaitForAsync("[...document.querySelectorAll('[class*=\"ModalContent-modalContent\"]')].length === 1");
        }

        // Reconnect opens Discord's invite with that server picked, and the window closes once the bot is back.
        private static async Task RejoinedServerAsync(HeadlessBrowser browser, WebCheck check)
        {
            await browser.RunAsync("window.invited = undefined; [...document.querySelectorAll('[class*=\"ModalBody\"] button')].find(b => b.innerText.trim() === 'Reconnect').click()");
            await browser.WaitForAsync("!!window.invited && document.body.innerText.includes('Waiting for the bot to join')");
            var invite = await browser.EvalAsync<string>("window.invited");
            check.Saw($"Reconnect opens Discord's invite, ending {invite[invite.IndexOf("&scope=", StringComparison.Ordinal)..]}");
            await check.ShotAsync(browser, "reconnect-waiting");
            invite.Should().Contain($"guild_id={MadeUpServer}").And.Contain("disable_guild_select=true");

            await browser.RunAsync($"Object.assign(window.__qaServer, {{ reachable: true, joined: true }}); {ReadAgain}");
            await browser.WaitForAsync("!document.querySelector('[class*=\"ModalContent-modalContent\"]')");
            var card = await browser.EvalAsync<string>($"{ServerCards}.find(c => c.innerText.includes('{MadeUpName}')).innerText.replace(/\\s+/g, ' ').trim()");
            check.Saw($"the bot is back: the window closed; its card: {card}");
            await check.ShotAsync(browser, "rejoined-card");
            card.Should().Contain("Connected");
        }
    }
}
