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
        private const string Gone = "That fix doesn't exist any more.";
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
                return new SeriesFixResult(null, Gone);
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
            fix.ShortName = ShortName(input);
            fix.MustContain = mustContain;
            fix.MustNotContain = mustNotContain;
            fix.Reason = input.Reason.Trim();
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
                return new SeriesFixResult(null, Gone);
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

            // The reason is how a fix that stops downloads is found: the request's card shows it.
            if (string.IsNullOrWhiteSpace(input.Reason))
            {
                return "Say why, in a few words: it shows on the request's card when the fix is used.";
            }

            if (_fixes.ForSeries(input.TvdbId) is { } other && other.Id != id)
            {
                return $"{other.Series} already has a fix: change that one.";
            }

            // Each fix has its own Sonarr tag, so two fixes never share terms.
            var shortName = ShortName(input);
            return _fixes.All().FirstOrDefault(f => f.Id != id && f.ShortName == shortName) is { } sameTag
                ? $"{sameTag.Series}'s fix already uses the short name {shortName}: choose another."
                : null;
        }

        private static List<string> Terms(IEnumerable<string> terms) =>
            (terms ?? Enumerable.Empty<string>()).Select(t => t?.Trim()).Where(t => !string.IsNullOrEmpty(t)).Distinct().ToList();

        // The name in the fix's Sonarr tag: the one typed, or the series', in Sonarr's tag letters (lowercase letters,
        // digits and dashes); a name with none of those letters uses the TheTVDB ID.
        private static string ShortName(SeriesFixInput input)
        {
            var typed = string.IsNullOrWhiteSpace(input.ShortName) ? input.Series ?? string.Empty : input.ShortName;
            var safe = NotTagSafe.Replace(typed.Trim().ToLowerInvariant(), "-").Trim('-');

            return safe.Length > 0 ? safe : $"tvdb-{input.TvdbId}";
        }
    }
}
