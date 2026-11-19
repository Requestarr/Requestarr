using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using Discord;
using FluentAssertions;
using NUnit.Framework;
using NzbDrone.Test.Common;
using Requestarr.Core.Discord.Gateway;
using Requestarr.Core.Imports;
using Requestarr.Core.Settings;

namespace Requestarr.Core.Test.Discord
{
    // The bot may be in a second server only so an import can read it. There it never creates, edits or deletes
    // anything, never registers commands and never posts, and what happens there is ignored. The source checks below
    // fail when a new event handler, a new way of reaching a channel or a new write skips that rule.
    [TestFixture]
    public class OtherServerFixture : TestBase
    {
        private const ulong Configured = 111;
        private const ulong Other = 222;

        private static readonly string SourceRoot = Path.GetFullPath(Path.Combine(TestContext.CurrentContext.TestDirectory, "..", "..", "src", "NzbDrone.Core", "Requestarr"));
        private static readonly string HostFile = Path.Combine(SourceRoot, "Discord", "Gateway", "DiscordClientHost.cs");
        private static readonly string GatewayFile = Path.Combine(SourceRoot, "Discord", "Gateway", "DiscordNetGateway.cs");
        private static readonly string HistoryFile = Path.Combine(SourceRoot, "Imports", "ChannelHistory.cs");

        // Every Discord event the bot listens to. A new one fails here until its handler checks the server.
        private static readonly string[] Events =
        {
            "Log", "Connected", "Disconnected", "LatencyUpdated", "Ready", "InteractionCreated", "UserLeft", "GuildMemberUpdated", "UserUpdated", "MessageReceived"
        };

        // The handlers for events that come from a server, and the member check they share.
        private static readonly string[] ServerHandlers = { "OnInteraction", "OnUserLeft", "OnMessage", "OnMemberUpdated", "Seen" };

        // What changes something in Discord.
        private static readonly Regex Write = new(@"\.(Send\w*Async|Modify\w*Async|Delete\w*Async|Create\w*Async|Add\w*Async|Remove\w*Async|Pin\w*Async|Unpin\w*Async|BulkOverwrite\w*Async|Trigger\w*Async)\(");

        [Test]
        public void only_the_configured_server_is_ours()
        {
            ServerScope.IsOurs(Configured, Configured).Should().BeTrue();
            ServerScope.IsOurs(Other, Configured).Should().BeFalse();
            ServerScope.IsOurs(null, Configured).Should().BeFalse();
            ServerScope.IsOurs(0, 0).Should().BeFalse("no server is ours before one is configured");
        }

        [Test]
        public void a_direct_message_is_answered_but_another_server_is_not()
        {
            ServerScope.IsOursOrDirect(null, Configured).Should().BeTrue();
            ServerScope.IsOursOrDirect(Configured, Configured).Should().BeTrue();
            ServerScope.IsOursOrDirect(Other, Configured).Should().BeFalse();
        }

        [Test]
        public void the_bot_listens_only_to_the_events_checked_here()
        {
            var host = Source(HostFile);

            Regex.Matches(host, @"Client\.(\w+) \+=").Select(m => m.Groups[1].Value).Should().BeEquivalentTo(Events);
        }

        [Test]
        public void every_handler_for_a_server_event_ignores_other_servers()
        {
            var host = Source(HostFile);

            foreach (var handler in ServerHandlers)
            {
                Body(host, handler).Should().Contain("ServerScope.", $"{handler} must ignore other servers");
            }

            // Setup, its channels and roles, and the commands: only in the configured server.
            var ready = Body(host, "OnReady");
            ready.Should().Contain("Client.GetGuild(_settings.Current.GuildId)");
            Regex.Matches(ready, @"(\w+)\.BulkOverwrite\w*Async").Select(m => m.Groups[1].Value).Should().OnlyContain(g => g == "guild");
            Regex.IsMatch(Source(SourceRoot, "*.cs"), @"Global\w*Command\w*Async").Should().BeFalse("commands are registered only in the configured server");
        }

