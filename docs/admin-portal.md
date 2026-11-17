# The admin portal's checklist

The web app is the admin's full view of Requestarr: everything an admin can see or do in Discord's admin channels, plus views Discord can't give (lists across every member, filters, full histories). This is the list of everything an admin has in Discord today, each mapped to the portal page that covers it and the task that builds it:

- **5a:** the requests, members and queue pages: reading.
- **5b:** settings in the web app, with beta and production running without a provisioning file.
- **5c:** the actions on these pages, plus outages and the audit log.

Discord stays the place members act; the portal never replaces a Discord message, it shows the same things and, from 5c, does the same actions.

## The admin channel

| In Discord | What it is | Portal page | Task |
|---|---|---|---|
| The admin panel's **Pending queue** (with its count) | Everything waiting on an admin, oldest first | Queue | 5a |
| `/admin requests` (**Skip**, **Start over**) | The same walk, one private card at a time | Queue | 5a (read), 5c (act) |
| Approval card (**Approve**, **Deny**, **Already available**) | A request that needs approval | Queue and the request's page | 5a (read), 5c (act) |
| Access request card (**Approve…**, **Deny**) | Someone in the welcome channel asking to join | Queue | 5a (read), 5c (act) |
| Paused access requests notice (**Unblock**) | Someone who clicked too often; they aren't a member yet, so no member page has them | Access requests (a page of its own) | 5c |
| Media server link request (**Approve Plex link**, **Deny Plex link**) | A member asking to link their Plex account | Queue (waiting); the member's page shows the account once linked | 5a (read), 5c (act) |
| Download in trouble (**Search again**, **Try adding again**, **Cancel request**) | A sent request that failed, stalled or couldn't be added | Queue and the request's page | 5a (read), 5c (act) |
| Instance down (**Check now**; the alert's **Announce to users**) | A Sonarr or Radarr that can't be reached | Queue | 5a (read), 5c (act and outage history) |
| Removal waiting on the admin (**Force removal**, **Drop removal**) | A requester's No, a vote kept by Keep it, a failed deletion, a removal waiting on its requester | Queue and the request's page | 5a (read), 5c (act) |
| The admin panel's **Members** (member picker) and `/admin user` | One member's card | Member's page | 5a (read), 5c (act) |
| Member card: **Add member** | Onboard someone new | Members | 5c |
| Member card: access per content type (off, needs approval, auto-approve) | What they can request | Members and the member's page | 5a (read), 5c (change) |
| Member card: **More** menu (limits, instance) | Requests per rolling window, which instance their requests go to | Members and the member's page | 5a (read), 5c (change) |
| Member card: **Suspend**, **Unsuspend**, **Remove** | Their status | Members and the member's page | 5a (read), 5c (act) |
| Member card: **Link Plex…** | Link their media server account | The member's page | 5a (read), 5c (act) |
| The admin panel's **Users** | Every member, with their card | Members | 5a |
| Member card: **Their requests** | What one member asked for | The member's page, Requests | 5a |
| The admin panel's **Remove a title** (**Start removal**, **Start the vote**, **Force: delete now**) | Start a removal of any title in any library | Requests (a title's page) | 5c |
| Upgrade card (**Read changes**) | What changed in this version | System → Status, About | 5c |
| The admin panel's **Help** and `/admin help` | How Requestarr works | Each page's own wording; the setup guide | none (stays in Discord) |
| Account events (member added, access changed, suspended, Plex linked) | A post per change | The member's page, History | 5a |

## The admin feed

| In Discord | What it is | Portal page | Task |
|---|---|---|---|
| One card per request, moved to the bottom on every change: who asked, which instance, the state, the seasons | Every request's current state | Requests (a row per request) and the request's page | 5a |
| Feed card buttons (**Approve**, **Deny**, **Already available**, **Cancel**, **Add seasons**, **Delete**) | The actions valid for the request's state | The request's page | 5c |
| Download progress (grabbed, downloading, stalled, imported, available, failed) | Where the download is | Requests (status) and the request's page, Timeline | 5a |
| Removal cards (asked, voting, kept, forced, deleted) | A removal's state | The request's page, Removal and Timeline | 5a |

## Everywhere else an admin looks

| In Discord | What it is | Portal page | Task |
|---|---|---|---|
| Each member's private channel: their request messages and panel | What they asked for and its state | The member's page, Requests | 5a |
| A follower's own request message | A member who asked for a title someone already requested | Requests (Followers) and the request's page | 5a |
| Removal votes (**Keep it**) in each member's channel | Who wants to keep a title | The request's page, Removal | 5a |
| The announcements channel: outage notices, recovery, upgrade cards | What members were told | Outages | 5c |
| The QA checklist and bug reports (QA and dev servers) | Testing a build | Not in the portal: testing stays in Discord | none |
| Provisioning settings (cooldowns, vote and answer windows, spam thresholds, instances, connections) | How Requestarr behaves | Settings | 5b |
| The audit log (every action, who and when) | Requestarr's own record | Each page's Timeline or History (5a); a searchable Audit log page (5c) | 5a, 5c |

## Followers and watchers

A request's **requester** is the member who asked first; everyone else who asked for the same title is a **follower**. Requestarr has no separate "watch" today: a member who isn't on a request is only asked about it in a removal vote. The pages show the requester and every follower.
