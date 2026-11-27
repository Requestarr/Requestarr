using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using System.Threading.Tasks;
using FluentAssertions;
using NUnit.Framework;
using Requestarr.Core.Discord;

namespace Requestarr.Core.Test.Qa.Web
{
    // Post History, on a dev stack just reset where the only imported history is the test member's, made up by the run
    // (QA_CHECKS_HISTORY_SEEDED old requests titled "Automated check: post history N"): the test member is onboarded with
    // history left out, then their history is posted from their page (paused, resumed and cancelled on the way), the
    // Members toolbar and Requestarr's server window, each read back from their channel in the dev server. Nothing reaches
    // anyone else; the check's posts are deleted afterwards and the run resets the stack.
    public partial class QaWebChecksFixture
    {
        private const string HistoryTitle = "Automated check: post history";
        private const string ToolbarButtons = "[...document.querySelectorAll('[class*=\"PageToolbar\"] button')].map(b => b.innerText.trim())";

        private static int HistorySeeded => int.Parse(Environment.GetEnvironmentVariable("QA_CHECKS_HISTORY_SEEDED") ?? "0");

        [Test]
        public Task post_history() => CheckAsync(nameof(post_history), async (browser, check) =>
        {
            TestMember.Should().NotBeNullOrEmpty("the run names the test member for this check");
            HistorySeeded.Should().BePositive("the run gives the test member made-up imported requests");
            using var discord = new DiscordReadBack();
            string channel = null;

            try
            {
                await SignInAsync(browser);
                await OnboardWithoutHistoryAsync(browser, check);
                channel = await discord.ChannelIdAsync(ChannelLayout.UserChannelName(TestMember));
                channel.Should().NotBeNull("onboarding gave the test member their channel");
                (await discord.CardsAsync(channel, HistoryTitle)).Should().BeEmpty("onboarded with history left out");

                var member = await PortalAsync<int>(browser, "members", $"m => m.find(x => x.discordName === {JsonSerializer.Serialize(TestMember)})?.id ?? 0");
                await MemberPagePostingAsync(browser, check, member);
                await AllPostedOnceAsync(browser, check, discord, channel, member);
                await MembersToolbarAsync(browser, check, discord, channel);
                await ServerWindowAsync(browser, check);
            }
            finally
            {
                await DeleteHistoryPostsAsync(discord, channel);
            }
        });

        // Onboarded from the Onboard page with their history left out, so it's the Post History buttons that post it.
        private static async Task OnboardWithoutHistoryAsync(HeadlessBrowser browser, WebCheck check)
        {
            const string PostHistory = "document.querySelector('[class*=\"ModalBody\"] input[name=\"postHistory\"]')";
            await OpenAddMembersAsync(browser);
            await TypeAsync(browser, AddMembersFilter, TestMember);
            await browser.WaitForAsync($"!!{TestMemberRow}");
            await browser.RunAsync($"{TestMemberRow}.querySelector('label').click()");
            await PressToolbarAsync(browser, "Onboard");
            await browser.WaitForAsync(OnboardReady);

            if (await browser.EvalAsync<bool>($"{PostHistory}?.checked ?? false"))
            {
                await browser.RunAsync($"{PostHistory}.click()");
            }

            var window = await browser.EvalAsync<string>(ModalText);
            check.Saw($"Onboard window for the test member, history left out: {window}");
            await check.ShotAsync(browser, "onboard-history-left-out");
            await AnswerAsync(browser, "Onboard", true);
            await browser.WaitForAsync("document.body.innerText.includes('failed.')", TimeSpan.FromSeconds(60));
            var said = await browser.EvalAsync<string>("document.querySelector('[class*=\"ModalBody\"] [class*=\"Alert-\"]').innerText.trim()");
            check.Saw($"onboarding: \"{said}\"");
            said.Should().StartWith("Added 1, 0 failed");
        }

