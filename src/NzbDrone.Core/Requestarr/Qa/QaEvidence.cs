using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using NzbDrone.Common.EnvironmentInfo;

namespace Requestarr.Core.Qa
{
    public record QaEvidenceFile(string Name, byte[] Bytes);

    // The screenshots the automated checks took for a checklist item, kept in the app data folder (qa-evidence/{item key}) so the
    // item's card can show them after a restart. A new result replaces the item's screenshots.
    public interface IQaEvidenceStore
    {
        IReadOnlyList<string> Replace(string itemKey, IReadOnlyList<QaEvidenceFile> files);
        IReadOnlyList<QaEvidenceFile> Read(string itemKey, IEnumerable<string> names);
    }

    public class QaEvidenceStore : IQaEvidenceStore
    {
        public const int MaxFiles = 4;
        public const int MaxBytes = 2 * 1024 * 1024;

        private static readonly Regex SafeName = new(@"^[a-z0-9][a-z0-9.-]{0,79}\.(jpg|png)$");

        private readonly IAppFolderInfo _folders;

        public QaEvidenceStore(IAppFolderInfo folders)
        {
            _folders = folders;
        }

        // Only short lower-case names ending .jpg or .png are kept (they become attachment names in Discord), at most
        // four, each up to 2 MB; anything else is left out.
        public IReadOnlyList<string> Replace(string itemKey, IReadOnlyList<QaEvidenceFile> files)
        {
            var folder = Folder(itemKey);

            if (Directory.Exists(folder))
            {
                Directory.Delete(folder, true);
            }

            var kept = (files ?? new List<QaEvidenceFile>())
                .Where(f => f?.Name != null && SafeName.IsMatch(f.Name) && f.Bytes is { Length: > 0 and <= MaxBytes })
                .DistinctBy(f => f.Name)
                .Take(MaxFiles)
                .ToList();

            if (kept.Count > 0)
            {
                Directory.CreateDirectory(folder);
            }

            foreach (var file in kept)
            {
                File.WriteAllBytes(Path.Combine(folder, file.Name), file.Bytes);
            }

            return kept.Select(f => f.Name).ToList();
        }

        public IReadOnlyList<QaEvidenceFile> Read(string itemKey, IEnumerable<string> names) =>
            (names ?? Enumerable.Empty<string>())
                .Where(n => SafeName.IsMatch(n))
                .Select(n => Path.Combine(Folder(itemKey), n))
                .Where(File.Exists)
                .Select(p => new QaEvidenceFile(Path.GetFileName(p), File.ReadAllBytes(p)))
                .ToList();

        // Item keys are a slug and a hash ("web-app-logo.3f9a2c1d"): safe as a folder name.
        private string Folder(string itemKey) => Path.Combine(_folders.AppDataFolder, "qa-evidence", Regex.Replace(itemKey, "[^a-z0-9.-]", "-"));
    }
}
