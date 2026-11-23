# Changelog

What changed, for the people who use Requestarr. Two sections only, **For users** and **For admins**, in plain words;
a section with nothing in it is left out, and a version with nothing for either has only its heading. Requestarr posts
these in Discord: both sections in the admin channel, and **For users** in the announcements channel, word for word.
Each version heading carries the day it was made, which its card in Discord shows.

Unreleased is what changed since the last build the beta testers got, written as where things ended up: a change to
something already under Unreleased rewrites that line, and a change to something the testers already have is a new
line (see "Changelog" in docs/development.md). The staging promotion gives it the build's number.

Numbering changed with build 0.7.0004. Up to 0.3.4 every change took its own number, and those stay as they are; the
versions after it, 0.4.0 to 0.6.0, never reached the testers and are folded into Unreleased. From 0.7.0004 on, every
finished goal's build takes the next number, the beta and the release carry the number of the build promoted to them,
and a beta heading names the goals' builds it includes.

## Unreleased

### For users
- **Anime versions in plain words:** the bot now says "English dub" or "subtitled version", and when two people want different versions it says which one is kept.
- **Request messages don't name servers:** your request goes where it belongs by what you asked for, so the bot no longer says which server took it.

### For admins
- **Simpler bot setup:** you paste only the bot's token. Requestarr notices when the bot joins your server, makes its admin role, and lets you pick admins in the web app.
- **Servers as cards:** each server the bot is in has a card, and the + card adds the bot to another server, for example only to import from Requestrr. A saved token shows only its last four characters.
- **Beta knows it's beta:** the beta image says so, so there's no Production or Beta to choose any more.
- **Clearer Sonarr and Radarr forms:** they open in their card's window, with plainer wording. New ones start with Standard series, the Any profile and new members approved automatically.
- **Test shows its result:** Test shows a green check or a red mark, with the reason where you're looking. Leaving with unsaved changes asks first.
- **Delete, with history kept:** a Sonarr or Radarr can now be deleted. Its requests keep showing where they went, and any still waiting are flagged for a new home.
- **Requests go by what's asked for:** each Sonarr or Radarr says what it's for: everything, anime or nothing yet, and anime goes to the anime one. This replaces the Default tick box and members' own servers; any you set are dropped, their access and limits kept.
- **Download updates sort themselves out:** Requestarr fills in its own address from your browser and tests it. Each card says whether downloads are reported right away or on a timer, and why.

## 0.7.0010 (2026-11-22), with 0.7.0004 to 0.7.0010

### For users
- **Asking for access:** friends who aren't members yet can ask for access from the welcome channel and hear back privately. Pressing the button too often pauses their requests until an admin lifts the pause.
- **Your channel:** your request channel sits with the announcements channel in the Requestarr Users group, and your panel is always its newest message.
- **Update cards:** each update shows its build number and day, with the main change underneath. Read changes shows you the full list, just for you.
- **Tidier answers:** pressing Help, My requests or Read changes again replaces your earlier answer instead of adding a copy. Welcome messages and notices are easy-to-read coloured cards.
- **Your name stays yours:** when you change your name in Discord, Requestarr uses the new one and renames your request channel to match.
- **Outage notices:** while requests are paused, the notice stays pinned in the announcements channel and goes once things work again.
- **Report a problem:** the bug reports channel has a button to tell the people who build Requestarr what went wrong. Reports are public, and the form says so before you send.
- **New look:** the logo's arrow is a soft charcoal instead of black. Link Plex is hidden until an admin connects a Plex server.

### For admins
- **The admin portal in the web app:** everything you do in Discord's admin channels can be done in the web app, which works like Sonarr's. Requests and Members have posters, filters, select mode and pages that update by themselves, and Discord's messages follow along.
- **Settings in the web app:** a new server is set up from the web app alone, starting with a first-run setup. Connections lists Discord, Sonarr and Radarr as cards like Prowlarr's apps, and Settings, then Changes, records who changed what.
- **Beta on a Synology:** the beta is one container from GitHub's registry, installed in Container Manager and using your production Sonarr and Radarr. It shows a BETA strip, and Sonarr and Radarr list it as Requestarr Beta.
- **Moving from Requestrr:** Settings, then Import from Requestrr, reads past requests from Requestrr's old channels and shows them before anything is saved. Everyone who asked is listed under Members, and nothing is posted in the old channels.
- **Safe removals:** the Delete files setting starts off, so an agreed removal waits in the admin queue for you to delete by hand. Titles not in your library, like imported ones, can be put to a removal vote too.
- **Members who rename:** Requestarr knows each member by their Discord ID, so a new name changes nothing about them. Their request channel and role follow the new username, and the rename shows in their history.
- **Build numbers and safe updates:** every build has one number, like 0.7.0010, shown everywhere from the update cards to bug reports. Requestarr backs up its data before starting a newer build, so going back is the older build plus that backup.
- **A tidier Discord:** access requests and spam warnings arrive as cards in the admin channel, panels stay at the bottom, and channels sit in two groups. A reinstall never makes second copies.

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
