#!/usr/bin/env python3
"""Checks QA-CHECKLIST.md against CHANGELOG.md (docs/development.md#qa-checklist). CI's Changelog job runs it on every
pull request; run it locally with `python3 .github/checklist_check.py QA-CHECKLIST.md CHANGELOG.md`.

The QA checklist is what the owner tests on the QA server, in Discord, before a build goes to staging. Pull requests add
their items under "## Unreleased", the way they add changelog lines; the staging promotion numbers both. Checked:
  - versions: every numbered changelog version from 0.4.3 on has a "## 0.7.0004 (YYYY-MM-DD)" section here (up to
    0.6.0 "x.y.z"), with the same date, in the same order, and there are no others; "## Unreleased" only on top;
  - a version with nothing anyone can test by hand has only its heading (the same escape as a heading-only changelog
    entry); otherwise only items;
  - each item, short steps and not a paragraph (owner, 2026-11-20), with a name unique in its version:
        - **Name**
          - Steps:
            1. One short action.
          - Expect:
            - One thing to see.
          - Checked automatically: the <check> check.
    1 to 5 steps numbered in order, 1 to 4 Expect bullets, no sentence over 20 words, and no more than 80 words in its
    name, steps and Expect together;
  - who checks it, on its last line (every Unreleased item; numbered versions without one are the owner's):
    "  - Checked automatically: the <check> check." naming one of the automated checks (docker/qa_checks.py), or
    "  - Checked by the owner, <reason>: <why>." with the reason a person has to: needs human eyes, unsure of the
    desired behavior, or couldn't verify;
  - plain words, as in the changelog: no backticks, file names, paths, command flags, settings keys or environment
    variable names, since the owner reads these in Discord.
"""
import os
import re
import sys

sys.path.insert(0, os.path.dirname(os.path.abspath(__file__)))
sys.path.insert(0, os.path.join(os.path.dirname(os.path.dirname(os.path.abspath(__file__))), "docker"))
from changelog_check import CODE  # noqa: E402
from build_numbers import text as shown  # noqa: E402
from qa_checks import CHECKS, CHECKED_BY, ITEM, STEP, BULLET, STEPS_LINE, EXPECT_LINE, CHECKED_LINE  # noqa: E402

FIRST = (0, 4, 3)
VERSION = re.compile(r"^## (\d+)\.(\d+)\.(\d+) \((\d{4}-\d{2}-\d{2})\)$")
UNRELEASED = "## Unreleased"
MAX_STEPS = 5
MAX_EXPECT = 4
MAX_SENTENCE_WORDS = 20
MAX_ITEM_WORDS = 80
SENTENCE_END = re.compile(r"(?<=[.!?])\s+")
SHAPE = ("each item is '- **Name**', then '  - Steps:' over '    1. ...', '  - Expect:' over '    - ...', "
         "then who checks it")


def versions(text):
    found = []
    for number, line in enumerate(text.splitlines(), 1):
        match = VERSION.match(line.strip())
        if match:
            found.append((tuple(int(match.group(i)) for i in (1, 2, 3)), match.group(4), number))
    return found


def check(checklist, changelog):
    problems = version_problems(checklist, changelog)
    current, item, names = None, None, set()
    # A result is recorded against an item's name and words, so the same item word for word in two versions would be
    # one result for two cards.
    wordings = set()
    for number, line in enumerate(checklist.splitlines(), 1):
        line = line.rstrip()
        text = line.strip()
        if text == UNRELEASED and current is not None:
            problems.append((number, "'## Unreleased' goes once, on top"))
        if VERSION.match(text) or text == UNRELEASED:
            problems += finished(item, wordings)
            current, item, names = text, None, set()
            continue
        if current is None or not text:
            continue
        problems += [(number, "plain words only: line has %s" % what) for pattern, what in CODE if pattern.search(line)]
        if text.startswith("#"):
            problems.append((number, "only version headings: '%s'" % text))
            continue
        name = ITEM.match(line)
        if name:
            problems += finished(item, wordings)
            item = {"name": name.group(1).strip(), "line": number, "steps": [], "expect": [], "part": None,
                    "checked": False, "unreleased": current == UNRELEASED}
            if item["name"].lower() in names:
                problems.append((number, "item '%s' appears twice in this version" % item["name"]))
            names.add(item["name"].lower())
            continue
        problems += item_line(number, line, item)
    return problems + finished(item, wordings)


def version_problems(checklist, changelog):
    wanted = [(v, d) for v, d, _ in versions(changelog) if v >= FIRST]
    have = versions(checklist)
    if [(v, d) for v, d, _ in have] == wanted:
        return []
    missing = [v for v, _ in wanted if v not in [h[0] for h in have]]
    extra = [v for v, _, _ in have if v not in [w[0] for w in wanted]]
    return [(1, "versions don't match the changelog from 0.4.3 on (same versions, dates and order): missing %s, extra %s"
             % (", ".join(map(shown, missing)) or "none", ", ".join(map(shown, extra)) or "none"))]


