using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace Requestarr.Core.Imports
{
    public record HistoryEmbed(string Title, string Url);

    // One message of an old request channel, as much of it as an import reads: who wrote it, the slash command's member
    // when it answers one (InvokedBy, from Discord's interaction metadata, never from the text), its text and cards'
    // titles and links, and whom it mentions. Never stored: an import keeps only what it works out from it.
    public record HistoryMessage(
        ulong Id,
        ulong ChannelId,
        DateTime Timestamp,
        ulong AuthorId,
        bool AuthorIsBot,
        string Content,
        IReadOnlyList<HistoryEmbed> Embeds,
        ulong? InvokedById,
        string InvokedByName,
        IReadOnlyList<ulong> Mentions,
        int Components)
    {
        // Another bot's message whose text and cards Discord withheld: the bot can't read message content.
        public bool LooksWithheld => AuthorIsBot && string.IsNullOrEmpty(Content) && Embeds.Count == 0 && Components == 0 && InvokedById.HasValue;
    }

    // A message as Discord's API returns it (GET /channels/{id}/messages), for recorded channels: the import reads
    // these fields and nothing else.
    public static class DiscordMessageJson
    {
        // Timestamps stay as Discord wrote them (with their offset), never turned into this machine's time on the way.
        private static readonly JsonSerializerSettings AsWritten = new() { DateParseHandling = DateParseHandling.None };

        public static IReadOnlyList<HistoryMessage> ParseAll(string json) =>
            JsonConvert.DeserializeObject<JArray>(json, AsWritten).OfType<JObject>().Select(Parse).ToList();

        public static HistoryMessage Parse(JObject message)
        {
            // Newer messages carry interaction_metadata; older ones interaction. Both name the member who ran the command.
            var invoked = (message["interaction_metadata"] ?? message["interaction"])?["user"];

            return new HistoryMessage(
                Id(message["id"]),
                Id(message["channel_id"]),
                DateTime.Parse((string)message["timestamp"], CultureInfo.InvariantCulture, DateTimeStyles.AdjustToUniversal),
                Id(message["author"]?["id"]),
                (bool?)message["author"]?["bot"] ?? false,
                (string)message["content"] ?? string.Empty,
                (message["embeds"] as JArray ?? new JArray()).Select(e => new HistoryEmbed((string)e["title"], (string)e["url"])).ToList(),
                invoked == null ? null : Id(invoked["id"]),
                invoked == null ? null : (string)invoked["global_name"] ?? (string)invoked["username"],
                (message["mentions"] as JArray ?? new JArray()).Select(m => Id(m["id"])).ToList(),
                (message["components"] as JArray)?.Count ?? 0);
        }

        private static ulong Id(JToken token) => ulong.Parse((string)token, CultureInfo.InvariantCulture);
    }
}
