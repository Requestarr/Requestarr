#!/usr/bin/env python3
"""Checks CHANGELOG.md against the changelog rule (docs/development.md#changelog). CI's Changelog job runs it on every
pull request; run it locally with `python3 .github/changelog_check.py CHANGELOG.md`.

"## Unreleased" (the lines pull requests add until the next build sent to testers, always on top) and every
"## x.y.zzzz (YYYY-MM-DD)" build (a staging build's may add ", with 0.7.0005 to 0.7.0011"; the text above the first
heading is free) is checked for:
  - sections: only "### For users" and "### For admins", each at most once; no "Internal" or any other section;
  - no empty section: a heading with no "- " line under it;
  - only headings and "- " lines: no loose text inside a version;
  - plain words: no code-style text in a line, meaning backticks, file names (provisioning.yml), paths (docker/x),
    command flags (-Stack, --ref), settings keys (removal.voteDays) or environment variable names (DEV_DISCORD_*);
  - no placeholders: "nothing this time", "n/a", "TBD" and the like;
  - no summary without substance: a line with "minor fixes", "various improvements" or another summary phrase must say,
    on the same line, what that changes (at least five more words);
  - short: each line one to three sentences, at most 50 words after its bold lead, and 3,500 characters of lines in a
    build, so Read changes is one message (the security line the staging promotion may add isn't counted); as many
    lines as there are real changes, with no cap on how many;
    (owner, 2026-11-23: "every note should be like 1-3 sentences not a bible"); Unreleased says where a thing ended
    up, so a change rewrites its line to the final state instead of adding to it;
  - versions: "## 0.7.0004 (YYYY-MM-DD)", the day the build was made, each once, newest first; versions up to 0.6.0
    keep their "x.y.z" numbers (docs/development.md#versions); Unreleased only on top, never empty.

`--self-test` runs every rule against built-in samples and fails if one doesn't catch its sample.
"""
import datetime
import os
import re
import sys

sys.path.insert(0, os.path.dirname(os.path.abspath(__file__)))
from build_numbers import SECURITY_LINE, text as shown  # noqa: E402

SECTIONS = ("For users", "For admins")
UNRELEASED = "Unreleased"

EXTENSIONS = "cs|csproj|props|ps1|psm1|cmd|bat|sh|py|ya?ml|json|md|toml|xml|env|txt|ts|tsx|js|css|dll|exe|sln"
CODE = [
    (re.compile(r"`"), "code formatting (backticks)"),
    (re.compile(r"\b[\w.-]+\.(?:%s)\b" % EXTENSIONS, re.IGNORECASE), "a file name"),
    (re.compile(r"(?:^|\s)(?:\.{1,2}/|~/|[\w.-]+/[\w.-]+/)|\b(?:docker|src|docs|deploy|\.github)/|\w\\\w"), "a file path"),
    (re.compile(r"(?:^|[\s(])--?[A-Za-z][\w-]*"), "a command flag"),
    (re.compile(r"\b[a-z]+\.[a-z]+[A-Z]\w*\b"), "a settings key"),
    (re.compile(r"\b[A-Z][A-Z0-9]*_[A-Z0-9_]+\b"), "an environment variable name"),
]

PLACEHOLDER_PHRASES = ["nothing this time", "nothing to report", "nothing new", "no changes", "no user-facing changes",
                       "nothing for users", "nothing for admins", "tbd", "todo", "placeholder", "lorem ipsum"]
PLACEHOLDER_LINES = {"none", "n/a", "na", "-", "nothing", "...", "etc", "see above", "same as above"}

SUMMARY_PHRASES = ["minor fixes", "minor improvements", "minor changes", "minor updates", "various improvements",
                   "various fixes", "various changes", "various updates", "bug fixes", "small fixes", "small improvements",
                   "general improvements", "performance improvements", "stability improvements", "improvements",
                   "fixes", "misc", "miscellaneous", "tweaks", "cleanup", "clean-up", "updates", "changes"]
FILLER = ["and", "&", "other", "some", "a few", "several", "plus", "more", "lots of", "many"]

# How long a line may be (docs/development.md#changelog).
MAX_WORDS = 50
MAX_SENTENCES = 3
# Discord shows at most 4,000 characters in one message's cards; this leaves room for the headings, the compose file
# link and the security line (VersionCards.MessageTextLength).
MAX_CHARACTERS = 3500

LEAD = re.compile(r"^\*\*[^*]+\*\*")
# A sentence ends at . ! or ? (and any closing quote or bracket) before a space and a capital, a number or a command;
# the last one ends the line. Version numbers like 0.7.0010, "Settings, then Changes" and e.g. don't end one.
SENTENCE_END = re.compile(r"(?<!\be\.g)(?<!\bi\.e)(?<!\betc)(?<!\bvs)[.!?][\"'”’)]*(?=\s+[\"“'(]?[A-Z0-9/])")


