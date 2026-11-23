using System.Linq;
using System.Threading.Tasks;
using Requestarr.Core.Discord.Interactions;

namespace Requestarr.Core.Test.Discord.Harness
{
    public abstract class RequestCardScenario : RequestScenario
    {
        public const int DarkMatter = 425409;
        public const int DarkMatter2015 = 292174;

        protected Task<InteractionRun> Click(ulong user, string customId) =>
            Harness.SendAsync(FakeInteraction.Button(customId).By(user).InGuild(FakeDiscordGuild.Guild).InChannel(ChannelOf(user)));

        protected Task<InteractionRun> Choose(ulong user, string customId, params string[] values) =>
            Harness.SendAsync(FakeInteraction.SelectMenu(customId, values).By(user).InGuild(FakeDiscordGuild.Guild).InChannel(ChannelOf(user)));

        protected static string Field(InteractionReply reply, string name) => reply.Embeds.Single().Fields.SingleOrDefault(f => f.Key == name).Value;

        protected static string[] FieldNames(InteractionReply reply) => reply.Embeds.Single().Fields.Select(f => f.Key).ToArray();
    }
}
