using System;
using System.Linq;

namespace Requestarr.Core.Requests
{
    // What's wrong with an imported request that was in trouble already.
    public enum ImportTrouble
    {
        Stuck,
        ManualImport,
        Failed
    }

    public static class ImportTroubles
    {
        // How Radarr ("Manual Import required.") and Sonarr word a download they can't import by themselves.
        private static readonly string[] ManualImportWords = { "manual import", "automatic import is not possible" };

        public static ImportTrouble Of(Request request)
        {
            if (request.Progress == DownloadProgress.Failed)
            {
                return ImportTrouble.Failed;
            }

            var isWaitingForAdmin = ManualImportWords.Any(w => request.ProgressDetail?.Contains(w, StringComparison.OrdinalIgnoreCase) == true);

            return isWaitingForAdmin ? ImportTrouble.ManualImport : ImportTrouble.Stuck;
        }
    }
}
