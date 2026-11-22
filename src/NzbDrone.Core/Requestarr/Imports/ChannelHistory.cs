using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json.Serialization;
using System.Threading.Tasks;
using Discord;
using Discord.WebSocket;
using Requestarr.Core.Announcements;
using Requestarr.Core.Discord;
using Requestarr.Core.Discord.Gateway;
using Requestarr.Core.Settings;

namespace Requestarr.Core.Imports
{
    // A channel an admin names for an import, as the bot sees it. Problem: why it can't be read, in words, or null.
    // HasSourceReplies: the old bot (Requestrr) answered there, going by the channel's newest messages.
    public record ImportChannel([property: JsonNumberHandling(JsonNumberHandling.WriteAsString)] ulong Id, string Server, string Name, bool CanRead, string Problem, bool HasSourceReplies = false);

    // What the Import page shows above the channels: the link that adds the bot to another server only to read it,
    // whether the bot may read other bots' messages (unknown while it isn't connected), and the Developer Portal page
    // where that's turned on. The links are null until the bot's application ID is known.
    public record ImportAccess(string ReadInviteUrl, bool? MessageContent, string PortalUrl);

    // Reading old request channels: in any server the bot is in, through Discord's API. Only reads: nothing is posted,
    // edited or reacted to. Discord.Net waits out Discord's rate limits between pages.
    public interface IChannelHistory
    {
        Task<ImportAccess> AccessAsync();

        Task<ImportChannel> DescribeAsync(ulong channelId);

        // Every text channel the bot can read, in every server it's in: a shortcut for picking them.
        Task<IReadOnlyList<ImportChannel>> ReadableChannelsAsync();

        // A page of the channel's messages before the one given (or the newest), newest first; empty at its start.
        Task<IReadOnlyList<HistoryMessage>> ReadAsync(ulong channelId, ulong? before);
    }

    public static class ImportText
    {
        public const string NoChannels = "Name at least one channel.";
        public const string NotConnected = "The bot isn't connected to Discord right now. Check Settings, then Connections, Discord, and try again.";
        public const string NoMessageContent = "Discord won't show the bot other bots' messages: turn on **Message Content Intent** in the Discord Developer Portal (your application, then Bot, then Privileged Gateway Intents), save, and try again.";

        public const string NotText = "That channel isn't a text channel (a category, forum, voice or thread channel): Requestrr answered in text channels.";

        public static string Gone(string channel) => $"The bot lost sight of channel {channel} part way through reading it. Check it's still there and the bot can read it, then preview again.";

        public static string NotFound(string invite) =>
            "The bot can't find that channel: it isn't in that channel's server, or there's no channel with that ID." +
            (invite != null ? $" Invite it to that server to read it (it only reads there): {invite}" : string.Empty);

        public static string CantSee(string channel, string server) =>
            $"The bot can't see #{channel} in {server}: give its role **View Channel** and **Read Message History** in that channel.";

        public static string NoHistory(string channel, string server) =>
            $"The bot can see #{channel} in {server} but can't read what was said before: give its role **Read Message History** in that channel.";
    }

    // Whether the application has the Message Content intent (or its limited form). A yes is kept; a no is asked of
    // Discord again each time, fresh (Discord.Net keeps its first answer otherwise), so turning the intent on in the
    // Developer Portal is seen without restarting Requestarr.
    public class MessageContentIntent
    {
        private readonly Func<Task<ApplicationFlags>> _fresh;
        private bool _on;

        public MessageContentIntent(Func<Task<ApplicationFlags>> fresh)
        {
            _fresh = fresh;
        }

        public async Task<bool> IsOnAsync()
        {
            if (!_on)
            {
                var flags = await _fresh();
                _on = flags.HasFlag(ApplicationFlags.GatewayMessageContent) || flags.HasFlag(ApplicationFlags.GatewayMessageContentLimited);
            }

            return _on;
        }
    }

    // The bot's view of Discord. The import page names any server the bot isn't in with the read-only invite link.
    public class DiscordChannelHistory
    {
        // Discord's most per request.
        public const int PageSize = 100;

        private readonly IDiscordClientHost _host;
        private DiscordSocketClient _intentClient;
        private MessageContentIntent _messageContent;