def words(text):
    return re.findall(r"[A-Za-z0-9']+", text.lower())


def strip(line):
    return re.sub(r"[*_]", "", line).strip()


def too_long(text):
    """Problems with one line's length: the words after its bold lead, and its sentences."""
    body = LEAD.sub("", text).strip()
    problems = []
    count = sum(1 for token in body.split() if re.search(r"[A-Za-z0-9]", token))
    if count > MAX_WORDS:
        problems.append("%d words after the bold lead: at most %d; say what changed in one to three short sentences" % (count, MAX_WORDS))
    sentences = len(SENTENCE_END.findall(body)) + 1
    if sentences > MAX_SENTENCES:
        problems.append("%d sentences: at most %d; leave details to the setup guide" % (sentences, MAX_SENTENCES))
    return problems


def check_line(text):
    """Problems with one '- ' line's text."""
    problems = too_long(text)
    for pattern, what in CODE:
        match = pattern.search(text)
        if match:
            problems.append("%s (%r): name things the way the reader sees them in Discord" % (what, match.group(0).strip()))

    plain = strip(text).lower().rstrip(".!:")
    if plain in PLACEHOLDER_LINES or any(re.search(r"\b%s\b" % re.escape(p), plain) for p in PLACEHOLDER_PHRASES):
        problems.append("placeholder text: leave the section out instead")

    if any(re.search(r"\b%s\b" % re.escape(p), plain) for p in SUMMARY_PHRASES):
        rest = plain
        for phrase in SUMMARY_PHRASES + FILLER:
            rest = re.sub(r"\b%s\b" % re.escape(phrase), " ", rest)
        if len(words(rest)) < 5:
            problems.append("a summary without substance: say on the same line what it changes for the reader")
    return problems


def check(text):
    """[(line number, problem)] for a whole CHANGELOG.md."""
    problems = []
    version = None
    seen_versions = []
    section = None
    section_line = 0
    section_items = 0
    seen_sections = set()

    unreleased_line = 0
    unreleased_items = 0

    def close_section():
        if section is not None and section_items == 0:
            problems.append((section_line, "empty section '%s': leave the heading out when there's nothing under it" % section))

    def close_unreleased():
        if version is UNRELEASED and unreleased_items == 0:
            problems.append((unreleased_line, "an empty '## Unreleased': leave it out until a pull request adds a line"))

    for number, raw in enumerate(text.splitlines(), 1):
        line = raw.rstrip()
        if line.startswith("## "):
            close_section()
            section = None
            seen_sections = set()
            if line == "## Unreleased":
                if seen_versions or version is not None:
                    problems.append((number, "'## Unreleased' goes once, on top"))
                version, unreleased_line = UNRELEASED, number
                continue
            close_unreleased()
            match = re.fullmatch(r"## ((\d+)\.(\d+)\.(\d+)) \((\d{4})-(\d{2})-(\d{2})\)(?:, with \d+\.\d+\.\d{4,} to \d+\.\d+\.\d{4,})?", line)
            version = tuple(int(part) for part in match.groups()[1:4]) if match else None
            if not match or not real_date(*match.groups()[4:]) or shown(version) != match.group(1):
                problems.append((number, "a version heading is '## 0.7.0004 (YYYY-MM-DD)' (up to 0.6.0 'x.y.z'), the day it was made, and at most the goals' builds it includes (', with 0.7.0005 to 0.7.0011')"))
                version = None
                continue
            if version in seen_versions:
                problems.append((number, "version %s appears twice" % shown(version)))
            elif seen_versions and version > seen_versions[-1]:
                problems.append((number, "versions go newest first: %s is above %s" % (shown(seen_versions[-1]), line[3:])))
            seen_versions.append(version)
        elif version is None:
            continue
        elif line.startswith("### "):
            close_section()
            section = line[4:].strip()
            section_line = number
            section_items = 0
            if section not in SECTIONS:
                problems.append((number, "section '%s': only 'For users' and 'For admins'; work neither sees is left out" % section))
            elif section in seen_sections:
                problems.append((number, "section '%s' appears twice in this version" % section))
            seen_sections.add(section)
        elif line.startswith("- "):
            if section is None:
                problems.append((number, "a line outside 'For users' or 'For admins'"))
            section_items += 1
            unreleased_items += version is UNRELEASED
            problems.extend((number, p) for p in check_line(line[2:]))
        elif line.strip():
            problems.append((number, "only headings and '- ' lines inside a version"))
    close_section()
    close_unreleased()
    return problems + too_big(text)


