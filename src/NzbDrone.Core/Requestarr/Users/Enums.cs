using System.Collections.Generic;

namespace Requestarr.Core.Users
{
    public enum UserStatus
    {
        Active = 0,
        Suspended = 1,
        Removed = 2,

        // Known only from an import of another bot's requests: no role, channel or access until an admin onboards them.
        // Imported: seen in this Requestarr's Discord server. ImportedLeft: seen there before, gone now.
        // ImportedElsewhere: never seen there (the import read another server).
        Imported = 3,
        ImportedLeft = 4,
        ImportedElsewhere = 5
    }

    public enum ContentType
    {
        Movie = 1,
        Tv = 2,
        Music = 3,

        // Stored but not offered until a delivery method exists.
        Comic = 4,
        Book = 5
    }

    public enum AccessState
    {
        Off = 0,
        NeedsApproval = 1,
        AutoApprove = 2
    }

    public static class ContentTypes
    {
        public static readonly IReadOnlyList<ContentType> All = new[] { ContentType.Movie, ContentType.Tv, ContentType.Music, ContentType.Comic, ContentType.Book };

        public static readonly IReadOnlyList<ContentType> Offered = new[] { ContentType.Movie, ContentType.Tv, ContentType.Music };

        // What users can request today, and what their panel and help list. Music joins once Lidarr exists.
        public static readonly IReadOnlyList<ContentType> Requestable = new[] { ContentType.Movie, ContentType.Tv };

        public static bool IsOffered(ContentType type) => type == ContentType.Movie || type == ContentType.Tv || type == ContentType.Music;
    }
}
