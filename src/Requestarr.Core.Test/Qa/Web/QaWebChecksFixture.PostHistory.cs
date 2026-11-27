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
    // history left out; their page's Post History starts posting it and is paused, resumed and cancelled, each read back
    // from their channel (a pause or cancel stops the posts); the Members toolbar posts the rest, each once and oldest
    // first; their page's Post History then posts nothing; and Requestarr's server window posts the server's history in
    // the admin feed, paused, resumed and cancelled the same way. Nothing reaches anyone else; the check's posts are
    // deleted and read back gone, and the run resets the stack.
    public partial class QaWebChecksFixture
    {
        private const string HistoryTitle = "Automated check: post history";
        private const string ToolbarButtons = "[...document.querySelectorAll('[class*=\"PageToolbar\"] button')].map(b => b.innerText.trim())";
        private const string WindowButtons = "[...document.querySelectorAll('[class*=\"ModalContent-modalContent\"] button')]";

        // The sidebar's messages so far, so what an action shows there (a new message, or one updated in place) can be
        // told apart.
        private const string SidebarMessages = "[...document.querySelectorAll('[class*=\"Message-message\"]')].map(m => m.innerText.trim()).join(' / ')";

        // Longer than the history's 2 seconds between posts: what's paused or cancelled has had time to show it stopped.
        private static readonly TimeSpan Stopped = TimeSpan.FromSeconds(6);

        private static int HistorySeeded => int.Parse(Environment.GetEnvironmentVariable("QA_CHECKS_HISTORY_SEEDED") ?? "0");

        [Test]
        public Task post_history() => CheckAsync(nameof(post_history), async (browser, check) =>
        {
            TestMember.Should().NotBeNullOrEmpty("the run names the test member for this check");
            HistorySeeded.Should().BePositive("the run gives the test member made-up imported requests");
            using var discord = new DiscordReadBack();
            string channel = null;
            var cleaned = false;

            try
            {
                await SignInAsync(browser);
                await OnboardWithoutHistoryAsync(browser, check);
                channel = await discord.ChannelIdAsync(ChannelLayout.UserChannelName(TestMember));
                channel.Should().NotBeNull("onboarding gave the test member their channel");
                (await discord.CardsAsync(channel, HistoryTitle)).Should().BeEmpty("onboarded with history left out");

                var member = await PortalAsync<int>(browser, "members", $"m => m.find(x => x.discordName === {JsonSerializer.Serialize(TestMember)})?.id ?? 0");
                await MemberPagePostingAsync(browser, check, discord, channel, member);
                await MembersToolbarAsync(browser, check, discord, channel);
                await PostedOnceAsync(browser, check, discord, channel, member);
                await ServerWindowAsync(browser, check, discord);

                var left = await DeleteHistoryPostsAsync(discord, channel);
                cleaned = true;
                check.Saw($"cleanup: the check's posts deleted, {left} left, read back");
                left.Should().Be(0, "the check's posts are deleted");
            }
            finally
            {
                if (!cleaned)
                {
                    await DeleteHistoryPostsAsync(discord, channel);
                }
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

        // Their page's Post History, then Pause History (nothing more lands), Resume History (it carries on) and Cancel
        // History, asked first (nothing more lands, some left unposted).
        private static async Task MemberPagePostingAsync(HeadlessBrowser browser, WebCheck check, DiscordReadBack discord, string channel, int member)
        {
            await browser.GoAsync($"{Url}/members/{member}");
            await browser.WaitForAsync($"{ToolbarButtons}.includes('Post History')");
            await MarkMessagesAsync(browser);
            await PressToolbarAsync(browser, "Post History");
            await browser.WaitForAsync($"{ToolbarButtons}.includes('Pause History')");
            await PressToolbarAsync(browser, "Pause History");
            await browser.WaitForAsync($"{ToolbarButtons}.includes('Resume History')");
            check.Saw($"their page's Post History, then Pause History: \"{await NewMessageAsync(browser)}\"; the toolbar has {string.Join(", ", await browser.EvalAsync<string[]>(ToolbarButtons))}");
            await check.ShotAsync(browser, "member-paused");
            var paused = await StoppedAtAsync(discord, channel, check, "paused");

            await PressToolbarAsync(browser, "Resume History");
            await browser.WaitForAsync($"{ToolbarButtons}.includes('Pause History')");
            var resumed = await ChannelCardsAsync(discord, channel, paused + 1);
            check.Saw($"resumed: {resumed.Count} posted so far");
            resumed.Count.Should().BeGreaterThan(paused, "Resume carries on posting");

            await PressToolbarAsync(browser, "Cancel History");
            await browser.WaitForAsync($"{ModalText}.includes('Stop posting imported requests?')");
            check.Saw($"Cancel History asks first: {await browser.EvalAsync<string>(ModalText)}");
            await check.ShotAsync(browser, "member-cancel-confirm");
            await AnswerAsync(browser, "Cancel History", true);
            await browser.WaitForAsync($"!{ToolbarButtons}.includes('Cancel History')", TimeSpan.FromSeconds(30));
            var cancelled = await StoppedAtAsync(discord, channel, check, "cancelled");
            cancelled.Should().BeLessThan(HistorySeeded, "Cancel History drops what wasn't posted yet");
        }

        // The Members list's toolbar, with the test member picked: Post History (asked first) posts the rest, so their
        // channel has each old request once, oldest first, silently.
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

            var cards = await ChannelCardsAsync(discord, channel, HistorySeeded);
            check.Saw($"their channel, read back: {string.Join(" | ", cards.Select(c => $"{c.Title}{(c.IsSilent ? " (silent)" : string.Empty)}"))}");
            cards.Select(c => c.Title).Should().OnlyHaveUniqueItems("each old request is posted once").And.HaveCount(HistorySeeded);
            cards.Select(c => c.Title).Should().BeInAscendingOrder(Comparer<string>.Create((a, b) => Year(a).CompareTo(Year(b))), "oldest first");
            cards.Should().OnlyContain(c => c.IsSilent && !c.Mentions, "history goes out without a notification");
            await PressToolbarAsync(browser, "Stop Selecting");
        }

        // Their page's Post History once everything is posted: it says so, and nothing more lands.
        private static async Task PostedOnceAsync(HeadlessBrowser browser, WebCheck check, DiscordReadBack discord, string channel, int member)
        {
            await browser.GoAsync($"{Url}/members/{member}");
            await browser.WaitForAsync($"{ToolbarButtons}.includes('Post History')");
            await MarkMessagesAsync(browser);
            await PressToolbarAsync(browser, "Post History");
            check.Saw($"their page's Post History once all is posted: \"{await NewMessageAsync(browser)}\"");
            await check.ShotAsync(browser, "member-posted");
            (await StoppedAtAsync(discord, channel, check, "after a second Post History")).Should().Be(HistorySeeded, "a second press posts nothing");
        }

        // Requestarr's server window on Connections: Post History posts the server's history in the admin feed (theirs
        // is posted already), with how many are posted, and Pause, Resume and Cancel Posting stop and carry on the feed's
        // posts. Its Cancel Posting doesn't ask first, as the server window is built.
        private static async Task ServerWindowAsync(HeadlessBrowser browser, WebCheck check, DiscordReadBack discord)
        {
            const string Buttons = $"{WindowButtons}.map(b => b.innerText.trim())";
            const string Progress = "[...document.querySelectorAll('[class*=\"ModalContent-modalContent\"] [class*=\"ProgressBar\"]')].map(p => p.innerText.trim()).find(t => t) ?? ''";
            var feed = await discord.ChannelIdAsync(ChannelLayout.FeedChannelName);
            await browser.GoAsync($"{Url}/settings/connections");
            await browser.WaitForAsync($"{Cards}.some(c => c.innerText.includes(\"Requestarr's server\"))");
            await browser.RunAsync($"{Cards}.find(c => c.innerText.includes(\"Requestarr's server\")).click()");
            await browser.WaitForAsync($"{Buttons}.includes('Post History')");
            await PressInWindowAsync(browser, "Post History");

            await browser.WaitForAsync($"{Buttons}.includes('Pause')");
            await PressInWindowAsync(browser, "Pause");
            await browser.WaitForAsync($"{Buttons}.includes('Resume')");
            check.Saw($"the server window, paused: {await browser.EvalAsync<string>(Progress)}; buttons {string.Join(", ", await browser.EvalAsync<string[]>(Buttons))}");
            await check.ShotAsync(browser, "server-paused");
            var paused = await StoppedAtAsync(discord, feed, check, "the feed, paused");

            await PressInWindowAsync(browser, "Resume");
            await browser.WaitForAsync($"{Buttons}.includes('Pause')");
            (await ChannelCardsAsync(discord, feed, paused + 1)).Count.Should().BeGreaterThan(paused, "Resume carries on posting");
            var progress = await browser.EvalAsync<string>(Progress);
            check.Saw($"resumed: {progress}");
            progress.Should().MatchRegex(@"\d+ of \d+ posted", "the window shows how many are posted");

            await PressInWindowAsync(browser, "Cancel Posting");
            await browser.WaitForAsync($"{Buttons}.includes('Post History')", TimeSpan.FromSeconds(30));
            check.Saw($"Cancel Posting: back to {string.Join(", ", await browser.EvalAsync<string[]>(Buttons))}; {await browser.EvalAsync<string>(Progress)}");
            await check.ShotAsync(browser, "server-cancelled");
            (await StoppedAtAsync(discord, feed, check, "the feed, cancelled")).Should().BeLessThan(HistorySeeded, "Cancel Posting drops what wasn't posted yet");
        }

        // A button of the open window by its words; a clear failure when it isn't there (posting finished meanwhile).
        private static Task PressInWindowAsync(HeadlessBrowser browser, string label) =>
            browser.RunAsync($"const b = {WindowButtons}.find(b => b.innerText.trim() === '{label}'); if (!b) {{ throw new Error('The window has no {label} button'); }} b.click()");

        // How many history cards a channel has once it's stood still for longer than the posts' pace, and that it stood still.
        private static async Task<int> StoppedAtAsync(DiscordReadBack discord, string channel, WebCheck check, string when)
        {
            var before = (await discord.CardsAsync(channel, HistoryTitle)).Count;
            await Task.Delay(Stopped);
            var after = (await discord.CardsAsync(channel, HistoryTitle)).Count;
            check.Saw($"{when}: {before} posted, {after} after {Stopped.TotalSeconds:0} seconds more");
            after.Should().Be(before, $"nothing more lands once {when}");

            return after;
        }

        // The channel's history cards once there are this many, or what's there after a while (each posts 2 seconds apart).
        private static async Task<IReadOnlyList<PostedCard>> ChannelCardsAsync(DiscordReadBack discord, string channel, int expected)
        {
            var until = DateTime.UtcNow + TimeSpan.FromMinutes(3);
            var cards = await discord.CardsAsync(channel, HistoryTitle);

            while (cards.Count < expected && DateTime.UtcNow < until)
            {
                await Task.Delay(TimeSpan.FromSeconds(3));
                cards = await discord.CardsAsync(channel, HistoryTitle);
            }

            return cards;
        }

        // "Automated check: post history 3 (1992)" -> 1992: the made-up requests are a year apart, oldest first.
        private static int Year(string title) => int.Parse(title[(title.LastIndexOf('(') + 1)..^1]);

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

        // The check's own posts deleted: the test member's channel and the admin feed's cards for the made-up requests.
        // How many are left, read back.
        private static async Task<int> DeleteHistoryPostsAsync(DiscordReadBack discord, string channel)
        {
            var left = 0;

            foreach (var id in new[] { channel, await discord.ChannelIdAsync(ChannelLayout.FeedChannelName) }.Where(i => i != null))
            {
                await discord.DeleteAsync(id, await discord.CardsAsync(id, HistoryTitle));
                left += (await discord.CardsAsync(id, HistoryTitle)).Count;
            }

            return left;
        }
    }
}