def item_line(number, line, item):
    """One line inside an item: a part's heading, a step, an Expect bullet, or who checks it (always last)."""
    if item is None:
        return [(number, SHAPE)]
    if line.startswith(CHECKED_LINE):
        return checker(number, line, item)
    if item["checked"]:
        return [(number, "item '%s': who checks it is its last line" % item["name"])]
    if line in (STEPS_LINE, EXPECT_LINE):
        return part(number, line, item)
    entry = (STEP if item["part"] == "steps" else BULLET).match(line)
    if item["part"] is None or not entry:
        return [(number, "item '%s': %s" % (item["name"], SHAPE))]
    if item["part"] == "expect":
        item["expect"].append((number, entry.group(1).strip()))
        return []
    item["steps"].append((number, entry.group(2).strip()))
    if int(entry.group(1)) != len(item["steps"]):
        return [(number, "item '%s': steps are numbered 1, 2, 3 in order" % item["name"])]
    return []


def part(number, line, item):
    wanted = "steps" if line == STEPS_LINE else "expect"
    # Steps come first and Expect last, so either heading after Expect, or a second Steps, is out of place.
    out_of_place = item["part"] in ("expect", wanted)
    item["part"] = wanted
    return [(number, "item '%s': Steps once, then Expect once" % item["name"])] if out_of_place else []


def finished(item, wordings):
    """The checks on a whole item, once its last line is read."""
    if item is None:
        return []
    name, line = item["name"], item["line"]
    steps, expect = [t for _, t in item["steps"]], [t for _, t in item["expect"]]
    problems = []
    if not 1 <= len(steps) <= MAX_STEPS:
        problems.append((line, "item '%s' needs Steps: 1 to %d numbered steps (it has %d)" % (name, MAX_STEPS, len(steps))))
    if not 1 <= len(expect) <= MAX_EXPECT:
        problems.append((line, "item '%s' needs Expect: 1 to %d bullets (it has %d)" % (name, MAX_EXPECT, len(expect))))
    for number, text in item["steps"] + item["expect"]:
        problems += long_sentences(number, name, text)
    count = len((" ".join([name] + steps + expect)).split())
    if count > MAX_ITEM_WORDS:
        problems.append((line, "item '%s' has %d words; at most %d in its name, steps and Expect: make it two items"
                         % (name, count, MAX_ITEM_WORDS)))
    wording = " ".join(" ".join([name] + steps + ["Expect:"] + expect).split())
    if wording in wordings:
        problems.append((line, "item '%s' is already on the checklist word for word: reword it or drop the copy" % name))
    wordings.add(wording)
    if item["unreleased"] and not item["checked"]:
        problems.append((line, "item '%s' must say who checks it on its last line: '  - Checked automatically: the <check> check.' "
                               "or '  - Checked by the owner, <needs human eyes | unsure of the desired behavior | couldn't verify>: <why>.'" % name))
    return problems


def long_sentences(number, name, text):
    return [(number, "item '%s': a sentence over %d words: '%s'" % (name, MAX_SENTENCE_WORDS, sentence))
            for sentence in SENTENCE_END.split(text) if len(sentence.split()) > MAX_SENTENCE_WORDS]


def checker(number, line, item):
    match = CHECKED_BY.match(line.strip())
    if item["checked"]:
        return [(number, "item '%s' says who checks it twice" % item["name"])]
    item["checked"] = True
    if not match:
        return [(number, "an item's last line is only '  - Checked automatically: the <check> check.' or '  - Checked by the owner, "
                         "<needs human eyes | unsure of the desired behavior | couldn't verify>: <why>.'")]
    # Numbered versions are never edited, so a check retired since stays named there.
    if item["unreleased"] and match.group("check") and match.group("check") not in CHECKS:
        return [(number, "item '%s' names no automated check: '%s' (the checks are in docker/qa_checks.py)" % (item["name"], match.group("check")))]
    return long_sentences(number, item["name"], match.group("why") or "")


BAD_CHANGELOG = """# Changelog

## 0.4.4 (2026-10-25)

## 0.4.3 (2026-10-25)
"""

GOOD = """# QA checklist

Intro text is free.

## Unreleased
- **Build number**
  - Steps:
    1. Open System, then Status.
  - Expect:
    - The build's number with dev and its commit.
  - Checked automatically: the web app status page check.
- **Bot picture**
  - Steps:
    1. Open the bot's profile in Discord.
    2. Look at its picture.
  - Expect:
    - The plain picture.
  - Checked by the owner, needs human eyes: the profile popout is only in the Discord app.

## 0.4.4 (2026-10-25)
- **Logo**
  - Steps:
    1. Open the web app and look at the header.
  - Expect:
    - The Dev logo.

## 0.4.3 (2026-10-25)
"""

