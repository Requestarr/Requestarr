using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json.Serialization;
using System.Threading.Tasks;
using Discord;
using Discord.WebSocket;
using Requestarr.Core.Announcements;
using Requestarr.Core.Discord.Gateway;

namespace Requestarr.Core.Imports
{
    // A channel an admin names for an import, as the bot sees it. Problem: why it can't be read, in words, or null.
    public record ImportChannel([property: JsonNumberHandling(JsonNumberHandling.WriteAsString)] ulong Id, string Server, string Name, bool CanRead, string Problem);

    // Reading old request channels: in any server the bot is in, through Discord's API. Only reads: nothing is posted,
    // edited or reacted to. Discord.Net waits out Discord's rate limits between pages.
    public interface IChannelHistory
    {
        Task<ImportChannel> DescribeAsync(ulong channelId);

        // Every text channel the bot can read, in every server it's in: a shortcut for picking them.
        Task<IReadOnlyList<ImportChannel>> ReadableChannelsAsync();

        // A page of the channel's messages before the one given (or the newest), newest first; empty at its start.
        Task<IReadOnlyList<HistoryMessage>> ReadAsync(ulong channelId, ulong? before);
    }

    public static class ImportText
    {
        public const string NoChannels = "Name at least one channel.";
        public const string NotConnected = "The bot isn't connected to Discord right now. Check Settings, then Discord, and try again.";
        public const string NoMessageContent = "Discord won't show the bot other bots' messages: turn on **Message Content Intent** in the Discord Developer Portal (your application, then Bot, then Privileged Gateway Intents), save, and try again.";

        public static string NotFound(string invite) =>
            "The bot can't find that channel: it isn't in that channel's server, or there's no channel with that ID." +
            (invite != null ? $" Invite it to that server first: {invite}" : string.Empty);

        public static string CantSee(string channel, string server) =>
            $"The bot can't see #{channel} in {server}: give its role **View Channel** and **Read Message History** in that channel.";

        public static string NoHistory(string channel, string server) =>
            $"The bot can see #{channel} in {server} but can't read what was said before: give its role **Read Message History** in that channel.";
    }

    // The bot's view of Discord. The import page names any server the bot isn't in with the invite link from the setup.
    public class DiscordChannelHistory
    {
        private const int PageSize = 100;

        private readonly IDiscordClientHost _host;
        private bool? _canReadContent;

        public DiscordChannelHistory(IDiscordClientHost host)
        {
            _host = host;
        }

        public async Task<ImportChannel> DescribeAsync(ulong channelId, string invite)
        {
            if (_host.Client?.ConnectionState != ConnectionState.Connected)
            {
                return new ImportChannel(channelId, null, null, false, ImportText.NotConnected);
            }

            if (_host.Client.GetChannel(channelId) is not SocketTextChannel channel)
            {
                return new ImportChannel(channelId, null, null, false, ImportText.NotFound(invite));
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
            if (_host.Client?.ConnectionState != ConnectionState.Connected || !await CanReadContentAsync())
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
                return Array.Empty<HistoryMessage>();
            }

            var page = before.HasValue ? channel.GetMessagesAsync(before.Value, Direction.Before, PageSize) : channel.GetMessagesAsync(PageSize);

            return (await page.FlattenAsync()).Select(Map).ToList();
        }

        // Whether the application has the Message Content intent (or its limited form), asked once.
        private async Task<bool> CanReadContentAsync()
        {
            _canReadContent ??= (await _host.Client.GetApplicationInfoAsync()).Flags is var flags &&
                                (flags.HasFlag(ApplicationFlags.GatewayMessageContent) || flags.HasFlag(ApplicationFlags.GatewayMessageContentLimited));

            return _canReadContent.Value;
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
                invoked == null ? null : invoked.GlobalName ?? invoked.Username,
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
                ? DiscordMessageJson.ParseAll(File.ReadAllText(file)).Where(m => before == null || m.Id < before).OrderByDescending(m => m.Id).Take(100).ToList()
                : Array.Empty<HistoryMessage>();
        }
    }

    // Discord's channels, or the recorded ones on a dev stack that has them.
    public class ChannelHistory : IChannelHistory
    {
        private readonly DiscordChannelHistory _discord;
        private readonly RecordedChannelHistory _recorded;
        private readonly Settings.ISetupService _setup;

        public ChannelHistory(IDiscordClientHost host, IReleaseEnvironment environment, Settings.ISetupService setup)
        {
            _discord = new DiscordChannelHistory(host);
            _recorded = Recordings(environment);
            _setup = setup;
        }

        public Task<ImportChannel> DescribeAsync(ulong channelId) =>
            _recorded != null ? Task.FromResult(_recorded.Describe(channelId)) : _discord.DescribeAsync(channelId, _setup.Status().InviteUrl);

        public Task<IReadOnlyList<ImportChannel>> ReadableChannelsAsync() =>
            _recorded != null ? Task.FromResult(_recorded.Channels()) : _discord.ReadableChannelsAsync();

        public Task<IReadOnlyList<HistoryMessage>> ReadAsync(ulong channelId, ulong? before) =>
            _recorded != null ? Task.FromResult(_recorded.Read(channelId, before)) : _discord.ReadAsync(channelId, before);

        private static RecordedChannelHistory Recordings(IReleaseEnvironment environment) =>
            environment.IconSet == "dev" && Environment.GetEnvironmentVariable(RecordedChannelHistory.FolderVariable) is { Length: > 0 } folder
                ? new RecordedChannelHistory(folder)
                : null;
    }
}
