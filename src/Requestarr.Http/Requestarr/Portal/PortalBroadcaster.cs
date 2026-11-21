using System;
using NzbDrone.Common.TPL;
using NzbDrone.Core.Datastore.Events;
using NzbDrone.Core.Messaging.Events;
using NzbDrone.SignalR;
using Requestarr.Core.Access;
using Requestarr.Core.Backends;
using Requestarr.Core.Removals;
using Requestarr.Core.Requests;
using Requestarr.Core.Users;

namespace Requestarr.Http.Portal
{
    // Tells the web app's open pages that the portal changed, wherever the change came from (Discord, the web app, a
    // download), as Servarr's controllers broadcast their resources (RestControllerWithSignalR): a "requests",
    // "members" or "queue" message with the "sync" action, which Servarr's SignalRConnector turns into reading them
    // again, as Sonarr's pages do. A burst of changes (an import, a vote closing) sends one message after it settles.
    public class PortalBroadcaster : IHandle<ModelEvent<Request>>,
                                     IHandle<ModelEvent<RequestSubscriber>>,
                                     IHandle<ModelEvent<RemovalProposal>>,
                                     IHandle<ModelEvent<RequestarrUser>>,
                                     IHandle<ModelEvent<UserMediaAccess>>,
                                     IHandle<ModelEvent<AccessRequest>>,
                                     IHandle<ModelEvent<Outage>>,
                                     IHandle<AdminQueueChangedEvent>
    {
        // How long the portal must be quiet before the pages are told.
        private static readonly TimeSpan Settle = TimeSpan.FromSeconds(1);

        private readonly IBroadcastSignalRMessage _signalR;
        private readonly Debouncer _requests;
        private readonly Debouncer _members;
        private readonly Debouncer _queue;

        public PortalBroadcaster(IBroadcastSignalRMessage signalR)
        {
            _signalR = signalR;
            _requests = new Debouncer(() => Send("requests"), Settle);
            _members = new Debouncer(() => Send("members"), Settle);
            _queue = new Debouncer(() => Send("queue"), Settle);
        }

        public void Handle(ModelEvent<Request> message) => RequestsChanged();

        public void Handle(ModelEvent<RequestSubscriber> message) => RequestsChanged();

        public void Handle(ModelEvent<RemovalProposal> message) => RequestsChanged();

        public void Handle(ModelEvent<RequestarrUser> message) => _members.Execute();

        public void Handle(ModelEvent<UserMediaAccess> message) => _members.Execute();

        public void Handle(ModelEvent<AccessRequest> message) => _queue.Execute();

        public void Handle(ModelEvent<Outage> message) => _queue.Execute();

        public void Handle(AdminQueueChangedEvent message) => _queue.Execute();

        // A request's change shows in Requests, in its requester's and followers' counts, and in the queue.
        private void RequestsChanged()
        {
            _requests.Execute();
            _members.Execute();
            _queue.Execute();
        }

        private void Send(string name)
        {
            if (_signalR.IsConnected)
            {
                _signalR.BroadcastMessage(new SignalRMessage { Name = name, Body = new { Action = "sync" } });
            }
        }
    }
}
