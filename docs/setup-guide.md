# Requestarr setup guide

> Requestarr is in early development. This guide grows as each phase ships. Sections marked "coming" are not usable yet.

## 1. Before you start

You need:

- A Discord server where you are the owner or have Manage Server.
- Docker and Docker Compose on the host that will run Requestarr.
- Sonarr 4 and Radarr 5 or later, reachable from that host. Lidarr is optional.
- Optional: a Plex server, for library sharing.

## 2. Create your Discord bot

Every Requestarr install uses its own bot. Discord's official reference is https://discord.com/developers/docs/quick-start/getting-started.

1. Open https://discord.com/developers/applications and choose **New Application**. Name it, for example "Requestarr".
2. **General Information:** an icon and description are optional. Leave Interactions Endpoint URL, Linked Roles, Terms of Service, and Privacy Policy empty. They are only for public verified bots, which a self-hosted bot never needs.
3. **Installation:** set Installation Contexts to **Guild Install** only, set **Install Link** to **None**, and save.
   Do this before step 4. Discord refuses to make a bot private while an install link is set ("Private application cannot have a default authorization link").
4. **Bot:**
   - Set the bot username. Leave its icon and banner empty: Requestarr sets them itself (5.13).
   - Turn **Public Bot** off, so nobody else can add your bot.
   - Under Privileged Gateway Intents, turn **Server Members Intent** on. Requestarr needs it to detect members leaving. Leave Presence and Message Content off.
   - Ignore the Bot Permissions grid on this page. It is only a calculator and saves nothing.
   - Save.
5. **OAuth2 → URL Generator:**
   - Scopes: tick `bot` and `applications.commands`.
   - Permissions: tick Manage Roles, Manage Channels, View Channels, Send Messages, Embed Links, Read Message History, and Pin Messages.
     Pin Messages lets Requestarr pin an outage notice while the outage lasts (5.7).
   - Integration Type: **Guild Install**.

   The generated link ends with `permissions=2251800082205712&integration_type=0&scope=bot+applications.commands`.

   **Already added the bot without Pin Messages?** You don't need to re-invite it. Open **Server Settings → Roles**, pick the bot's role, turn on **Pin Messages**, and save. Outage notices are pinned from then on.
6. Open the generated link, pick your server, and authorize. The bot shows as offline until Requestarr runs.
7. **Bot → Reset Token**, then paste the token straight into Requestarr's secrets file (section 3). Never paste it into chat, issues, or screenshots. If you lose it, reset it again.
8. In your server, open **Server Settings → Roles** and keep the bot's role above any roles it should manage.

Do not verify a self-hosted bot. Verification is only for bots in 100 or more servers, and it permanently locks the name and owner.

## 3. Install Requestarr (Docker Compose)

Requestarr's image is published to GitHub's container registry as `ghcr.io/requestarr/requestarr` for x86-64 and ARM servers, but only the early access `staging` tag so far; `latest` and version tags arrive with the first release. Until then you build it from source. This needs Git and Docker with Compose v2.

### 3.1 Build the image

```bash
git clone https://github.com/Requestarr/Requestarr.git
cd Requestarr
docker build -f docker/Dockerfile -t requestarr:latest .
```

The image runs Requestarr on .NET 10 as a non-root user. It keeps all its data in `/config` and listens on port 9797.

### 3.2 Create an install folder