        // Their page's Post History, then Pause History, Resume History and Cancel History (asked first) while it posts.
        private static async Task MemberPagePostingAsync(HeadlessBrowser browser, WebCheck check, int member)
        {
            await browser.GoAsync($"{Url}/members/{member}");
            await browser.WaitForAsync($"{ToolbarButtons}.includes('Post History')");
            await MarkMessagesAsync(browser);
            await PressToolbarAsync(browser, "Post History");
            check.Saw($"their page's Post History: \"{await NewMessageAsync(browser)}\"");

            await browser.WaitForAsync($"{ToolbarButtons}.includes('Pause History')");
            await PressToolbarAsync(browser, "Pause History");
            await browser.WaitForAsync($"{ToolbarButtons}.includes('Resume History')");
            check.Saw($"paused: the toolbar has {string.Join(", ", await browser.EvalAsync<string[]>(ToolbarButtons))}");
            await check.ShotAsync(browser, "member-paused");

            await PressToolbarAsync(browser, "Resume History");
            await browser.WaitForAsync($"{ToolbarButtons}.includes('Pause History')");
            check.Saw("resumed: Pause History is back");

            await PressToolbarAsync(browser, "Cancel History");
            await browser.WaitForAsync($"{ModalText}.includes('Stop posting imported requests?')");
            check.Saw($"Cancel History asks first: {await browser.EvalAsync<string>(ModalText)}");
            await check.ShotAsync(browser, "member-cancel-confirm");
            await AnswerAsync(browser, "Cancel History", true);
            await browser.WaitForAsync($"!{ToolbarButtons}.includes('Cancel History')", TimeSpan.FromSeconds(30));
            check.Saw($"cancelled: the toolbar has {string.Join(", ", await browser.EvalAsync<string[]>(ToolbarButtons))}");
        }

        // Post History again posts what the cancel left, so their channel has each old request once, oldest first,
        // silently; pressing it once more posts nothing.
        private static async Task AllPostedOnceAsync(HeadlessBrowser browser, WebCheck check, DiscordReadBack discord, string channel, int member)
        {
            var before = (await discord.CardsAsync(channel, HistoryTitle)).Count;
            check.Saw($"posted in their channel before the cancel stopped it: {before} of {HistorySeeded}");

            await browser.GoAsync($"{Url}/members/{member}");
            await browser.WaitForAsync($"{ToolbarButtons}.includes('Post History')");
            await PressToolbarAsync(browser, "Post History");
            var cards = await ChannelCardsAsync(discord, channel, HistorySeeded);
            check.Saw($"their channel, read back: {string.Join(" | ", cards.Select(c => $"{c.Title}{(c.IsSilent ? " (silent)" : string.Empty)}"))}");
            cards.Select(c => c.Title).Should().OnlyHaveUniqueItems("each old request is posted once").And.HaveCount(HistorySeeded);
            cards.Should().OnlyContain(c => c.IsSilent && !c.Mentions, "history goes out without a notification");

            await MarkMessagesAsync(browser);
            await PressToolbarAsync(browser, "Post History");
            check.Saw($"Post History once more: \"{await NewMessageAsync(browser)}\"");
            await Task.Delay(TimeSpan.FromSeconds(5));
            (await discord.CardsAsync(channel, HistoryTitle)).Should().HaveCount(HistorySeeded, "a second press posts nothing");
            await check.ShotAsync(browser, "member-posted");
        }

        // The Members list's toolbar, with the test member picked: Post History, and nothing more lands (all posted).
        private static async Task MembersToolbarAsync(HeadlessBrowser browser, WebCheck check, DiscordReadBack discord, string channel)
        {
            await browser.GoAsync($"{Url}/members");
            await browser.WaitForAsync("!!document.querySelector('[class*=\"IndexTable-row\"]')");
            await PressToolbarAsync(browser, "Select Members");
            await ScrollToRowAsync(browser, TestMemberRow);
            await browser.WaitForAsync($"!!{TestMemberRow}.querySelector('input[type=checkbox]')");
            await browser.RunAsync($"{TestMemberRow}.querySelector('label').click()");
            await MarkMessagesAsync(browser);
            await PressToolbarAsync(browser, "Post History");
            await browser.WaitForAsync($"{ModalText}.includes('Post History: ')");
            check.Saw($"the Members toolbar's Post History asks first: {await browser.EvalAsync<string>(ModalText)}");
            await check.ShotAsync(browser, "members-toolbar");
            await AnswerAsync(browser, "Post History", true);
            check.Saw($"confirmed: \"{await NewMessageAsync(browser)}\"");
            await Task.Delay(TimeSpan.FromSeconds(5));
            (await discord.CardsAsync(channel, HistoryTitle)).Should().HaveCount(HistorySeeded, "theirs was posted already");
            await PressToolbarAsync(browser, "Stop Selecting");
        }

