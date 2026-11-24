# Changelog

What changed, for the people who use Requestarr. Two sections only, **For users** and **For admins**, each grouped
into **New**, **Fixed** and **Changed** as Sonarr's and Radarr's updates are, in plain words; a section or group with
nothing in it is left out, and a version with nothing for either has only its heading. Requestarr posts these in
Discord: both sections in the admin channel, and **For users** in the announcements channel, word for word. Each
version heading carries the day it was made, which its card in Discord shows.

This file is written from the changelog's entries, one small file per change, and is never edited by hand (see
"Changelog" in docs/development.md). Unreleased is what changed since the last build the beta testers got. A change
that reworks something they already have is its own line, saying what changed for them; one that reworks something
they never got shows only in its final form.

Numbering changed with build 0.7.0004. Up to 0.3.4 every change took its own number, and those stay as they are, as
written then; the versions after it, 0.4.0 to 0.6.0, never reached the testers and are folded into 0.7.0010. From
0.7.0004 on, every finished goal's build takes the next number, the beta and the release carry the number of the build
promoted to them, and a beta heading names the goals' builds it includes.

## Unreleased

### For users

#### New
- **Your history comes with you:** requests you made with the old bot can show in your channel, once. Type /history there if they don't.
- **Series fixes shown:** when a show is added with a fix for a known mix-up, its request message says so and why.

#### Changed
- **Anime versions in plain words:** the bot now says "English dub" or "subtitled version", and when two people want different versions it says which one is kept.
- **Wording that fits your server:** removal votes and help talk about the library, not apps your server may not use.
- **Request messages don't name servers:** your request goes where it belongs by what you asked for, so the bot no longer says which server took it.

### For admins

#### New
- **Servers as cards:** each server the bot is or was in has a card. Open it to take the bot out, or to add it back where it was removed. The + card adds the bot to another server, for example only to import from Requestrr, and closes once the bot joins.
- **Delete, with history kept:** a media manager can now be deleted. Its requests keep showing where they went, and any still waiting are flagged for a new home.
- **Imported history in Discord:** post a member's old requests in their channel when you onboard them, from their page, or with /admin history, which also does the whole server. An import posts them in the admin feed when that's on in the server's window. Quietly, oldest first, and only once.
- **Imported history in System, then Tasks:** while history is posted, System, then Tasks lists it with how many are posted so far.
- **Requests go by what's asked for:** each media manager says what it's for: all other shows (or movies), anime or nothing yet, and anime goes to the anime one. This replaces the Default tick box and members' own servers; any you set are dropped, their access and limits kept.
- **Series fixes:** a ready list of fixes for shows Sonarr grabs the wrong releases for, under Media managers. A fix is set up in Sonarr only when its show is requested, and the request says so. It comes with one: Pokémon (1997) never grabs Pokémon Horizons.

#### Changed
- **Simpler bot setup:** you paste only the bot's token. Requestarr notices when the bot joins your server, makes its admin role, and lets you pick admins in the web app.
- **No Production or Beta to choose:** the beta image says it's beta, so the first-run setup no longer asks.
- **Clearer media manager forms:** they open in their card's window, with plainer wording. New ones start with Standard series, the Any profile and new members approved automatically.
- **Test shows its result:** Test shows a green check or a red mark, with the reason where you're looking. Test All says every connection passed, or names each one that failed. Leaving with unsaved changes asks first.
- **Nothing takes over by itself:** turning off or deleting the media manager for all other shows (or movies) warns first, then pauses those requests and lists them in your queue until another is set to take them. Anime keeps going.
- **Download updates sort themselves out:** Requestarr fills in its own address from your browser and tests it. Each card says whether downloads are reported right away or on a timer, and why.
- **Saved bot token:** a saved token shows only its last four characters.
- **Adding the bot for an import:** use the + card beside the servers, since the Invite bot button is gone. A server's card takes the bot out, or adds it back.

## 0.7.0010 (2026-11-22), with 0.7.0004 to 0.7.0010

### For users

#### New
- **Asking for access:** friends who aren't members yet can ask for access from the welcome channel, and they hear back privately.
- **Access requests paused:** pressing the request button too many times in a short while pauses that person's access requests. An admin can lift the pause, and the message they see says so.
- **Update cards:** each update in the announcements channel shows its build number and day, with the main change and how many more. Read changes shows you the full list, just for you.
- **Your name stays yours:** when you change your name in Discord, Requestarr calls you by your new name, and a new username renames your request channel to match.
- **Report a problem:** the bug reports channel has a Report a problem button. Pick the part of Requestarr, say what happened and what you expected, and it reaches the people who build it. Reports are public, and the form says so.

