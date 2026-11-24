using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Discord;
using Discord.WebSocket;
using NLog;
using NzbDrone.Common.Messaging;
using NzbDrone.Core.Lifecycle;
using NzbDrone.Core.Messaging.Events;
using Requestarr.Core.Audit;
using Requestarr.Core.Discord.Interactions;
using Requestarr.Core.Provisioning;
using Requestarr.Core.Settings;
using Requestarr.Core.Users;

namespace Requestarr.Core.Discord.Gateway
{
    public enum DiscordBotState
    {
        NotConfigured,
        Connecting,
        Connected,
        Disconnected,
        Failed
    }

    public class DiscordStatusChangedEvent : IEvent
    {
    }

    // A person in the server as Discord has them now: their profile changed, or they pressed something.
    public class DiscordMemberSeenEvent : IEvent
    {
        public DiscordMemberSeenEvent(GuildMember member)
        {
            Member = member;
        }

        public GuildMember Member { get; }
    }

    // A Discord server the bot is or was in, for the web app's server cards. Ours: the one Requestarr runs in; every other
    // one it only reads (for an import from Requestrr). Not reachable: Discord says it's unavailable, or the bot isn't in it
    // any more (not joined), with the invite that adds it back to that server. The ID is text, since JavaScript can't hold
    // it as a number.
    public record DiscordServer(string Id, string Name, string IconUrl, bool IsOurs, bool Reachable, bool Joined = true)
    {
        public string ReconnectUrl { get; init; }
    }

    // The bot as Discord shows it, once it's connected.
    public record DiscordBotIdentity(string Name, string AvatarUrl);

    public interface IDiscordClientHost
    {
        DiscordSocketClient Client { get; }
        DiscordBotState State { get; }
        string LastError { get; }

        // Connected to Discord, whatever came of setting up its server.
        bool IsOnline { get; }

        // Every server the bot is in, Requestarr's first; Requestarr's own server too when the bot isn't in it.
        IReadOnlyList<DiscordServer> Servers { get; }
        DiscordBotIdentity Bot { get; }

        // Makes a server the bot is in Requestarr's, and sets it up there. False: the bot isn't in it, or a
        // provisioning file says which server it is.
        Task<bool> ChooseServerAsync(ulong guildId, Actor actor);

        // The bot leaves a server it only reads; never Requestarr's own. False when it isn't in that server.
        Task<bool> LeaveServerAsync(ulong guildId);
        Task StartAsync(DiscordSettings settings);
        Task StopAsync();

        // Reconnects with the settings as they are now (the web app changed them): stops, then starts if configured.
        Task RestartAsync();
        Task WaitUntilReadyAsync(TimeSpan timeout);
    }

