using System;
using NzbDrone.Common.TPL;
using NzbDrone.Core.Messaging.Events;
using NzbDrone.SignalR;
using Requestarr.Core.Discord.Gateway;

namespace Requestarr.Http.Settings
{
    // Tells the web app's open setup and Connections pages that the bot's state or its servers changed (it connected,
    // joined a server, was removed from one), as PortalBroadcaster tells the portal's pages: a "setup" message they
    // answer by reading the setup again, so the page notices the join on its own.
    public class SetupBroadcaster : IHandle<DiscordStatusChangedEvent>
    {
        private static readonly TimeSpan Settle = TimeSpan.FromSeconds(1);

        private readonly IBroadcastSignalRMessage _signalR;
        private readonly Debouncer _setup;

        public SetupBroadcaster(IBroadcastSignalRMessage signalR)
        {
            _signalR = signalR;
            _setup = new Debouncer(Send, Settle);
        }

        public void Handle(DiscordStatusChangedEvent message) => _setup.Execute();

        private void Send()
        {
            if (_signalR.IsConnected)
            {
                _signalR.BroadcastMessage(new SignalRMessage { Name = "setup", Body = new { Action = "sync" } });
            }
        }
    }
}
