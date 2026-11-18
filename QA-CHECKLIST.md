# QA checklist

What admins on the QA server test, in Discord, before a build goes to staging. Requestarr posts these items in the QA checklist channel for the build QA runs and every version staging doesn't have yet; admins press Passes or Failing on each. When every item has passed and no bug report from QA is still open, the build can go to staging.

Every pull request adds its items under Unreleased, the way it adds its changelog lines: each one says what to do and what you should see, in plain words. The line under each item says who checks it: the automated checks, naming the check that verifies it (run on every promotion to QA, with its evidence on the item's card), or the owner, with the reason a person has to (it needs human eyes, the behavior wanted isn't settled, or the automated checks couldn't verify it). Unreleased is what to test since the last build the beta testers got: a change to something an Unreleased item already tests rewrites that item instead of adding another. The promotion to staging gives them the build's number. The items for 0.4.3 to 0.6.0, which never reached the testers, are folded into it. See docs/development.md#qa-checklist.

## Unreleased
- **Update cards:** In the admin channel and in the announcements channel, look at the update cards. You should see one card in each, titled Requestarr, the build number with qa and a short code, then since the last beta, with the main change underneath and how many more, and no cards for the versions 0.4.0 to 0.6.0.
  - Checked automatically: the live update cards check.
- **Read changes:** Press Read changes on the update card. You should see the full list of what changed since the last beta, only to you.
  - Checked automatically: the live Read changes check.
- **Build number in the web app:** Open System, then Status. You should see the same number, qa and short code as the version, the same number on the logo, and under About the copyright of Requestarr's contributors with the Servarr team's notice.
  - Checked automatically: the web app status page check.
- **Web app logo:** Look at the web app's header in the light theme, then in the dark theme, and at the icon in the browser tab. You should see a full-height R beside the yellow QA bar, straight on the header with no box around it, the arrow in a soft charcoal, and the logo and the tab icon on charcoal gray in the dark theme, never on black.
  - Checked automatically: the web app header check.
- **Signing in and out:** Open the web app and sign in with the QA login, then open the account menu at the top right and press Logout, then open the web app's address again. You should see the login page with the QA logo every time you're signed out, never a browser error, and signing in again bring you back to where you were going.
  - Checked automatically: the web app sign-in and sign-out check.
- **General settings:** Sign in, open Settings, then General, change a setting the provisioning file doesn't set, such as Backup Retention, and press Save. You should see it save with no error, the fields the file sets, such as Allowed Hosts, Authentication and Username, read-only and greyed out in the light and the dark theme with "Set by your provisioning file", and the port with a note about Docker.
  - Checked automatically: the web app general settings check.
- **Sign-in logo:** Sign out of the web app and look at the sign-in page in the light theme, then in the dark theme. You should see the logo with a light arrow, easy to read on the gray box, in both.
  - Checked automatically: the web app sign-in page check.
- **Loading screen:** Reload the web app and watch it while it starts. You should see the QA logo, the same one as the header's, with no purple square.
  - Checked automatically: the web app loading screen check.
- **Bot picture and banner:** In the Requestarr QA server, click the bot's name to open its profile. You should see the plain Requestarr picture and the yellow QA banner.
  - Checked by the owner, needs human eyes: the bot's profile only shows in the Discord app, and how the picture and banner look there is a judgement.
- **The admin portal's pages:** In the web app, open Requests, Members, Queue, Access requests, Outages and Audit log from the menu on the left, and System, then What's new, in the light and then the dark theme, and open a request and a member from the lists. You should see every request with its requester, followers, status and instance, with filters; a request's page with its timeline in plain words and its actions; each member's access and limits with their counts, and a member's page with their requests, the titles they follow, removals, history and actions; the same waiting items as the admin channel's queue; who is waiting to join or paused; what can't be reached and past outages; the audit log with its filters; and each version's changes.
  - Checked automatically: the web app portal pages check.
- **Requestarr's settings pages:** In the web app, open Settings, then Discord, Sonarr and Radarr, Requests, Connections and Changes, in the light and then the dark theme. You should see every setting the provisioning file sets, read-only with a note that this server takes them from its provisioning file, the bot's status, both test instances listed, and Changes listing who changed a setting, when, and from what to what.
  - Checked automatically: the web app settings pages check.
- **Followers:** On a request's page in the web app, look at Followers. You should see every member who asked for the title after its requester.
  - Checked automatically: the web app portal pages check.
- **Acting from the web app:** As an admin in the web app, deny a pending request with a reason, approve another, suspend and then unsuspend a member, start and then drop a removal, and approve someone asking to join. You should see each done as it is from Discord: the member's request messages, their channel's notices and the admin channel's cards change the same way, naming you with (web app).
  - Checked automatically: the live portal actions check.
- **Header search:** On any page of the web app, press / and type part of a request's title, then part of a member's name, in the light and the dark theme. You should see the search placed and styled like Sonarr's, the request and the member found under Requests and Members, and each opening its page.
  - Checked automatically: the web app header search check.
- **Renamed members:** Change a test member's server display name in Discord, then open their page in the web app. You should see their new name on their page and their request channel's description, a Renamed line in their History saying from what to what, and their Discord ID with a button to copy it; the header search finds them by their old name and by their Discord ID, and a new Discord username renames their request channel and role to match.
  - Checked automatically: the live member renames check.
- **Project links:** In the web app, open System, then Status, and look at More Info. You should see the home page and issue links open the Requestarr project in the Requestarr organization on GitHub.
  - Checked automatically: the web app project links check.
- **Access requests paused:** With a test account that isn't a member, press Request access in the welcome channel again and again, about fifteen times, until the welcome channel disappears for that account. You should see the blocked channel's message titled Access requests paused, saying an admin can lift the pause, and in the admin channel a notice with Unblock; pressing Unblock brings the welcome channel back for that account.
  - Checked automatically: the live access requests check.
- **The QA checklist:** Open the QA checklist channel in the Requestarr QA group. You should see a summary card at the top, then a card for each thing to test with Passes and Failing buttons, and a member who isn't an admin shouldn't see that group.
  - Checked automatically: the live checklist channel check.
- **Passing an item:** Press Passes on a card, then press it again. You should see the card say who passed it and when, the summary count go up by one, and no "Something went wrong" message.
  - Checked automatically: the live passing check.
- **Failing an item:** Press Failing on three cards one after another, and each time pick the part of Requestarr and describe what went wrong. You should see a form open every time, each card marked failing with a link to its own bug report, never "You've sent a report just now", and the summary say the checklist can't complete while one fails.
  - Checked automatically: the live failing check.
- **Passes carry over:** After the next promotion to QA, look at the QA checklist channel. You should see every item you passed before still passed, with your name and when, and the summary saying how many carried over and how many are new or changed.
  - Checked automatically: the live carry-over check.
- **Report a problem:** In the bug reports channel in the Requestarr Users group, press Report a problem, pick a part, read the note that reports are public, and send a short test report. You should see your report's card, in the order the reports were sent with the Report a problem message below them and no reply pointing at a deleted message, linking to its issue in the Requestarr organization on GitHub once it's filed.
  - Checked automatically: the live bug reports check.
- **Report limit:** As a member who isn't an admin, send a report, then press Report a problem again straight away. You should see Requestarr ask you to wait, and no second card posted.
  - Checked automatically: the live report limit check.
- **Why a report isn't on GitHub:** Look at the card of a report you sent. You should see either the link to its GitHub issue, or the exact reason it isn't there yet, such as the server having no GitHub access key.
  - Checked automatically: the live bug reports check.
- **Only your items on QA:** Open the QA checklist channel. You should see only the items that need you, each card saying why, the summary counting only those, your earlier passes kept, and nothing from the automated checks.
  - Checked by the owner, needs human eyes: how the checklist reads in the Discord app.