LONG = "one two three four five six seven eight nine ten eleven twelve thirteen fourteen fifteen sixteen seventeen eighteen nineteen twenty twentyone."

# Each rule and a sample that breaks only it, as an Unreleased item after the good sample's heading.
BREAKS = {
    "needs Steps": "- **No steps**\n  - Expect:\n    - It.\n  - Checked automatically: the live passing check.",
    "needs Expect": "- **No expect**\n  - Steps:\n    1. Do it.\n  - Checked automatically: the live passing check.",
    "1 to 5 numbered steps (it has 6)": "- **Six steps**\n  - Steps:\n" + "".join("    %d. Do it.\n" % n for n in range(1, 7))
                                         + "  - Expect:\n    - It.\n  - Checked automatically: the live passing check.",
    "1 to 4 bullets (it has 5)": "- **Five bullets**\n  - Steps:\n    1. Do it.\n  - Expect:\n" + "    - It.\n" * 5
                                  + "  - Checked automatically: the live passing check.",
    "a sentence over 20 words": "- **Long step**\n  - Steps:\n    1. " + LONG + "\n  - Expect:\n    - It.\n"
                                "  - Checked automatically: the live passing check.",
    "at most 80 in its name": "- **Long item**\n  - Steps:\n" + "".join("    %d. %s\n" % (n, " ".join(["word"] * 18)) for n in range(1, 6))
                              + "  - Expect:\n    - It.\n  - Checked automatically: the live passing check.",
    "numbered 1, 2, 3 in order": "- **Out of order**\n  - Steps:\n    2. Do it.\n  - Expect:\n    - It.\n"
                                 "  - Checked automatically: the live passing check.",
    "Steps once, then Expect once": "- **Expect first**\n  - Expect:\n    - It.\n  - Steps:\n    1. Do it.\n"
                                    "  - Checked automatically: the live passing check.",
    "each item is '- **Name**'": "- **Old style:** Do it. You should see it.\n  - Checked automatically: the live passing check.",
    "its last line": "- **After the checker**\n  - Steps:\n    1. Do it.\n  - Checked automatically: the live passing check.\n"
                     "  - Expect:\n    - It.",
    "must say who checks it": "- **Unchecked**\n  - Steps:\n    1. Do it.\n  - Expect:\n    - It.",
    "names no automated check": "- **Made up**\n  - Steps:\n    1. Do it.\n  - Expect:\n    - It.\n  - Checked automatically: the made-up check.",
    "says who checks it twice": "- **Twice**\n  - Steps:\n    1. Do it.\n  - Expect:\n    - It.\n  - Checked automatically: the live passing check.\n"
                                "  - Checked automatically: the live failing check.",
    "last line is only": "- **Bad checker**\n  - Steps:\n    1. Do it.\n  - Expect:\n    - It.\n  - Checked by the owner, because I said so.",
    "appears twice in this version": "- **Build number**\n  - Steps:\n    1. Open it.\n  - Expect:\n    - Another.\n"
                                     "  - Checked automatically: the web app status page check.",
    "word for word": "- **Logo**\n  - Steps:\n    1. Open the web app and look at the header.\n  - Expect:\n    - The Dev logo.\n"
                     "  - Checked automatically: the web app header check.",
    "backticks": "- **Code**\n  - Steps:\n    1. Open `the file`.\n  - Expect:\n    - It.\n  - Checked automatically: the live passing check.",
    "a file name": "- **File**\n  - Steps:\n    1. Open provisioning.yml.\n  - Expect:\n    - It.\n  - Checked automatically: the live passing check.",
    "only version headings": "### Section",
    "goes once, on top": "## Unreleased",
}


def self_test():
    wrong = []
    for rule, sample in BREAKS.items():
        found = [p for _, p in check(GOOD.replace("## Unreleased\n", "## Unreleased\n" + sample + "\n"), BAD_CHANGELOG)]
        print("  %s: %s" % (rule, "; ".join(found) or "nothing caught"))
        if not any(rule in p for p in found):
            wrong.append(rule)
    versions_found = [p for _, p in check(GOOD.replace("## 0.4.3 (2026-10-25)\n", ""), BAD_CHANGELOG)]
    if not any("versions don't match" in p for p in versions_found):
        wrong.append("versions don't match")
    good = check(GOOD, BAD_CHANGELOG)
    if wrong or good:
        print("SELF-TEST FAILED: missed %s; good sample problems %s" % (wrong, good))
        return 1
    print("Self-test passed: every rule caught its sample (%d), and the good sample passed." % (len(BREAKS) + 1))
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
