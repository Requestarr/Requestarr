using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Discord;
using FluentAssertions;
using NUnit.Framework;
using Requestarr.Core.Discord.Gateway;
using Requestarr.Core.Discord.Interactions;
using Requestarr.Core.Test.Discord.Harness;
using Requestarr.Core.Test.Framework;

namespace Requestarr.Core.Test.Admin
{
    // The registered slash commands: /request, /history and /help for everyone, and /admin (user, requests, history, help)
    // hidden from non-admins. The admin commands it replaced are no longer handled, and the docs say where each one went.
    [TestFixture]
    public class AdminCommandsFixture : RequestScenario
    {
        private static readonly string[] OldCommands =
        {
            "/admin user add", "/admin user access", "/admin user quota", "/admin user info", "/admin user suspend", "/admin user remove",
            "/admin user mediaserver", "/admin queue", "/admin approve", "/admin mediaserver approve", "/admin help"
        };

        [Test]
        public void exactly_request_history_help_and_admin_are_registered()
        {
            var commands = DiscordCommandDefinitions.Build().Cast<SlashCommandProperties>().ToDictionary(c => c.Name.Value);

            commands.Keys.Should().BeEquivalentTo("request", "history", "help", "admin");
            commands["admin"].DefaultMemberPermissions.Value.Should().Be(GuildPermission.ManageGuild);
            commands["request"].DefaultMemberPermissions.IsSpecified.Should().BeFalse();
            commands["request"].Options.Value.Select(o => o.Name).Should().Equal("type", "query");
            commands["request"].Options.Value.First().IsAutocomplete.Should().BeTrue();
        }

        [Test]
        public void admin_tree_holds_every_admin_command()
        {
            var admin = DiscordCommandDefinitions.Build().Cast<SlashCommandProperties>().Single(c => c.Name.Value == "admin");

            admin.Options.Value.Should().OnlyContain(o => o.Type == ApplicationCommandOptionType.SubCommand);
            admin.Options.Value.Select(o => o.Name).Should().Equal("user", "requests", "history", "help");

            var user = admin.Options.Value.Single(o => o.Name == "user").Options.Single();
            (user.Name, user.Type, user.IsRequired).Should().Be(("member", ApplicationCommandOptionType.User, true));

            admin.Options.Value.Single(o => o.Name == "requests").Options.Should().BeNullOrEmpty("it walks everything; no number or title to know");
            admin.Options.Value.Single(o => o.Name == "help").Options.Should().BeNullOrEmpty();
            DiscordCommandDefinitions.Leaves().Select(l => l.Path).Should().Equal("request", "history", "help", "admin user", "admin requests", "admin history", "admin help");
        }

        [TestCase("user add")]
        [TestCase("queue")]
        [TestCase("approve")]
        [TestCase("requests")]
        [TestCase("quota")]
        [TestCase("mediaserver request")]
        [TestCase("request movie")]
        [TestCase("admin user add")]
        [TestCase("admin user access")]
        [TestCase("admin user quota")]
        [TestCase("admin user suspend")]
        [TestCase("admin user remove")]
        [TestCase("admin user info")]
        [TestCase("admin user mediaserver")]
        [TestCase("admin queue")]
        [TestCase("admin approve")]
        [TestCase("admin deny")]
        [TestCase("admin available")]
        [TestCase("admin cancel")]
        [TestCase("admin mediaserver approve")]
        [TestCase("admin mediaserver deny")]
        [TestCase("admin request")]
        public async Task old_top_level_commands_are_no_longer_handled(string old)
        {
            var run = await Harness.SendAsync(FakeInteraction.SlashCommand(old).By(FakeDiscordGuild.Owner).InGuild(FakeDiscordGuild.Guild).InChannel(AdminChannel));

            run.Outcome.Should().Be(DispatchOutcome.Unhandled);
            run.LastReply.Content.Should().Be(InteractionDispatcher.UnknownInteractionMessage);
        }

        [Test]
        public void the_command_reference_maps_every_old_command()
        {
            var commands = File.ReadAllText(RepoFiles.Path("docs", "commands.md"));
            var table = commands.Substring(commands.IndexOf("### Where the old admin commands went"));

            foreach (var old in OldCommands)
            {
                table.Should().Contain($"`{old}", $"{old} must say where it went");
            }

            table.Should().Contain("`unsuspend`").And.Contain("`deny` / `available` / `cancel`").And.Contain("`/admin mediaserver approve` / `deny`");
            commands.Should().Contain("### `/admin user member:@someone`").And.Contain("### `/admin requests`").And.NotContain("/admin request request");
        }

        [Test]
        public void the_setup_guide_admin_section_describes_the_new_layout()
        {
            var guide = File.ReadAllText(RepoFiles.Path("docs", "setup-guide.md"));
            var start = guide.IndexOf("## 5. Admin setup in Discord");
            var admin = guide.Substring(start, guide.IndexOf("## 6. ") - start);

            admin.Should().Contain("**Add or find a member**").And.Contain("### 5.6 The member card").And.Contain("`/admin requests`").And.Contain("### 5.7 When Sonarr or Radarr can't be reached");
            admin.Should().NotContain("/admin user add").And.NotContain("/admin queue").And.NotContain("/admin approve").And.NotContain("/admin mediaserver");
        }
    }
}
