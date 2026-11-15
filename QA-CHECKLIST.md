# QA checklist

What admins on the QA server test, in Discord, before a build goes to staging. Requestarr posts these items in the QA checklist channel for the build QA runs and every version staging doesn't have yet; admins press Passes or Failing on each. When every item has passed and no bug report from QA is still open, the build can go to staging.

Every pull request adds its items under Unreleased, the way it adds its changelog lines: each one says what to do and what you should see, in plain words. The line under each item says who checks it: Claude Code, with the check that verifies it (run on dev before a build is promoted to QA, with its evidence in the promotion; these items never appear on QA), or the owner, with the reason a person has to (it needs human eyes, the behavior wanted isn't settled, or Claude Code couldn't verify it). Unreleased is what to test since the last build the beta testers got: a change to something an Unreleased item already tests rewrites that item instead of adding another. The promotion to staging gives them the build's number. The items for 0.4.3 to 0.6.0, which never reached the testers, are folded into it. See docs/development.md#qa-checklist.

## Unreleased
- **Only your items on QA:** Open the QA checklist channel. You should see only the items that need you, each card saying why, the summary counting only those, your earlier passes kept, and nothing of Claude Code's.
  - Checked by the owner, needs human eyes: how the checklist reads in the Discord app.
- **Update cards:** In the admin channel and in the announcements channel, look at the update cards. You should see one card in each, titled Requestarr, the build number with qa and a short code, then since the last beta, with the main change underneath and how many more, and no cards for the versions 0.4.0 to 0.6.0.
  - Checked by Claude Code: the live update cards check.
- **Read changes:** Press Read changes on the update card. You should see the full list of what changed since the last beta, only to you.
  - Checked by Claude Code: the live Read changes check.
- **Build number in the web app:** Open System, then Status. You should see the same number, qa and short code as the version, and the same number on the logo.
  - Checked by Claude Code: the web app status page check.
- **Web app logo:** Look at the web app's header in the light theme, then in the dark theme, and at the icon in the browser tab. You should see a full-height R beside the yellow QA bar, straight on the header with no box around it, the arrow in a soft charcoal, and the logo and the tab icon on charcoal gray in the dark theme, never on black.
  - Checked by Claude Code: the web app header check.
- **Signing in and out:** Open the web app and sign in with the QA login, then open the account menu at the top right and press Logout, then open the web app's address again. You should see the login page with the QA logo every time you're signed out, never a browser error, and signing in again bring you back to where you were going.
  - Checked by Claude Code: the web app sign-in and sign-out check.
- **General settings:** Sign in, open Settings, then General, change a setting the provisioning file doesn't set, such as Backup Retention, and press Save. You should see it save with no error, the fields the file sets, such as Allowed Hosts, Authentication and Username, read-only with "Set by your provisioning file", and the port with a note about Docker.
  - Checked by Claude Code: the web app general settings check.
- **Sign-in logo:** Sign out of the web app and look at the sign-in page in the light theme, then in the dark theme. You should see the logo with a light arrow, easy to read on the gray box, in both.
  - Checked by Claude Code: the web app sign-in page check.
- **Loading screen:** Reload the web app and watch it while it starts. You should see the QA logo, the same one as the header's, with no purple square.
  - Checked by Claude Code: the web app loading screen check.
- **Bot picture and banner:** In the Requestarr QA server, click the bot's name to open its profile. You should see the plain Requestarr picture and the yellow QA banner.
  - Checked by the owner, needs human eyes: the bot's profile only shows in the Discord app, and how the picture and banner look there is a judgement.
- **Project links:** In the web app, open System, then Status, and look at More Info. You should see the home page and issue links open the Requestarr project in the Requestarr organization on GitHub.
  - Checked by Claude Code: the web app project links check.
- **Access requests paused:** With a test account that isn't a member, press Request access in the welcome channel again and again, about fifteen times, until the welcome channel disappears for that account. You should see the blocked channel's message titled Access requests paused, saying an admin can lift the pause, and in the admin channel a notice with Unblock; pressing Unblock brings the welcome channel back for that account.
  - Checked by Claude Code: the live access requests check.
- **The QA checklist:** Open the QA checklist channel in the Requestarr QA group. You should see a summary card at the top, then a card for each thing to test with Passes and Failing buttons, and a member who isn't an admin shouldn't see that group.
  - Checked by Claude Code: the live checklist channel check.
- **Passing an item:** Press Passes on a card, then press it again. You should see the card say who passed it and when, the summary count go up by one, and no "Something went wrong" message.
  - Checked by Claude Code: the live passing check.
- **Failing an item:** Press Failing on three cards one after another, and each time pick the part of Requestarr and describe what went wrong. You should see a form open every time, each card marked failing with a link to its own bug report, never "You've sent a report just now", and the summary say the checklist can't complete while one fails.
  - Checked by Claude Code: the live failing check.
- **Passes carry over:** After the next promotion to QA, look at the QA checklist channel. You should see every item you passed before still passed, with your name and when, and the summary saying how many carried over and how many are new or changed.
  - Checked by Claude Code: the live carry-over check.
- **Report a problem:** In the bug reports channel in the Requestarr Users group, press Report a problem, pick a part, read the note that reports are public, and send a short test report. You should see your report's card, in the order the reports were sent with the Report a problem message below them and no reply pointing at a deleted message, linking to its issue in the Requestarr organization on GitHub once it's filed.
  - Checked by Claude Code: the live bug reports check.
- **Report limit:** As a member who isn't an admin, send a report, then press Report a problem again straight away. You should see Requestarr ask you to wait, and no second card posted.
  - Checked by Claude Code: the live report limit check.
- **Why a report isn't on GitHub:** Look at the card of a report you sent. You should see either the link to its GitHub issue, or the exact reason it isn't there yet, such as the server having no GitHub access key.
  - Checked by Claude Code: the live bug reports check.
