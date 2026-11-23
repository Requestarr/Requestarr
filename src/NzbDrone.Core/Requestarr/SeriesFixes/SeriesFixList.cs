using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;
using Requestarr.Core.Audit;

namespace Requestarr.Core.SeriesFixes
{
    // What an admin types for a fix. ShortName may be left empty: it's made from the series.
    public record SeriesFixInput(int TvdbId, string Series, string ShortName, List<string> MustContain, List<string> MustNotContain, string Reason, bool Enabled);

    // A save's or delete's answer: the fix, or the problem in plain words.
    public record SeriesFixResult(SeriesFix Fix, string Problem);

    public interface ISeriesFixList
    {
        List<SeriesFix> All();
        SeriesFixResult Save(int? id, SeriesFixInput input, Actor actor);
        SeriesFixResult Delete(int id, Actor actor);
    }

    // The admins' list of fixes (the web app's Series fixes): add, change, turn off. A shipped fix can't be deleted, so an
    // update doesn't bring it back; it can be turned off.
    public class SeriesFixList : ISeriesFixList
    {
        private const string ChangeAction = "seriesfix.change";
        private static readonly Regex NotTagSafe = new("[^a-z0-9]+", RegexOptions.Compiled);

        private readonly ISeriesFixRepository _fixes;
        private readonly IAuditService _audit;

        public SeriesFixList(ISeriesFixRepository fixes, IAuditService audit)
        {
            _fixes = fixes;
            _audit = audit;
        }

        public List<SeriesFix> All() => _fixes.All().OrderBy(f => f.Series).ToList();

        public SeriesFixResult Save(int? id, SeriesFixInput input, Actor actor)
        {
            var existing = id.HasValue ? _fixes.Find(id.Value) : null;

            if (id.HasValue && existing == null)
            {
                return new SeriesFixResult(null, "That fix doesn't exist any more.");
            }

            var mustContain = Terms(input.MustContain);
            var mustNotContain = Terms(input.MustNotContain);

            if (Problem(id, input, mustContain, mustNotContain) is { } problem)
            {
                return new SeriesFixResult(null, problem);
            }

            var fix = existing ?? new SeriesFix();
            fix.TvdbId = input.TvdbId;
            fix.Series = input.Series.Trim();
            fix.ShortName = TagSafe(string.IsNullOrWhiteSpace(input.ShortName) ? input.Series : input.ShortName);
            fix.MustContain = mustContain;
            fix.MustNotContain = mustNotContain;
            fix.Reason = string.IsNullOrWhiteSpace(input.Reason) ? null : input.Reason.Trim();
            fix.Enabled = input.Enabled;
            fix = _fixes.Upsert(fix);

            _audit.Record(actor, ChangeAction, "seriesfix", fix.Id.ToString(), $"{(existing == null ? "added" : "changed")} {fix.Series} (TheTVDB {fix.TvdbId}), {(fix.Enabled ? "on" : "off")}");

            return new SeriesFixResult(fix, null);
        }

        public SeriesFixResult Delete(int id, Actor actor)
        {
            var fix = _fixes.Find(id);

            if (fix == null)
            {
                return new SeriesFixResult(null, "That fix doesn't exist any more.");
            }

            if (fix.Shipped)
            {
                return new SeriesFixResult(null, "This fix comes with Requestarr, so it can't be deleted: turn it off instead.");
            }

            _fixes.Delete(id);
            _audit.Record(actor, ChangeAction, "seriesfix", id.ToString(), $"deleted {fix.Series} (TheTVDB {fix.TvdbId})");

            return new SeriesFixResult(fix, null);
        }

        private string Problem(int? id, SeriesFixInput input, IReadOnlyCollection<string> mustContain, IReadOnlyCollection<string> mustNotContain)
        {
            if (input.TvdbId <= 0)
            {
                return "Enter the show's TheTVDB ID.";
            }

            if (string.IsNullOrWhiteSpace(input.Series))
            {
                return "Name the show.";
            }

            if (!mustContain.Any() && !mustNotContain.Any())
            {
                return "Add at least one term the releases must or must not contain.";
            }

            return _fixes.ForSeries(input.TvdbId) is { } other && other.Id != id
                ? $"{other.Series} already has a fix: change that one."
                : null;
        }

        private static List<string> Terms(IEnumerable<string> terms) =>
            (terms ?? Enumerable.Empty<string>()).Select(t => t?.Trim()).Where(t => !string.IsNullOrEmpty(t)).Distinct().ToList();

        // Sonarr's tag labels are lowercase letters, digits and dashes.
        private static string TagSafe(string name) => NotTagSafe.Replace(name.Trim().ToLowerInvariant(), "-").Trim('-');
    }
}
