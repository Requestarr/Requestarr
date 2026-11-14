#!/usr/bin/env python3
"""Checks QA-CHECKLIST.md against CHANGELOG.md (docs/development.md#qa-checklist). CI's Changelog job runs it on every
pull request; run it locally with `python3 .github/checklist_check.py QA-CHECKLIST.md CHANGELOG.md`.

The QA checklist is what admins on the QA server test, in Discord, before a build goes to staging. Pull requests add
their items under "## Unreleased", the way they add changelog lines; the staging promotion numbers both. Checked:
  - versions: every numbered changelog version from 0.4.3 on has a "## 0.7.0004 (YYYY-MM-DD)" section here (up to
    0.6.0 "x.y.z"), with the same date, in the same order, and there are no others; "## Unreleased" only on top;
  - a version with nothing anyone can test by hand has only its heading (the same escape as a heading-only changelog
    entry); otherwise only "- " item lines;
  - each item: "- **Name:** what to do. You should see what happens.", with a name unique in its version and the
    words "You should see" saying what a pass looks like;
  - who checks it, on the line under it (every Unreleased item; numbered versions without one are the owner's):
    "  - Checked by Claude Code: the <check> check." naming one of Claude Code's checks (docker/qa_checks.py), or
    "  - Checked by the owner, <reason>: <why>." with the reason a person has to: needs human eyes, unsure of the
    desired behavior, or couldn't verify;
  - plain words, as in the changelog: no backticks, file names, paths, command flags, settings keys or environment
    variable names, since admins read these in Discord.
"""
import os
import re
import sys

sys.path.insert(0, os.path.dirname(os.path.abspath(__file__)))
sys.path.insert(0, os.path.join(os.path.dirname(os.path.dirname(os.path.abspath(__file__))), "docker"))
from changelog_check import CODE  # noqa: E402
from build_numbers import text as shown  # noqa: E402
from qa_checks import CHECKS, CHECKED_BY  # noqa: E402

FIRST = (0, 4, 3)
VERSION = re.compile(r"^## (\d+)\.(\d+)\.(\d+) \((\d{4}-\d{2}-\d{2})\)$")
UNRELEASED = "## Unreleased"
ITEM = re.compile(r"^- \*\*([^*:]+):\*\* (.+)$")
SUB_LINE = "  - "


def versions(text):
    found = []
    for number, line in enumerate(text.splitlines(), 1):
        match = VERSION.match(line.strip())
        if match:
            found.append((tuple(int(match.group(i)) for i in (1, 2, 3)), match.group(4), number))
    return found


def check(checklist, changelog):
    problems = []
    wanted = [(v, d) for v, d, _ in versions(changelog) if v >= FIRST]
    have = versions(checklist)
    if [(v, d) for v, d, _ in have] != wanted:
        missing = [v for v, _ in wanted if v not in [h[0] for h in have]]
        extra = [v for v, _, _ in have if v not in [w[0] for w in wanted]]
        problems.append((1, "versions don't match the changelog from 0.4.3 on (same versions, dates and order): missing %s, extra %s"
                         % (", ".join(map(shown, missing)) or "none", ", ".join(map(shown, extra)) or "none")))

    current = None
    last = None
    names = set()
    # A result is recorded against an item's name and words, so the same item word for word in two versions would be
    # one result for two cards.
    wordings = set()
    for number, line in enumerate(checklist.splitlines(), 1):
        text = line.strip()
        if text == UNRELEASED and current is not None:
            problems.append((number, "'## Unreleased' goes once, on top"))
        if VERSION.match(text) or text == UNRELEASED:
            problems += unchecked(last)
            current, last = text, None
            names = set()
            continue
        if current is None or not text:
            continue
        if line.startswith(SUB_LINE):
            problems += checker(number, line, last)
            if last:
                last["checked"] = True
            continue
        if text.startswith("#"):
            problems.append((number, "only version headings: '%s'" % text))
            continue
        item = ITEM.match(text)
        if not item:
            problems.append((number, "only '- **Name:** what to do. You should see ...' lines inside a version"))
            continue
        name, body = item.group(1).strip(), item.group(2)
        problems += unchecked(last)
        last = {"name": name, "line": number, "checked": False, "unreleased": current == UNRELEASED}
        if name.lower() in names:
            problems.append((number, "item '%s' appears twice in this version" % name))
        names.add(name.lower())
        wording = " ".join(text.split())
        if wording in wordings:
            problems.append((number, "item '%s' is already on the checklist word for word: reword it or drop the copy" % name))
        wordings.add(wording)
        if "you should see" not in body.lower():
            problems.append((number, "item '%s' must say 'You should see' what a pass looks like" % name))
        for pattern, what in CODE:
            if pattern.search(text):
                problems.append((number, "plain words only: '%s' has %s" % (name, what)))
    return problems + unchecked(last)


