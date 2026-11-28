"""Remove everything Requestarr created in one local stack's Discord server, for a clean start.

Deletes: Requestarr's channel groups ("Requestarr Admin", "Requestarr QA", "Requestarr Users" and its numbered ones,
and the older "Requestarr Requests") and every channel in them (including archived ones), #requestarr-admin, #requestarr-feed,
#requestarr-announcements, #requestarr-welcome, #requestarr-access-blocked and members' request channels wherever they
are, the per-user
"requestarr-*" roles and the shared "Requestarr Members" and "Requestarr Blocked" roles.
Keeps: the bot's own role, the Requestarr Admin role, and anything else in the server.
The "what's new" posts go with their channels, and the reset wipes their record with the database, so they're posted
fresh when Requestarr starts again, as that stack keeps them
(docs/development.md#version-posts): QA posts every version staging doesn't have yet, dev the version it runs.

Reads STACK_DISCORD_BOT_TOKEN and STACK_DISCORD_GUILD_ID from the environment: the chosen stack's own bot and
server (QA: DISCORD_*, dev: DEV_DISCORD_* in docker/secrets.env). Never prints the token.
Run through `dev.ps1 reset-qa` / `reset-dev` (or dev.sh).

--lanes removes only what the automated run's lanes of live checks made (docker/qa_checks.py): every channel, group and
role whose name starts with a lane's prefix ("lane3-requestarr-admin", "archived-lane3-requests-...").
"""
import json
import os
import re
import sys
import time
import urllib.error
import urllib.request

API = "https://discord.com/api/v10"
ADMIN_CHANNELS = ("requestarr-admin", "requestarr-feed", "requestarr-qa-checklist", "requestarr-announcements", "requestarr-welcome",
                  "requestarr-access-blocked", "requestarr-bug-reports")
CATEGORIES = ("Requestarr Admin", "Requestarr QA", "Requestarr Users", "Requestarr Requests")
USER_ROLE_PREFIX = "requestarr-"
SHARED_ROLES = ("Requestarr Members", "Requestarr Blocked")
LANE_NAME = re.compile(r"^(archived-)?lane\d+-", re.IGNORECASE)


def call(method, path):
    request = urllib.request.Request(API + path, method=method, headers={
        "Authorization": "Bot " + os.environ["STACK_DISCORD_BOT_TOKEN"],
        "User-Agent": "DiscordBot (https://github.com/Requestarr/Requestarr, 1.0)",
    })
    while True:
        try:
            with urllib.request.urlopen(request) as response:
                body = response.read()
                return json.loads(body) if body else None
        except urllib.error.HTTPError as error:
            if error.code == 429:
                time.sleep(float(json.loads(error.read()).get("retry_after", 1)) + 0.1)
                continue
            raise SystemExit(f"Discord refused {method} {path}: HTTP {error.code}")


def main():
    guild = os.environ.get("STACK_DISCORD_GUILD_ID")
    if not os.environ.get("STACK_DISCORD_BOT_TOKEN") or not guild:
        raise SystemExit("this stack's bot token and server ID must be set in docker/secrets.env (QA: DISCORD_*, dev: DEV_DISCORD_*)")
    server = call("GET", f"/guilds/{guild}")
    print(f"cleaning the Discord server '{server['name']}'")
    if sys.argv[1:] == ["--lanes"]:
        clean_lanes(guild)
        return

    channels = call("GET", f"/guilds/{guild}/channels")
    categories = {c["id"] for c in channels if c["type"] == 4 and c["name"].startswith(CATEGORIES)}
    # Members' request channels too, wherever they are: one left loose at the top level after a group was deleted.
    member = lambda c: c["type"] == 0 and c["name"].startswith(("requests-", "archived-requests-"))
    doomed = [c for c in channels if c["parent_id"] in categories or (c["type"] == 0 and c["name"] in ADMIN_CHANNELS) or member(c)]
    doomed += [c for c in channels if c["id"] in categories]

    for channel in doomed:
        call("DELETE", f"/channels/{channel['id']}")
        print(f"deleted channel {'category ' if channel['type'] == 4 else '#'}{channel['name']}")

    for role in call("GET", f"/guilds/{guild}/roles"):
        if (role["name"].startswith(USER_ROLE_PREFIX) or role["name"] in SHARED_ROLES) and not role.get("managed"):
            call("DELETE", f"/guilds/{guild}/roles/{role['id']}")
            print(f"deleted role {role['name']}")

    left = call("GET", f"/guilds/{guild}/channels")
    remaining = [c["name"] for c in left if c["parent_id"] in categories or c["id"] in categories or c["name"] in ADMIN_CHANNELS or member(c)]
    print(f"Requestarr channels left in the server: {len(remaining)}")
    sys.exit(1 if remaining else 0)


def clean_lanes(guild):
    """Every channel and role a lane of live checks named with its prefix, gone; exits 1 when any is left."""
    lanes = [c for c in call("GET", f"/guilds/{guild}/channels") if LANE_NAME.match(c["name"])]
    # Channels before their groups, so none is left loose for a moment.
    for channel in sorted(lanes, key=lambda c: c["type"] == 4):
        call("DELETE", f"/channels/{channel['id']}")
    roles = [r for r in call("GET", f"/guilds/{guild}/roles") if LANE_NAME.match(r["name"]) and not r.get("managed")]
    for role in roles:
        call("DELETE", f"/guilds/{guild}/roles/{role['id']}")
    left = [c for c in call("GET", f"/guilds/{guild}/channels") if LANE_NAME.match(c["name"])]
    print(f"removed the live check lanes' {len(lanes)} channels and {len(roles)} roles; left: {len(left)}")
    sys.exit(1 if left else 0)


if __name__ == "__main__":
    main()