    // Owns the Discord.Net gateway connection: login, events, command registration, the
    // watchdog, and handing interactions to the dispatcher.
    public class DiscordClientHost : IDiscordClientHost,
                                     IHandleAsync<ProvisioningCompletedEvent>,
                                     IHandle<ApplicationShutdownRequested>
    {
        // Only what Requestarr needs: server structure, member join/leave and role changes, and that a message was
        // posted (its own, so panels move below it; message text isn't read, so no privileged message intent).
        public static readonly GatewayIntents Intents = GatewayIntents.Guilds | GatewayIntents.GuildMembers | GatewayIntents.GuildMessages;

        private static readonly TimeSpan WatchdogInterval = TimeSpan.FromSeconds(30);

        private readonly IDiscordSettingsStore _settings;
        private readonly Lazy<IInteractionDispatcher> _dispatcher;
        private readonly Lazy<IGuildSetupService> _guildSetup;
        private readonly Lazy<IUserManagementService> _users;
        private readonly Lazy<IPanelService> _panels;
        private readonly Lazy<Requestarr.Core.Requests.IRequestFeed> _feed;
        private readonly Lazy<Requestarr.Core.Access.IAccessRequestService> _access;
        private readonly Lazy<IDiscordServerChoice> _serverChoice;
        private readonly Lazy<IDiscordAdmins> _admins;
        private readonly IEventAggregator _eventAggregator;
        private readonly Logger _logger;
        private readonly SemaphoreSlim _startLock = new(1, 1);

        private DiscordWatchdog _watchdog;
        private Timer _watchdogTimer;
        private TaskCompletionSource _ready = new(TaskCreationOptions.RunContinuationsAsynchronously);

        public DiscordClientHost(IDiscordSettingsStore settings,
                                 Lazy<IInteractionDispatcher> dispatcher,
                                 Lazy<IGuildSetupService> guildSetup,
                                 Lazy<IUserManagementService> users,
                                 Lazy<IPanelService> panels,
                                 Lazy<Requestarr.Core.Requests.IRequestFeed> feed,
                                 Lazy<Requestarr.Core.Access.IAccessRequestService> access,
                                 Lazy<IDiscordServerChoice> serverChoice,
                                 Lazy<IDiscordAdmins> admins,
                                 IEventAggregator eventAggregator,
                                 Logger logger)
        {
            _settings = settings;
            _dispatcher = dispatcher;
            _guildSetup = guildSetup;
            _users = users;
            _panels = panels;
            _feed = feed;
            _access = access;
            _serverChoice = serverChoice;
            _admins = admins;
            _eventAggregator = eventAggregator;
            _logger = logger;
        }

        public DiscordSocketClient Client { get; private set; }
        public DiscordBotState State { get; private set; } = DiscordBotState.NotConfigured;
        public string LastError { get; private set; }

        public bool IsOnline => Client?.ConnectionState == ConnectionState.Connected;

        public IReadOnlyList<DiscordServer> Servers
        {
            get
            {
                var ours = _settings.Current.GuildId;
                var guilds = Client?.Guilds.ToList() ?? new List<SocketGuild>();
                var servers = guilds
                    .Select(g => new DiscordServer(Text(g.Id), g.Name, g.IconUrl, g.Id == ours, g.IsConnected))
                    .OrderByDescending(s => s.IsOurs).ThenBy(s => s.Name, StringComparer.CurrentCultureIgnoreCase)
                    .ToList();

                if (ours != 0 && IsOnline && guilds.All(g => g.Id != ours))
                {
                    servers.Insert(0, new DiscordServer(Text(ours), null, null, IsOurs: true, Reachable: false, Joined: false));
                }

                return servers;
            }
        }

        public DiscordBotIdentity Bot => Client?.CurrentUser is { } bot ? new DiscordBotIdentity(bot.GlobalName ?? bot.Username, bot.GetDisplayAvatarUrl()) : null;

        // Replaceable in tests; the real one ends the process so Docker restarts it.
        public Action<string> Terminate { get; set; } = reason =>
        {
            LogManager.GetCurrentClassLogger().Fatal("Discord watchdog: {0}. Exiting so the container restarts.", reason);
            LogManager.Flush();
            Environment.Exit(1);
        };

        public void HandleAsync(ProvisioningCompletedEvent message)
        {
            StartIfConfiguredAsync().GetAwaiter().GetResult();
        }

        public async Task RestartAsync()
        {
            await StopAsync();
            await StartIfConfiguredAsync();
        }

        private async Task StartIfConfiguredAsync()
        {
            var settings = _settings.Current;

            if (!settings.CanConnect)
            {
                SetState(DiscordBotState.NotConfigured, null);
                return;
            }

            await StartAsync(settings);
        }

        public void Handle(ApplicationShutdownRequested message)
        {
            StopAsync().GetAwaiter().GetResult();
        }

        public async Task StartAsync(DiscordSettings settings)
        {
            await _startLock.WaitAsync();

            try
            {
                if (Client != null)
                {
                    return;
                }

                _ready = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);

                Client = new DiscordSocketClient(new DiscordSocketConfig
                {
                    GatewayIntents = Intents,
                    AlwaysDownloadUsers = false,
                    LogLevel = LogSeverity.Info
                });

                Client.Log += OnLog;
                Client.Connected += OnConnected;
                Client.Disconnected += OnDisconnected;
                Client.LatencyUpdated += (_, _) =>
                {
                    _watchdog?.OnHeartbeat();
                    return Task.CompletedTask;
                };
                Client.Ready += OnReady;
                Client.InteractionCreated += OnInteraction;
                Client.JoinedGuild += OnJoinedServer;
                Client.LeftGuild += OnServersChanged;
                Client.GuildAvailable += OnServersChanged;
                Client.GuildUnavailable += OnServersChanged;
                Client.UserJoined += OnUserJoined;
                Client.UserLeft += OnUserLeft;
                Client.GuildMemberUpdated += OnMemberUpdated;
                Client.UserUpdated += OnUserUpdated;
                Client.MessageReceived += OnMessage;

                _watchdog = new DiscordWatchdog(() => DateTime.UtcNow, reason => Terminate(reason));
                _watchdogTimer = new Timer(_ => _watchdog.Check(), null, WatchdogInterval, WatchdogInterval);

                SetState(DiscordBotState.Connecting, null);

                await Client.LoginAsync(TokenType.Bot, settings.BotToken);
                await Client.StartAsync();
            }
            catch (Exception ex)
            {
                // A login that failed (a wrong token) never connects: without its client and watchdog, the watchdog
                // can't restart the app over it. A corrected setting starts a new client.
                _logger.Error(ex, "Discord bot failed to start");
                _watchdogTimer?.Dispose();
                _watchdogTimer = null;
                Client?.Dispose();
                Client = null;
                SetState(DiscordBotState.Failed, ex.Message);
            }
            finally
            {
                _startLock.Release();
            }
        }