#### Fixed
- **No stacked copies:** pressing Help, My requests or Read changes again replaces your earlier answer instead of adding another copy under it.

#### Changed
- **Your channel:** your request channel sits with the announcements channel in the Requestarr Users group, and your panel is always its newest message.
- **Easier to read:** the welcome message, the paused notice and Read changes are coloured cards with large headings, at normal message size.
- **Outage notices:** while requests are paused, the notice stays pinned at the top of the announcements channel, and it goes away once things work again.
- **Softer logo:** the arrow in Requestarr's logo is a soft charcoal instead of solid black, so it sits more gently next to the teal.
- **Link Plex is hidden for now:** the button comes back once the admin connects a Plex server.

### For admins

#### New
- **Member pages show their Discord profile:** the header behind a member's name shows their Discord banner, or their profile colour when they have none, with their picture large.
- **History removal from Discord:** a title your library doesn't have, often from an import, can be put to a removal vote from Discord too. Remove a title finds it, and its feed card has Delete; only Requestarr's lists change.
- **Years for imported titles:** a title imported without a year gets it from The Movie Database, so it shows as "Hollow Trees (2026)". Titles already imported are filled in too.
- **Pages update by themselves:** Requests, Members, the Queue and every other list change as soon as something changes, in Discord or anywhere else, with no refresh, as Sonarr's pages do.
- **Long jobs in System, then Tasks:** an import from Requestrr and onboarding several people at once run as tasks, listed under System, then Tasks while they run, with their messages under Events.
- **Settings in the web app:** a server without a provisioning file is set up and run from the web app alone. A first-run setup asks for your sign-in, the Discord bot and a first Sonarr or Radarr, which you can skip.
- **Connections as cards:** Settings, then Connections, holds the Discord bot, each Sonarr and Radarr and the media servers as cards, the way Sonarr's Indexers page does. Each shows whether it's connected, with Test All Connections in the toolbar.
- **Cast with nothing to set:** cast from The Movie Database works with a key built into Requestarr. To use your own, set it on the server's container; System, then Status, warns if the built-in one stops working.
- **Settings, then Changes:** every change to a setting is recorded: who made it, when, and from what to what.
- **Sonarr and Radarr settings like Prowlarr's apps:** adding or changing one uses the same window as Prowlarr's apps. Quality profiles and root folders are listed once the address and key work.
- **The admin portal in the web app:** everything an admin does in Discord's admin channels can be done in the web app too, with the same rules. Discord's messages change as if it had been done there, naming you with "(web app)".
- **Requests:** every request with who asked, who follows it, its status and where it went, with Sonarr's toolbar: Options, View, Sort, and Filter with filters you save. Each status is a coloured label, with a legend and totals at the bottom.
- **Three views of requests:** Posters, Overview and Table, as in Sonarr, each with its own options. Every request gets its poster and details once, imported ones included.
- **A request's page:** opens like Sonarr's series page, with its whole history in plain words and its actions. Approve, deny, already available, cancel, search again, try adding again, add seasons, and every removal step are there.
- **Select requests:** pick requests as in Sonarr, then Approve, Deny or Start removal from the toolbar. Each acts on the picks it can and confirms with the list of titles.
- **Members:** everyone with their Discord picture and @handle, their access, limits and counts, people known only from an import included. It has the same options, sorting, filters and select mode as Requests.
- **A member's page:** their picture, @handle, Discord ID and status, with what can be done to them: change access and limits, suspend, unsuspend, remove, and link Plex.
- **Onboard:** lists everyone in your Discord server who isn't a member yet, to pick one or many, set up their access together and onboard them, with each one's progress and Retry.
- **Queue, access requests, outages and audit log:** the Queue is what waits on you, each item opening the page that acts on it. Access requests, Outages and the Audit log each have their own page.
- **Header search:** press / to find a request by title, or a member by name, Discord username, Discord ID or an earlier name. It also finds titles in your library nobody requested, with Start removal right there.
- **Followers at a glance:** lists show the requester and up to five follower pictures with a count such as 3 following. Pressing it opens the request's Followers list.
- **Long lists:** Requests and Members are one long list with a letter bar, as in Sonarr. Other lists show 25 at a time with Sonarr's pager, and each list's options choose its columns.
- **Open in Sonarr or Radarr:** a request whose title is in your library has a button going straight to that title's page. If your browser can't reach an instance at its usual address, set Address in your browser on it.
- **Beta on a Synology:** the beta is one container from GitHub's registry, installed in Container Manager the way you'd install Sonarr. It uses your production library, so nothing downloads twice.
- **This server is Production or Beta:** the first-run setup asks which server this is. Beta shows the BETA strip and names its connection in Sonarr and Radarr Requestarr Beta, so the two can be told apart.
- **Delete files switch:** a setting under Settings, then Requests, decides whether Requestarr may delete anything, and it starts off. While it's off, an agreed removal waits as Delete it yourself until you press I deleted it.
- **Safe updates and rollbacks:** Requestarr backs up its data before it first starts a newer build, listed under System, then Backup. An older build started on newer data keeps running and says so instead of failing.
- **Keep the audit log for:** choose how long Requestarr keeps its record of who did what, from 90 days to forever, under Settings, then Requests. It starts at 1 year, and requests, members and renames are never removed.
- **Members who rename:** Requestarr knows each member by their Discord ID, so a rename changes nothing about them. Their names follow Discord, and each rename shows in their History.
- **Moving from Requestrr:** import past requests from Requestrr's old channels under Settings, then Import from Requestrr. You see every request before anything is written, and running it again adds nothing twice.
- **Imported people:** everyone who asked is listed under Members as imported, whether or not they're in your server. No role or channel is made until you onboard them.
- **Importing from another server:** Invite bot asks only to read there. The bot creates nothing and posts nothing in that server, and you can remove it once the import is done.
- **Build numbers:** every finished piece of work gets the next build number, like 0.7.0005, kept through testing, the beta and the release. A release is that exact build, never rebuilt.
- **Access requests:** everyone in the server sees a welcome message with Request access. Requests arrive as cards in the admin channel and the pending queue, where you approve with the access you choose or deny with a reason.
- **Default access for new members:** each media manager says what someone new gets for its type: Off, Needs approval or Approved automatically. Ones you already had keep Needs approval.
- **Spam protection:** repeated clicks on the welcome buttons make people wait, and if they keep going their access requests are paused. You get one notice, with Unblock.
- **Update cards in the admin channel:** each update is a card with its build number and day, the main change and how many more. Read changes shows the full list privately.
- **The bot keeps its own picture and banner:** Requestarr gives its bot the Requestarr picture and your environment's banner, and updates them with a release. A picture you chose yourself is kept.
- **Bug reports become issues:** reports can be filed on GitHub in the Requestarr organization, with the version and who reported it. It's off unless you turn it on with your own access key.

