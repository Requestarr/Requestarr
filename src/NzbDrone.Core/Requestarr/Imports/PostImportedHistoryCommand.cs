using System;
using System.Threading.Tasks;
using NLog;
using NzbDrone.Common.Instrumentation.Extensions;
using NzbDrone.Core.Messaging.Commands;

namespace Requestarr.Core.Imports
{
    // Imported history posting under System, then Tasks while it runs, with its progress ("312 of 467 posted"), where
    // Servarr lists its background work. The posting itself stays HistoryPosting's own worker, so nothing queued mid-run
    // is dropped; this only follows that worker until it finishes or is paused (Resume starts a new one).
    public class PostImportedHistoryCommand : Command
    {
        public override bool SendUpdatesToClient => true;

        public override bool UpdateScheduledTask => false;
    }

    public class PostImportedHistoryProgress : IExecute<PostImportedHistoryCommand>
    {
        // As often as the worker posts.
        public static readonly TimeSpan Every = HistoryPosting.Pace;

        private readonly IHistoryPosting _posting;
        private readonly Logger _logger;

        public PostImportedHistoryProgress(IHistoryPosting posting, Logger logger)
        {
            _posting = posting;
            _logger = logger;
        }

        // Tests make it immediate.
        public Func<TimeSpan, Task> Wait { get; set; } = Task.Delay;

        public void Execute(PostImportedHistoryCommand message)
        {
            var status = _posting.Status();

            while (status.State == HistoryPostingState.Posting)
            {
                _logger.ProgressInfo(Progress(status));
                Wait(Every).GetAwaiter().GetResult();
                status = _posting.Status();
            }

            _logger.ProgressInfo(Progress(status));
        }

        // The task's message: it stays as the last one once the task has finished.
        public static string Progress(HistoryPostingStatus status) =>
            $"Imported history: {status.Posted} of {status.Total} posted{(status.State == HistoryPostingState.Paused ? ", paused" : string.Empty)}";
    }
}
