using NzbDrone.Core.Messaging.Commands;

namespace Requestarr.Core.Portal
{
    // Onboarding the people picked in the web app, as a Servarr command, so it shows in System, then Tasks while it
    // runs and its messages in Events, as Sonarr's long jobs do. The run (who, with what access) waits in
    // PortalOnboarding under RunId.
    public class OnboardMembersCommand : Command
    {
        public string RunId { get; set; }

        public override bool SendUpdatesToClient => true;

        public override bool UpdateScheduledTask => false;

        public override string CompletionMessage => "Onboarding finished";
    }
}