        public async Task StopAsync()
        {
            var client = Client;

            if (client == null)
            {
                return;
            }

            _watchdogTimer?.Dispose();
            _watchdogTimer = null;
            Client = null;

            try
            {
                await client.StopAsync();
                await client.LogoutAsync();
            }
            catch (Exception ex)
            {
                _logger.Debug(ex, "Error while stopping the Discord client");
            }

            client.Dispose();
        }

        public async Task WaitUntilReadyAsync(TimeSpan timeout)
        {
            if (await Task.WhenAny(_ready.Task, Task.Delay(timeout)) != _ready.Task)
            {
                throw new TimeoutException($"Discord did not become ready within {timeout.TotalSeconds:0} seconds: {LastError}");
            }
        }

        private Task OnConnected()
        {
            _watchdog?.OnConnected();
            SetState(DiscordBotState.Connected, null);
            return Task.CompletedTask;
        }

        private Task OnDisconnected(Exception ex)
        {
            _watchdog?.OnDisconnected();
            SetState(DiscordBotState.Disconnected, ex?.Message);
            return Task.CompletedTask;
        }

        private async Task OnReady()
        {
            try
            {
                await LearnApplicationAsync();

                if (_settings.Current.GuildId == 0)
                {
                    await ChooseOnlyServerAsync();
                    return;
                }

                await SetUpServerAsync();
            }
            catch (Exception ex)
            {
                _logger.Error(ex, "Discord server setup failed");
                SetState(DiscordBotState.Failed, ex.Message);
            }
        }

        // The application the token belongs to, so the invite links work without the admin copying its ID.
        private async Task LearnApplicationAsync()
        {
            if (_settings.Current.ApplicationId != 0)
            {
                return;
            }

            var application = await Client.GetApplicationInfoAsync();
            _serverChoice.Value.SaveApplication(application.Id);
        }

        // No server chosen yet: one the bot is alone in, added with the full invite, is Requestarr's; in several, the
        // admin picks in the web app. A server it was added to only to read (for an import) never is.
        private async Task ChooseOnlyServerAsync()
        {
            if (Client.Guilds.Count == 1 && CanRunIn(Client.Guilds.First()))
            {
                await ChooseServerAsync(Client.Guilds.First().Id, Actor.System);
                return;
            }

            ServersChanged();
        }

