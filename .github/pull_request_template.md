<!-- Pull requests go into `dev`, a fix for something found in QA or staging included; `qa`, `staging` and `main` only take promotions. See docs/development.md#release-flow. -->
## What changed


## Changelog
- [ ] `changelog/entries/`: one entry file per real change (no build: the promotion to staging fills it in), then `python3 .github/changelog_entries.py write` for `CHANGELOG.md`, never edited by hand. Following the changelog rule:
  - `audience:` users or admins, shown under **For users** and **For admins** (leave out work neither sees: tests, build, refactoring); `kind:` new, fixed or changed
  - `line:` leads with the thing that changed in bold, then what it means for the reader, in plain words
  - no code, file names, commands, flags, settings keys or jargon; no "minor fixes" without saying what they fix; no placeholders
  - short: one to three sentences, at most 50 words after the bold lead; as many entries as there are real changes, with no cap
  - `replaces:` the entries this reworks, undoes or replaces; `line_if_seen:` what changed, for readers who already had a released one it replaces. An entry not released yet is edited to where it ended up instead; a released one never changes
- [ ] or, when nothing changes for users or admins, no entries and this line in the description: `No changelog: <why>`
- [ ] `QA-CHECKLIST.md`: what an admin can test by hand, under `## Unreleased` (nothing when nobody can)

`python3 .github/changelog_check.py CHANGELOG.md` runs the same check as CI. See `docs/development.md#changelog`.
