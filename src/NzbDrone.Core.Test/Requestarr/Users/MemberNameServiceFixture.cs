using System;
using System.Linq;
using System.Threading.Tasks;
using FluentAssertions;
using NUnit.Framework;
using Requestarr.Core.Access;
using Requestarr.Core.Audit;
using Requestarr.Core.Discord;
using Requestarr.Core.Test.Discord.Harness;
using Requestarr.Core.Users;

namespace Requestarr.Core.Test.Users
{
    // Members are known by their Discord ID; their names follow Discord: stored, recorded as a member event, and their
    // channel and role renamed to match.
    [TestFixture]
    public class MemberNameServiceFixture : DiscordScenario
    {
        private MemberNameService Names => Mocker.Resolve<MemberNameService>();

        private AuditEntry LastEntry => AuditTrail.Last();

        private Task<bool> Renamed(ulong member, string username = null, string displayName = null)
        {
            var now = Discord.Members[member];
            Discord.Members[member] = now with { Username = username ?? now.Username, DisplayName = displayName ?? now.DisplayName };

            return Names.RefreshAsync(Discord.Members[member]);
        }

        [Test]
        public async Task a_new_display_name_is_stored_recorded_and_put_on_the_channel_topic()
        {
            await AddUser(Alice);
            var channel = ChannelFor(Alice).Name;

            (await Renamed(Alice, displayName: "Alice Cooper")).Should().BeTrue();

            UserFor(Alice).DisplayName.Should().Be("Alice Cooper");
            ChannelFor(Alice).Name.Should().Be(channel, "the channel is named after the username, which didn't change");
            ChannelFor(Alice).Topic.Should().Be(ChannelLayout.UserChannelTopic("Alice Cooper"));
            LastEntry.Action.Should().Be(MemberNameService.RenameAction);
            LastEntry.TargetId.Should().Be(UserFor(Alice).Id.ToString());
            LastEntry.Detail.Should().Be("from \"alice\" to \"Alice Cooper\"");
        }

        [Test]
        public async Task a_new_username_renames_their_channel_and_role()
        {
            await AddUser(Alice);

            await Renamed(Alice, username: "alice2");

            ChannelFor(Alice).Name.Should().Be(ChannelLayout.UserChannelName("alice2"));
            Discord.Roles[RoleOf(Alice)].Should().Be(ChannelLayout.UserRoleName("alice2"));
            LastEntry.Detail.Should().Be("Discord username from @alice to @alice2");
            MemberNameService.FormerNames(LastEntry.Detail).Should().Equal("alice");
        }

        [Test]
        public void a_former_display_name_reads_back_whole()
        {
            MemberNameService.FormerNames("from \"Back to Basics\" to \"Basics\"; Discord username from @back to @basics").Should().Equal("Back to Basics", "back");
        }

        [Test]
        public async Task the_same_names_change_nothing()
        {
            await AddUser(Alice);
            var entries = AuditTrail.Count;

            (await Names.RefreshAsync(Discord.Members[Alice])).Should().BeFalse();

            AuditTrail.Count.Should().Be(entries);
        }

        [Test]
        public async Task a_removed_members_channel_stays_archived_under_their_new_name()
        {
            await AddUser(Alice);
            var channel = ChannelOf(Alice);
            await Users.RemoveAsync(Alice, Actor.System);

            await Renamed(Alice, username: "alice2");

            Discord.Channels[channel].Name.Should().Be(ChannelLayout.ArchivedName(ChannelLayout.UserChannelName("alice2")));
        }

        [Test]
        public async Task someone_waiting_to_join_is_shown_by_their_name_now()
        {
            var requests = Mocker.Resolve<IAccessRequestRepository>();
            var asked = requests.Insert(new AccessRequest { DiscordUserId = Bob.ToString(), Username = "bob", DisplayName = "bob", Status = AccessRequestState.Pending, Requested = DateTime.UtcNow });

            (await Renamed(Bob, displayName: "Bobby")).Should().BeFalse("they aren't a member");

            requests.Find(asked.Id).DisplayName.Should().Be("Bobby");
        }

        [Test]
        public async Task the_start_catches_up_a_rename_missed_while_offline_and_a_channel_renamed_by_hand()
        {
            await AddUser(Alice);
            await AddUser(Bob);
            Discord.Members[Alice] = Discord.Members[Alice] with { Username = "alice2", DisplayName = "Al" };
            await Discord.RenameChannelAsync(ChannelOf(Bob), "requests-somebody");

            await Names.CatchUpAsync();

            UserFor(Alice).DisplayName.Should().Be("Al");
            ChannelFor(Alice).Name.Should().Be(ChannelLayout.UserChannelName("alice2"));
            Discord.Roles[RoleOf(Alice)].Should().Be(ChannelLayout.UserRoleName("alice2"));
            ChannelFor(Bob).Name.Should().Be(ChannelLayout.UserChannelName("bob"), "a channel that doesn't match is put right");
            AuditTrail.Count(e => e.Action == MemberNameService.RenameAction).Should().Be(1, "only Alice was renamed");
        }
    }
}