        private static bool CanRunIn(SocketGuild guild) => guild.CurrentUser?.GuildPermissions is { ManageChannels: true, ManageRoles: true };

        // Requestarr's channels, roles, commands and panels in its server.
        private async Task SetUpServerAsync()
        {
            var guild = Client.GetGuild(_settings.Current.GuildId);

            if (guild == null)
            {
                SetState(DiscordBotState.Failed, $"The bot is not a member of server {_settings.Current.GuildId}. Add it again from Settings, then Connections.");
                return;
            }

            await _guildSetup.Value.EnsureAsync();

            if (_admins.Value != null)
            {
                await _admins.Value.EnsureOwnerAsync();
            }

            // Replaces every registered command, so commands from older versions disappear.
            await guild.BulkOverwriteApplicationCommandAsync(DiscordCommandDefinitions.Build().ToArray());
            await _panels.Value.EnsureAllAsync();
            await _feed.Value.SeedAsync();

            // Blocked roles removed by hand while the bot was away count as unblocking.
            if (_access.Value != null)
            {
                await _access.Value.ReconcileBlocksAsync();
            }

            // Members role sync and what's new on upgrade (AnnouncementService).
            _eventAggregator.PublishEvent(new Announcements.DiscordReadyEvent());

            _logger.Info("Discord bot ready in server '{0}'", guild.Name);
            _ready.TrySetResult();

            // The setup that failed before works now: the bot was added back to its server.
            if (State == DiscordBotState.Failed && IsOnline)
            {
                SetState(DiscordBotState.Connected, null);
            }
            else
            {
                ServersChanged();
            }
        }

        public async Task<bool> ChooseServerAsync(ulong guildId, Actor actor)
        {
            if (Client?.GetGuild(guildId) == null || !_serverChoice.Value.SaveServer(guildId, actor))
            {
                return false;
            }

            try
            {
                await SetUpServerAsync();
            }
            catch (Exception ex)
            {
                _logger.Error(ex, "Discord server setup failed");
                SetState(DiscordBotState.Failed, ex.Message);
            }

            return true;
        }

        public async Task<bool> LeaveServerAsync(ulong guildId)
        {
            var guild = Client?.GetGuild(guildId);

            if (guild == null || guildId == _settings.Current.GuildId)
            {
                return false;
            }

            await guild.LeaveAsync();
            return true;
        }

        // The bot was added to a server: with none chosen yet, the only one it's in becomes Requestarr's; Requestarr's
        // own, added back, is set up again; any other shows as a card that only reads.
        private Task OnJoinedServer(SocketGuild guild)
        {
            _ = Task.Run(async () =>
            {
                try
                {
                    if (_settings.Current.GuildId == 0)
                    {
                        await ChooseOnlyServerAsync();
                        return;
                    }

                    // Added back to Requestarr's own server (it had been removed): set up there again.
                    if (guild.Id == _settings.Current.GuildId)
                    {
                        await SetUpServerAsync();
                        return;
                    }

                    ServersChanged();
                }
                catch (Exception ex)
                {
                    _logger.Error(ex, "Handling the bot joining server {0} failed", guild.Id);
                    SetState(DiscordBotState.Failed, ex.Message);
                }
            });

            return Task.CompletedTask;
        }

        private Task OnServersChanged(SocketGuild guild)
        {
            ServersChanged();
            return Task.CompletedTask;
        }

        private void ServersChanged() => _eventAggregator.PublishEvent(new DiscordStatusChangedEvent());

        private static string Text(ulong id) => id.ToString(CultureInfo.InvariantCulture);

