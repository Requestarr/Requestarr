#!/usr/bin/env python3
"""Checks the changelog against its rule (docs/development.md#changelog). CI's Changelog job runs it on every pull
request; run it locally with `python3 .github/changelog_check.py CHANGELOG.md`.

The changelog is kept as entries, one small file per change under changelog/entries, with the builds listed in
changelog/builds.txt (.github/changelog_entries.py says how they're written). This checks:
  - every entry: its name, only the known fields, an audience (users or admins), a kind (new, fixed or changed), a line,
    and a build only once it's listed in the builds;
  - every line (and line_if_seen): plain words, meaning no backticks, file names (provisioning.yml), paths (docker/x),
    command flags (-Stack, --ref), settings keys (removal.voteDays) or environment variable names (DEV_DISCORD_*); no
    placeholders ("nothing this time", "n/a", "TBD"); a summary phrase ("minor fixes") only with at least five more words
    saying what it changes; one to three sentences and at most 50 words after the bold lead (owner, 2026-11-23: "every
    note should be like 1-3 sentences not a bible");
  - replaces: every one names an existing entry, no chain loops back, and an entry replacing one testers already have,
    as a different kind, has line_if_seen saying what changed for them;
  - CHANGELOG.md is exactly what the entries write (it's generated, never edited by hand).
There's no cap on how many entries a build or a section has: a big build lists each thing that changed (owner,
2026-11-24: "if it's a big patch it should have bullets for each item that changed").

`--self-test` runs every rule against built-in samples, a 20-line section that passes, and the fold cases (never
posted, posted, a partly posted chain), and fails if one doesn't hold.
"""
import os
import re
import sys

sys.path.insert(0, os.path.dirname(os.path.abspath(__file__)))
import changelog_entries as entries_module  # noqa: E402

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


# ---- the entries ------------------------------------------------------------------------------------------------

def loops(entries):
    """Entries whose chain of replaces comes back to them."""
    found = []
    for entry in entries.values():
        seen, todo = set(), list(entry.replaces)
        while todo:
            entry_id = todo.pop()
            if entry_id == entry.id:
                found.append(entry.id)
                break
            if entry_id in seen or entry_id not in entries:
                continue
            seen.add(entry_id)
            todo.extend(entries[entry_id].replaces)
    return found


def check_entry(entry, entries, builds):
    """Problems with one entry file."""
    problems = []
    if not entries_module.ID.match(entry.id):
        problems.append("its name: lower-case letters, digits, dots and dashes, as in 2026-11-24-long-tv-shows")
    problems += ["an unknown field '%s': only %s" % (name, ", ".join(entries_module.FIELDS)) for name in entry.unknown]
    if entry.audience not in entries_module.AUDIENCES:
        problems.append("audience '%s': users or admins" % entry.audience)
    if entry.kind not in entries_module.KINDS:
        problems.append("kind '%s': new, fixed or changed" % entry.kind)
    if entry.build_text and (entry.build is None or entry.build not in builds):
        problems.append("build '%s' isn't in the builds list: leave it out until the staging promotion fills it in" % entry.build_text)
    if not entry.line:
        problems.append("no line: say what changed, with a bold lead")
    for field, text in (("line", entry.line), ("line_if_seen", entry.line_if_seen)):
        problems += ["%s: %s" % (field, p) for p in check_line(text)] if text else []
    for replaced in entry.replaces:
        if replaced not in entries:
            problems.append("replaces '%s', which isn't an entry" % replaced)
        elif replaced == entry.id:
            problems.append("replaces itself")
        elif entries[replaced].build and entries[replaced].kind != entry.kind and not entry.line_if_seen:
            problems.append("replaces '%s', which testers already have, as a different kind: add line_if_seen, what changed for them" % replaced)
    return problems


def check_changelog(changelog):
    """[(where, problem)] for every entry and build, and replacement loops."""
    builds = [b.version for b in changelog.builds]
    problems = [("changelog/builds.txt", "%s appears twice" % entries_module.shown(v)) for v in sorted(set(builds)) if builds.count(v) > 1]
    for entry_id, entry in sorted(changelog.entries.items()):
        problems += [("changelog/entries/%s.txt" % entry_id, p) for p in check_entry(entry, changelog.entries, builds)]
    problems += [("changelog/entries/%s.txt" % entry_id, "its replaces come back to it: a replacement can't loop") for entry_id in loops(changelog.entries)]
    return problems


def check_builds_file(text):
    return [("changelog/builds.txt:%d" % number, "'%s' isn't 'version | YYYY-MM-DD | notes' (notes: with x to y, flat)" % row)
            for number, build, row in entries_module.parse_builds(text) if build is None]


def check(root, path):
    """[(where, problem)] for the entries under root/changelog and the generated file at path."""
    changelog = entries_module.Changelog.load(root)
    problems = check_changelog(changelog)
    builds_path = os.path.join(root, "changelog", "builds.txt")
    if os.path.exists(builds_path):
        with open(builds_path, encoding="utf-8") as handle:
            problems += check_builds_file(handle.read())
    with open(path, encoding="utf-8") as handle:
        committed = handle.read().replace("\r\n", "\n")
    if committed != changelog.render():
        problems.append((os.path.basename(path), "doesn't match the entries: it's written from them, never by hand; run "
                                                 "python3 .github/changelog_entries.py write"))
    return problems