def unchecked(item):
    """An Unreleased item says who checks it (a numbered version's item without one is the owner's)."""
    if item and item["unreleased"] and not item["checked"]:
        return [(item["line"], "item '%s' must say who checks it on the line under it: '  - Checked by Claude Code: the <check> check.' "
                               "or '  - Checked by the owner, <needs human eyes | unsure of the desired behavior | couldn't verify>: <why>.'" % item["name"])]
    return []


def checker(number, line, item):
    match = CHECKED_BY.match(line.strip())
    if item is None:
        return [(number, "a 'Checked by' line goes right under its item")]
    if item["checked"]:
        return [(number, "item '%s' says who checks it twice" % item["name"])]
    if not match:
        return [(number, "under an item only '  - Checked by Claude Code: the <check> check.' or '  - Checked by the owner, "
                         "<needs human eyes | unsure of the desired behavior | couldn't verify>: <why>.'")]
    # Numbered versions are never edited, so a check retired since stays named there.
    if item["unreleased"] and match.group("check") and match.group("check") not in CHECKS:
        return [(number, "item '%s' names no check Claude Code has: '%s' (the checks are in docker/qa_checks.py)" % (item["name"], match.group("check")))]
    return [(number, "plain words only: who checks '%s' has %s" % (item["name"], what)) for pattern, what in CODE if pattern.search(line)]


BAD_CHANGELOG = """# Changelog

## 0.4.4 (2026-10-25)

## 0.4.3 (2026-10-25)
"""

BAD = """# QA checklist

## 0.4.4 (2026-10-25)
## Unreleased
- **Logo:** look at the logo.
- **Logo:** open `provisioning.yml`. You should see it.
  - Checked by Claude Code: the made-up check.
  - Checked by the owner, needs human eyes: twice.
- **Popout:** look at the bot's profile. You should see the picture.
  - Checked by the owner, because I said so.
  - Checked by Claude Code: the web app status page check.
## 0.4.2 (2026-10-24)
- **Logo:** open `provisioning.yml`. You should see it.
Some loose text.
### Section
"""

GOOD = """# QA checklist

Intro text is free.

## Unreleased
- **Build number:** open System, then Status. You should see the build's number with dev and its commit.
  - Checked by Claude Code: the web app status page check.
- **Bot picture:** open the bot's profile in Discord. You should see the plain picture.
  - Checked by the owner, needs human eyes: the profile popout is only in the Discord app.

## 0.4.4 (2026-10-25)
- **Logo:** open the web app and look at the header. You should see the Dev logo.

## 0.4.3 (2026-10-25)
"""


def self_test():
    found = check(BAD, BAD_CHANGELOG)
    expected = ["versions don't match", "must say 'You should see'", "appears twice", "backticks", "a file name",
                "only '- **Name:**", "only version headings", "goes once, on top", "word for word",
                "must say who checks it", "names no check Claude Code has", "says who checks it twice", "under an item only"]
    missing = [e for e in expected if not any(e in p for _, p in found)]
    good = check(GOOD, BAD_CHANGELOG)
    for _, p in found:
        print("  caught: " + p)
    if missing or good:
        print("SELF-TEST FAILED: missed %s; good sample problems %s" % (missing, good))
        return 1
    print("Self-test passed: every rule caught its sample, and the good sample passed.")
    return 0


def main(argv):
    if argv[1:] == ["--self-test"]:
        return self_test()
    if len(argv) != 3:
        print(__doc__)
        return 2
    with open(argv[1], encoding="utf-8") as handle:
        checklist = handle.read()
    with open(argv[2], encoding="utf-8") as handle:
        changelog = handle.read()
    problems = check(checklist, changelog)
    for number, problem in problems:
        if os.environ.get("GITHUB_ACTIONS"):
            print("::error file=%s,line=%d::%s" % (argv[1], number, problem))
        else:
            print("%s:%d: %s" % (argv[1], number, problem))
    if problems:
        print("%d problem(s): see the QA checklist rule in docs/development.md#qa-checklist." % len(problems))
        return 1
    print("%s matches the changelog and follows the QA checklist rule." % argv[1])
    return 0


if __name__ == "__main__":
    sys.exit(main(sys.argv))
