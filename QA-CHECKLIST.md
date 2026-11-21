# QA checklist

What the owner tests on the QA server, in Discord, before a build goes to staging. Requestarr posts the items that need a person in the QA server's checklist channel for the build QA runs and every version staging doesn't have yet; the owner presses Passes or Failing on each. When every item has passed and no bug report from QA is still open, the build can go to staging. The dev server has no checklist channel.

Every pull request adds its items under Unreleased, the way it adds its changelog lines. Each item is a bold name, numbered Steps (at most 5, one short action each), Expect bullets (at most 4, one thing to see each) and a line saying who checks it: the automated checks, naming the check that verifies it (run before every promotion to QA), or the owner, with the reason a person has to (it needs human eyes, the behavior wanted isn't settled, or the automated checks couldn't verify it). No sentence over 20 words and no item over 80; a longer item is two items. Unreleased is what to test since the last build the beta testers got: a change to something an Unreleased item already tests rewrites that item instead of adding another. The promotion to staging gives them the build's number. See docs/development.md#qa-checklist.

## Unreleased
- **Update cards**
  - Steps:
    1. Open the admin channel, then the announcements channel.
    2. Look at the update card in each.
  - Expect:
    - One card in each, titled Requestarr.
    - The build number with qa, like 0.7.0005-qa, then since the last beta.
    - The main change underneath, and how many more.
    - No cards for the versions 0.4.0 to 0.6.0.
  - Checked automatically: the live update cards check.
- **Read changes**
  - Steps:
    1. Press Read changes on the update card.
  - Expect:
    - The full list of what changed since the last beta.
    - Only you see it.
  - Checked automatically: the live Read changes check.
- **Build number in the web app**
  - Steps:
    1. Open System, then Status.
    2. Look at the version, the logo and About.
  - Expect:
    - The build number with qa, like 0.7.0005-qa.
    - The same number on the logo.
    - Under About, the copyright of Requestarr's contributors and the Servarr team's notice.
    - Under About, the credit to The Movie Database.
  - Checked automatically: the web app status page check.
- **Web app logo**
  - Steps:
    1. Look at the web app's header in the light theme.
    2. Switch to the dark theme and look again.
    3. Look at the icon in the browser tab.
  - Expect:
    - A full-height R beside the yellow QA bar, with no box around it.
    - The arrow in a soft charcoal.
    - In the dark theme, the logo and the tab icon on charcoal gray, never on black.
  - Checked automatically: the web app header check.
- **Signing in and out**
  - Steps:
    1. Open the web app and sign in with the QA login.
    2. Open the account menu at the top right, then press Logout.
    3. Open the web app's address again.
    4. Sign in again.
  - Expect:
    - The login page with the QA logo every time you're signed out.
    - Never a browser error.
    - Signing in brings you back to where you were going.
  - Checked automatically: the web app sign-in and sign-out check.
- **General settings**
  - Steps:
    1. Sign in, then open Settings, then General.
    2. Change a setting the provisioning file doesn't set, such as Backup Retention.
    3. Press Save.
    4. Look again in the dark theme.
  - Expect:
    - It saves with no error.
    - Fields the file sets, such as Allowed Hosts, Authentication and Username, are greyed out.
    - They say "Set by your provisioning file", in both themes.
    - The port has a note about Docker.
  - Checked automatically: the web app general settings check.
- **Sign-in logo**
  - Steps:
    1. Sign out of the web app.
    2. Look at the sign-in page in the light theme, then the dark theme.
  - Expect:
    - The logo with a light arrow, easy to read on the gray box, in both.
  - Checked automatically: the web app sign-in page check.
- **Loading screen**
  - Steps:
    1. Reload the web app and watch it while it starts.
  - Expect:
    - The QA logo, the same as the header's.
    - No purple square.
  - Checked automatically: the web app loading screen check.
- **Bot picture and banner**
  - Steps:
    1. In the Requestarr QA server, click the bot's name to open its profile.
  - Expect:
    - The plain Requestarr picture.
    - The yellow QA banner.
  - Checked by the owner, needs human eyes: the bot's profile only shows in the Discord app, and how it looks is a judgement.
