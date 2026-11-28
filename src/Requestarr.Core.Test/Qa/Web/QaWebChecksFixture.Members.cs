using System;
using System.Text.Json;
using System.Threading.Tasks;
using FluentAssertions;
using NUnit.Framework;

namespace Requestarr.Core.Test.Qa.Web
{
    public partial class QaWebChecksFixture
    {
        private const string AddMembersFilter = "document.querySelector('input[class*=\"AddMembers-filter\"]')";

        // The Onboard window once its fields have loaded: its Onboard button can be pressed.
        private const string OnboardReady = $"{ModalButton}.some(b => b.innerText.trim() === 'Onboard' && !b.disabled)";

        // The test member the run names (QA_CHECKS_TEST_MEMBER, their Discord username): they're onboarded from the Onboard
        // page, then suspended and unsuspended from the Members list's toolbar, and the run removes them afterwards.
        // Without one, the check changes nothing.
        private static string TestMember => Environment.GetEnvironmentVariable("QA_CHECKS_TEST_MEMBER");

        // The test member's row in the list shown, by their Discord username.
        private static string TestMemberRow => $"[...document.querySelectorAll('tbody tr, [class*=\"IndexTable-row\"]')].find(r => r.innerText.includes('@{TestMember}'))";

        // Onboard and the Members list, at the owner's width, in both themes. Onboard lists the server's people
        // with a box each and one in the header, narrows as you type, and with someone picked its toolbar's Onboard opens a
        // window with what they'll get, whose Cancel leaves them picked. The Members list has no buttons on its rows, and
        // Select Members shows a box on each and the actions on what's picked in the toolbar, left of Onboard. With a test
        // member named, they're onboarded, suspended and unsuspended.
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
                await ChangeAccessWindowAsync(browser, check, theme);
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
            await browser.WaitForAsync("!!document.querySelector('tbody tr, [class*=\"Alert-info\"]')");
        }

        private static Task TypeAsync(HeadlessBrowser browser, string input, string words) =>
            browser.RunAsync($"const e = {input}; Object.getOwnPropertyDescriptor(HTMLInputElement.prototype, 'value').set.call(e, {JsonSerializer.Serialize(words)}); e.dispatchEvent(new Event('input', {{ bubbles: true }}))");

        private static async Task AddMemberListAsync(HeadlessBrowser browser, WebCheck check, string theme)
        {
            await OpenAddMembersAsync(browser);
            var rows = await browser.EvalAsync<int>("document.querySelectorAll('tbody tr').length");

            if (rows == 0)
            {
                check.Saw($"Onboard ({theme}): everyone in the server is a member already");
                return;
            }

            var boxes = await browser.EvalAsync<int>("document.querySelectorAll('tbody tr input[type=checkbox]').length");
            var header = await browser.EvalAsync<bool>("!!document.querySelector('thead input[type=checkbox]')");
            var first = await browser.EvalAsync<string>("document.querySelector('tbody tr td:nth-child(2)').innerText.trim().split('\\n')[0]");
            check.Saw($"Onboard ({theme}): {rows} people, {boxes} boxes, a box in the header: {header}; first: {first}");
            boxes.Should().Be(rows);
            header.Should().BeTrue();
            await check.ShotAsync(browser, $"add-member-{theme}");

            var words = first.Length > 3 ? first[..3] : first;
            await TypeAsync(browser, AddMembersFilter, words);
            await browser.WaitForAsync($"[...document.querySelectorAll('tbody tr')].every(r => r.innerText.toLowerCase().includes({JsonSerializer.Serialize(words.ToLowerInvariant())}))");
            var narrowed = await browser.EvalAsync<int>("document.querySelectorAll('tbody tr').length");
            check.Saw($"typing \"{words}\" ({theme}): {narrowed} of {rows} left, each one matching");
            await TypeAsync(browser, AddMembersFilter, string.Empty);

            await browser.RunAsync("document.querySelector('tbody tr label').click()");
            await PressToolbarAsync(browser, "Onboard");
            await browser.WaitForAsync(OnboardReady);
            var title = await browser.EvalAsync<string>("document.querySelector('[class*=\"ModalHeader\"]').innerText.trim()");
            var types = await browser.EvalAsync<string[]>("[...document.querySelectorAll('[class*=\"ModalBody\"] legend')].map(l => l.innerText.trim())");
            check.Saw($"one picked, then Onboard ({theme}): a window \"{title}\" with {string.Join(", ", types)}");
            title.Should().Be($"Onboard {first}");
            types.Should().NotBeEmpty();
            await check.ShotAsync(browser, $"set-up-{theme}");
            await AnswerAsync(browser, "Onboard", false);
            await browser.WaitForAsync("!document.querySelector('[class*=\"ModalBody\"]')");

            // Cancel leaves the pick as it was, ready to Onboard again.
            var ticked = await browser.EvalAsync<bool>($"[...document.querySelectorAll('tbody tr')].find(r => r.innerText.includes({JsonSerializer.Serialize(first)})).querySelector('input[type=checkbox]').checked");
            check.Saw($"Cancel in the Onboard window ({theme}): {first} still ticked: {ticked}");
            await check.ShotAsync(browser, $"cancelled-{theme}");
            ticked.Should().BeTrue("Cancel changes nothing, the pick included");
        }

