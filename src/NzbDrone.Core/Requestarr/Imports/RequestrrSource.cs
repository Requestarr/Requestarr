using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text.RegularExpressions;
using Requestarr.Core.Users;

namespace Requestarr.Core.Imports
{
    public enum SourceMessageKind
    {
        // Not one of the source's messages, or one an import has no use for.
        Other,
        Sent,
        AlreadyAvailable,
        NotFound,
        Abandoned,
        Finished
    }

    // What one message of another request bot means. Requester: who asked (the slash command's member); for a finished
    // download, whom it was announced to. Seasons: empty for every season. Title and Year come from its card.
    public record SourceMessage(
        HistoryMessage Message,
        SourceMessageKind Kind,
        ContentType? Type,
        string Title,
        int? Year,
        int? TmdbId,
        int? TvdbId,
        IReadOnlyList<int> Seasons,
        IReadOnlyList<ulong> People);

    // A request bot an admin can import from: Requestrr now; another (Doplarr, say) is another source, the import the same.
    public interface IImportSource
    {
        string Name { get; }

        SourceMessage Read(HistoryMessage message);
    }

    // How one version of Requestrr words its replies. Another version's wording is another entry in Versions, nothing else.
    public record RequestrrWording(Regex Sent, Regex AlreadyAvailable, Regex NotFound, Regex Abandoned, Regex Finished);

    // Requestrr (thomst08/requestrr): a slash command's reply, edited to its final state, with a card linking the title on
    // The Movie Database (movies) or TheTVDB (TV); later, "has finished downloading!" mentioning the requester.
    public class RequestrrSource : IImportSource
    {
        public const string SourceName = "Requestrr";

        // A version's replies, as its channel shows them (November 2026). The TV reply names "all seasons" or the seasons.
        public static readonly IReadOnlyList<RequestrrWording> Versions = new[]
        {
            new RequestrrWording(
                new Regex(@"^Your request for (?:\*\*(?<seasons>all seasons|seasons? [^*]+)\*\* of )?\*\*(?<title>.+?)\*\* was sent successfully!", RegexOptions.IgnoreCase),
                new Regex(@"^This (?:movie|show|tv show|series) is already available", RegexOptions.IgnoreCase),
                new Regex(@"^I could not find any (?:movie|show|tv show|series)", RegexOptions.IgnoreCase),
                new Regex(@"^Please select a (?:movie|show|tv show|series)", RegexOptions.IgnoreCase),
                new Regex(@"^The (?:movie|show|tv show|series|season|episode)s? \*\*(?<title>.+?)\*\*.* has finished downloading!", RegexOptions.IgnoreCase))
        };

        private static readonly Regex TmdbMovie = new(@"themoviedb\.org/movie/(?<id>\d+)", RegexOptions.IgnoreCase);
        private static readonly Regex TmdbTv = new(@"themoviedb\.org/tv/(?<id>\d+)", RegexOptions.IgnoreCase);
        private static readonly Regex Tvdb = new(@"thetvdb\.com/(?:.*[?&]id=|series/|dereferrer/series/)(?<id>\d+)", RegexOptions.IgnoreCase);
        private static readonly Regex TitleYear = new(@"^(?<title>.+?)\s*\((?<year>\d{4})\)\s*$");
        private static readonly Regex Numbers = new(@"\d+");

        public string Name => SourceName;

        public SourceMessage Read(HistoryMessage message)
        {
            if (!message.AuthorIsBot)
            {
                return Of(message, SourceMessageKind.Other);
            }

            foreach (var wording in Versions)
            {
                if (wording.Sent.Match(message.Content) is { Success: true } sent)
                {
                    return WithCard(message, SourceMessageKind.Sent, Invoker(message), sent.Groups["title"].Value, Seasons(sent.Groups["seasons"].Value));
                }

                if (wording.Finished.Match(message.Content) is { Success: true } finished)
                {
                    return WithCard(message, SourceMessageKind.Finished, message.Mentions, finished.Groups["title"].Value, Array.Empty<int>());
                }

                if (Skipped(wording, message.Content) is { } kind)
                {
                    return WithCard(message, kind, Invoker(message), null, Array.Empty<int>());
                }
            }

            return Of(message, SourceMessageKind.Other);
        }

        private static SourceMessageKind? Skipped(RequestrrWording wording, string content)
        {
            if (wording.AlreadyAvailable.IsMatch(content))
            {
                return SourceMessageKind.AlreadyAvailable;
            }

            if (wording.NotFound.IsMatch(content))
            {
                return SourceMessageKind.NotFound;
            }

            return wording.Abandoned.IsMatch(content) ? SourceMessageKind.Abandoned : null;
        }

        private static IReadOnlyList<ulong> Invoker(HistoryMessage message) => message.InvokedById is { } id ? new[] { id } : Array.Empty<ulong>();

        private static SourceMessage Of(HistoryMessage message, SourceMessageKind kind) =>
            new(message, kind, null, null, null, null, null, Array.Empty<int>(), Array.Empty<ulong>());

        // The title, year and IDs from the card when there is one; the text's title otherwise.
        private static SourceMessage WithCard(HistoryMessage message, SourceMessageKind kind, IReadOnlyList<ulong> people, string textTitle, IReadOnlyList<int> seasons)
        {
            var card = message.Embeds.FirstOrDefault(e => e.Url != null);
            var named = TitleYear.Match(card?.Title ?? string.Empty);
            var fallback = string.IsNullOrEmpty(textTitle) ? null : textTitle;
            var title = named.Success ? named.Groups["title"].Value : card?.Title ?? fallback;
            int? year = named.Success ? int.Parse(named.Groups["year"].Value, CultureInfo.InvariantCulture) : null;
            var url = card?.Url ?? string.Empty;

            var movie = Id(TmdbMovie, url);
            var tvdb = Id(Tvdb, url);
            var tmdbTv = Id(TmdbTv, url);
            return new SourceMessage(message, kind, TypeOf(movie, tvdb ?? tmdbTv), title, year, movie ?? tmdbTv, tvdb, seasons, people);
        }

        // A link to a film is a movie; to a show (TheTVDB, or The Movie Database's TV pages), TV; no link, unknown.
        private static ContentType? TypeOf(int? movie, int? show)
        {
            if (movie.HasValue)
            {
                return ContentType.Movie;
            }

            return show.HasValue ? ContentType.Tv : null;
        }

        private static int? Id(Regex link, string url) =>
            link.Match(url) is { Success: true } match && int.TryParse(match.Groups["id"].Value, NumberStyles.None, CultureInfo.InvariantCulture, out var id) ? id : null;

        // "all seasons" (or none named) is every season; "season 2", "seasons 1, 2 and 3" those.
        private static IReadOnlyList<int> Seasons(string named) =>
            string.IsNullOrEmpty(named) || named.StartsWith("all", StringComparison.OrdinalIgnoreCase)
                ? Array.Empty<int>()
                : Numbers.Matches(named).Select(m => int.Parse(m.Value, CultureInfo.InvariantCulture)).Distinct().OrderBy(s => s).ToList();
    }
}