# ---- self-test --------------------------------------------------------------------------------------------------

def entry(entry_id, **fields):
    return entries_module.Entry(entry_id, {name.rstrip("_"): value for name, value in fields.items()})


def sample(*items, builds=()):
    return entries_module.Changelog({e.id: e for e in items}, [entries_module.Build(b, "2026-11-22") for b in builds], "# Changelog\n")


def self_test():
    released = (0, 7, 10)
    bad = sample(
        entry("Bad Name", audience="users", kind="new", line="**Fine:** a line."),
        entry("odd-fields", audience="everyone", kind="tweak", line="**Fine:** a line.", colour="blue"),
        entry("unknown-build", audience="users", kind="new", build="0.7.0099", line="**Fine:** a line."),
        entry("no-line", audience="users", kind="new"),
        entry("code", audience="admins", kind="changed", line="**Code:** set removal.voteDays in provisioning.yml."),
        entry("long", audience="admins", kind="changed", line="**Too long:** " + " ".join(["word"] * 51) + "."),
        entry("sentences", audience="admins", kind="changed", line="**Too many sentences:** one. Two. Three. Four"),
        entry("summary", audience="admins", kind="fixed", line="Minor fixes."),
        entry("dangling", audience="users", kind="new", replaces="nowhere", line="**Fine:** a line."),
        entry("loop-a", audience="users", kind="new", replaces="loop-b", line="**Fine:** a line."),
        entry("loop-b", audience="users", kind="new", replaces="loop-a", line="**Fine:** a line."),
        entry("shipped", audience="users", kind="new", build="0.7.0010", line="**Shipped:** a line."),
        entry("rework", audience="users", kind="changed", replaces="shipped", line="**Rework:** a line."),
        builds=[released])
    found = [p for _, p in check_changelog(bad)]
    expected = ["its name", "unknown field 'colour'", "audience 'everyone'", "kind 'tweak'", "build '0.7.0099'", "no line",
                "a settings key", "a file name", "words after the bold lead", "4 sentences", "summary without substance",
                "'nowhere', which isn't an entry", "can't loop", "add line_if_seen"]
    missing = [e for e in expected if not any(e in p for p in found)]
    bad_rows = check_builds_file("0.7.0010 | 2026-11-22\n0.7.0011 | someday\n")

    # A section of 20 lines passes: there's no cap on how many changes a build lists.
    big = sample(*[entry("2026-11-24-change-%02d" % n, audience="admins", kind="new", line="**Change %d:** one more thing you can do now." % n) for n in range(20)])
    good_problems = check_changelog(big)
    rendered = big.render()

    # Folding: a reader who never got the replaced entry sees the replacing one alone, as New; one who got it sees
    # line_if_seen; a chain folds all the way; a failed post still counts as never posted.
    chain = {e.id: e for e in (
        entry("a", audience="admins", kind="new", line="**A:** first."),
        entry("b", audience="admins", kind="changed", replaces="a", line="**B:** second.", line_if_seen="**B seen:** second."),
        entry("c", audience="admins", kind="fixed", replaces="b", line="**C:** third.", line_if_seen="**C seen:** third."))}
    everything = list(chain.values())

    def fold(posted):
        return [(e.id, text, kind) for e, text, kind in entries_module.fold(chain, everything, set(posted))]

    folds = [
        (fold([]), [("c", "**C:** third.", "new")]),
        (fold(["a"]), [("a", "**A:** first.", "new"), ("c", "**C seen:** third.", "fixed")]),
        (fold(["a", "b"]), [("a", "**A:** first.", "new"), ("b", "**B seen:** second.", "changed"), ("c", "**C seen:** third.", "fixed")]),
    ]
    wrong_folds = [got for got, wanted in folds if got != wanted]

    for problem in found:
        print("  caught: " + problem)
    if missing or len(bad_rows) != 1 or good_problems or rendered.count("- **Change") != 20 or wrong_folds:
        print("SELF-TEST FAILED: missed %s; bad build rows caught %d of 1; 20-line sample problems %s; folds wrong %s"
              % (missing, len(bad_rows), good_problems, wrong_folds))
        return 1
    print("Self-test passed: every rule caught its sample, a section of 20 lines passed, and every fold case held.")
    return 0


def main(argv):
    if argv[1:] == ["--self-test"]:
        return self_test()
    if len(argv) != 2:
        print(__doc__)
        return 2
    path = os.path.abspath(argv[1])
    problems = check(os.path.dirname(path), path)
    for where, problem in problems:
        if os.environ.get("GITHUB_ACTIONS"):
            print("::error file=%s::%s" % (where.split(":")[0], problem))
        else:
            print("%s: %s" % (where, problem))
    if problems:
        print("%d problem(s): see the changelog rule in docs/development.md#changelog." % len(problems))
        return 1
    print("%s follows the changelog rule and matches its %d entries." % (argv[1], len(entries_module.Changelog.load(os.path.dirname(path)).entries)))
    return 0


if __name__ == "__main__":
    sys.exit(main(sys.argv))
