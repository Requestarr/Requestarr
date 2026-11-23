using NzbDrone.Common.Extensions;

namespace Requestarr.Core.Discord
{
    // Discord connection settings. They come only from the provisioning file and are kept
    // in memory, so the bot token is never written to the database or config.xml.
    public class DiscordSettings
    {
        public string BotToken { get; init; }
        public ulong ApplicationId { get; init; }
        public ulong GuildId { get; init; }
        public const string DefaultAdminRoleName = "Requestarr Admin";

        public ulong? AdminRoleId { get; init; }
        public string AdminRoleName { get; init; } = DefaultAdminRoleName;
        public ulong? AdminChannelId { get; init; }
        public ulong? RequestCategoryId { get; init; }

        public bool IsConfigured => CanConnect && ApplicationId != 0 && GuildId != 0;

        // The token is all the bot needs to connect: it reads its application from Discord, and its server is the one
        // it joins (or the one the admin picks when it's in several).
        public bool CanConnect => BotToken.IsNotNullOrWhiteSpace();
    }

    public interface IDiscordSettingsStore
    {
        DiscordSettings Current { get; }
        void Set(DiscordSettings settings);
    }

    public class DiscordSettingsStore : IDiscordSettingsStore
    {
        public DiscordSettings Current { get; private set; } = new();

        public void Set(DiscordSettings settings)
        {
            Current = settings ?? new DiscordSettings();
        }
    }
}
