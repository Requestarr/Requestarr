using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using FluentAssertions;
using NUnit.Framework;
using Requestarr.Core.Audit;
using Requestarr.Core.Portal;
using Requestarr.Core.Requests;
using Requestarr.Core.Test.Discord.Harness;
using Requestarr.Core.Users;

namespace Requestarr.Core.Test.Portal
{
    // Add member's bulk onboarding, with the real services and a fake Discord server: each person picked gets the member
    // card's onboarding (role, private channel, their panel) and the access chosen, one after another; someone who can't
    // be onboarded in the middle fails with the reason, and the rest are still onboarded.
    [TestFixture]
    public class PortalOnboardingFixture : DiscordScenario
    {
        private const ulong Carol = 900000000000001004;

        // Not in the server: Discord has no such member, so onboarding them fails.
        private const ulong Gone = 900000000000001099;

        private static readonly Actor Admin = Actor.Web("owner");

        private static readonly List<OnboardAccess> MoviesOnly = new()
        {
            new OnboardAccess(ContentType.Movie, AccessState.AutoApprove, 3, 7, null),
            new OnboardAccess(ContentType.Tv, AccessState.Off, null, null, null)
        };

        [Test]
        public async Task everyone_picked_is_onboarded_in_turn_and_one_failure_in_the_middle_doesnt_stop_the_rest()
        {
            Discord.AddMember(Carol, "carol");
            var onboarding = Mocker.Resolve<PortalOnboarding>();
            onboarding.Pace = TimeSpan.Zero;
            var people = new List<OnboardPerson> { new(Alice.ToString(), "alice"), new(Gone.ToString(), "gone"), new(Carol.ToString(), "carol") };

            var started = onboarding.Start(people, MoviesOnly, Admin);
            await onboarding.WhenDoneAsync(started.Id);
            var run = onboarding.Find(started.Id);

            started.People.Should().HaveCount(3);
            run.IsDone.Should().BeTrue();
            run.People.Select(p => (p.Name, p.State)).Should().Equal(("alice", OnboardState.Added), ("gone", OnboardState.Failed), ("carol", OnboardState.Added));
            run.People[1].Message.Should().NotBeNullOrWhiteSpace();

            foreach (var member in new[] { Alice, Carol })
            {
                var user = Mocker.Resolve<IRequestarrUserRepository>().All().Single(u => Discord.Members[member].DisplayName == u.DisplayName);
                user.Status.Should().Be(UserStatus.Active);
                Discord.Members[member].RoleIds.Should().Contain(ulong.Parse(user.DiscordRoleId), "they got their role");
                var channel = Discord.Channels[ulong.Parse(user.PrivateChannelId)];
                channel.Messages.Should().NotBeEmpty("their panel is in their channel, as the newest message");

                var access = Mocker.Resolve<IUserMediaAccessRepository>().GetForUser(user.Id).ToDictionary(a => a.ContentType);
                access[ContentType.Movie].State.Should().Be(AccessState.AutoApprove);
                access[ContentType.Movie].QuotaLimit.Should().Be(3);
                access[ContentType.Movie].QuotaWindow.Should().Be(TimeSpan.FromDays(7));
                access[ContentType.Tv].State.Should().Be(AccessState.Off);
            }

            Mocker.Resolve<IRequestarrUserRepository>().All().Should().HaveCount(2, "nobody is made for someone who couldn't be onboarded");
        }

        [Test]
        public async Task the_server_list_leaves_out_members_and_bots_and_puts_imported_people_first()
        {
            Discord.AddMember(Carol, "carol");
            Discord.AddMember(900000000000001005, "helperbot", true);
            await Users.AddAsync(Bob, new Dictionary<ContentType, AccessState>(), Admin);
            var imported = Mocker.Resolve<IRequestarrUserRepository>().Insert(new RequestarrUser { DisplayName = "carol", Status = UserStatus.Imported, Added = DateTime.UtcNow });
            Mocker.Resolve<IUserIdentityRepository>().Insert(new UserIdentity { UserId = imported.Id, Provider = UserIdentity.DiscordProvider, ExternalId = Carol.ToString(), Linked = DateTime.UtcNow });
            Mocker.Resolve<IRequestRepository>().Insert(new Request { OwnerUserId = imported.Id, ContentType = ContentType.Movie, Title = "Heat", Status = RequestStatus.NotInLibrary, Requested = DateTime.UtcNow });

            var listed = await Mocker.Resolve<PortalMemberActions>().ServerMembersAsync();

            listed.Select(p => (p.Username, p.ImportedRequests)).Should().Equal(("carol", 1), ("alice", 0), ("mod", 0), ("owner", 0));
            listed.Single(p => p.Username == "mod").Roles.Should().Equal("Requestarr Admin");
        }
    }
}
