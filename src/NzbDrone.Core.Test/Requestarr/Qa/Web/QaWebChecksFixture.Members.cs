using System;
using System.Text.Json;
using System.Threading.Tasks;
using FluentAssertions;
using NUnit.Framework;

namespace Requestarr.Core.Test.Qa.Web
{
    public partial class QaWebChecksFixture
    {
        private const string AddMembersFilter = "document.querySelector('[class*=\"AddMembers-filter\"]')";

        // The test member the run names (QA_CHECKS_TEST_MEMBER, their Discord username): they're onboarded through Add
        // member, then suspended and unsuspended from the Members list's bar, and the run removes them afterwards.
        // Without one, the check changes nothing.
        private static string TestMember => Environment.GetEnvironmentVariable("QA_CHECKS_TEST_MEMBER");

        // Add member and the Members list, at the owner's width, in both themes. Add member lists the server's people
        // with a box each and one in the header, narrows as you type, and picking someone shows the bar's Set up, whose
        // step sums up what they'll get. The Members list has a box and its actions on every row, and the bar offers
        // Suspend, Unsuspend and Change access. With a test member named, they're onboarded, suspended and unsuspended.
        [Test]
        public Task add_members() => CheckAsync(nameof(add_members), AddMembersAsync, OwnersWidth);

        private static async Task AddMembersAsync(HeadlessBrowser browser, WebCheck check)
        {
            await SignInAsync(browser);

            foreach (var theme in Themes)
            {
                await browser.SetThemeAsync(theme);
                await AddMemberListAsync(browser, check, theme);
                await MembersListAsync(browser, check, theme);
            }

            if (!string.IsNullOrEmpty(TestMember))
            {
                await OnboardTestMemberAsync(browser, check);
                await SuspendAndUnsuspendAsync(browser, check);
            }
            else
            {
                check.Saw("no test member named, so nobody was onboarded, suspended or unsuspended");
            }
        }

        private static async Task OpenAddMembersAsync(HeadlessBrowser browser)
        {
            await browser.GoAsync($"{Url}/members/add");
            await browser.WaitForAsync("!!document.querySelector('tbody tr, [class*=\"Portal-empty\"]')");
        }

        private static Task TypeAsync(HeadlessBrowser browser, string input, string words) =>
            browser.RunAsync($"const e = {input}; Object.getOwnPropertyDescriptor(HTMLInputElement.prototype, 'value').set.call(e, {JsonSerializer.Serialize(words)}); e.dispatchEvent(new Event('input', {{ bubbles: true }}))");

        private static async Task AddMemberListAsync(HeadlessBrowser browser, WebCheck check, string theme)
        {
            await OpenAddMembersAsync(browser);
            var rows = await browser.EvalAsync<int>("document.querySelectorAll('tbody tr').length");

            if (rows == 0)
            {
                check.Saw($"Add member ({theme}): everyone in the server is a member already");
                return;
            }

            var boxes = await browser.EvalAsync<int>("document.querySelectorAll('tbody tr input[type=checkbox]').length");
            var header = await browser.EvalAsync<bool>("!!document.querySelector('thead input[type=checkbox]')");
            var first = await browser.EvalAsync<string>("document.querySelector('tbody tr td:nth-child(2)').innerText.trim().split('\\n')[0]");
            check.Saw($"Add member ({theme}): {rows} people, {boxes} boxes, a box in the header: {header}; first: {first}");
            boxes.Should().Be(rows);
            header.Should().BeTrue();
            await check.ShotAsync(browser, $"add-member-{theme}");

            var words = first.Length > 3 ? first[..3] : first;
            await TypeAsync(browser, AddMembersFilter, words);
            await browser.WaitForAsync($"[...document.querySelectorAll('tbody tr')].every(r => r.innerText.toLowerCase().includes({JsonSerializer.Serialize(words.ToLowerInvariant())}))");
            var narrowed = await browser.EvalAsync<int>("document.querySelectorAll('tbody tr').length");
            check.Saw($"typing \"{words}\" ({theme}): {narrowed} of {rows} left, each one matching");
            await TypeAsync(browser, AddMembersFilter, string.Empty);

            await browser.RunAsync("document.querySelector('tbody tr input[type=checkbox]').click()");
            await browser.WaitForAsync($"{Bar}?.innerText.includes('1 selected')");
            check.Saw($"one picked ({theme}): the bar says \"{await BarTextAsync(browser)}\"");
            await browser.RunAsync($"[...{Bar}.querySelectorAll('button')].find(b => b.innerText.startsWith('Set up')).click()");
            await browser.WaitForAsync("document.body.innerText.includes('Onboard 1 people:')");
            var summary = await browser.EvalAsync<string>("[...document.querySelectorAll('p')].find(p => p.innerText.startsWith('Onboard')).innerText");
            check.Saw($"Set up ({theme}): \"{summary}\"");
            await check.ShotAsync(browser, $"set-up-{theme}");
        }