**Without a provisioning file** (how the beta server and production run): leave out `provisioning.yml`, `secrets.env` and the provisioning lines below. On the first start the web portal opens its first-run setup (your sign-in, the Discord bot, a first Sonarr or Radarr), and every other setting is under **Settings**, recorded on **Settings → Changes**. [B.3](#b3-the-first-run-setup) walks through it. With a provisioning file, its settings are applied on every start and the settings pages show them read-only.

With a provisioning file, put three files in a folder of your choice, for example `requestarr/`.

**`compose.yaml`**

```yaml
services:
  requestarr:
    image: requestarr:latest
    container_name: requestarr
    env_file: secrets.env
    environment:
      REQUESTARR_PROVISIONING_FILE: /provisioning/provisioning.yml
      TZ: Etc/UTC
    volumes:
      - ./config:/config
      - ./provisioning.yml:/provisioning/provisioning.yml:ro
    ports:
      - "9797:9797"
    restart: unless-stopped
```

The image runs as user ID 1654. If you bind-mount `./config` as above, make that folder writable for it (`mkdir config && sudo chown 1654:1654 config`), or use a named volume instead.

**`secrets.env`**

This holds every secret. Keep it out of version control and backups that others can read. On Linux, `chmod 600 secrets.env`.

```ini
REQUESTARR_ADMIN_USERNAME=admin
REQUESTARR_ADMIN_PASSWORD=choose-a-long-password
REQUESTARR_API_KEY=at-least-20-random-characters
# Section 2, step 7
DISCORD_BOT_TOKEN=
```

To generate random values, run `openssl rand -hex 16` once per value.

**`provisioning.yml`**

Requestarr reads this on every start, so the configuration lives in a file instead of clicks.

- `${NAME}` is replaced with the value from `secrets.env`.
- `${NAME:-default}` supplies a default when the variable is empty.
- Keys in the file win over changes made in the web portal; keys you leave out are not touched. The settings pages show the keys the file sets as read-only, with "Set by your provisioning file", so a change there can't be undone silently on the next start.
- **Changing the web portal password:** with a provisioning file that sets the login (the local test stacks), change `REQUESTARR_ADMIN_PASSWORD` in `docker/secrets.env` and restart Requestarr; it's applied on the next start, and the same goes for the username and the API key. Without one (the beta server, and any install set up in the web app), change it under **Settings → General**.
- **Allowed hosts** (`general.allowedHosts`): every name Requestarr is reached by, comma-separated. That's the address you browse, and the name Sonarr and Radarr call it by in their webhooks (in Docker, its service name, such as `requestarr`). A request for any name not on the list is turned away, so a missing name breaks those webhooks. With sign-in required for every address, you can leave it out and every name is accepted. With sign-in only for other networks, the web portal asks for it, and the local test stacks set `localhost, 127.0.0.1, requestarr`.
- **The port** in Docker is the one inside the container. The address you browse uses the port Docker publishes (in your compose file), so change that instead; the settings page shows the port read-only there.

```yaml
version: 1

general:
  instanceName: Requestarr
  logLevel: info

auth:
  method: forms                       # web portal login page
  required: enabled                   # or disabledForLocalAddresses
  username: ${REQUESTARR_ADMIN_USERNAME}
  password: ${REQUESTARR_ADMIN_PASSWORD}
  apiKey: ${REQUESTARR_API_KEY}

# Section 5 explains the Discord settings.
discord:
  botToken: ${DISCORD_BOT_TOKEN:-}
```

The `backends` section (Sonarr, Radarr, and Lidarr, with any number of instances each) and the `mediaServers` section are described in section 4 and section 6 once they ship. Their format is already checked, so a typo shows up at startup.

### 3.3 First start

```bash
docker compose up -d
docker compose logs requestarr | grep -i provisioning
```

A successful start logs `Provisioning applied from /provisioning/provisioning.yml`. If the file has an error, the log names the line or the missing variable, and the web portal shows it under **System → Status**.

### 3.4 Check that it is healthy

```bash
docker compose ps                      # STATUS shows (healthy) after about 30 seconds
curl http://localhost:9797/ping        # {"status": "OK"}
```

The image has a built-in health check that calls `/ping`.

### 3.5 Open the web portal

Browse to `http://{host}:9797` and sign in with the username and password from `secrets.env`. The portal is for administrators only. Users never log in to it; they use Discord.

What's there:

- **Requests, Members and Queue:** every request, every member, and what waits on an admin, with everything an admin does in Discord's admin channels: approve, deny, cancel, add seasons, start, force or drop a removal, add a member, change their access and limits, suspend, unsuspend, remove, link Plex. Discord's messages change as if it had been done there.
- **Access requests, Outages and Audit log:** people asking to join or paused, what can't be reached now and past outages, and everything Requestarr recorded, by member, title, action and day. The audit log keeps an entry for 1 year by default (Settings, Requests, **Keep the audit log for**: 90 days, 6 months, 1 year, 2 years or forever; `auditLog.keepFor` in `provisioning.yml`). Older entries are removed once a day, and that is recorded too; requests, members, removals and renames (which the header search uses to find a name someone had before) stay. Histories, timelines and Settings, then Changes, show what the log still keeps, and a page whose older history was removed says so.
- **The search in the header** (press `/`): a request by title, a member by name or Discord username.
- **Settings:** Connections (what Requestarr connects to, as cards: the Discord bot, Sonarr and Radarr, and later Plex and Jellyfin) and Requests (Requestarr's own settings); Changes (every change to them); General and UI; and Setup (the first-run setup).
- **System:** Status, Tasks, Backup, Updates, What's new (each version's changes), Events, and Log Files.

### 3.6 Updating

Pull the source, rebuild the image, and restart:

```bash
git pull
docker build -f docker/Dockerfile -t requestarr:latest .
docker compose up -d
```

Requestarr never updates itself inside the container.

## 4. Connect Sonarr and Radarr

Requestarr sends movie requests to Radarr and TV requests to Sonarr. Supported versions are **Radarr 5 or later** and **Sonarr 4 or later**. Older versions are refused, with a clear message under **System → Status**.

### 4.1 API keys

In each Sonarr and Radarr, open **Settings → General → Security** and copy the **API Key**. Put it in `secrets.env`, never in `provisioning.yml`:

```ini
RADARR_API_KEY=paste-radarr-key
SONARR_API_KEY=paste-sonarr-key
```

### 4.2 Provisioning entries

Add a `backends` section to `provisioning.yml`. Each type takes a list:

```yaml
backends:
  radarr:
    - name: Radarr                  # unique name, shown on member cards
      url: http://radarr:7878       # as Requestarr reaches it (container name on the same Docker network)
      browserUrl: http://nas.local:7878   # optional: as your browser reaches it, for Open in Radarr links
      apiKey: ${RADARR_API_KEY}
      qualityProfile: HD-1080p      # profile name (or ID) in that Radarr
      rootFolder: /movies           # must already exist as a root folder in that Radarr
      default: true                 # used for users without an assigned instance
  sonarr:
    - name: Sonarr
      url: http://sonarr:8989
      apiKey: ${SONARR_API_KEY}
      qualityProfile: HD-1080p
      rootFolder: /tv
      seriesType: standard          # standard | anime | daily
      default: true
```

| Setting | Required | Notes |
|---|---|---|
| `name` | yes | Unique across all instances. |
| `url` | yes | Base URL, without `/api`. |
| `browserUrl` | no | Where your browser opens the instance, when `url` only works from Requestarr (a container name). Requests link to their title's page there (**Open in Sonarr**, **Open in Radarr**); without it, `url` is used when a browser can reach it, and otherwise there's no link. On a server set up in the web app, it's **Address in your browser** on the instance. |
| `apiKey` | yes | Use a `${VAR}` reference to `secrets.env`. |
| `qualityProfile` | no | Defaults to the instance's first profile. |
| `rootFolder` | no | Defaults to the instance's first root folder. |
| `seriesType` | no | Sonarr only. Applied to every series this instance gets from Requestarr. |
| `default` | no | One per type. Without it, the first listed instance is the default. |
| `enabled` | no | `false` keeps the instance but stops using it. |
| `pollingOnly` | no | `true`: no webhook on this instance; downloads are found by the backup poll only (section 4.8). |
| `defaultAccess` | no | What a new member gets for the type when this is their instance: `off`, `approval` or `auto` (section 5.5). Defaults to `approval`. On a server set up in the web app, it's **Default access for new members** on the instance. |

On every start, Requestarr checks each instance:

- it can connect
- the app and version are supported
- the quality profile exists
- the root folder exists

Problems show as a health error under **System → Status**, naming the instance and the cause.

The file is the source of truth. An instance removed from the file is disabled, not deleted, so existing requests keep their history.

### 4.3 Several instances of one type

List as many instances per type as you need. Common setups:

- a separate Radarr for 4K
- a separate Sonarr for anime, or separate sub and dub anime instances (see 4.7)

```yaml
  radarr:
    - name: Radarr
      url: http://radarr:7878
      apiKey: ${RADARR_API_KEY}
      default: true
    - name: Radarr 4K
      url: http://radarr4k:7878
      apiKey: ${RADARR_4K_API_KEY}
      qualityProfile: Ultra-HD
      rootFolder: /movies-4k
```

### 4.4 Which instance a user's requests go to

Users never pick an instance. By default every user's movie requests go to the default Radarr, and TV requests to the default Sonarr.

To send one user's requests elsewhere, open their card (`/admin user`, or **Users** on the admin panel) and pick the instance in its **Limits, instance, their requests…** menu, for example "Movies: Radarr 4K". The menu only appears when a type has more than one instance.

The assignment is per user and per content type, and the card shows it.

### 4.5 What happens to a request

1. The user types `/request` in their channel and picks a type. The type list only shows types they have access to. Then they enter a title, or an ID such as `tmdb:603` (movies) or `tvdb:12345` (TV). Results come from their instance's own search, so no extra API keys are needed.
   The whole request stays in **one message** that changes in place: results, details, seasons, confirmation, final status. Each request is its own message, so separate requests don't run together.
2. Each result line shows the year, the network or studio, and either the top three cast (with The Movie Database key, see 4.6) or the genre. That helps tell apart titles with the same name.
3. They pick a title and see its card: a large poster and everything Sonarr or Radarr knows about it. That covers genres, rating, age rating, runtime, network or studio, status, seasons, air or release dates, and the overview. The card also shows the top cast and episode count when The Movie Database key is set, and which instance it goes to. The card says if the title is:
   - already in the library
   - already requested by someone, in which case they can follow that request instead of making a second one
   - previously denied, and why
4. For TV, they choose one of two paths:
   - **All seasons:** sends the request straight away for every season now, and new seasons as they air (Sonarr's monitor-new-seasons setting). Specials (season 0) are not included.
   - **Choose seasons:** pick individual seasons, including **Specials** if they want them, and optionally **Also new seasons**. Every season of any show can be picked. Long shows (more than 24 seasons) page through them with ◀ / ▶, and picks are kept across pages. After **Continue**, Requestarr restates the choice in plain words, for example "seasons 1–2 and 36 only; new seasons won't be added", and they press **Request** or **Back**.

   Either way, the final status says exactly which seasons were requested.
5. What happens next depends on their access level:
   - **off:** refused
   - **needs approval:** an Approve / Deny card appears in `#requestarr-admin`; Deny asks for a reason
   - **auto-approve:** sent straight away
6. The message ends with a clear status: **Request sent**, **Waiting for approval** (with a Cancel button), **Following**, **Already available**, or **Not requested** with the reason. TV requests also get **Add seasons**: the owner, anyone following, or an admin can add more seasons (or new seasons as they air) at any time, without approval and without using a limit. Seasons are never removed there.
7. When a request is approved, Requestarr adds it to the instance with the instance's quality profile, root folder and series type, monitored, and starts a search. After that the message follows the download: downloading, stuck or failed, partly available, available (section 4.8).
8. A request for a title that's already in the library when you act ends as 🎉 **Already available** with a "good news" message to the user (never denied, cancelled or failed, whichever you press, and also when Sonarr or Radarr reports it was added meanwhile); use the **Already available** button on the approval card, feed card or request card when you have it some other way.
9. Whenever a request changes later (approved, already available, denied, cancelled by an admin, switched to another anime language, downloading, available, or seasons added by someone else), the user's old request message is deleted and a fresh one is posted at the bottom of their channel. It mentions them, so they get a ping, and only has the buttons that still make sense (Cancel while it waits for approval, none once decided). Followers' messages get the same treatment. The approval card is edited in place. Details:
   - The user's own **Cancel** edits their message in place, without a repost or ping.
   - A message that's still in the middle of its steps (results, seasons, confirmation) is never reposted.
   - If the bot can't delete the old message, the new one is still posted and the old one loses its buttons.
   - No separate notice repeats the news. A notice is sent only to someone with no message to repost: the message was deleted, or the request is from before this version.

   Buttons clicked late re-check the request and never change one that's already decided.

Quotas (section 5.6) count requests a user makes over a rolling window. Following someone else's request doesn't count. When a user hits the limit, Requestarr tells them when their next request frees up.

### 4.6 Cast from The Movie Database (optional)

Sonarr and Radarr don't return cast. With a The Movie Database key, Requestarr adds the top three cast to each result line and the card, and the episode count to TV cards. Without a key everything still works: result lines show the genre instead, and nothing errors.

**Requestarr's published image has its own key built in,** so cast works with nothing to set. To use your own key instead (for example if the built-in one stops working: **System → Status** then says so), set the environment variable `REQUESTARR_TMDB_API_KEY` on the container (Container Manager: the container's **Environment**) and restart Requestarr. It's never set in the web app. Requestarr uses, in order: the provisioning file's key, `REQUESTARR_TMDB_API_KEY`, the built-in key; with none, results show the genre.

With a provisioning file (the local test stacks), the key goes there:

1. Create an account at themoviedb.org, then open **Settings → API** and request a developer key (personal use is free).
2. Copy the **API Read Access Token** (the long one). The shorter v3 API key also works.
3. Add it to `secrets.env`:

   ```
   TMDB_API_KEY=your-token
   ```

4. Reference it in `provisioning.yml`:

   ```yaml
   tmdb:
     apiKey: ${TMDB_API_KEY:-}
   ```

5. Restart Requestarr.

The key is kept in memory only. It is never written to the database or logs. Cast lookups are cached for 12 hours and give up after a few seconds, so a slow or unavailable The Movie Database never holds up a request.

**Attribution:** TMDB's terms ask apps to use TMDB's logo, keep it less prominent than the app's own branding, and show the notice "… uses TMDB and the TMDB APIs but is not endorsed, certified, or otherwise approved by TMDB." in an About or Credits area. The web app's **System → Status**, under About, always shows the notice, and so does the README. Requestarr also does this while a key is in use:

- **Cards with cast:** a short "Cast from TMDB" footer with TMDB's small logo.
- **Result lists with cast:** a "Cast from TMDB" line.
- **`/help` and `/admin help`:** a **Credits** topic with TMDB's logo and the full notice.

The logo is TMDB's approved short logo, unaltered, as a PNG (Discord can't show SVG). Requestarr uses TMDB and the TMDB APIs but is not endorsed, certified, or otherwise approved by TMDB. If you use a key, The Movie Database's terms of use apply to you.