#### Fixed
- **Fixed: the audit log's trim deleted imported history.** The yearly trim went by when each event happened, so imported requests from years ago were deleted. It now goes by when Requestarr wrote each entry.
- **Titles already in your library:** titles added by hand or by the old Requestrr bot answer "already in the library" with no second copy, and their download events are ignored quietly.
- **No second copies after setting up again:** Requestarr finds its own groups, channels and panels even when its saved data is gone, so a reinstall never doubles them.
- **No stacked copies for admins:** pressing Pending queue, Users, Help or Read changes again replaces your earlier private answer instead of stacking copies.
- **Settings you can save:** the General settings page saves again, and each setting your provisioning file sets shows read-only, with a note to change it in the file.
- **Fixed: signing out:** after signing out, any page of the web app opens the login page instead of a bare browser error.
- **Safer logs:** a web address or user name with a line break in it can no longer add fake lines to Requestarr's log.

#### Changed
- **Keys kept out of sight:** the GitHub key for bug reports is set on the server's container, not in the web app. Keys and tokens you save are kept but never shown again.
- **No Tags page:** the Tags page is gone, since Requestarr doesn't use tags. Old links to the Discord, Sonarr and Radarr settings open the matching part of Connections.
- **Removing anything follows the removal flow:** a request that isn't in your library gets the same vote, Keep it and Force removal as any other. When it passes, it leaves Requestarr's lists with nothing to delete.
- **Two channel groups:** Requestarr's channels live in Requestarr Admin and Requestarr Users; existing channels move over with their messages kept.
- **Panels stay at the bottom:** every panel moves below anything new Requestarr posts in its channel, with no pings and no "edited" mark.
- **Outages:** the down alert, and the notice to users if you announced it, is pinned while the outage lasts and removed after. Every outage is kept in Requestarr's history.
- **Media server buttons only when connected:** Link Plex and the welcome channel's media server buttons show only for a media server you've connected, so they're hidden for now.
- **The web app's logo:** Requestarr's logo replaces the old purple square everywhere, with a coloured bar naming the environment. On your live install the bar shows the version you run.
- **Icons:** the browser tab, home screen icons and tiles use the charcoal arrow.
- **New home on GitHub:** Requestarr has its own GitHub organization, also called Requestarr. The Status page's links lead there, and beta updates come from there.
- **Kept up to date:** the parts Requestarr is built from are updated every week, and a build that fixes a security problem says so here.
- **Copyright:** Requestarr's copyright is held by its contributors, with the Servarr team's notice for the code it's built on. System, then Status, shows it.

