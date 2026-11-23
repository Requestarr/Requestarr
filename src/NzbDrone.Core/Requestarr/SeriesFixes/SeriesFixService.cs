using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using NLog;
using Requestarr.Core.Audit;
using Requestarr.Core.Backends;

namespace Requestarr.Core.SeriesFixes
{
    // A fix built in Sonarr: the tag to add the series with, and the fix, for the request card's line.
    public record AppliedFix(int TagId, SeriesFix Fix);

    public interface ISeriesFixService
    {
        // The turned-on fix for a series, or null.
        SeriesFix For(int? tvdbId);

        // Builds the series' fix in Sonarr when it has one: its tag and release profile, created or reused and brought
        // back in line with the list. Null when the series has no fix turned on or the instance isn't a Sonarr.
        Task<AppliedFix> BuildAsync(BackendInstance instance, int? tvdbId);

        // A listed series already in Sonarr: builds its fix and adds the tag, keeping every other tag.
        Task<AppliedFix> ApplyToLibraryAsync(BackendInstance instance, int? tvdbId, int seriesId, IReadOnlyCollection<int> currentTags);
    }

    public class SeriesFixService : ISeriesFixService
    {
        private readonly ISeriesFixRepository _fixes;
        private readonly ISonarrApi _sonarr;
        private readonly IAuditService _audit;
        private readonly Logger _logger;

        public SeriesFixService(ISeriesFixRepository fixes, ISonarrApi sonarr, IAuditService audit, Logger logger)
        {
            _fixes = fixes;
            _sonarr = sonarr;
            _audit = audit;
            _logger = logger;
        }

        public SeriesFix For(int? tvdbId) => tvdbId is { } id && _fixes.ForSeries(id) is { Enabled: true } fix ? fix : null;

        public async Task<AppliedFix> BuildAsync(BackendInstance instance, int? tvdbId)
        {
            if (instance?.Implementation != BackendImplementations.Sonarr || For(tvdbId) is not { } fix)
            {
                return null;
            }

            var tags = await _sonarr.GetTagsAsync(instance.Config);
            var tag = tags.FirstOrDefault(t => string.Equals(t.Name, fix.TagName, StringComparison.OrdinalIgnoreCase))
                      ?? await _sonarr.CreateTagAsync(instance.Config, fix.TagName);
            var profiles = await _sonarr.GetReleaseProfilesAsync(instance.Config);
            var ours = profiles.FirstOrDefault(p => string.Equals(p.Name, fix.ProfileName, StringComparison.OrdinalIgnoreCase));

            if (ours == null)
            {
                var profile = new ReleaseProfile
                {
                    Name = fix.ProfileName,
                    Required = fix.MustContain.ToList(),
                    Ignored = fix.MustNotContain.ToList(),
                    Tags = new List<int> { tag.Id }
                };
                await _sonarr.SaveReleaseProfileAsync(instance.Config, profile);
                _logger.Info("Series fix for {0} built on {1}", fix.Series, instance.Name);
            }
            else if (!InLine(ours, fix, tag.Id))
            {
                // Ours (named as Requestarr's): brought back in line with the list, as the anime profiles are.
                ours.Enabled = true;
                ours.Required = fix.MustContain.ToList();
                ours.Ignored = fix.MustNotContain.ToList();
                ours.Tags = new List<int> { tag.Id };
                await _sonarr.SaveReleaseProfileAsync(instance.Config, ours);
            }

            return new AppliedFix(tag.Id, fix);
        }

        public async Task<AppliedFix> ApplyToLibraryAsync(BackendInstance instance, int? tvdbId, int seriesId, IReadOnlyCollection<int> currentTags)
        {
            var applied = await BuildAsync(instance, tvdbId);

            if (applied != null && !currentTags.Contains(applied.TagId))
            {
                await _sonarr.SetSeriesTagsAsync(instance.Config, seriesId, currentTags.Append(applied.TagId).ToList());
                _audit.Record(Actor.Automation, "seriesfix.applied", "title", $"tv:{tvdbId}", $"{applied.Fix.Series} on {instance.Name}, already in the library: {applied.Fix.Reason}");
            }

            return applied;
        }

        private static bool InLine(ReleaseProfile profile, SeriesFix fix, int tagId) =>
            profile.Enabled && Same(profile.Required, fix.MustContain) && Same(profile.Ignored, fix.MustNotContain) && profile.Tags.SequenceEqual(new[] { tagId });

        private static bool Same(IEnumerable<string> a, IEnumerable<string> b) =>
            a.OrderBy(x => x, StringComparer.OrdinalIgnoreCase).SequenceEqual(b.OrderBy(x => x, StringComparer.OrdinalIgnoreCase), StringComparer.OrdinalIgnoreCase);
    }
}