        private static async Task MembersListAsync(HeadlessBrowser browser, WebCheck check, string theme)
        {
            await browser.GoAsync($"{Url}/members");
            await browser.WaitForAsync("!!document.querySelector('[class*=\"IndexTable-row\"], [class*=\"Alert-info\"]')");
            var rows = await browser.EvalAsync<int>("document.querySelectorAll('[class*=\"IndexTable-row\"]').length");
            var rowButtons = await browser.EvalAsync<int>("document.querySelectorAll('[class*=\"IndexTable-row\"] button').length");
            check.Saw($"Members ({theme}): {rows} rows, {rowButtons} buttons on them");
            rowButtons.Should().Be(0, "actions are in the toolbar, not on rows");

            if (rows > 0)
            {
                await JumpToLastLetterAsync(browser, check, theme);
                await PressToolbarAsync(browser, "Select Members");
                await browser.WaitForAsync("!!document.querySelector('[class*=\"VirtualTableHeader-header\"] input[type=checkbox]')");
                var boxes = await browser.EvalAsync<int>("document.querySelectorAll('[class*=\"IndexTable-row\"] input[type=checkbox]').length");
                await PressToolbarAsync(browser, "Select All");
                await browser.WaitForAsync("document.querySelectorAll('[class*=\"IndexTable-row\"] input[type=checkbox]:not(:checked)').length === 0");
                var actions = await browser.EvalAsync<string[]>("(b => b.slice(0, b.findIndex(x => x.innerText.trim() === 'Onboard')))([...document.querySelectorAll('[class*=\"PageToolbar\"] button')]).map(b => b.innerText.trim())");
                check.Saw($"Select Members, then Select All ({theme}): {boxes} boxes on {rows} rows, all ticked; left of Onboard: {string.Join(", ", actions)}");
                boxes.Should().Be(rows);
                actions.Should().Equal("Suspend", "Unsuspend", "Change access", "Post History", "Remove member");
                await check.ShotAsync(browser, $"members-picked-{theme}");
                await OnboardWindowAsync(browser, check, theme);
                await PressToolbarAsync(browser, "Stop Selecting");
            }
        }

        // Sorted by name, the letter bar's last letter keeps every row on screen drawn, even when the whole list fits on
        // the page and it can't scroll that far: no blank space opens between the top of the list and the first row. The
        // list's top is its header, or the toolbar once a longer list has scrolled the header away.
        private static async Task JumpToLastLetterAsync(HeadlessBrowser browser, WebCheck check, string theme)
        {
            const string Letters = "[...document.querySelectorAll('[class*=\"PageJumpBarItem-jumpBarItem\"]')]";
            const string Gap = "document.querySelector('[class*=\"IndexTable-row\"]').getBoundingClientRect().top - Math.max(document.querySelector('[class*=\"VirtualTableHeader-header\"]').getBoundingClientRect().bottom, document.querySelector('[class*=\"PageToolbar\"]').getBoundingClientRect().bottom)";
            var letter = await browser.EvalAsync<string>($"{Letters}.map(l => l.innerText.trim()).filter(l => /^[A-Z]$/.test(l)).at(-1) ?? ''");

            if (string.IsNullOrEmpty(letter))
            {
                check.Saw($"Members ({theme}): no letter bar, so nothing to jump to");
                return;
            }

            await browser.RunAsync($"{Letters}.find(l => l.innerText.trim() === '{letter}').click()");
            await browser.EvalAsync<bool>("new Promise(r => setTimeout(() => r(true), 800))");
            var gap = await browser.EvalAsync<double>(Gap);
            var drawn = await browser.EvalAsync<int>("document.querySelectorAll('[class*=\"IndexTable-row\"]').length");
            check.Saw($"Members ({theme}): the letter bar's {letter} leaves {drawn} rows drawn, the first {gap:0}px below the list's top");
            await check.ShotAsync(browser, $"members-jump-{theme}");
            gap.Should().BeLessThan(1, "the rows above the letter stay drawn");
        }