        public DiscordChannelHistory(IDiscordClientHost host)
        {
            _host = host;
        }

        private bool IsConnected => _host.Client?.ConnectionState == ConnectionState.Connected;

        // Unknown (null) while the bot isn't connected or Discord doesn't answer: the page still shows its invite link.
        public async Task<bool?> MessageContentAsync()
        {
            if (!IsConnected)
            {
                return null;
            }

            try
            {
                return await CanReadContentAsync();
            }
            catch (Exception ex) when (ex is global::Discord.Net.HttpException or ObjectDisposedException)
            {
                return null;
            }
        }

        public async Task<ImportChannel> DescribeAsync(ulong channelId, string invite)
        {
            if (!IsConnected)
            {
                return new ImportChannel(channelId, null, null, false, ImportText.NotConnected);
            }

            var found = _host.Client.GetChannel(channelId);

            if (found is not SocketTextChannel channel)
            {
                return new ImportChannel(channelId, null, null, false, found == null ? ImportText.NotFound(invite) : ImportText.NotText);
            }

            var problem = await ProblemAsync(channel);

            return new ImportChannel(channelId, channel.Guild.Name, channel.Name, problem == null, problem);
        }

        // Why the bot can't read the channel's history and its messages' content, or null.
        private async Task<string> ProblemAsync(SocketTextChannel channel)
        {
            var permissions = channel.Guild.CurrentUser.GetPermissions(channel);

            if (!permissions.ViewChannel)
            {
                return ImportText.CantSee(channel.Name, channel.Guild.Name);
            }

            if (!permissions.ReadMessageHistory)
            {
                return ImportText.NoHistory(channel.Name, channel.Guild.Name);
            }

            return await CanReadContentAsync() ? null : ImportText.NoMessageContent;
        }

        public async Task<IReadOnlyList<ImportChannel>> ReadableChannelsAsync()
        {
            if (!IsConnected || !await CanReadContentAsync())
            {
                return Array.Empty<ImportChannel>();
            }

            return _host.Client.Guilds.SelectMany(g => g.TextChannels)
                .Where(c => c.Guild.CurrentUser.GetPermissions(c) is { ViewChannel: true, ReadMessageHistory: true })
                .OrderBy(c => c.Guild.Name).ThenBy(c => c.Position)
                .Select(c => new ImportChannel(c.Id, c.Guild.Name, c.Name, true, null))
                .ToList();
        }

        public async Task<IReadOnlyList<HistoryMessage>> ReadAsync(ulong channelId, ulong? before)
        {
            if (_host.Client?.GetChannel(channelId) is not ITextChannel channel)
            {
                throw new InvalidOperationException(ImportText.Gone(channelId.ToString()));
            }

            var page = before.HasValue ? channel.GetMessagesAsync(before.Value, Direction.Before, PageSize) : channel.GetMessagesAsync(PageSize);

            return (await page.FlattenAsync()).Select(Map).ToList();
        }

        // A yes is kept only for the client that gave it: a restarted bot (another application, say) is asked afresh.
        private Task<bool> CanReadContentAsync()
        {
            var client = _host.Client;

            if (_messageContent == null || !ReferenceEquals(client, _intentClient))
            {
                _intentClient = client;
                _messageContent = new MessageContentIntent(() => FreshFlagsAsync(client));
            }

            return _messageContent.IsOnAsync();
        }

        // The application as Discord has it now: Discord.Net hands back its first answer until it's updated.
        private static async Task<ApplicationFlags> FreshFlagsAsync(DiscordSocketClient client)
        {
            var application = await client.GetApplicationInfoAsync();
            await application.UpdateAsync();

            return application.Flags;
        }

        private static HistoryMessage Map(IMessage message)
        {
            var invoked = (message as IUserMessage)?.InteractionMetadata?.User;

            return new HistoryMessage(
                message.Id,
                message.Channel.Id,
                message.Timestamp.UtcDateTime,
                message.Author.Id,
                message.Author.IsBot,
                message.Content ?? string.Empty,
                message.Embeds.Select(e => new HistoryEmbed(e.Title, e.Url)).ToList(),
                invoked?.Id,
                invoked?.GlobalName ?? invoked?.Username,
                message.MentionedUserIds.ToList(),
                message.Components.Count);
        }
    }

