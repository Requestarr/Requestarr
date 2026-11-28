using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text.Json;
using System.Threading.Tasks;
using Requestarr.Core.Test.Discord.Harness;

namespace Requestarr.Core.Test.Qa.Web
{
    // What a web check's action posted in the dev server, read back through Discord's API as the dev bot, and the check's
    // own posts deleted afterwards. Never another server's, and never anything but what the check made.
    public sealed class DiscordReadBack : IDisposable
    {
        // Discord's flag on a message sent without a notification.
        public const int Silent = 1 << 12;

        private const string Api = "https://discord.com/api/v10";
        private const int PageSize = 100;

        // Tries at one call while Discord asks to slow down.
        private const int MaxTries = 5;

        private readonly HttpClient _http = new();

        public DiscordReadBack()
        {
            _http.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bot", LiveDev.Token);
            _http.DefaultRequestHeaders.UserAgent.ParseAdd("DiscordBot (https://github.com/Requestarr/Requestarr, 1.0)");
        }

        // A channel of the dev server by its name, or null.
        public async Task<string> ChannelIdAsync(string name)
        {
            using var channels = JsonDocument.Parse(await GetAsync($"{Api}/guilds/{LiveDev.GuildId}/channels"));

            return channels.RootElement.EnumerateArray().FirstOrDefault(c => c.GetProperty("name").GetString() == name) is { ValueKind: JsonValueKind.Object } found
                ? found.GetProperty("id").GetString()
                : null;
        }

        // The channel's latest messages whose card's title starts with this, oldest first.
        public async Task<IReadOnlyList<PostedCard>> CardsAsync(string channelId, string titleStart)
        {
            using var messages = JsonDocument.Parse(await GetAsync($"{Api}/channels/{channelId}/messages?limit={PageSize}"));

            return messages.RootElement.EnumerateArray()
                .Select(PostedCard.From)
                .Where(c => c.Title?.StartsWith(titleStart, StringComparison.Ordinal) == true)
                .Reverse()
                .ToList();
        }

        // Each card deleted.
        public async Task DeleteAsync(string channelId, IEnumerable<PostedCard> cards)
        {
            foreach (var card in cards)
            {
                using var answer = await PacedAsync(() => _http.DeleteAsync($"{Api}/channels/{channelId}/messages/{card.Id}"));
            }
        }

        private async Task<string> GetAsync(string url)
        {
            using var answer = await PacedAsync(() => _http.GetAsync(url));
            answer.EnsureSuccessStatusCode();
            return await answer.Content.ReadAsStringAsync();
        }

        // Discord's answer, waiting as long as it says when it asks to slow down: the automated run's other checks use the
        // same bot at the same time.
        private static async Task<HttpResponseMessage> PacedAsync(Func<Task<HttpResponseMessage>> call)
        {
            for (var tries = 1; ; tries++)
            {
                var answer = await call();

                if (answer.StatusCode != HttpStatusCode.TooManyRequests || tries == MaxTries)
                {
                    return answer;
                }

                using (answer)
                {
                    using var limit = JsonDocument.Parse(await answer.Content.ReadAsStringAsync());
                    await Task.Delay(TimeSpan.FromSeconds(limit.RootElement.GetProperty("retry_after").GetDouble()));
                }
            }
        }

        public void Dispose() => _http.Dispose();
    }

    // A posted message as a check reads it: its card's title and text, its own text, and whether it went out silently.
    public record PostedCard(string Id, string Title, string Description, string Content, bool IsSilent, bool Mentions)
    {
        public static PostedCard From(JsonElement message)
        {
            var embed = message.GetProperty("embeds").EnumerateArray().FirstOrDefault();
            var flags = message.TryGetProperty("flags", out var value) ? value.GetInt32() : 0;
            var mentions = message.GetProperty("mentions").GetArrayLength() > 0 || message.GetProperty("mention_roles").GetArrayLength() > 0 || message.GetProperty("mention_everyone").GetBoolean();

            return new PostedCard(message.GetProperty("id").GetString(),
                Text(embed, "title"),
                Text(embed, "description"),
                message.GetProperty("content").GetString(),
                (flags & DiscordReadBack.Silent) != 0,
                mentions);
        }

        private static string Text(JsonElement embed, string name) =>
            embed.ValueKind == JsonValueKind.Object && embed.TryGetProperty(name, out var text) ? text.GetString() : null;
    }
}
