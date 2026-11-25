using System;

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
        // How Sonarr and Radarr word a download they can't import by themselves ("Manual Import required.").
        private const string ManualImportWords = "manual import";

        public static ImportTrouble Of(Request request)
        {
            if (request.Progress == DownloadProgress.Failed)
            {
                return ImportTrouble.Failed;
            }

            var isWaitingForAdmin = request.ProgressDetail?.Contains(ManualImportWords, StringComparison.OrdinalIgnoreCase) == true;

            return isWaitingForAdmin ? ImportTrouble.ManualImport : ImportTrouble.Stuck;
        }
    }
}