### 4.7 Anime: sub or dub

Anime is anything Sonarr lists with the genre **Anime**. For anime, the request card asks **Dub** (English audio) or **Sub** (original audio with subtitles) before seasons. Every other show is unchanged.

**What Requestarr sets up in each Sonarr.** On every start (and when an instance is checked), Requestarr makes sure these exist, creating only what's missing:

| Item | Name |
|---|---|
| Tag | `requestarr-dub` |
| Tag | `requestarr-sub` |
| Release profile, limited to the dub tag | `Requestarr: anime dub` |
| Release profile, limited to the sub tag | `Requestarr: anime sub` |

It only ever looks at items with exactly these names. Your own tags and release profiles are never changed, and it's safe to restart as often as you like. If you change the terms in `provisioning.yml`, Requestarr updates its own two profiles to match.

A requested anime series is added with the tag for its language, so Sonarr applies that release profile to it. Your quality profile and custom formats still decide between the releases that pass.

**Default release terms.** Release groups mark dubbed anime in a few common ways; the defaults cover them:

| Profile | Must contain (any one) | Must not contain | Why |
|---|---|---|---|
| Dub | `Dual Audio`, `Dual-Audio`, `DualAudio`, `Multi-Audio`, `Multi Audio`, `Dubbed`, `English Dub`, `Eng Dub`, `Eng-Dub`, `EN Dub`, `[Dub]`, `(Dub)` | nothing | Most English-dubbed anime is released as **dual audio** (English and Japanese tracks), so dual and multi audio count as dub. The others are the usual dub markers. Without a must-contain list, Sonarr would happily grab the far more common sub-only releases. |
| Sub | nothing | `Dubbed`, `English Dub`, `Eng Dub`, `Eng-Dub`, `EN Dub`, `[Dub]`, `(Dub)` | Most anime releases are subtitled and say nothing about it, so there's nothing reliable to require. Dub-only releases are blocked. Dual audio is allowed, because it still has the original audio and subtitles. |

Matching isn't case-sensitive in Sonarr. To change the terms for one instance, add an `anime` block to it. A list you leave out keeps its default:

```yaml
  sonarr:
    - name: Sonarr
      url: http://sonarr:8989
      apiKey: ${SONARR_API_KEY}
      anime:
        enabled: true          # false: no Sub/Dub question on this instance
        priority: dub          # dub (default) | sub | first
        dub:
          required: [Dual Audio, Dual-Audio, Dubbed, English Dub]
        sub:
          ignored: [Dubbed, English Dub]
```

`anime` only works on Sonarr instances; on a Radarr it's a provisioning error, reported at start like any other. With `enabled: false` the card doesn't ask, no tag is added, and the tags and profiles aren't created on that instance.

**One language per series, and who wins.** A series exists once on a Sonarr, so it can only be one language there. The card says what an existing series is set to, and the instance's rule:

- **dub** (default): a dub request wins over sub.
- **sub**: a sub request wins over dub.
- **first**: the first request decides, and later requests can't change it.

A request for the losing language doesn't create a second request; the user follows the existing one and is told it stays in that language. A request for the winning language switches the series: Requestarr swaps the Sonarr tag (keeping every other tag on it), updates any open request for it, reposts the owner's and followers' messages saying so, and records an `anime.switch` entry in the audit log. Series that Requestarr never tagged are left alone.

**What a switch re-downloads.** Sonarr only applies a release profile when it searches. Episodes already downloaded stay as they are; they aren't deleted or replaced. New episodes, and missing ones when Sonarr searches for them, come in the new language. To replace existing episodes too, search for them in Sonarr yourself (for example **Series → Search Monitored**), knowing that it downloads them all again. Users are told this when they switch a series.

**Series type.** Anime is always added as Sonarr's **Anime** series type (absolute episode numbering), whatever `seriesType` the instance has; other shows use the instance's `seriesType`. If a series numbers better as Standard, switch it in Sonarr (**Edit → Series Type**). Requestarr never switches it back.

**Separate sub and dub instances.** If you'd rather keep dubbed and subtitled anime apart, run two Sonarr instances. Give one `priority: dub` and the other `priority: sub`, and point users at one with `/admin user access ... type:TV instance:{name}`. Then the same show can exist in both languages, once per instance. Two warnings:

- The instances need **different root folders**. Two Sonarrs sharing a folder fight over the same files.
- Point your media server at them as **separate libraries**. In one library, the show appears twice.

### 4.8 Downloads and availability

Once a request is sent, Requestarr follows it in Sonarr or Radarr and tells the user (and anyone following it) as it changes:

| State | When |
|---|---|
| ⬇️ **Downloading** | Sonarr or Radarr grabbed a release and it's in the download queue. |
| ⚠️ **Download stuck** | Sonarr, Radarr or the download client flags it (for example "stalled with no connections" or "no files eligible for import"), or its remaining size hasn't changed for 6 hours. The reason is shown. |
| ⚠️ **Download failed** | The download failed. Sonarr and Radarr usually blocklist that release and try another by themselves, so the request stays open. |
| 🎉 **Partly available** | TV only: some requested seasons are there, and the message says which. |
| 🎉 **Available** | Everything requested is there. For TV, a season counts as available when every episode that has aired has a file; seasons that haven't aired yet don't hold the request back. |

Each change replaces the request message at the bottom of the user's channel, with a mention, and moves the request's card to the bottom of the admin feed (section 5.4). The same state twice is never announced twice.

**How Requestarr hears about it.** Two ways, and they work together:

1. **Webhook:** Requestarr adds its own connection, named "Requestarr (availability)", to each Sonarr and Radarr through their own interface, so there's nothing to set up there. It's called on grab, import and upgrade, and when a download needs attention. Requestarr then asks the instance what changed, so a webhook can't report anything the instance doesn't confirm. Requestarr never changes or removes your own connections.
2. **Backup poll:** every 15 minutes Requestarr checks all open requests anyway, so a missed or unreachable webhook only delays the news.

The webhook needs the address Sonarr and Radarr use to reach Requestarr. Add it to `provisioning.yml`:

```yaml
availability:
  webhookUrl: http://requestarr:9797   # how Sonarr and Radarr reach Requestarr (include any URL base)
  pollMinutes: 15                      # backup poll; at least 5
```

Which address to use:

| Setup | `webhookUrl` |
|---|---|
| Docker, Sonarr/Radarr in the same Compose file or on the same Docker network | `http://requestarr:9797` (the service or container name, and the container port) |
| Docker, Sonarr/Radarr on another network or another machine | `http://{host address}:{published port}`, for example the Docker host's LAN IP and the port from `ports:` |
| Not in Docker, all on one machine | `http://localhost:9797` |
| Not in Docker, different machines | `http://{Requestarr machine's LAN address}:9797` |

If Requestarr has a URL base (`general.urlBase`, for example `/requestarr`), add it: `http://requestarr:9797/requestarr`.

Each instance's webhook address carries its own token, made from Requestarr's API key, so other callers can't post fake updates. If you change the API key, Requestarr updates the webhooks on the next start.

**When it can't reach Requestarr.** On every start, Requestarr asks each instance to test its webhook. If the test fails, **System → Status** shows a warning naming the instance and the address, and the backup poll covers it, so nothing is lost; updates just arrive at the next poll.

**Polling only.** Leave `webhookUrl` out to poll everything, or set `pollingOnly: true` on one instance (section 4.2). Requestarr then removes its webhook from that instance.

## 5. Admin setup in Discord

### 5.1 Connect the bot

Add the Discord section to `provisioning.yml`, and put the values in `secrets.env`:

```yaml
discord:
  botToken: ${DISCORD_BOT_TOKEN}
  applicationId: ${DISCORD_APPLICATION_ID}   # Developer Portal → General Information
  guildId: ${DISCORD_GUILD_ID}               # your server's ID
  # adminRoleName: Requestarr Admin          # optional, see 5.2
  # adminRoleId: 123456789012345678          # optional: use an existing role instead
```

To copy IDs, turn on Developer Mode in Discord (User Settings → Advanced). Then right-click the server or role and choose **Copy ID**.

Restart Requestarr. The bot comes online, and **System → Status** in the web portal shows a Discord health warning or error until it connects. If the bot stays disconnected for 10 minutes, Requestarr exits so that Docker restarts it with a fresh connection.

### 5.2 Who is an admin

- **The server owner** is always a Requestarr admin and never needs a role.
- **Anyone holding the "Requestarr Admin" role** is an admin too. Requestarr creates this role on first start if it doesn't exist. It is placed below the bot's own role and has no Discord permissions of its own.

To make someone a co-admin, give them the role in **Server Settings → Roles** or from their member profile. To remove admin, take the role away.