- **Requests views**
  - Steps:
    1. In the web app, open Requests.
    2. Use View to switch between Overview, Posters and Table.
    3. In Posters, open Options and change the poster size.
    4. Do it again in the dark theme.
    5. Press Open in Sonarr in Overview.
  - Expect:
    - Sonarr's posters, overview and table, with a status-coloured bar under each poster.
    - Options sets the poster size and what shows with each title.
    - Table has a small poster on each row.
    - The series' page in Sonarr opens in a new tab.
  - Checked automatically: the web app request views check.
- **Jump bar and coming back**
  - Steps:
    1. In Requests, sort by Title.
    2. Press a letter in the bar on the right.
    3. Open a request, then go back.
  - Expect:
    - The list jumps to the titles starting with that letter.
    - Going back returns to the same place in the list.
  - Checked automatically: the web app request views check.
- **Requests and members pages**
  - Steps:
    1. In the web app, open Requests, then a request.
    2. Open Members, then a member.
    3. Do it again in the dark theme.
  - Expect:
    - Every request with its requester, followers, status and instance, with filters.
    - Request and member pages open like Sonarr's series page, actions in the toolbar.
    - Each member's picture beside their name and @handle, as tall as both, with access and counts.
    - Timeline and history entries open in full from their details button.
  - Checked automatically: the web app portal pages check.
- **Queue, outages and audit log**
  - Steps:
    1. In the web app, open Queue, Access requests, Outages and Audit log.
    2. Open System, then What's new.
    3. Do it again in the dark theme.
  - Expect:
    - The Queue has the same waiting items as the admin channel's queue.
    - Access requests shows who is waiting to join or paused.
    - Outages shows what can't be reached, and past outages.
    - The audit log with its filters, and each version's changes in What's new.
  - Checked automatically: the web app portal pages check.
- **Requestarr's settings pages**
  - Steps:
    1. In the web app, open Settings, then Connections.
    2. Press each + card, then a Sonarr card.
    3. Open Requests and Changes.
    4. Do it again in the dark theme.
  - Expect:
    - Cards under Discord, Media managers and Media servers, with Options below.
    - Media managers' + lists Sonarr and Radarr; Media servers' + says not built yet.
    - A card opens with Test, Cancel and Save; provisioned settings are read-only.
    - Requests keeps the audit log 1 year; Changes lists who changed what.
  - Checked automatically: the web app settings pages check.
- **Default access for new members**
  - Steps:
    1. In the web app, open Settings, then Connections.
    2. Press a Sonarr card.
  - Expect:
    - Default access for new members, set to Needs approval.
    - Its help says what someone you onboard gets from this media manager.
  - Checked automatically: the web app settings pages check.
- **Followers**
  - Steps:
    1. In the web app, open Requests, in each view.
    2. Press a title's follower pictures.
  - Expect:
    - The requester small, then at most five follower pictures and "+37 following" (a "+40" badge on Posters).
    - Rows stay one line however many follow a title.
    - The request's page opens at Followers, listing everyone with their @handle and when they followed.
  - Checked automatically: the web app request views check.
- **Importing from Requestrr**
  - Steps:
    1. Open Settings, then Import from Requestrr.
    2. If the old channels are in another server, use Invite the bot to read another server.
    3. Paste the old channel IDs, then press Preview.
    4. Press Confirm and import.
    5. Open Requests.
  - Expect:
    - Each channel named, with whether the bot can read it.
    - One list of past requests: requester, followers, how it ended, In library.
    - A line counting searches that weren't requests.
    - Requests show their original dates; missing titles say Not in library.
  - Checked by the owner, couldn't verify: the automated checks never read a real server's history.
- **Imported people**
  - Steps:
    1. After an import, open Members.
    2. Open an imported person's page.
    3. Run the import again.
  - Expect:
    - People in your server who aren't members say Imported, not onboarded.
    - Old-server people say Imported, not in this server, with no role or channel made.
    - Their page says where their history came from.
    - Running it again finds nothing new.
  - Checked by the owner, couldn't verify: the automated checks never read a real server's history.
- **Reading another server**
  - Steps:
    1. Open the invite from Invite the bot to read another server.
    2. After the import, look at that server's channels.
  - Expect:
    - The invite asks only to view channels and read their history.
    - The bot created and posted nothing there.
  - Checked by the owner, couldn't verify: the automated checks never touch a second real server.