## 0.3.4 (2026-10-24)

## 0.3.3 (2026-10-24)

## 0.3.2 (2026-10-24)

## 0.3.1 (2026-10-24)

## 0.3.0 (2026-10-24)

### For users
- **What's new history:** the announcements channel keeps a post for every version, so you can scroll back through everything that has changed.

### For admins
- **What's new history:** the admin channel keeps a post for every version as a lasting record. A new install posts the full history once when it first starts, oldest first.
- **Clearer what's new posts:** each post shows only the sections that have something in them, and a version with nothing for you or your members posts nothing at all.
- **Published container image:** Requestarr is now published on GitHub's container registry for x86-64 and ARM servers, so you can pull it instead of building it yourself.

## 0.2.4 (2026-10-24)

## 0.2.3 (2026-10-24)

## 0.2.2 (2026-10-24)

## 0.2.1 (2026-10-24)

## 0.2.0 (2026-10-22)

### For users
- **Remove what you're done with:** press **Remove…** on a request's message to put the title to a vote.
- **Still watching?** If a title you can watch might be removed, you get a message first: press **Keep it** to keep it. One keep is enough, nobody sees who pressed it, and you can take it back.
- **Your say on the admin's removals:** if the admin wants to remove something you asked for, you're asked first, and no answer by the date shown counts as yes.
- **Vote results:** everyone who was asked sees whether a title was kept or removed, and a removed title can be asked for again any time.
- **Fixed:** switching a series between dub and sub always said it was available; the message now says whether it's still on its way, partly there or ready.

### For admins
- **Removal votes:** press **Delete** on a downloaded title's feed card, or **Remove a title** on the admin panel to search every Sonarr and Radarr library, titles nobody requested included. The requester is asked first, you can force or drop a removal at every stage, and removals show in /admin requests.
- **What a removal deletes:** the files and the title go from Sonarr or Radarr without being added to the exclusion list; removing seasons deletes their files and stops monitoring them, and the show stays.
- **Removal timing:** votes run 7 days, requesters have 7 days to answer and are reminded on day 5; all three can be changed in your provisioning settings, ready for the admin web portal.
- **Nothing loaded from outside:** the TMDB logo and the feed card spacer are built into Requestarr and sent with each message, instead of being loaded from GitHub.

## 0.1.1 (2026-10-21)

## 0.1.0 (2026-10-21)

### For users
- **Request movies and TV:** type /request, pick a type, type a title, and press **Request** on the one you want.
- **Seasons and anime:** choose which TV seasons you want, or all of them including new ones as they air; for anime, choose English dub or subtitles first.
- **Follow instead of asking twice:** if someone already asked for a title, press **Follow** to be told when it's ready, without using your limit.
- **Your request keeps you posted:** its message shows when it's waiting for approval, sent, downloading, stuck and ready to watch, and you can cancel it while it's still waiting for approval.
- **Your own channel:** a pinned panel with **My requests**, **My limits**, **Link Plex** and **Help**; link your Plex account there and the admin approves it.
- **Announcements:** a channel that tells you about new features and about outages that affect everyone.

### For admins
- **Discord set up for you:** on first start Requestarr creates the admin channel, the admin feed, the request channels' category, the members role and the announcements channel, and recreates any that are deleted.
- **Members:** add them from the admin panel's member picker or /admin user; each gets a role, a private channel and a pinned panel. Suspend, unsuspend and remove them; anyone who leaves the server is suspended automatically.
- **Access per member:** for each type, off, needs approval or auto-approve, a limit per rolling window, and which Sonarr or Radarr their requests go to.
- **Approvals in one place:** approval cards, the admin feed with one card per request, Plex link requests with Approve and Deny, and /admin requests to go through everything waiting on you one item at a time.
- **Download tracking:** Requestarr adds itself as a connection in Sonarr and Radarr to follow downloads, and checks regularly as a backup.
- **Anime:** sub and dub tags and release profiles on Sonarr, with optional separate sub and dub instances.
- **Connection alerts:** you're told when Sonarr or Radarr can't be reached and when it's back, with **Announce to users** after 30 minutes.
