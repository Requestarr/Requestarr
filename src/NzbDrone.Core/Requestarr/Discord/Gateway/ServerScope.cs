namespace Requestarr.Core.Discord.Gateway
{
    // The one server Requestarr runs in is the configured one. The bot may also be in other servers, invited there only
    // so an import can read old request channels: there it never creates, edits or deletes anything, never registers
    // commands and never posts, and what happens there (members, messages, button presses) is ignored.
    public static class ServerScope
    {
        public static bool IsOurs(ulong? serverId, ulong configured) => configured != 0 && serverId == configured;

        // Direct messages belong to no server: the bot's own private messages to members are answered there.
        public static bool IsOursOrDirect(ulong? serverId, ulong configured) => serverId == null || IsOurs(serverId, configured);
    }
}
