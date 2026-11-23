using NzbDrone.Core.Messaging.Commands;

namespace Requestarr.Core.Imports
{
    // Posting imported history in Discord, as a Servarr command like the import itself, so it shows in System, then Tasks
    // while it runs. It posts what's queued (HistoryPosts) at a slow pace until nothing waits, it's paused or cancelled.
    public class PostHistoryCommand : Command
    {
        public override bool SendUpdatesToClient => true;

        public override bool UpdateScheduledTask => false;

        public override string CompletionMessage => "Imported history posted";
    }
}