def too_big(text):
    """Problems with a build's size (Unreleased's too): characters in all its lines. The security line the staging
    promotion may add isn't counted, so adding it never fails the release."""
    builds = []
    for number, raw in enumerate(text.splitlines(), 1):
        line = raw.rstrip()
        if line.startswith("## "):
            builds.append((number, []))
        elif builds and line.startswith("### "):
            builds[-1][1].append((number, line[4:].strip(), []))
        elif builds and builds[-1][1] and line.startswith("- ") and line != SECURITY_LINE:
            builds[-1][1][-1][2].append(line)
    problems = []
    for heading, sections in builds:
        size = sum(len(row) + 1 for _, _, rows in sections for row in rows)
        if size > MAX_CHARACTERS:
            problems.append((heading, "%d characters of lines in this build: at most %d, so Read changes is one message" % (size, MAX_CHARACTERS)))
    return problems


BAD = """# Changelog

## 0.6.3

## 0.6.2 (2026-02-30)

## 0.7.4 (2026-10-03)

## 0.6.0 (2026-10-02)

### For users
- **Requests:** run `/request` to ask.
- Set it in provisioning.yml.
- Edit docker/compose.yaml first.
- Start it with -Stack dev.
- Change removal.voteDays to 3.
- Set DEV_DISCORD_BOT_TOKEN.
- Nothing this time.
- Minor fixes.
- Various improvements and bug fixes.
- **Too long:** this line goes on and on, word after word, about every small detail of a feature that a reader only needed to hear about once, in a sentence or two, before the setup guide took over with the rest of it, the settings, the windows, the buttons, the menus, the colours and every last label on every page too.
- **Too many sentences:** one. Two. Three. Four

### For admins

### Internal
- Refactor.

Loose text.

## 0.6.1 (2026-10-01)

### For users
- **One:** a.
- **Two:** b.
- **Three:** c.
- **Four:** d.
- **Five:** e.
- **Six:** f.
- **Seven:** g.
- **Eight:** h.
- **Nine:** i.

## 0.6.1 (2026-10-01)

## Unreleased
"""

GOOD = """# Changelog

Text before the first version is free.

## Unreleased

### For users
- **Build numbers:** each test build now shows which build it is, the same number everywhere you look.

## 0.7.0005 (2026-10-26), with 0.7.0005 to 0.7.0005

### For users
- **Numbers:** a staging build names the goals' builds it includes.

## 0.7.0004 (2026-10-25)

### For users
- **Minor fixes:** request messages no longer show a Cancel button after a request is approved.
- **Long TV shows:** you can now pick any season, not just the 24 most recent.

### For admins
- **Removals:** Sonarr/Radarr titles nobody requested can be removed too, from /admin requests.
- Fixed: approving a request sometimes left the old Cancel button working, and the queue count updates after a restart.
- **Settings in the web app:** build 0.7.0010 is set up from the web app. Settings, then Changes, records who changed what. Is that clear?
- **A long lead that names the whole feature, its settings page and everything around it:** these few words, e.g. this note, stay well under the limit.
- **Three:** c.
- **Four:** d.
- **Five:** e.
- **Six:** f.

## 0.6.0 (2026-10-21)
"""


def real_date(year, month, day):
    try:
        datetime.date(int(year), int(month), int(day))
        return True
    except ValueError:
        return False


def self_test():
    big = "## 0.5.0 (2025-09-01)\n\n### For admins\n" + "".join("- **Long %d:** %s.\n" % (n, " ".join(["considerably"] * 45)) for n in range(7))
    found = check(BAD + big)
    expected = ["backticks", "a file name", "a file path", "a command flag", "a settings key", "an environment variable name",
                "placeholder", "summary without substance", "empty section", "section 'Internal'", "only headings",
                "appears twice", "newest first", "a version heading", "goes once, on top", "an empty '## Unreleased'",
                "words after the bold lead", "4 sentences", "characters of lines in this build"]
    missing = [e for e in expected if not any(e in p for _, p in found)]
    headings = sum("a version heading" in p for _, p in found)
    summaries = sum("summary without substance" in p for _, p in found)
    good = check(GOOD)
    for _, p in found:
        print("  caught: " + p)
    if missing or summaries != 2 or headings != 3 or good:
        print("SELF-TEST FAILED: missed %s; summaries caught %d of 2; malformed headings caught %d of 3; good sample problems %s" % (missing, summaries, headings, good))
        return 1
    print("Self-test passed: every rule caught its sample, and the good sample passed.")
    return 0


def main(argv):
    if argv[1:] == ["--self-test"]:
        return self_test()
    if len(argv) != 2:
        print(__doc__)
        return 2
    path = argv[1]
    with open(path, encoding="utf-8") as handle:
        problems = check(handle.read())
    for number, problem in problems:
        if os.environ.get("GITHUB_ACTIONS"):
            print("::error file=%s,line=%d::%s" % (path, number, problem))
        else:
            print("%s:%d: %s" % (path, number, problem))
    if problems:
        print("%d problem(s): see the changelog rule in docs/development.md#changelog." % len(problems))
        return 1
    print("%s follows the changelog rule." % path)
    return 0


if __name__ == "__main__":
    sys.exit(main(sys.argv))