        [Test]
        public void the_gateway_reaches_channels_and_servers_only_through_the_configured_server()
        {
            var gateway = Source(GatewayFile);

            Regex.Matches(gateway, @"Client\.Rest\.GetChannelAsync\(").Should().HaveCount(1, "every channel goes through the one lookup that refuses other servers");
            Body(gateway, "ChannelAsync").Should().Contain("ServerScope.IsOurs");
            gateway.Should().NotContain("Client.GetChannel(").And.NotContain(".Guilds");

            Regex.Matches(gateway, @"(GetGuild|GetGuildAsync|GetGuildUserAsync|AddRoleAsync|RemoveRoleAsync)\((\w+)")
                .Select(m => m.Groups[2].Value).Should().OnlyContain(argument => argument == "GuildId");
        }

        [Test]
        public void only_the_gateway_and_the_import_reach_discord_directly_and_the_import_only_reads()
        {
            var direct = new Regex(@"[Cc]lient\??\.(GetChannel|GetGuild|Guilds|Rest)\b");
            var others = Directory.GetFiles(SourceRoot, "*.cs", SearchOption.AllDirectories)
                .Where(f => f != HostFile && f != GatewayFile && f != HistoryFile)
                .Where(f => direct.IsMatch(File.ReadAllText(f)))
                .Select(Path.GetFileName);

            // The bot's own picture and banner belong to the application, not to a server.
            others.Should().BeEquivalentTo("BotProfile.cs");

            Write.Matches(Source(HistoryFile)).Select(m => m.Value).Should().BeEmpty("the import never changes anything in Discord");
        }

        [Test]
        public void the_read_only_invite_asks_only_to_read_and_registers_no_commands()
        {
            var permissions = (ChannelPermission)ulong.Parse(SetupService.ReadOnlyInvitePermissions);

            permissions.Should().Be(ChannelPermission.ViewChannel | ChannelPermission.ReadMessageHistory);
            SetupService.InviteUrl(42, SetupService.ReadOnlyInvitePermissions, SetupService.ReadOnlyInviteScope)
                .Should().Be("https://discord.com/oauth2/authorize?client_id=42&permissions=66560&integration_type=0&scope=bot");
            SetupService.InviteUrl(0, SetupService.ReadOnlyInvitePermissions, SetupService.ReadOnlyInviteScope).Should().BeNull();
        }

        [Test]
        public async Task turning_message_content_on_is_seen_without_a_restart()
        {
            var answers = new Queue<ApplicationFlags>(new[] { default(ApplicationFlags), ApplicationFlags.GatewayMessageContentLimited });
            var asked = 0;
            var intent = new MessageContentIntent(() =>
            {
                asked++;
                return Task.FromResult(answers.Dequeue());
            });

            (await intent.IsOnAsync()).Should().BeFalse();
            (await intent.IsOnAsync()).Should().BeTrue("a no is asked of Discord again");
            (await intent.IsOnAsync()).Should().BeTrue();
            asked.Should().Be(2, "a yes is kept");
        }

        private static string Source(string file)
        {
            if (!File.Exists(file))
            {
                Assert.Ignore("Source tree not available next to test output");
            }

            return File.ReadAllText(file);
        }

        private static string Source(string folder, string pattern)
        {
            if (!Directory.Exists(folder))
            {
                Assert.Ignore("Source tree not available next to test output");
            }

            return string.Join("\n", Directory.GetFiles(folder, pattern, SearchOption.AllDirectories).Select(File.ReadAllText));
        }

        // A method's text, from its signature to the next member at the same depth.
        private static string Body(string source, string method)
        {
            var start = Regex.Match(source, $@"\n        (private|public)[^\n(]* {method}\(");
            start.Success.Should().BeTrue($"{method} should exist");

            var next = Regex.Match(source.Substring(start.Index + 1), @"\n        (private|public|//)");

            return next.Success ? source.Substring(start.Index, next.Index + 1) : source.Substring(start.Index);
        }
    }
}