        private static async Task MembersListAsync(HeadlessBrowser browser, WebCheck check, string theme)
        {
            await browser.GoAsync($"{Url}/members");
            await browser.WaitForAsync("!!document.querySelector('tbody tr, [class*=\"Portal-empty\"]')");
            var rows = await browser.EvalAsync<int>("document.querySelectorAll('tbody tr').length");
            var boxes = await browser.EvalAsync<int>("document.querySelectorAll('tbody tr input[type=checkbox]').length");
            var offscreen = await browser.EvalAsync<int>("[...document.querySelectorAll('tbody [class*=\"RequestRows-actions\"]')].filter(a => a.getBoundingClientRect().right > innerWidth).length");
            check.Saw($"Members ({theme}): {boxes} boxes on {rows} rows; {offscreen} rows' actions past the window's edge");
            boxes.Should().Be(rows);
            offscreen.Should().Be(0);

            if (rows > 0)
            {
                await browser.RunAsync("document.querySelector('thead input[type=checkbox]').click()");
                await browser.WaitForAsync($"!!{Bar}");
                check.Saw($"every member picked ({theme}): \"{await BarTextAsync(browser)}\"");
                await check.ShotAsync(browser, $"members-picked-{theme}");
                await browser.RunAsync($"[...{Bar}.querySelectorAll('a, button')].find(e => e.innerText.trim() === 'Clear selection').click()");
            }
        }

        private static async Task OnboardTestMemberAsync(HeadlessBrowser browser, WebCheck check)
        {
            await OpenAddMembersAsync(browser);
            await TypeAsync(browser, AddMembersFilter, TestMember);
            await browser.WaitForAsync($"[...document.querySelectorAll('tbody tr')].some(r => r.innerText.includes('@{TestMember}'))");
            check.Saw($"the test member is listed: {await browser.EvalAsync<string>("document.querySelector('tbody').innerText.replace(/\\s+/g, ' ')")}");
            await browser.RunAsync($"[...document.querySelectorAll('tbody tr')].find(r => r.innerText.includes('@{TestMember}')).querySelector('input[type=checkbox]').click()");
            await browser.RunAsync($"[...{Bar}.querySelectorAll('button')].find(b => b.innerText.startsWith('Set up')).click()");
            await browser.WaitForAsync("[...document.querySelectorAll('button')].some(b => b.innerText.trim() === 'Confirm')");
            await browser.RunAsync("[...document.querySelectorAll('button')].find(b => b.innerText.trim() === 'Confirm').click()");
            await browser.WaitForAsync("document.body.innerText.includes('failed.')", TimeSpan.FromSeconds(60));
            var said = await browser.EvalAsync<string>("document.querySelector('[class*=\"Alert-\"]').innerText.trim()");
            var progress = await browser.EvalAsync<string>("document.querySelector('tbody').innerText.replace(/\\s+/g, ' ')");
            check.Saw($"onboarding the test member: \"{said}\"; {progress}");
            said.Should().StartWith("Added 1, 0 failed");
            await ShotBothAsync(browser, check, "onboarded");
        }

        // From the Members list's bar: Suspend, then Unsuspend, each asked first and read back.
        private static async Task SuspendAndUnsuspendAsync(HeadlessBrowser browser, WebCheck check)
        {
            foreach (var (action, status) in new[] { ("Suspend", "suspended"), ("Unsuspend", "active") })
            {
                await browser.GoAsync($"{Url}/members");
                await browser.WaitForAsync($"[...document.querySelectorAll('tbody tr')].some(r => r.innerText.includes('@{TestMember}'))");
                await browser.RunAsync($"[...document.querySelectorAll('tbody tr')].find(r => r.innerText.includes('@{TestMember}')).querySelector('input[type=checkbox]').click()");
                await browser.WaitForAsync($"!!{Bar}");
                await browser.RunAsync($"[...{Bar}.querySelectorAll('button')].find(b => b.innerText.trim() === '{action}').click()");
                await browser.WaitForAsync($"{ModalButton}.some(b => b.innerText.trim() === '{action}')");
                await AnswerAsync(browser, action, true);
                var said = await OutcomeAsync(browser);
                var now = await PortalAsync<string>(browser, "members", $"m => m.find(x => x.discordName === {JsonSerializer.Serialize(TestMember)})?.status ?? ''");
                check.Saw($"the bar's {action}: \"{said}\"; the test member is now {now}");
                now.Should().Be(status);
                await ShotBothAsync(browser, check, action.ToLowerInvariant());
            }
        }
    }
}