        // With everyone picked, Onboard opens one window over the list for the picks it can onboard: what they get, then
        // Onboard. Cancelled here, so nobody is onboarded.
        private static async Task OnboardWindowAsync(HeadlessBrowser browser, WebCheck check, string theme)
        {
            var onboardable = await PortalAsync<int>(browser, "members", "m => m.filter(x => ['imported', 'removed'].includes(x.status) && x.discordId).length");

            if (onboardable == 0)
            {
                check.Saw($"Onboard with picks ({theme}): nobody here can be onboarded");
                return;
            }

            await PressToolbarAsync(browser, "Onboard");
            await browser.WaitForAsync(OnboardReady);

            // One alone is named in the heading, without a list.
            var names = Math.Max(1, await browser.EvalAsync<int>("document.querySelectorAll('[class*=\"ModalBody\"] li').length"));
            check.Saw($"Onboard with everyone picked ({theme}): one window naming {names} people, the {onboardable} who can be onboarded; still on {await browser.EvalAsync<string>("location.pathname")}");
            names.Should().Be(onboardable, "only the picks it can onboard");
            await check.ShotAsync(browser, $"onboard-window-{theme}");
            await AnswerAsync(browser, "Onboard", false);
            await browser.WaitForAsync("!document.querySelector('[class*=\"ModalBody\"]')");
        }

        // Change access on a member's page: one window with every type's access and limit, and one Save for all of it.
        // Cancelled here, so nothing changes.
        private static async Task ChangeAccessWindowAsync(HeadlessBrowser browser, WebCheck check, string theme)
        {
            var member = await PortalAsync<int>(browser, "members", "m => m.find(x => x.status === 'active')?.id ?? 0");

            if (member == 0)
            {
                check.Saw($"Change access ({theme}): nobody here is onboarded");
                return;
            }

            await browser.GoAsync($"{Url}/members/{member}");
            await browser.WaitForAsync("[...document.querySelectorAll('[class*=\"PageToolbar\"] button')].some(b => b.innerText.trim() === 'Change access')");
            await PressToolbarAsync(browser, "Change access");
            await browser.WaitForAsync("!!document.querySelector('[class*=\"ModalBody\"] legend')");
            var buttons = await browser.EvalAsync<string[]>("[...document.querySelectorAll('[class*=\"ModalContent\"] button')].map(b => b.innerText.trim()).filter(t => t === 'Cancel' || t.startsWith('Save'))");
            check.Saw($"Change access ({theme}): {string.Join(", ", buttons)}");
            buttons.Should().Equal("Cancel", "Save");
            await check.ShotAsync(browser, $"change-access-{theme}");
            await AnswerAsync(browser, "Save", false);
            await browser.WaitForAsync("!document.querySelector('[class*=\"ModalBody\"]')");
        }

        private static async Task OnboardTestMemberAsync(HeadlessBrowser browser, WebCheck check)
        {
            await OpenAddMembersAsync(browser);
            await TypeAsync(browser, AddMembersFilter, TestMember);
            await browser.WaitForAsync($"!!{TestMemberRow}");
            check.Saw($"the test member is listed: {await browser.EvalAsync<string>("document.querySelector('tbody').innerText.replace(/\\s+/g, ' ')")}");
            await browser.RunAsync($"{TestMemberRow}.querySelector('label').click()");
            await PressToolbarAsync(browser, "Onboard");
            await browser.WaitForAsync(OnboardReady);
            await AnswerAsync(browser, "Onboard", true);
            await browser.WaitForAsync("document.body.innerText.includes('failed.')", TimeSpan.FromSeconds(60));
            var said = await browser.EvalAsync<string>("document.querySelector('[class*=\"ModalBody\"] [class*=\"Alert-\"]').innerText.trim()");
            var progress = await browser.EvalAsync<string>("document.querySelector('[class*=\"ModalBody\"] tbody').innerText.replace(/\\s+/g, ' ')");
            check.Saw($"onboarding the test member: \"{said}\"; {progress}");
            said.Should().StartWith("Added 1, 0 failed");
            await ShotBothAsync(browser, check, "onboarded");
        }

        // From the toolbar in select mode, the test member picked alone: Suspend, then Unsuspend, each read back.
        private static async Task SuspendAndUnsuspendAsync(HeadlessBrowser browser, WebCheck check)
        {
            foreach (var (action, status) in new[] { ("Suspend", "suspended"), ("Unsuspend", "active") })
            {
                await browser.GoAsync($"{Url}/members");
                await browser.WaitForAsync("!!document.querySelector('[class*=\"IndexTable-row\"]')");
                await PressToolbarAsync(browser, "Select Members");
                await ScrollToRowAsync(browser, TestMemberRow);
                await browser.WaitForAsync($"!!{TestMemberRow}.querySelector('input[type=checkbox]')");
                await browser.RunAsync($"{TestMemberRow}.querySelector('label').click()");
                await PressToolbarAsync(browser, action);
                var said = await MessageAsync(browser, action + ":");
                var now = await PortalAsync<string>(browser, "members", $"m => m.find(x => x.discordName === {JsonSerializer.Serialize(TestMember)})?.status ?? ''");
                check.Saw($"the toolbar's {action}: \"{said}\"; the test member is now {now}");
                now.Should().Be(status);
                await ShotBothAsync(browser, check, action.ToLowerInvariant());
            }
        }
    }
}