        // Requestarr's server window on Connections: Post History posts the server's history in the admin feed (theirs
        // is posted already), with how many are posted, and Pause, Resume and Cancel Posting while it posts.
        private static async Task ServerWindowAsync(HeadlessBrowser browser, WebCheck check)
        {
            const string Buttons = "[...document.querySelectorAll('[class*=\"ModalContent-modalContent\"] button')].map(b => b.innerText.trim())";
            const string Progress = "[...document.querySelectorAll('[class*=\"ModalContent-modalContent\"] [class*=\"ProgressBar\"]')].map(p => p.innerText.trim()).find(t => t) ?? ''";
            await browser.GoAsync($"{Url}/settings/connections");
            await browser.WaitForAsync($"{Cards}.some(c => c.innerText.includes(\"Requestarr's server\"))");
            await browser.RunAsync($"{Cards}.find(c => c.innerText.includes(\"Requestarr's server\")).click()");
            await browser.WaitForAsync($"{Buttons}.includes('Post History')");
            await browser.RunAsync("[...document.querySelectorAll('[class*=\"ModalContent-modalContent\"] button')].find(b => b.innerText.trim() === 'Post History').click()");

            await browser.WaitForAsync($"{Buttons}.includes('Pause')");
            await browser.RunAsync("[...document.querySelectorAll('[class*=\"ModalContent-modalContent\"] button')].find(b => b.innerText.trim() === 'Pause').click()");
            await browser.WaitForAsync($"{Buttons}.includes('Resume')");
            check.Saw($"the server window, paused: {await browser.EvalAsync<string>(Progress)}; buttons {string.Join(", ", await browser.EvalAsync<string[]>(Buttons))}");
            await check.ShotAsync(browser, "server-paused");

            await browser.RunAsync("[...document.querySelectorAll('[class*=\"ModalContent-modalContent\"] button')].find(b => b.innerText.trim() === 'Resume').click()");
            await browser.WaitForAsync($"{Buttons}.includes('Pause')");
            var progress = await browser.EvalAsync<string>(Progress);
            check.Saw($"resumed: {progress}");
            progress.Should().MatchRegex(@"\d+ of \d+ posted", "the window shows how many are posted");

            await browser.RunAsync("[...document.querySelectorAll('[class*=\"ModalContent-modalContent\"] button')].find(b => b.innerText.trim() === 'Cancel Posting').click()");
            await browser.WaitForAsync($"{Buttons}.includes('Post History')", TimeSpan.FromSeconds(30));
            check.Saw($"Cancel Posting: back to {string.Join(", ", await browser.EvalAsync<string[]>(Buttons))}; {await browser.EvalAsync<string>(Progress)}");
            await check.ShotAsync(browser, "server-cancelled");
        }

        // The channel's history cards once there are this many, or what's there after a while (each posts 2 seconds apart).
        private static async Task<IReadOnlyList<PostedCard>> ChannelCardsAsync(DiscordReadBack discord, string channel, int expected)
        {
            var until = DateTime.UtcNow + TimeSpan.FromMinutes(2);
            var cards = await discord.CardsAsync(channel, HistoryTitle);

            while (cards.Count < expected && DateTime.UtcNow < until)
            {
                await Task.Delay(TimeSpan.FromSeconds(3));
                cards = await discord.CardsAsync(channel, HistoryTitle);
            }

            return cards;
        }

        // The sidebar's messages so far, so what an action shows there (a new message, or one updated in place) can be
        // told apart.
        private const string SidebarMessages = "[...document.querySelectorAll('[class*=\"Message-message\"]')].map(m => m.innerText.trim()).join(' / ')";

        private static Task MarkMessagesAsync(HeadlessBrowser browser) => browser.RunAsync($"window.__qaMessages = {SidebarMessages}");

        private static async Task<string> NewMessageAsync(HeadlessBrowser browser)
        {
            try
            {
                await browser.WaitForAsync($"{SidebarMessages} !== window.__qaMessages", TimeSpan.FromSeconds(10));
            }
            catch (TimeoutException)
            {
                // The same words again (the same answer to the same press) leave the message as it was.
            }

            await Task.Delay(TimeSpan.FromSeconds(1));
            return await browser.EvalAsync<string>(SidebarMessages);
        }

        // The check's own posts: the test member's channel and the admin feed's cards for the made-up requests.
        private static async Task DeleteHistoryPostsAsync(DiscordReadBack discord, string channel)
        {
            foreach (var id in new[] { channel, await discord.ChannelIdAsync(ChannelLayout.FeedChannelName) }.Where(i => i != null))
            {
                await discord.DeleteAsync(id, await discord.CardsAsync(id, HistoryTitle));
            }
        }
    }
}