- **Titles Requestarr didn't add**
  - Steps:
    1. In your request channel, request a movie already in the test Radarr, not requested through Requestarr.
  - Expect:
    - It says the movie is already in the library, ready for you.
    - No second copy in Radarr.
  - Checked automatically: the live shared library check.
- **Deleting turned off**
  - Steps:
    1. With deleting files turned off, start a removal of a title.
    2. Let the vote pass with no keeps.
    3. Open the admin queue.
    4. Delete the file in Radarr yourself.
  - Expect:
    - The removal card says deleting is turned off; everyone asked is told it's deleted by hand.
    - The title still in Radarr with its file.
    - A Delete it yourself item naming the title and the Radarr it's in.
    - Once it's deleted, the removal closes.
  - Checked automatically: the live deleting turned off check.
- **Acting on one request**
  - Steps:
    1. In Requests, press Select Requests, then tick one Not in library row.
    2. Press Start removal in the toolbar, then start it.
  - Expect:
    - No buttons on rows or posters.
    - Left of Refresh: Approve, Deny, Start removal, plus what its page offers.
    - Those it doesn't allow are greyed out.
    - A removal vote starts, as for a title in the library.
  - Checked automatically: the web app request actions check.
- **Picking requests**
  - Steps:
    1. In Requests, press Select Requests, then tick three rows.
    2. Press Select All, then Unselect All.
    3. Press Stop Selecting.
  - Expect:
    - A box on each row, as in Sonarr; nothing below the list.
    - An action is live when any of the three allows it.
    - Select All picks every request the filter shows.
    - The boxes and actions go away after Stop Selecting.
  - Checked automatically: the web app request actions check.
- **Removing a title from search**
  - Steps:
    1. In the header search, type a title in Radarr nobody requested.
    2. Press Start removal on it.
  - Expect:
    - It's listed under In your library, not requested, with its poster.
    - Start removal opens its dialog for that title.
  - Checked automatically: the web app request actions check.
- **Long lists**
  - Steps:
    1. In Requests, scroll to the end of the list.
    2. Choose Movies in Filter.
    3. Press Select Requests, then Select All, then scroll to the end.
  - Expect:
    - One scroll with no pager, as Sonarr's series list.
    - The footer's totals count only movies; its colours match the labels.
    - Every movie ticked, at the end of the list too.
  - Checked automatically: the web app paging check.
- **Member page sections**
  - Steps:
    1. Open the page of a member with many requests.
    2. Make the window narrow, then phone-sized.
  - Expect:
    - Each heading says how many, such as Their requests (101).
    - Each section has Sonarr's pager across its full width, never squeezed.
    - No Requester column in their own requests.
  - Checked automatically: the web app paging check.
- **Onboard list**
  - Steps:
    1. In Members, press Onboard.
    2. Type part of a name in the box at the top.
    3. Tick someone, then press Set up.
  - Expect:
    - Everyone in the server who isn't a member, with joined date and roles.
    - The list narrows as you type.
    - A summary of what they'll get, then Confirm.
  - Checked automatically: the web app add member check.
- **Onboarding from the list**
  - Steps:
    1. In Onboard, tick someone, press Set up, then Confirm.
  - Expect:
    - Each person's progress, then who was added and who failed and why.
    - Their role, channel and panel in Discord.
  - Checked automatically: the web app add member check.
- **Picking members**
  - Steps:
    1. In Members, press Select Members, then tick a member.
    2. Press Suspend in the toolbar, then Unsuspend.
  - Expect:
    - Left of Onboard: Suspend, Unsuspend, Change access, Remove member.
    - Those no pick allows are greyed out.
    - The status changes each time.
  - Checked automatically: the web app add member check.
- **Onboarding from Members**
  - Steps:
    1. In Members, press Select Members, then tick people imported but not onboarded.
    2. Press Onboard, then Cancel.
  - Expect:
    - One window over the list, naming only those who can be onboarded.
    - What they get, then Confirm; no other pages.
  - Checked automatically: the web app add member check.
- **Acting from the web app**
  - Steps:
    1. As an admin in the web app, deny a pending request with a reason.
    2. Approve another request.
    3. Suspend a member, then unsuspend them.
    4. Start a removal, then drop it.
    5. Approve someone asking to join.
  - Expect:
    - Each one done as it is from Discord, its answer in the sidebar's messages.
    - The member's request messages and their channel's notices change the same way.
    - The admin channel's cards change too, naming you with (web app).
  - Checked automatically: the live portal actions check.