        // A press or command from another server the bot is in (one it was invited to only for an import) is ignored.
        private Task OnInteraction(SocketInteraction interaction)
        {
            if (!ServerScope.IsOursOrDirect(interaction.GuildId, _settings.Current.GuildId))
            {
                return Task.CompletedTask;
            }

            // Never block the gateway task.
            _ = Task.Run(async () =>
            {
                try
                {
                    var mapped = DiscordInteractionMapper.Map(interaction);

                    if (mapped != null)
                    {
                        await _dispatcher.Value.DispatchAsync(mapped, new DiscordNetResponder(interaction));
                    }

                    Seen(interaction.User);
                }
                catch (Exception ex)
                {
                    _logger.Error(ex, "Discord interaction failed");
                }
            });

            return Task.CompletedTask;
        }

        // Someone joined: they're in the server now (Seen checks it's this one), so someone known only from an import is too.
        private Task OnUserJoined(SocketGuildUser user)
        {
            if (ServerScope.IsOurs(user.Guild.Id, _settings.Current.GuildId))
            {
                Seen(user);
            }

            return Task.CompletedTask;
        }

        private Task OnUserLeft(SocketGuild guild, SocketUser user)
        {
            if (!ServerScope.IsOurs(guild.Id, _settings.Current.GuildId) || user.IsBot)
            {
                return Task.CompletedTask;
            }

            _ = Task.Run(async () =>
            {
                try
                {
                    await _users.Value.HandleMemberLeftAsync(user.Id);
                }
                catch (Exception ex)
                {
                    _logger.Error(ex, "Handling member departure failed");
                }
            });

            return Task.CompletedTask;
        }

        // Something the bot posted in the server: that channel's panel moves below it (PanelService waits a moment so
        // several posts in a row move it once). Private replies never reach the channel, so they don't count.
        private Task OnMessage(SocketMessage message)
        {
            if (message.Author.Id == Client?.CurrentUser?.Id && message.Channel is SocketGuildChannel channel && ServerScope.IsOurs(channel.Guild.Id, _settings.Current.GuildId) &&
                !message.Flags.GetValueOrDefault().HasFlag(MessageFlags.Ephemeral) && message.Type != MessageType.ChannelPinnedMessage)
            {
                _panels.Value.NoticeBotMessage(channel.Id, message.Id);
            }

            return Task.CompletedTask;
        }

        // A member's roles changed: removing the blocked role by hand unblocks them. Their names may have changed too.
        private Task OnMemberUpdated(Cacheable<SocketGuildUser, ulong> before, SocketGuildUser after)
        {
            if (!ServerScope.IsOurs(after.Guild.Id, _settings.Current.GuildId) || after.IsBot)
            {
                return Task.CompletedTask;
            }

            Seen(after);

            _ = Task.Run(async () =>
            {
                try
                {
                    if (_access.Value != null)
                    {
                        await _access.Value.MemberRolesChangedAsync(after.Id, after.Roles.Select(r => r.Id).ToList());
                    }
                }
                catch (Exception ex)
                {
                    _logger.Error(ex, "Handling a member's role change failed");
                }
            });

            return Task.CompletedTask;
        }

        // A Discord username changed (Discord may tell the server as a member update instead; either is handled).
        private Task OnUserUpdated(SocketUser before, SocketUser after)
        {
            Seen(after as SocketGuildUser ?? Client.GetGuild(_settings.Current.GuildId)?.GetUser(after.Id));

            return Task.CompletedTask;
        }

        // Someone in this server, as they are now; a press from a direct message carries no server profile. Never in the
        // way of anything else: a failure here is only logged.
        private void Seen(IUser user)
        {
            try
            {
                if (user is SocketGuildUser member && ServerScope.IsOurs(member.Guild?.Id, _settings.Current.GuildId) && !member.IsBot)
                {
                    _eventAggregator.PublishEvent(new DiscordMemberSeenEvent(DiscordNetGateway.ToMember(member)));
                }
            }
            catch (Exception ex)
            {
                _logger.Warn(ex, "Couldn't pass on member {0}'s profile", user?.Id);
            }
        }

