using System;
using NzbDrone.Core.Configuration;
using Requestarr.Core.Provisioning;

namespace Requestarr.Core.Removals
{
    // Removal vote timings, in days. Kept in Requestarr's settings (the admin web portal reads and
    // changes them there); provisioning sets the ones it names on every start.
    public interface IRemovalSettings
    {
        int VoteDays { get; }
        int RequesterTimeoutDays { get; }

        // The day of the requester's answer window they're reminded on; 0 when it's too short for one.
        int ReminderDay { get; }

        // The timings removals use: the days above, or dev fast votes' while they're on.
        bool FastVotes { get; }
        FastVoteTimings FastVoteTimings { get; }
        TimeSpan VoteWindow { get; }
        TimeSpan RequesterTimeout { get; }
        TimeSpan? ReminderAfter { get; }

        // Whether a removal may delete anything in Sonarr or Radarr. Off: an agreed removal deletes nothing and waits in
        // the admin queue for an admin to delete it by hand.
        bool DeleteFiles { get; }

        void Apply(RemovalProvisioning provisioning);
    }

    public class RemovalSettings : IRemovalSettings
    {
        public const int DefaultVoteDays = 7;
        public const int DefaultRequesterTimeoutDays = 7;
        public const int DefaultReminderDay = 5;

        private readonly IConfigService _config;
        private readonly IDevFastVotes _fastVotes;

        public RemovalSettings(IConfigService config, IDevFastVotes fastVotes)
        {
            _config = config;
            _fastVotes = fastVotes;
        }

        public bool FastVotes => _fastVotes.On;

        public FastVoteTimings FastVoteTimings => _fastVotes.Timings;

        public TimeSpan VoteWindow => FastVotes ? FastVoteTimings.VoteWindow : TimeSpan.FromDays(VoteDays);

        public TimeSpan RequesterTimeout => FastVotes ? FastVoteTimings.RequesterTimeout : TimeSpan.FromDays(RequesterTimeoutDays);

        public TimeSpan? ReminderAfter
        {
            get
            {
                if (FastVotes)
                {
                    return FastVoteTimings.Reminder;
                }

                return ReminderDay > 0 ? TimeSpan.FromDays(ReminderDay) : null;
            }
        }

        public int VoteDays => AtLeastOne(_config.RemovalVoteDays, DefaultVoteDays);

        public int RequesterTimeoutDays => AtLeastOne(_config.RemovalRequesterTimeoutDays, DefaultRequesterTimeoutDays);

        public int ReminderDay => _config.RemovalReminderDay is var day && day >= 1 && day < RequesterTimeoutDays ? day : 0;

        public bool DeleteFiles => _config.RemovalDeleteFiles;

        public void Apply(RemovalProvisioning provisioning)
        {
            if (provisioning?.VoteDays is { } vote)
            {
                _config.RemovalVoteDays = Math.Max(1, vote);
            }

            if (provisioning?.RequesterTimeoutDays is { } timeout)
            {
                _config.RemovalRequesterTimeoutDays = Math.Max(1, timeout);
            }

            if (provisioning?.ReminderDay is { } reminder)
            {
                _config.RemovalReminderDay = Math.Max(0, reminder);
            }

            if (provisioning?.DeleteFiles is { } deleteFiles)
            {
                _config.RemovalDeleteFiles = deleteFiles;
            }
        }

        private static int AtLeastOne(int value, int fallback) => value >= 1 ? value : fallback;
    }
}
