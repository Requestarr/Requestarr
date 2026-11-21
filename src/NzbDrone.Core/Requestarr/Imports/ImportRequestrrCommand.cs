using System;
using NzbDrone.Core.Messaging.Commands;

namespace Requestarr.Core.Imports
{
    // Writing a confirmed Requestrr import, as a Servarr command, so it shows in System, then Tasks while it runs and
    // its messages in Events, as Sonarr's long jobs do. The preview it writes waits in ImportService under JobId; the
    // admin who confirmed it is recorded on what it writes.
    public class ImportRequestrrCommand : Command
    {
        public Guid JobId { get; set; }

        public string ConfirmedBy { get; set; }

        public int? ConfirmedById { get; set; }

        public override bool SendUpdatesToClient => true;

        public override bool UpdateScheduledTask => false;

        public override string CompletionMessage => "Requestrr import finished";
    }
}
