# The admin portal's checklist

The web app is the admin's full view of Requestarr: everything an admin can see or do in Discord's admin channels, plus views Discord can't give (lists across every member, filters, full histories). This is the list of everything an admin has in Discord, each mapped to where the portal has it and the task that built it:

- **5a:** the requests, members and queue pages: reading.
- **5b:** settings in the web app, with beta and production running without a provisioning file.
- **5c:** the actions on these pages, plus access requests, outages, the audit log and the header search.

Every entry is covered. Discord stays the place members act; the portal never replaces a Discord message. It shows the same things and does the same actions through the same services, with the same rules, so Discord's messages change exactly as if the button had been pressed there. Discord names an admin acting in the web app as "name (web app)".

## The admin channel

| In Discord | What it is | In the portal | Task |
|---|---|---|---|
| The admin panel's **Pending queue** (with its count) | Everything waiting on an admin, oldest first | Queue: each item opens the page that acts on it | 5a, 5c |
| `/admin requests` (**Skip**, **Start over**) | The same walk, one private card at a time | Queue, with every item at once | 5a, 5c |
| Approval card (**Approve**, **Deny**, **Already available**) | A request that needs approval | The request's page: Approve, Deny (with a reason), Already available | 5c |
| Access request card (**Approve…**, **Deny**) | Someone in the welcome channel asking to join | Access requests: Approve (choosing each type's access), Deny (with a reason) | 5c |
| Paused access requests notice (**Unblock**) | Someone who clicked too often; they aren't a member yet | Access requests, Paused: Unblock | 5c |
| Media server link request (**Approve Plex link**, **Deny Plex link**) | A member asking to link their Plex account | Queue: Approve and Deny on the item; the member's page shows the account once linked | 5c |
| Download in trouble (**Search again**, **Try adding again**, **Cancel request**) | A sent request that failed, stalled or couldn't be added | The request's page: Search again, Try adding again, Cancel request | 5c |
| Instance down (**Check now**; the alert's **Announce to users**) | A Sonarr or Radarr that can't be reached | Outages, Can't be reached now: Check now, Announce to users | 5c |
| Removal waiting on the admin (**Force removal**, **Drop removal**) | A requester's No, a vote kept by Keep it, a failed deletion, a removal waiting on its requester | The request's page: Force removal, Drop removal; a title nobody requested: on its Queue item | 5c |
| The admin panel's **Members** (member picker) and `/admin user` | One member's card | The member's page | 5a |
| Member card: **Add member** | Onboard someone new | Members: Add member (searches the Discord server) | 5c |
| Member card: access per content type (off, needs approval, auto-approve) | What they can request | The member's page: Access, Save access | 5c |
| Member card: **More** menu (limits, instance) | Requests per rolling window, which instance their requests go to | The member's page: Instance, Limit, Save access and Save limit | 5c |
| Member card: **Suspend**, **Unsuspend**, **Remove** | Their status | The member's page: Suspend or Unsuspend, Remove member | 5c |
| Member card: **Link Plex…** | Link their media server account | The member's page: Link Plex | 5c |
| The admin panel's **Users** | Every member, with their card | Members | 5a |
| Member card: **Their requests** | What one member asked for | The member's page, Requests | 5a |
| The admin panel's **Remove a title** (**Start removal**, **Start the vote**, **Force: delete now**) | Start a removal of any title in any library | Requests: Remove a title (library search, the seasons, Force) | 5c |
| Upgrade card (**Read changes**) | What changed in this version | System, What's new | 5c |
| The admin panel's **Help** and `/admin help` | How Requestarr works | Each page's own wording; the setup guide | stays in Discord |
| Account events (member added, access changed, suspended, Plex linked) | A post per change | The member's page, History; the Audit log | 5a, 5c |

## The admin feed

| In Discord | What it is | In the portal | Task |
|---|---|---|---|
| One card per request, moved to the bottom on every change: who asked, which instance, the state, the seasons | Every request's current state | Requests (a row per request) and the request's page | 5a |
| Feed card buttons (**Approve**, **Deny**, **Already available**, **Cancel**, **Add seasons**, **Delete**) | The actions valid for the request's state | The request's page: the same actions, for the same states (Delete is Start removal) | 5c |
| Download progress (grabbed, downloading, stalled, imported, available, failed) | Where the download is | Requests (status) and the request's page, Timeline | 5a |
| Removal cards (asked, voting, kept, forced, deleted) | A removal's state | The request's page, Removal (with who pressed Keep it) and Timeline | 5a, 5c |

## Everywhere else an admin looks

| In Discord | What it is | In the portal | Task |
|---|---|---|---|
| Each member's private channel: their request messages and panel | What they asked for and its state | The member's page, Requests | 5a |
| A follower's own request message | A member who asked for a title someone already requested | Requests (Followers) and the request's page | 5a |
| Removal votes (**Keep it**) in each member's channel | Who wants to keep a title | The request's page, Removal: Keep it | 5c |
| The announcements channel: outage notices, recovery, upgrade cards | What members were told | Outages (each outage, and who announced it), System, What's new | 5c |
| The QA checklist and bug reports (QA and dev servers) | Testing a build | Not in the portal: testing stays in Discord | none |
| Provisioning settings (cooldowns, vote and answer windows, spam thresholds, instances, connections) | How Requestarr behaves | Settings: Connections, Requests; Changes | 5b |
| Requestrr's old request channels (on a server moving from Requestrr) | Who asked for what before Requestarr | Settings, Import from Requestrr: a preview, then the import, recorded in the audit log (setup guide, 5.15) | Requestrr import |
| The audit log (every action, who and when) | Requestarr's own record, kept for 1 year by default (Settings, Requests: Keep the audit log for) | Each page's Timeline or History; the Audit log, searchable by member, title, action and day | 5a, 5c |

Finding a request or a member quickly: the search in the header (press `/`) finds requests by title and members by name, Discord username, Discord ID or a name they had before. It only finds what Requestarr has; it never looks up or requests a new title.

## Followers

A request's **requester** is the member who asked first; everyone else who asked for the same title is a **follower**. The Requests views, the queue and the header search keep each title to one line however many follow it: the requester's small picture and name (the @handle on hover), then up to five follower pictures and a count ("+37 following"), the first ten names on hover. Pressing it opens the request's page at its Followers section, which lists every follower with their @handle and when they followed, earliest first or sorted by name, scrolling when the list is long. The Requests page's Member filter finds a title by its requester or any follower. Members who played a title on the media server ("watched") come with the media server work in Phase 3 (Plex first, Jellyfin and others behind the same provider).