- **Header search**
  - Steps:
    1. On any page of the web app, press /.
    2. Type part of a request's title, then part of a member's name.
    3. Do it again in the dark theme.
  - Expect:
    - The search placed and styled like Sonarr's.
    - The request under Requests, and the member under Members.
    - Each one opens its page.
  - Checked automatically: the web app header search check.
- **Renamed members**
  - Steps:
    1. Change a test member's server display name in Discord.
    2. Open their page in the web app.
    3. Search for their old name, then their Discord ID.
    4. Change their Discord username.
  - Expect:
    - Their new name on their page and their request channel's description.
    - A Renamed line in History, and their Discord ID with a copy button.
    - The header search finds them by their old name and their Discord ID.
    - A new username renames their request channel and role to match.
  - Checked automatically: the live member renames check.
- **Project links**
  - Steps:
    1. In the web app, open System, then Status.
    2. Look at More Info.
  - Expect:
    - The home page and issue links open the Requestarr project in the Requestarr organization on GitHub.
  - Checked automatically: the web app project links check.
- **Access requests paused**
  - Steps:
    1. With a test account that isn't a member, open the welcome channel.
    2. Press Request access about fifteen times, until the channel disappears.
    3. Press Unblock on the admin channel's notice.
  - Expect:
    - The blocked channel's message is titled Access requests paused.
    - It says an admin can lift the pause.
    - The admin channel has a notice with Unblock.
    - Unblock brings the welcome channel back for that account.
  - Checked automatically: the live access requests check.
- **The QA checklist**
  - Steps:
    1. Open the QA checklist channel in the Requestarr QA group.
    2. Look at it as a member who isn't an admin.
  - Expect:
    - A summary card at the top, then a card for each thing to test.
    - Each card has its steps as a numbered list and what to expect as bullets.
    - Passes and Failing buttons on each card.
    - A member who isn't an admin doesn't see the group.
  - Checked automatically: the live checklist channel check.
- **Passing an item**
  - Steps:
    1. Press Passes on a card.
    2. Press it again.
  - Expect:
    - The card says who passed it and when.
    - The summary count goes up by one.
    - No "Something went wrong" message.
  - Checked automatically: the live passing check.
- **Failing an item**
  - Steps:
    1. Press Failing on a card.
    2. Pick the part of Requestarr and describe what went wrong.
    3. Do the same on two more cards, one after another.
  - Expect:
    - A form opens every time, never "You've sent a report just now".
    - Each card marked failing, with a link to its own bug report.
    - The summary says the checklist can't complete while one fails.
  - Checked automatically: the live failing check.
- **Passes carry over**
  - Steps:
    1. After the next promotion to QA, open the QA checklist channel.
  - Expect:
    - Every item you passed before still passed, with your name and when.
    - An item whose words changed starts again.
    - The summary says how many carried over and how many are new or changed.
  - Checked automatically: the live carry-over check.
- **Report a problem**
  - Steps:
    1. Open the bug reports channel in the Requestarr Users group.
    2. Press Report a problem and pick a part.
    3. Read the note that reports are public.
    4. Send a short test report.
  - Expect:
    - Your report's card, with the reports in the order they were sent.
    - The Report a problem message below them.
    - No reply pointing at a deleted message.
    - Once it's filed, a link to its issue in the Requestarr organization on GitHub.
  - Checked automatically: the live bug reports check.
- **Report limit**
  - Steps:
    1. As a member who isn't an admin, send a report.
    2. Press Report a problem again straight away.
  - Expect:
    - Requestarr asks you to wait.
    - No second card posted.
  - Checked automatically: the live report limit check.
- **Why a report isn't on GitHub**
  - Steps:
    1. Look at the card of a report you sent.
  - Expect:
    - Either the link to its GitHub issue, or the exact reason it isn't there yet.
    - For example, the server having no GitHub access key.
  - Checked automatically: the live bug reports check.
- **Only your items on QA**
  - Steps:
    1. Open the QA checklist channel on the QA server.
    2. Read a few cards.
  - Expect:
    - Only the items that need you, each saying why.
    - Steps as a numbered list and Expect as bullets, on their own lines.
    - The summary counting only those items.
    - Nothing from the automated checks.
  - Checked by the owner, needs human eyes: how the checklist cards read in the Discord app.
