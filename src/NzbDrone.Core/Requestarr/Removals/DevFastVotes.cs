using System;
using System.IO;
using NzbDrone.Common.EnvironmentInfo;

namespace Requestarr.Core.Removals
{
    // Local test stacks only (QA and dev): removal timings in minutes, so a vote closing, the requester's
    // reminder and silence-as-yes can be watched by hand. On while a marker file is in Requestarr's data folder
    // (docker/dev.ps1 fast-votes on), and only when the container runs as a local test stack
    // (REQUESTARR_DEV_STACK, which only docker/compose.yaml sets). reset-qa and reset-dev wipe the data folder,
    // so a reset is always back to the real settings.
    public interface IDevFastVotes
    {
        bool On { get; }

        FastVoteTimings Timings { get; }
    }

    public record FastVoteTimings(TimeSpan VoteWindow, TimeSpan RequesterTimeout, TimeSpan Reminder);

    public class DevFastVotes : IDevFastVotes
    {
        public const string MarkerFile = "dev-fast-votes";
        public const string DevStackVariable = "REQUESTARR_DEV_STACK";

        // What `fast-votes on` gives: minutes, slow enough to watch and press by hand.
        public static readonly FastVoteTimings Minutes = new(TimeSpan.FromMinutes(5), TimeSpan.FromMinutes(5), TimeSpan.FromMinutes(3));

        // The automated live check's: the same steps in seconds, so a run doesn't wait out real minutes.
        public static readonly FastVoteTimings Seconds = new(TimeSpan.FromSeconds(30), TimeSpan.FromSeconds(30), TimeSpan.FromSeconds(15));

        private readonly IAppFolderInfo _folders;

        public DevFastVotes(IAppFolderInfo folders)
        {
            _folders = folders;
        }

        // Settable for tests.
        public Func<string, string> Variable { get; set; } = Environment.GetEnvironmentVariable;

        // Settable for the live check.
        public FastVoteTimings Timings { get; set; } = Minutes;

        public bool DevStack => string.Equals(Variable(DevStackVariable), "true", StringComparison.OrdinalIgnoreCase);

        public bool On => DevStack && File.Exists(Path.Combine(_folders.AppDataFolder, MarkerFile));
    }
}
