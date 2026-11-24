using System.Collections.Generic;
using System.Threading.Tasks;
using Requestarr.Core.Discord.Interactions;
using Requestarr.Core.Imports;
using Requestarr.Core.Users;

namespace Requestarr.Core.Discord.Handlers
{
    // /history: a member has their imported requests posted in their own channel, once. /admin history: an admin does it
    // for one member, or for the whole server.
    public class HistoryHandler : IInteractionHandler
    {
        private readonly ICallerResolver _callers;
        private readonly IUserManagementService _users;
        private readonly IHistoryPosting _posting;

        public HistoryHandler(ICallerResolver callers, IUserManagementService users, IHistoryPosting posting)
        {
            _callers = callers;
            _users = users;
            _posting = posting;
        }

        public IEnumerable<(InteractionKind Kind, string Route)> Routes => new[]
        {
            (InteractionKind.SlashCommand, "history"),
            (InteractionKind.SlashCommand, "admin history")
        };

        public async Task HandleAsync(DiscordInteraction interaction, IInteractionResponder responder)
        {
            var caller = _callers.Resolve(interaction);
            var isOwn = interaction.Route == "history";

            if ((isOwn ? caller.UserDenial : caller.AdminDenial) is { } denial)
            {
                await responder.Ephemeral(denial);
                return;
            }

            // A server with years of requests takes longer than Discord waits for an answer.
            await responder.DeferAsync(ephemeral: true);
            await responder.Ephemeral(isOwn ? Own(interaction, caller) : ForAdmin(interaction, caller));
        }

        private string Own(DiscordInteraction interaction, CallerContext caller) =>
            _posting.PostMember(caller.User.Id, HandlerSupport.ActorFor(interaction, caller)).Message;

        private string ForAdmin(DiscordInteraction interaction, CallerContext caller)
        {
            var actor = HandlerSupport.ActorFor(interaction, caller);

            if (interaction.GetSnowflake("member") is not { } member)
            {
                return _posting.PostServer(actor).Message;
            }

            return _users.FindByDiscordId(member) is { } user ? _posting.PostMember(user.Id, actor).Message : DiscordText.HistoryNoChannel;
        }
    }
}