    // On the dev stack only, with REQUESTARR_IMPORT_RECORDINGS set to a folder: channels recorded as Discord's API
    // returns them ({channel ID}.json), so an import can be tried without reading anyone's real server.
    public class RecordedChannelHistory
    {
        public const string FolderVariable = "REQUESTARR_IMPORT_RECORDINGS";
        public const string Server = "Recorded channels (dev)";

        private readonly string _folder;

        public RecordedChannelHistory(string folder)
        {
            _folder = folder;
        }

        public IReadOnlyList<ImportChannel> Channels() =>
            Directory.Exists(_folder)
                ? Directory.GetFiles(_folder, "*.json").Select(Path.GetFileNameWithoutExtension).Where(n => ulong.TryParse(n, out _))
                    .Select(n => new ImportChannel(ulong.Parse(n), Server, $"recorded-{n}", true, null)).ToList()
                : Array.Empty<ImportChannel>();

        public ImportChannel Describe(ulong channelId) =>
            Channels().FirstOrDefault(c => c.Id == channelId) ?? new ImportChannel(channelId, null, null, false, ImportText.NotFound(null));

        public IReadOnlyList<HistoryMessage> Read(ulong channelId, ulong? before)
        {
            var file = Path.Combine(_folder, $"{channelId}.json");

            return File.Exists(file)
                ? DiscordMessageJson.ParseAll(File.ReadAllText(file)).Where(m => before == null || m.Id < before).OrderByDescending(m => m.Id).Take(DiscordChannelHistory.PageSize).ToList()
                : Array.Empty<HistoryMessage>();
        }
    }

    // Discord's channels, or the recorded ones on a dev stack whose recordings folder has some (looked at each time, so
    // recordings can be added while it runs).
    public class ChannelHistory : IChannelHistory
    {
        private readonly DiscordChannelHistory _discord;
        private readonly IReleaseEnvironment _environment;
        private readonly IDiscordSettingsStore _settings;

        public ChannelHistory(IDiscordClientHost host, IReleaseEnvironment environment, IDiscordSettingsStore settings)
        {
            _discord = new DiscordChannelHistory(host);
            _environment = environment;
            _settings = settings;
        }

        private ulong ApplicationId => _settings.Current.ApplicationId;

        private string ReadInviteUrl => SetupService.InviteUrl(ApplicationId, SetupService.ReadOnlyInvitePermissions, SetupService.ReadOnlyInviteScope);

        public async Task<ImportAccess> AccessAsync()
        {
            // Recorded channels are read from files, not from Discord, so the intent doesn't apply to them.
            var messageContent = Recorded() != null ? true : await _discord.MessageContentAsync();
            var portal = ApplicationId == 0 ? null : $"https://discord.com/developers/applications/{ApplicationId}/bot";

            return new ImportAccess(ReadInviteUrl, messageContent, portal);
        }

        public Task<ImportChannel> DescribeAsync(ulong channelId) =>
            Recorded() is { } recorded ? Task.FromResult(recorded.Describe(channelId)) : _discord.DescribeAsync(channelId, ReadInviteUrl);

        public Task<IReadOnlyList<ImportChannel>> ReadableChannelsAsync() =>
            Recorded() is { } recorded ? Task.FromResult(recorded.Channels()) : _discord.ReadableChannelsAsync();

        public Task<IReadOnlyList<HistoryMessage>> ReadAsync(ulong channelId, ulong? before) =>
            Recorded() is { } recorded ? Task.FromResult(recorded.Read(channelId, before)) : _discord.ReadAsync(channelId, before);

        private RecordedChannelHistory Recorded()
        {
            var folder = Environment.GetEnvironmentVariable(RecordedChannelHistory.FolderVariable);

            if (_environment.IconSet != "dev" || string.IsNullOrEmpty(folder))
            {
                return null;
            }

            var recorded = new RecordedChannelHistory(folder);
            return recorded.Channels().Count > 0 ? recorded : null;
        }
    }
}