Anyone who can manage roles in your server can hand out this role. Discord doesn't let Requestarr see who granted it, so keep Manage Roles to people you trust.

Two optional settings in `provisioning.yml` change which role counts:

- **`adminRoleName`:** a different name. An existing role with that name is reused.
- **`adminRoleId`:** a specific role you already have, used instead of creating one.

Requestarr checks admin status itself on every command and button click. What Discord shows or hides changes nothing about who may act.

### 5.3 Who sees which commands

Requestarr registers exactly three commands:

- **`/request`** is visible to everyone. Its type list is filled in for each person from their own access, so someone without TV access never sees TV. A type also stays hidden until a Sonarr, Radarr or Lidarr for it has connected at least once (5.7).
- **`/help`** is visible to everyone. It opens user help, which only the asker can see, filtered to what they can use.
- **`/admin`** has three parts: `/admin user` opens a member's card, `/admin requests` goes through everything waiting on you one item at a time, and `/admin help` opens admin help. It is registered so that only members with **Manage Server** see it, which means ordinary users never do.

Everything else is a button, menu or short form: for users on their panel (5.5) and request cards; for admins on the admin panel, member and request cards, approval cards and the feed (5.4 to 5.10).

User help and admin help are separate. Admin help has its own topics, plus a **What users see** branch so you can walk someone through their help. User help never shows admin topics, even to an admin. Both are built from the same definitions as the commands and buttons, so they can't describe something that no longer exists.

**Optional:** co-admins who hold the Requestarr Admin role but not Manage Server won't see `/admin`. You can also keep `/admin` out of every channel except the admin channel. As the server owner:

1. Open **Server Settings → Integrations → (your Requestarr bot) → Manage**.
2. Under **Commands**, pick `/admin`.
3. Under **Roles & Members**, add the **Requestarr Admin** role and allow it.
4. Under **Channels**, turn off **All Channels** and allow only `#requestarr-admin`.

This is display only. Requestarr still refuses `/admin` from non-admins and outside the admin channel.

### 5.4 The admin channel and request channels