        private Task OnLog(LogMessage message)
        {
            var level = message.Severity switch
            {
                LogSeverity.Critical => LogLevel.Fatal,
                LogSeverity.Error => LogLevel.Error,
                LogSeverity.Warning => LogLevel.Warn,
                LogSeverity.Info => LogLevel.Info,
                _ => LogLevel.Debug
            };

            _logger.Log(level, message.Exception, "Discord.Net {0}: {1}", message.Source, message.Message);
            return Task.CompletedTask;
        }

        private void SetState(DiscordBotState state, string error)
        {
            State = state;
            LastError = error;
            _eventAggregator.PublishEvent(new DiscordStatusChangedEvent());
        }
    }

    public static class DiscordInteractionMapper
    {
        public static DiscordInteraction Map(SocketInteraction interaction)
        {
            var member = interaction.User as SocketGuildUser;
            var roles = member?.Roles.Select(r => r.Id).ToList() ?? new List<ulong>();

            // Who and where are the same for every kind; the rest is what that kind carries.
            DiscordInteraction Base(InteractionKind kind,
                                    string command = null,
                                    string customId = null,
                                    Dictionary<string, object> options = null,
                                    IReadOnlyList<string> values = null,
                                    IReadOnlyDictionary<string, string> fields = null,
                                    string focusedOption = null,
                                    ulong? messageId = null) => new()
            {
                Kind = kind,
                CommandName = command,
                CustomId = customId,
                Options = options ?? new Dictionary<string, object>(),
                FocusedOption = focusedOption,
                Values = values ?? new List<string>(),
                Fields = fields ?? new Dictionary<string, string>(),
                UserId = interaction.User.Id,
                Username = interaction.User.Username,
                DisplayName = member?.DisplayName ?? interaction.User.GlobalName ?? interaction.User.Username,
                RoleIds = roles,
                GuildId = interaction.GuildId ?? 0,
                ChannelId = interaction.ChannelId ?? 0,
                MessageId = messageId
            };

            switch (interaction)
            {
                case SocketSlashCommand command:
                {
                    var path = new List<string> { command.Data.Name };
                    var options = new Dictionary<string, object>();
                    Flatten(command.Data.Options, path, options);
                    return Base(InteractionKind.SlashCommand, command: string.Join(' ', path), options: options);
                }

                case SocketMessageComponent component:
                {
                    var kind = component.Data.Type == ComponentType.Button ? InteractionKind.Button : InteractionKind.SelectMenu;
                    return Base(kind, customId: component.Data.CustomId, values: component.Data.Values?.ToList(), messageId: component.Message?.Id);
                }

                case SocketAutocompleteInteraction autocomplete:
                {
                    var options = autocomplete.Data.Options.ToDictionary(o => o.Name, o => o.Value);
                    return Base(InteractionKind.Autocomplete, command: autocomplete.Data.CommandName, options: options, focusedOption: autocomplete.Data.Current?.Name);
                }

                case SocketModal modal:
                {
                    var fields = modal.Data.Components.ToDictionary(c => c.CustomId, c => c.Value);
                    return Base(InteractionKind.ModalSubmit, customId: modal.Data.CustomId, fields: fields, messageId: modal.Message?.Id);
                }

                default:
                    return null;
            }
        }

        private static void Flatten(IEnumerable<SocketSlashCommandDataOption> options, List<string> path, Dictionary<string, object> values)
        {
            foreach (var option in options ?? Enumerable.Empty<SocketSlashCommandDataOption>())
            {
                if (option.Type == ApplicationCommandOptionType.SubCommand || option.Type == ApplicationCommandOptionType.SubCommandGroup)
                {
                    path.Add(option.Name);
                    Flatten(option.Options, path, values);
                    continue;
                }

                values[option.Name] = option.Value switch
                {
                    IUser user => user.Id,
                    IRole role => role.Id,
                    IChannel channel => channel.Id,
                    var other => other
                };
            }
        }
    }
}