On first start, Requestarr creates these, or reuses them if they already exist. They live in two channel groups that Requestarr keeps (and recreates if they're deleted):

- **Requestarr Admin:** `#requestarr-admin` and `#requestarr-feed`. Only the Requestarr Admin role, the owner and the bot see the group, and both channels follow the group's permissions.
- **Requestarr Users:** `#requestarr-welcome` (5.12), `#requestarr-announcements` (5.8), `#requestarr-access-blocked` (5.12) and every member's private request channel. When the group reaches Discord's limit of 50 channels, new private channels go into "Requestarr Users 2", "3" and so on, with the same group permissions. The private channels keep their own permissions on top.

A server set up before this version is moved over on the next start: the "Requestarr Requests" group becomes "Requestarr Users" (renamed in place, so the private channels don't move), the admin channel, feed and announcements move into their groups, and nothing loses its messages.

- **`#requestarr-admin`:** run `/admin` here, and use the admin panel. Approval cards for requests, access requests (5.12) and media server access also appear here, and so do account events: new users, access changes, suspensions (including a member leaving the server), blocks and Plex link requests. Only the Requestarr Admin role, the owner, and the bot can see it. It's for admin actions only: admins make their own requests from their own request channel, like everyone (see 5.12).
- **`#requestarr-feed`:** the admin feed, next to the admin channel and just as private. It has one small card per request: thumbnail, title, type, who asked, the instance, the state and, for TV, the seasons and who follows it. Every change deletes the card and posts a fresh one at the bottom, so the newest activity is always last. If you delete the channel, Requestarr recreates it on the next start. The first time a feed channel exists, it's filled with cards for requests that are still waiting or in progress, not finished ones.
- **`#requestarr-announcements`** and the **Requestarr Members** role: news for every member, read-only for them (5.8).
You can rename the channels. Requestarr tracks them by ID, and puts a channel back in its group on the next start if it was moved out.

**Panels stay at the bottom.** Every panel (the admin panel, each member's panel, the welcome message and the blocked channel's message) is always the newest message in its channel, where people look: whenever Requestarr posts something else there, the panel moves below it a moment later, without pinging anyone. Panels aren't pinned, because they're always last anyway and every re-pin would add a "pinned a message" notice.

The feed card's buttons are only those valid now, and every click is checked again on the server:

| Button | Shown | What it does |
|---|---|---|
| **Approve**, **Deny**, **Already available** | while it waits for approval | the same as on the approval card; acting in either place updates both, and the user's message |
| **Cancel request** | while it waits, or once sent until something starts downloading | waiting: cancels it. Sent: cancels it and unmonitors the title in Sonarr or Radarr, so nothing more is searched for or downloaded. Nothing is deleted; the title stays there. |
| **Add seasons** | open TV requests | adds seasons for the requester (no approval, no limit) |
| **Delete** | once something is downloaded, and no removal is open | starts a removal (5.9) |
| **Force removal**, **Drop removal** | while a removal is open or waiting on you | Force deletes now (it asks first); Drop leaves the title |

The admin channel's panel has five buttons. Everything they open is visible only to you:

- **Pending queue (n):** the same as `/admin requests`, below. The number is how many items are waiting on you, and it updates by itself.
- **Users:** everyone with their status, channel and access, and a menu that opens their member card.
- **Add or find a member:** Discord's member picker. Pick anyone in the server to open their member card, and add them from there.
- **Remove a title:** searches every Sonarr and Radarr library, including titles nobody requested through Requestarr, and starts a removal (5.9).
- **Help:** admin help (the same as `/admin help`).

Requestarr posts the panel on first start and re-posts it if it is deleted.

**Everything waiting on you: `/admin requests`.** It takes no options. It shows one private card at a time, oldest first, with a counter ("3 of 7"), and goes through:

| Item | The card shows | Its buttons |
|---|---|---|
| A request waiting for approval | the full request card, how long it has waited, and for the requester and anyone following: their access for that type, how much of their limit they've used, and how many requests they have waiting | **Approve**, **Deny** (asks for a reason), **Already available**, **Cancel** (asks for an optional reason) |
| An approved request that failed or is stuck | the same, plus what went wrong in plain words and what you can do | **Try adding again** if it couldn't be added to Sonarr or Radarr, otherwise **Search again**; then **Already available** and **Cancel** |
| An access request from the welcome channel (5.12) | who asked, what they'd like, and their note | **Approve…** (choose their access), **Deny** (asks for an optional reason) |
| A Plex link someone asked for | who asked, their Plex username, and their access | **Approve Plex link**, **Deny Plex link** |
| A Sonarr or Radarr that can't be reached (5.7) | since when, and that requests for it are paused | **Check now** |

Every card also has **Skip**, and **Back** after the first. Acting on an item moves straight to the next one and says what happened at the top. If an item was already handled somewhere else in the meantime (on its approval card, for example), it's skipped and you're told so; nothing is done twice. At the end it says "Nothing left waiting on you", or, if you skipped some, how many still wait, with **Start over**.

**Search again** asks Sonarr or Radarr to search for the title again; the stuck download stays in your download client for you to remove there. The item stays in the list until the download moves on.

`/admin` and the admin panel work only in the admin channel (the feed's buttons work in the feed). Users' commands and panel buttons work only in their own channel.

### 5.5 Add your first user

In `#requestarr-admin`, press **Add or find a member** on the admin panel and pick the person (or run `/admin user` and pick them there). Their card says they aren't a Requestarr user yet; press **Add to Requestarr**.

Requestarr then:

1. Creates the Requestarr user from the member's Discord account.
2. Creates a role just for them (`requestarr-{name}`) and gives it to them.
3. Creates their private channel (`#requests-{name}`). Only that role, the admin role (and the owner), and the bot can see it.
4. Posts one message: the user's **panel**, which is also their welcome, and always the newest message in their channel. It greets them and lists what they may request, always matching their real access. It holds no how-to text, because instructions live in Help and kept text goes stale. Its buttons are:
   - **My requests:** their requests and the ones they follow.
   - **My limits:** their quota usage.
   - **Link Plex:** a short form for their Plex username, which sends you an access request. It shows only while a Plex server is connected to Requestarr, and disappears once their account is linked. Connecting a Plex server comes with Plex sharing in a later release, so for now this button isn't there.
   - **Help:** user help (the same as `/help`).

   The panel updates itself when you change their access or link their account. Existing channels get the new panel in place on the next start.

By default, a new user gets, for each type, the **Default access for new members** of the media manager their requests go to: its default instance's (4.4), or the one you choose for them. Each Sonarr and Radarr has it in its edit window under **Settings → Connections**: off, needs approval or auto-approve (Approved automatically). Instances you had before the setting existed give **needs approval**, as before, and so does an instance in a provisioning file without `defaultAccess` (4.2). A type with no instance turned on is off, and so is music until Lidarr arrives.

The card then shows their access, ready to change (5.6).

Discord allows 250 roles per server, and every user needs one. Requestarr also uses three shared roles: Requestarr Admin, Requestarr Members and Requestarr Blocked (one role each, not one per person). Adding stops a few roles short of that limit and says so.

### 5.6 The member card: access, limits and more

`/admin user` (or **Users** or **Add or find a member** on the panel) opens a member's card, visible only to you. It shows their status, channel, access, limits, instance and media server account, and only the controls that apply to them now. Every change updates the card in place.

There are three access levels for each content type:

| Level | Meaning |
|---|---|
| off | The user can't request this type. |
| needs approval | Requests wait for an admin to approve them. |
| auto-approve | Requests go straight through. |

| On the card | What it does |
|---|---|
| **Movies access**, **TV access**, **Music access** | One menu per type, set to the current level. Pick another level and the card changes; nothing else is posted. Every type is shown to you, even one with nothing connected yet; granting such a type tells you privately that the member won't see it until an instance for it connects (5.7). |
| **Limits, instance, their requests…** | One menu for the rest: a type's limit (enter how many requests and per how many days, 7 if left empty; 0 removes the limit; following someone else's request never counts), which instance their requests go to (only when a type has several Sonarr or Radarr instances, 4.4), and **Their requests**. |
| **Link Plex…** / **Change Plex link…** | Links their Plex account directly. |
| **Approve Plex link** / **Deny Plex link** | Only while they've asked for Plex access themselves. |
| **Suspend** / **Unsuspend** | Suspend locks their channel: they can read it but can't post or request. Unsuspend unlocks it and gives their role back if they rejoined. |
| **Remove** | Asks first. Archives their channel (renamed `archived-…`, visible only to admins), deletes their role, and keeps their request history. Their card then offers **Add to Requestarr** to bring them back. |
| **Their requests** (in the menu above) | Their requests and the ones they follow, opening as request cards with the same buttons as their feed cards. |

Comics and books are not offered yet.

### 5.7 When Sonarr or Radarr can't be reached

Requestarr checks every Sonarr and Radarr once a minute. Each has one of three states:

| State | What users see |
|---|---|
| **Never connected** | Nothing. The type stays hidden from everyone, even members with access, until an instance for it answers for the first time. Admins can still grant it. |
| **Connected** | The type, if they have access. |
| **Lost** (it connected before, and stops answering) | The type as before. New requests for it say requests are paused right now. Nobody's access changes. |

When an instance has been unreachable for 3 minutes, one alert is posted in `#requestarr-admin` and pinned, and the instance is an item in `/admin requests` until it answers again. When it's back, the alert is unpinned and removed, and one more message (not pinned) says it's back and for how long it was down. Several outages at once each have their own pinned alert. A blip shorter than 3 minutes posts nothing.

Discord is only the working view: every outage is also kept in Requestarr's own record (which instance, when it started and ended, how long, and whether and by whom it was announced to users), for the admin web portal's outage history.

If it's still down after 30 minutes, the alert gets an **Announce to users** button. Users are told only if you press it (5.8).

Music stays hidden until Lidarr support arrives in a later version.

### 5.8 The announcements channel

`#requestarr-announcements` is for every member. Members can read it but can't post or start threads in it; admins (and the owner) can type their own announcements there directly, and the bot posts there. Everyone else in the server doesn't see it.

Members see it through one shared role, **Requestarr Members**. They get it when they're added or unsuspended and lose it when they're suspended or removed, the same way as their request channel. On every start, Requestarr makes sure every active member has the role and nobody else does, so existing members are added the first time this version starts. If the channel is deleted, it's recreated on the next start; if its permissions were changed, members are made read-only again.

What Requestarr posts there:

| When | In `#requestarr-announcements` | In `#requestarr-admin` |
|---|---|---|
| A Sonarr or Radarr is still down after 30 minutes and you press **Announce to users** on its alert | A plain notice, for example "TV requests are paused" and that the admin knows, pinned while the outage lasts. The button then goes away. | The alert says who announced it. |
| That outage ends | The notice is unpinned and removed, and "TV requests work again" is posted, automatically, only if it was announced. An outage you never announced is never mentioned to users. | The usual "is back" message. |
| Requestarr starts on a new version | A one-line card with the version and its date, for example "Requestarr 0.4.0 · October 25", and the main change in a few words, for example "Softer logo, and 3 more". Nothing if a version has nothing for users. | The same kind of card, its line counting both sections, for example "Member cards, and 9 more". A version with nothing for either isn't posted. |

Every version card has **Read changes**: it shows the full card only to whoever pressed it, with the version and the day it was made as the title, then **For users** and **For admins** (only those with something in them), one line per change. Members see only their section. A long version is split into several cards between lines, never in the middle of one.

The "what's new" cards are a lasting record: each version is posted once, oldest first, even across restarts, and stays. The first time a server starts, it posts the full history from 0.1.0 on. A server that still has the older text posts gets them replaced by cards once, the history kept. The changes come from the project's changelog, written with every change in plain words (see [development](development.md#changelog)). Test servers keep them differently ([version posts](development.md#version-posts)).

### 5.9 Removing titles

Nothing leaves the library without asking the people who can watch it. A removal is of a whole title, or (TV) of chosen seasons.

**Starting one**

| Who | Where | Then |
|---|---|---|
| The requester | **Remove…** on their request message, once something of it is downloaded | Straight to the vote. |
| An admin, a requested title | **Delete** on its feed card, or **Remove a title** on the admin panel | The requester is asked first, in their channel: **Yes, remove it** starts the vote; **No, keep it** keeps it and the removal waits on you in `/admin requests`, where **Force removal** removes it anyway. No answer by the deadline counts as yes (the message says so), with a reminder on the reminder day. |
| An admin, a title nobody requested here | **Remove a title** on the admin panel: it searches every Sonarr and Radarr library, including titles never requested through Requestarr | Straight to the vote. A title whose requester was removed counts as nobody's. |

**The vote.** Every active user with access to that content type gets a **Still watching?** message in their own channel, with the title's card, which seasons, when the vote closes, and **Keep it**. Users who gain access while it's open get it too. One keep is enough: when the vote closes with a keep, the title stays; with none, it's removed. Users can take their own keep back while it's open. Nobody is told who pressed Keep it; you see who in `/admin requests`. Nothing is changed while a vote is open: the title stays monitored and its files stay.

**One at a time.** Only one removal can be open per title and season scope. The whole title overlaps every season, so a second removal of it, or of any of its seasons, is refused while one is open; removals of different seasons of the same show can run side by side.

**Removing.** The whole title: its files are deleted and it's removed from Sonarr or Radarr, **without** adding it to the exclusion list, so anyone can request it again later (as a new request). Seasons: those seasons' episode files are deleted and the seasons unmonitored; the show stays. The request (and everyone following it) is closed as removed, or loses those seasons.

**Force** is available at every stage: over a requester's No, over keeps, or to skip the vote altogether (**Force: delete now** when starting one). It always asks first, and the audit log records who and when.

**Outcomes.** Everyone who was asked gets their message reposted with the outcome (kept, removed, or removed by the admin), without buttons; so does the requester. The feed card follows the removal through every step: requester asked, voting (with the number of keeps), kept, removed, forced, cancelled. If Sonarr or Radarr refuses the deletion, nobody is told an outcome; the removal waits in `/admin requests`, and Force tries again.

**Settings.** In days, under `removal:` in `provisioning.yml` (each optional), and stored in Requestarr's settings so the admin web portal can change them:

```yaml
removal:
  voteDays: 7              # how long a vote is open
  requesterTimeoutDays: 7  # how long the requester has to answer an admin's removal
  reminderDay: 5           # the day of that the requester is reminded (0: no reminder)
  deleteFiles: false       # whether Requestarr may delete in Sonarr and Radarr (default off)
```

**Delete files** (Settings → Requests, or `deleteFiles` above) starts **off**: Requestarr deletes nothing in Sonarr or Radarr. A removal that's agreed (a vote with no keeps, or **Force**) then does everything else (the outcome is recorded and everyone asked is told) and waits in `/admin requests` and the web app's **Queue** as **Delete it yourself**, naming the title and the Sonarr or Radarr it's in. Delete it there, then press **I deleted it**: its requests close. **Drop removal** keeps it. Every card that starts a removal says deleting is turned off on this server. Turn it on once you trust removals on your server; the change is recorded on **Settings → Changes**.

### 5.10 Where everything is

For every command and button, see the [command reference](commands.md).

Users can also ask for media server access themselves with the **Link Plex** button on their panel (only while a Plex server is connected; see 5.5). An Approve / Deny card then appears in the admin channel, and their member card shows the same choice. Approving links the account and removes the Link Plex button from their panel. Plex invites are sent automatically in a later release.

### 5.11 When someone leaves the server

If a user leaves or is removed from the Discord server, Requestarr suspends them automatically and locks their channel. Nothing is deleted. If they rejoin, press **Unsuspend** on their card (`/admin user`): Requestarr gives their role back and unlocks the channel. The admin channel gets a note when someone leaves.

Every action in this section, whether an admin or Requestarr did it, is recorded in Requestarr's audit log.

### 5.12 The welcome channel and access requests

`#requestarr-welcome` is open to everyone in the server, and only Requestarr posts there (nobody can post or start threads). Its one message, a coloured card with a large heading and kept current, says what Requestarr is and how it works, with a **Request access** button. It's named with the `requestarr-` prefix so it can't clash with your server's own welcome channel.

**Request access** opens a short private form: what they'd like (only the types with a Sonarr or Radarr that has connected) and, after **Continue**, an optional note. Sending it:

- posts a card in `#requestarr-admin` with who asked, what they'd like and their note, and adds it to `/admin requests` (the panel's count goes up);
- **Approve…** on the card lets you choose their real access, one menu per type (what they asked for starts at its media manager's default access for new members, the rest off), then **Add with this access** adds them exactly as **Add to Requestarr** does (5.5): role, private channel, panel. Nobody is added just by asking;
- **Deny** asks for an optional reason.

They get the outcome as a private message from the bot: on approval, where their new channel is; on a denial, your reason and when they can ask again. (If their Discord settings refuse private messages from the server, they see it the next time they press the button.)

Rules for the button:

- One open request per person. Pressing it again while one is open answers privately with its status and creates nothing.
- After a denial, they can ask again only after a cooldown (7 days by default, `accessRequests.denialCooldownDays`).
- Members who are already in are told where their channel is.
- Admins (the owner and anyone with the Requestarr Admin role) ask the same way and get their own request channel straight away: no card for you to decide, the types they picked on automatic approval and the rest off. Change their access later on their member card like anyone's.

**Media server buttons.** Next to **Request access** there's one button per media server you've connected (Plex, Jellyfin), and only those: with none connected, there are none. Someone can ask for media server access on its own, or tick it together with Requestarr access in the form; either way it reaches you exactly like a Link Plex request. Connecting a media server comes with Plex sharing in a later release, so for now there are no media server buttons.

**Spam protection.** Only clicks that can't do anything count (while a request is open, during a cooldown, or after being told their status), within the last hour; the click that opens the form never counts. Discord can't hide a shared button from one person, so Requestarr answers those clicks privately and does nothing else:

| Counted clicks | What happens |
|---|---|
| 5 within the hour | they wait 1 minute; every click meanwhile only says how long is left |
| 10 within the hour | they wait 24 hours |
| 5 more within 24 hours of the 10th | they're blocked |

Blocking gives them the **Requestarr Blocked** role. It hides `#requestarr-welcome` from them and shows `#requestarr-access-blocked`, a read-only channel whose one message, "Access requests paused", tells them the request button was pressed too many times in a short while, that an admin can lift the pause, and to send one a message. You get one notice in `#requestarr-admin`. A pause lasts until you lift it: **Unblock** on that notice or on their member card, or remove the role from them in Discord. Rapid clicking costs nothing: no messages to you, and one small record per person.

All of these are settings, with these defaults, under `accessRequests:` in `provisioning.yml` (each optional), stored so the admin web portal can change them later:

```yaml
accessRequests:
  denialCooldownDays: 7       # wait after a denial before asking again
  spam:
    windowMinutes: 60         # only clicks in the last hour count
    shortCooldownClicks: 5    # this many: wait shortCooldownMinutes
    shortCooldownMinutes: 1
    longCooldownClicks: 10    # this many: wait longCooldownHours
    longCooldownHours: 24
    blockClicks: 5            # this many more within blockWithinHours: blocked
    blockWithinHours: 24
```

### 5.13 The bot's picture and banner

Requestarr gives its bot the Requestarr picture and a banner, and updates them when a new release changes them, so you never upload them by hand. The picture is the same everywhere. The banner is the teal one, or the dev, QA or beta one on those test servers.

- It happens when the bot starts, and only when something differs. An unchanged picture is never uploaded again, so it stays well inside Discord's limit on how often a bot can change its picture. If Discord refuses anyway, the log says so and Requestarr tries again on a later start. The bot runs normally either way.
- It needs no permission and no change to the invite link: a bot changes its own picture and banner.
- **Your own picture stays.** Requestarr only replaces a picture or banner it set itself, or puts one in when the bot has none. It remembers Discord's id for each one it sets. If the id changes, someone changed it by hand, and Requestarr leaves it alone from then on. A picture the bot already had before Requestarr managed it counts as your choice too.
- **To let Requestarr replace your own,** set `takeOver: true`: Requestarr's picture and banner then always win, and one changed by hand is replaced on the next start.

Settings, under `botProfile:` in `provisioning.yml` (each optional), stored so the admin web portal can change them later:

```yaml
botProfile:
  keepUpToDate: true   # false: Requestarr never touches the bot's picture or banner
  takeOver: false      # true: replace one chosen by hand too (Requestarr's always wins)
```

### 5.14 Bug reports

`#requestarr-bug-reports`, in the Requestarr Users group, lets members and admins tell the people who build Requestarr about a problem. Nobody types in it: **Report a problem** opens a menu of Requestarr's parts, then a short form (what happened, what they expected). Requestarr adds your server's environment, its version, the time and the reporter's Discord name, nothing else from your server, and posts the report as a card in the channel, so you see every report.

**Filing on GitHub is off on production.** Only a provisioning file turns it on there (`fileIssues: true` below); there's no setting for it in the web app. Turned on, each report also becomes an issue on Requestarr's public GitHub repository, which anyone can read; the form says so before anything is sent. It needs an access key of your own that can only read and write issues on that repository (GitHub: Settings, Developer settings, Fine-grained tokens, Issues "Read and write", nothing else). Leave it off unless you want your members' reports to be public. Requestarr's own test servers (dev, QA and beta) always file.

Without a provisioning file (beta), the access key comes from the container's `REQUESTARR_GITHUB_ISSUES_TOKEN` environment variable; it's never set in the web app. The repository and the limits are built in. A provisioning file can still set them, and the key, as below; they last only while the file names them.

**Each card says where its report is.** The link to its GitHub issue, or exactly why it isn't there: filing is turned off, the server has no access key, or what GitHub said (the key was refused, it can't create issues there, the repository wasn't found) or that GitHub couldn't be reached. A report that couldn't be filed is filed the next time the bot connects, for example after you add the key and restart, without anyone sending it again, and its card then shows the link.

```yaml
bugReports:
  fileIssues: false                    # true: each report becomes a public GitHub issue
  githubToken: ${GITHUB_ISSUES_TOKEN}  # in secrets.env, never here
  repository: Requestarr/Requestarr
  reportsPerDay: 5                     # per person
  minutesBetween: 2                    # per person, between two reports
```

The limits stop the button flooding anything: someone who reaches them is told how long to wait, and nothing is posted.

### 5.15 Moving from Requestrr

If your server used Requestrr, Requestarr can fill itself in with who asked for what in Requestrr's channels, so your
members keep their requests and nobody asks twice. It's in the web app under Settings, then **Import from Requestrr**,
and the first-run setup links to it once it's done.

1. **Let the bot read the channels.** Requestarr's bot needs to be in the server where those channels are, and for
   each channel: **View Channel** and **Read Message History**.
   - **Channels in your Requestarr server:** the bot is already there.
   - **Channels in a different server:** on the Import page, press **Invite bot** in the toolbar. It
     asks only to view channels and read their history. In any server other than the one you set Requestarr up in,
     the bot only reads: it creates no channels, roles or commands, posts nothing, and ignores what members do there.
   - **The Message Content intent:** Requestrr wrote what happened in each message's text, so the bot needs it to read
     those replies. In the Discord Developer Portal, your application, then **Bot**, then **Privileged Gateway
     Intents**, turn on **Message Content Intent** and save. The Import page says when it's off and links there;
     after saving, reopen the page. No restart is needed.

   Requestarr only reads: nothing is posted, edited, moved or deleted in those channels, and no message text is kept.
2. **Tick the channels.** The page lists the channels Requestrr answered in (it looks at each channel's newest
   messages), under their server's name; **Show every channel** lists the rest, for one it missed. Tick the old
   request channels. The info icon beside **Channels** says what the page does.
3. **The list.** Ticking a channel reads it, with how many messages have been read beside it. A channel with a lot
   of history takes a while, because Discord limits how fast a bot can read. Each channel is read once: unticking it
   and ticking it again, or ticking another, doesn't read it twice (until Requestarr restarts or an import runs). The
   list below is every past request in the ticked channels, whether or not it's in your library now:
   - **Each title:** its type, the member who asked first (the requester), everyone who asked after them
     (followers), the day, and how it ended in Requestrr: **Sent**, or **Already available** (Requestrr said it was
     already there; that counts as asking for it too).
   - **In library:** yes, with the Sonarr or Radarr that has it, or no. Titles are matched by their The Movie
     Database or TheTVDB number, never by name, so nothing is guessed.
   - **Titles Requestarr already has a request for** are listed too: the people who asked Requestrr follow that
     request, and no second one is made.
   - **Not requests:** searches that found nothing and title pickers left without a choice aren't listed; one line
     under the list says how many there were.
4. **Import** (in the toolbar at the top of the page). Nothing is written before this.
   - **Requests:** each keeps its original date, its history starts with "Imported from Requestrr", and a title
     Requestrr announced as finished downloading shows when it became available.
   - **Not in library:** a title none of your Sonarr or Radarr has is kept as history with that status. Nothing is
     sent anywhere, and it never stops anyone requesting the title again: a new request is made as usual.
   - **Members:** everyone who asked appears under Members, by their current Discord name when they're in your server.
     Someone who isn't a member yet shows as one of:
     - **Imported, not onboarded:** in your Requestarr server.
     - **Imported, not in this server:** found only where the import read (another server), never seen in yours.
       Joining your server makes them not onboarded.
     - **Imported, left the server:** seen in your server before, gone now.

     No role, channel or access is created for them. Their page says where their history came from (for example
     "#media-requests in The CheaZiest"), and **Onboard** there adds them the usual way; their imported requests stay
     theirs.
   - **The audit log** records the import and who ran it.

**Running it again is safe.** Requestarr remembers every message it imported, so a second run adds only what's new.
A title that was Not in library and is in your Sonarr or Radarr now is linked to it on the next run.

**Afterwards:** if you invited the bot to a different server only for the import, you can remove it from there (in
that server, right-click the bot, then **Kick**). Requestarr keeps everything it imported.

## 6. Connect Plex (optional)

_Coming in Phase 3:_ Plex token and library-to-content-type mapping.

## 7. Lidarr (optional)

_Coming in Phase 2._

## Developing Requestarr

Working on Requestarr itself, or testing a build by hand? Two local stacks, each with test Sonarr and Radarr instances and its own Discord server and bot, run side by side: QA (the `qa` branch, for testing by hand) is started with `.\docker\dev.ps1 up` from PowerShell in the repo folder (or `docker/dev.sh up` with Git Bash), and dev (the `dev` branch, for automated testing) with `.\docker\dev.ps1 -Stack dev up`. Changes merge to `dev` first and reach `qa` only when promoted ([release flow](development.md#release-flow)). Their helpers stop and start a test instance, make a requested title look downloaded (`fake-download`), and make removal votes take minutes instead of days (`fast-votes on`). If PowerShell says running scripts is disabled, run `Set-ExecutionPolicy -Scope CurrentUser RemoteSigned` once (or use `.\docker\dev.cmd`). Everything is in [development](development.md#docker-stack).

## Running the beta

For the project owner: an always-on Requestarr for early access testers, on its own Discord server (Requestarr Beta). It uses **your production Sonarr and Radarr**: one library, so testers request from the titles you already have, and nothing downloads twice. It runs the published `beta` image, which only ever holds builds you passed in QA ([release flow](development.md#release-flow)).

It's one container, installed the way you'd install Sonarr or Radarr from a registry: the image from GitHub's registry, then a name, a port, a folder, your time zone and the GitHub key for bug reports. No compose file, no other files, no shell. Everything else is set in its web app.

**Production's files are safe by default.** Requestarr deletes nothing in Sonarr or Radarr until you turn on **Delete files** (B.4). Until then a removal that's agreed only waits in the admin queue for you to delete it by hand.

**Titles Requestarr didn't add.** Your Sonarr and Radarr already hold titles added by hand or by the old Requestrr bot, and Requestrr may still add more. Requestarr shares the library with them: a request for one answers "already in the library" and adds nothing, Sonarr and Radarr's download events for them are ignored quietly, and a removal of one only starts from the admin panel's **Remove a title**.

Do the steps in order; each ends with what to check before going on.

### What you need

- Your Synology with **Container Manager** (DSM 7.2 or newer).
- Your production Sonarr and Radarr, and their API keys (each one's **Settings → General → API Key**).
- The Requestarr Beta Discord server, and a Discord application with a bot for it, set up as in [section 2](#2-create-your-discord-bot) (Server Members Intent on, invited with the same permissions). You need its bot token, application ID and the server's ID.
- **GitHub's registry in Container Manager.** The image is `ghcr.io/requestarr/requestarr`, on GitHub's registry only. In **Container Manager → Registry → Settings**, `ghcr.io` should be listed; if it isn't, **Add**: name `GitHub`, address `https://ghcr.io`. Once the package is public on GitHub it needs no login. While it's private, give that entry your GitHub user and, as the password, a token that can only read packages (GitHub → Settings → Developer settings → Personal access tokens → classic, **read:packages**).
- **A GitHub key for bug reports:** a fine-grained token with only Issues: read and write on Requestarr/Requestarr (GitHub: Settings, Developer settings, Fine-grained tokens). Beta files every report on GitHub with it; it goes on the container (B.2), never in the web app.
- **A folder for Requestarr's data.** In **File Station**, in the `docker` shared folder, create `requestarr-beta`. Its settings, database and backups live there, where you can see and back them up. Requestarr runs as its own user inside the container, not as you, so let it write there: right-click the folder, **Properties → Permission → Create**, user or group **Everyone**, tick **Read** and **Write**, apply to this folder, sub-folders and files, **Done**, **Save**.

### B.1 Download the image

1. **Container Manager → Registry → Settings**: select `ghcr.io` and press **Use**. Container Manager searches only the registry in use, so this is what lets it find the image. Close **Settings**.
2. In **Registry**, search for `requestarr/requestarr`, select it and press **Download**.
3. Choose the tag **`beta`** and **Apply**.

If the search finds nothing (some versions of Container Manager can't search GitHub's registry), use **Image → Add → Add from URL** with `ghcr.io/requestarr/requestarr:beta` instead. If that won't download either, set it up as a project with the compose file instead (B.9), which always downloads.

When you're done, put **Use** back on Docker Hub (Registry → Settings) if you install other things from there.

**Check:** **Image** lists `ghcr.io/requestarr/requestarr` with the tag `beta`.

### B.2 Run it

In **Image**, select `ghcr.io/requestarr/requestarr:beta` and press **Run**. Each screen:

1. **General settings:** **Container name** `requestarr-beta`. Tick **Enable auto-restart**. Leave the resources as they are. **Next.**
2. **Advanced settings:**
   - **Port settings:** the container's port `9797` (TCP) gets the local port **`19797`** (beta's: the standard 9797 with a 1 in front, so production keeps 9797). Remove any other ports listed.
   - **Volume settings:** **Add Folder**, choose `docker/requestarr-beta`, and mount it at **`/config`**, read and write.
   - **Environment:** add the variable **`TZ`** with your time zone, for example `Europe/London` ([the list](https://en.wikipedia.org/wiki/List_of_tz_database_time_zones)). Also add **`REQUESTARR_GITHUB_ISSUES_TOKEN`** with the GitHub key for bug reports (a fine-grained token with only Issues: read and write on Requestarr/Requestarr), so beta's reports reach GitHub; it's never set in the web app. Cast from The Movie Database needs nothing: the image has Requestarr's own key built in ([4.6](#46-cast-from-the-movie-database-optional) says how to use yours instead).
   - Leave the network as **bridge**. **Next.**
3. **Summary:** tick **Run this container after the wizard is finished**, then **Done**.

**Check:** **Container** shows `requestarr-beta` running, and its **Log** (Details, then Log) ends with "Application started". If the log says access to a path under `/config` is denied, the folder's permission (What you need) is missing: add it, then start the container again.

### B.3 The first-run setup

Open `http://<your server's address>:19797`. A new install opens its first-run setup:

1. **Your sign-in.** Choose **Forms (login page)**, keep **Authentication Required** on **Enabled** (beta requires sign-in for every address, so it needs no allowed hosts), and choose a username and password. Save, then sign in.
2. **This server and the Discord bot.** **This server is:** choose **Beta**, before adding Sonarr and Radarr: left on Production, Requestarr would name its connection there like production's, and it refuses to take over a connection another Requestarr owns. It gives the web app the BETA strip and beta's icons (from the next page you open), keeps every "what's new" post for good, and names Requestarr's connection in Sonarr and Radarr for beta. Then paste the beta bot's token, its application ID and the Requestarr Beta server's ID, and **Save**. **Bot status** says Connected, and **Add the bot to your Discord server** opens Discord's invite page if it isn't there yet.
3. **Production's Sonarr.** You can **Skip for now** and add Sonarr and Radarr later on the Connections page. Otherwise press **Add Sonarr**: a name (`Sonarr`), its address as the Synology reaches it (`http://<your server's address>:8989`, the server's own address, never a container name), its API key. Press **Test**, choose its quality profile and the root folder your TV is in, tick **Default**, and **Save**.

The setup then says Requestarr is ready. Finish under **Settings → Connections**, which holds what Requestarr connects to as cards, the way Sonarr's Indexers page does: **Discord**, **Media managers** (Sonarr and Radarr) and **Media servers** (Plex and Jellyfin, not built yet), with **Options** below. Press a card to change it; the **+** card in Media managers adds a Sonarr or Radarr.

- **Media managers, the + card, Radarr:** production's Radarr the same way (and Sonarr, if you skipped it in the setup) (`http://<your server's address>:7878`, its key, **Test**, its quality profile, the root folder your movies are in, **Default**, **Save**).
- **Options:** **Requestarr's address for Sonarr and Radarr** `http://<your server's address>:19797` (Sonarr and Radarr call it there) and **Check downloads every** `5` minutes, then **Save Changes** in the toolbar.

Every change is recorded on **Settings → Changes**. Keys and tokens are kept in Requestarr's own database and never shown again. **This server is** stays on the **Discord** card under **Settings → Connections**.

**Check:** the web app shows the BETA strip; **Settings → Connections** shows both Sonarr and Radarr cards Connected (**Test All Connections** tests them again and shows the result on their cards); **System → Status** has no Discord, Sonarr, Radarr or webhook warnings; and in Sonarr and Radarr, **Settings → Connect** lists **Requestarr Beta (availability)**. Requestarr adds that connection itself, named for beta so it can be told apart from production's Requestarr later. Its **Test** passes.

### B.4 Delete files, and why it starts off

**Settings → Requests → Delete files in Sonarr and Radarr** decides whether Requestarr may delete anything in your Sonarr and Radarr: a removal vote that passes, **Force removal**, **Delete** on a feed card, and **Remove a title**. It starts **off**, because beta runs new code against your real library, and a beta bug must never delete production's files.

While it's off, a removal still runs as usual: the requester is asked, the vote is held and closed, the outcome is recorded and everyone who was asked is told. Then, instead of deleting, it puts **Delete it yourself** in the admin queue (`/admin requests`, and the web app's **Queue**), with the title and the Sonarr or Radarr it's in. Delete it there yourself, then press **I deleted it**: its requests close and everyone is told. **Drop removal** keeps it after all. Every card that starts a removal says deleting is turned off on this server.

Turn it on once you trust removals on beta. The change is recorded on **Settings → Changes**.

**Check:** **Settings → Requests** shows **Delete files in media managers** off.

### B.5 Check Requestarr in Discord

**Check:**

- In the Requestarr Beta server: the bot is online, `#requestarr-admin` (in the Requestarr Admin group) has the admin panel at the bottom, and `#requestarr-welcome` (in the Requestarr Users group) shows the welcome message with **Request access**.
- `#requestarr-admin` shows "what's new" for every build, and `#requestarr-announcements` the members' part. On beta these posts stay for good.

Then add yourself and the testers from the admin panel's member picker ([5.5](#55-add-your-first-user)), once they've joined the Requestarr Beta server.

### After installing, check

These need the beta server, so they aren't on the QA checklist. Check them once beta runs, and the update after its next build:

- **The image:** Container Manager found and downloaded `ghcr.io/requestarr/requestarr:beta` (B.1), or one of its fallbacks did.
- **The folder's permission is enough:** the container's log ends with "Application started", with no access denied under `/config` (B.2).
- **The web app** on port 19797 shows the BETA strip (B.3).
- **The connection in production:** production's Sonarr and Radarr list **Requestarr Beta (availability)** under **Settings → Connect**, and its **Test** passes (B.3).
- **The GitHub key reaches beta:** a report sent with **Report a problem** gets a card with its GitHub link (the key set on the container in B.2).
- **Updates:** when the next build reaches beta, **Image** shows **Update available** on it (B.6), or downloading it again works.
- **Skipping Sonarr or Radarr** (on a fresh install, if you try it): **Skip for now** in the setup's third step finishes the setup, and **Settings → Connections** shows only the **+** card under Media managers.

### B.6 Updating

Updates happen only when you ask, never on a schedule. When a new build reaches beta:

1. **Container Manager → Image.** Container Manager checks its images now and then, and `ghcr.io/requestarr/requestarr:beta` shows **Update available**.
2. Select it and press **Update**. Container Manager downloads the new build and recreates `requestarr-beta` from it with the same settings: name, port, folder, time zone and keys. Everything in the folder carries over.

**If Update doesn't appear** (Container Manager doesn't always check other registries than Docker Hub): download the tag again (**Registry**, `requestarr/requestarr`, **Download**, tag `beta`, which replaces the old image), then in **Container**, stop `requestarr-beta`, **Action → Delete** it (its folder keeps all its data), and **Run** the image again as in B.2.

**Before it changes anything, Requestarr backs up its data.** When it starts on a newer build than the one that last ran, it takes a backup first; the web app lists it under **System → Backup** as an **Update** backup.

**Check:** `#requestarr-admin` gets the new build's "what's new", and **System → Status** shows its number.

**When a build needs a change to how the container runs** (a new port or folder), its "what's new" in `#requestarr-admin` says so in plain words: recreate the container with the change (stop it, delete it, Run the image again with the new settings).

Each build sent to the beta has its own number, counting every build: 0.7.0005 is the fifth. A release is that same build, with the same number, so what production runs is exactly what the beta testers ran.

### B.7 Going to a specific build

To run one particular build, for example to skip a bad one: **Registry**, `requestarr/requestarr`, **Download**, and choose that build's number as the tag (for example `0.7.0004`). Then in **Container**, stop `requestarr-beta`, **Action → Delete** it, and **Run** `ghcr.io/requestarr/requestarr:0.7.0004` as in B.2, with the same name, port, folder, time zone and keys. It stays on that build until you run the `beta` image again the same way.

**Check:** **System → Status** shows that build's number.

### B.8 Rolling back

Going back means the older build **and** the data from before the update, because a newer build may have changed the database in ways an older one can't read:

1. Run the older build (B.7).
2. In the web app, **System → Backup**: on the **Update** backup taken just before you moved past this build (the one from when the next build first started; the newest one if you went back one build), press **Restore**. Requestarr restarts on that data.

Anything done since that update (new requests, votes) is lost with the restore.

If an older build starts on a newer database without the restore, Requestarr keeps running, and **System → Status** and its log say, in plain words, that the database was last used by a newer Requestarr and to restore the Update backup from before that update.

**Check:** **System → Status** shows the older number and no database warning.

### B.9 With a compose file instead

If you'd rather keep the container's settings in a file, [`deploy/beta/compose.yaml`](../deploy/beta/compose.yaml) runs the same container: **Container Manager → Project → Create**, name `requestarr-beta`, a path for the project (for example `/docker/requestarr-beta-project`), **Create docker-compose.yml**, paste the whole file and fill in its values block (port, time zone, and the data folder made in What you need, with its permission), then **Next** and **Done**. Then the first-run setup (B.3) as above. To update, the project's **Action → Build** downloads the newest `beta` and restarts on it; a specific build is its number in place of `beta` on the `image:` line, then **Build**.

### Taking the beta down

**Container Manager → Container → `requestarr-beta` → Action → Stop** stops it and keeps everything. **Delete** removes the container; Requestarr's data stays in `docker/requestarr-beta` until you delete that folder too. Your Sonarr and Radarr keep everything; remove the **Requestarr Beta (availability)** connection from each if you won't run beta again.

### Earlier beta setups

The first beta folder (with its own Sonarr and Radarr, `-beta` media folders and `update.sh`) is retired: beta now shares production's Sonarr and Radarr. No beta server ran it, so there's nothing to move.

## 8. Troubleshooting

- **"Private application cannot have a default authorization link":** set Installation → Install Link to None first.
- **The bot cannot create or assign roles:** move the bot's role higher in Server Settings → Roles.
- **Members leaving are not detected:** Server Members Intent is off.
- **"Only the Requestarr admin can use this command":** you are not the server owner and don't have the role set in `adminRoleId`.
- **"Admin commands only work in the Requestarr admin channel":** run `/admin` and use the admin panel in `#requestarr-admin`, not in a user's channel.
- **"Backend check failed" under System → Status:** the message names the instance and the cause: wrong URL or API key, an unsupported version (Radarr 5+ and Sonarr 4+ only), or a quality profile or root folder that doesn't exist in that instance. Fix `provisioning.yml` and restart.
- **Users don't see /admin:** that's intended; it only shows to members with Manage Server (see 5.3).
- **System → Status warns about Pin Messages:** the bot's role lacks **Pin Messages**, so outage notices are posted but not pinned while an outage lasts. Everything else keeps working. Turn it on in Server Settings → Roles (see section 2, step 5).
- **The /request type list is empty, or a type is missing:** that user has the type set to off (change it on their card, `/admin user`), or no instance for it has connected yet (5.7).
- **Old admin commands such as `/admin user add` or `/admin approve` still show:** Discord caches commands; press Ctrl+R in the Discord app. They were replaced by `/admin user`, `/admin requests` and the panel.
- **Discord still shows old commands after an update:** press Ctrl+R in the Discord app, or fully quit it from the system tray and reopen it.
- **The bot doesn't come online:** check **System → Status** for the Discord message. Common causes are a wrong token, the bot not being invited to the server in `guildId`, or a missing application ID.
- **"Provisioning file could not be applied" under System → Status:** the message names the problem, for example an environment variable that is not set or an invalid value. Fix `provisioning.yml` or `secrets.env`, then `docker compose restart requestarr`.
- **"can't reach Requestarr at …" under System → Status:** that Sonarr or Radarr can't call Requestarr's webhook. Downloads are still tracked by the backup poll. Check `availability.webhookUrl` against the table in section 4.8, then restart Requestarr. On Docker, Requestarr and the instance must share a network for `http://requestarr:9797` to work.
- **The container restarts or stays unhealthy:** check `docker compose logs requestarr`. A common cause is a `./config` folder the container cannot write to (see section 3.2).
