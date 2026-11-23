#!/usr/bin/env python3
"""Requestarr's build numbers (docs/development.md#versions). This is the only thing that takes a number.

A version is MAJOR.MINOR.BUILD, with BUILD a running four-digit number: 0.7.0004. Every finished goal takes the next
number for its commit on dev, recorded as a git tag on that commit (v0.7.0004). QA and staging reuse the number of the
build promoted to them, so a number always points at one tested commit, and production ships the file staging built. A
fix into staging takes the next number on its own release commit. A number is never reused: a bad build keeps its
number and the fix takes the next one.
  - BUILD counts every build sent; it goes back to 0000 only when MAJOR rolls up.
  - MINOR rolls up once the newest build has shipped (production, "ship it", is main): 0.7.0004 shipped, next 0.8.0005.
  - MAJOR: 1.0 on the owner's word (the version 1 scope), then when admins must act on an upgrade (a "**Before you
    upgrade:**" line under For admins). MINOR goes to 0 and BUILD starts again: 1.0.0001.
A numbered dev commit is labelled 0.7.0005-dev, and 0.7.0005-qa on QA; staging and production show 0.7.0005. A dev
build made between goals (a commit without a number) shows the next number and its commit: 0.7.0006-dev.4f2a9c1.
Versions up to 0.6.0 kept their x.y.z numbers, one per pull request; three of those builds went to testers (below).

CHANGELOG.md and QA-CHECKLIST.md: pull requests add their lines under "## Unreleased"; the staging promotion turns
it into "## 0.7.0011 (YYYY-MM-DD)", the changelog's heading naming the goals' builds it includes ("## 0.7.0011
(YYYY-MM-DD), with 0.7.0005 to 0.7.0011"). Numbered sections never change after that, unless the owner approves
rewriting one changelog section, recorded in the pull request's description (check --rewrite; release.sh reads the
"Rewrite of a numbered version:" line). Its heading stays; the QA checklist's numbered sections never change.

  python3 .github/build_numbers.py next [--major] [--changelog <file>] [--shipped <x.y.zzzz>] [--before <commit>]
        the number the next build sent to testers takes (from 1.0, a major when that changelog's Unreleased has a
        "Before you upgrade" line for admins; --shipped: as if that build had shipped; --before: as it was before
        that commit, for checking a pull request whose branch has already moved to it)
  python3 .github/build_numbers.py number <commit> [--major] [--dry-run]
        numbers a finished goal's commit on dev: tags it v<the next number> on origin and prints the number; a commit
        that has one prints it again. Refuses a commit that isn't on origin/dev, or one older than a numbered commit.
        (--major: as next; --dry-run: says which number it would take and tags nothing)
  python3 .github/build_numbers.py image <event> <branch> <commit> [<test tag>]
        what the Image workflow publishes, from CHANGELOG.md here: key=value lines (mode build or retag, tags, ...)
  python3 .github/build_numbers.py label <dev|qa|test> <commit>
        how a stack's build is labelled: 0.7.0005-dev for a numbered commit, otherwise 0.7.0006-dev.4f2a9c1
  python3 .github/build_numbers.py release <dir> <number> <YYYY-MM-DD> [--security] [--from <x.y.zzzz>]
        turns Unreleased into that number in <dir>/CHANGELOG.md and <dir>/QA-CHECKLIST.md (--security: adds the line
        telling admins a security fix is in this build; --from: the oldest goal's build it includes, which the
        changelog's heading names)
  python3 .github/build_numbers.py merge <ours dir> <theirs dir> <out dir> [<their merge base dir>]
        a merge-back of both files: every numbered section of both, highest first; Unreleased keeps the lines of both
        that no numbered section has yet
  python3 .github/build_numbers.py check <change|release|staging-fix|merge-back> <base dir> <head dir> [<upper dir>]
        [--number <x.y.zzzz>] [--no-changelog] [--rewrite <x.y.zzzz>]   a pull request's two files against the rules;
        exit 1 with the reasons (--rewrite: the owner approved rewriting that changelog section's lines)
  python3 .github/build_numbers.py --self-test

next reads the tags with `git ls-remote --tags origin` (no login) and which builds shipped from origin/main.
"""
import os
import re
import subprocess
import sys

# A line under a QA checklist item that belongs to it ("  - Checked by ...").
CONTINUATION = "  "
UNRELEASED = "Unreleased"
HEADING = re.compile(r"^## (?:(?P<unreleased>Unreleased)|(?P<version>\d+\.\d+\.\d+)(?: .*)?)$")
TAG = re.compile(r"^v(\d+)\.(\d+)\.(\d{4,})$")
FILES = ("CHANGELOG.md", "QA-CHECKLIST.md")

# Builds sent to testers before numbers were kept: the three staging image publishes of October 24, 2026 (0.3.1 at
# 00239c8, 0.3.3 at bd92a1e, 0.3.4 at 1bc61b8). The counter starts after them, on the first minor not already used.
EARLIER_BUILDS = 3

# Released changelog sections the owner approved rewriting, each also recorded in its pull request's description. A
# merge-back keeps the lower branch's words for these until the promotions carry the rewrite up.
APPROVED_REWRITES = {
    (0, 7, 10): 'owner, 2026-11-23: "every note should be like 1-3 sentences not a bible"',
}
FIRST_NUMBERED = (0, 7)

SECTION_ORDER = ("", "For users", "For admins")
ADMINS = "For admins"
LEAD = re.compile(r"- \*\*([^*]+)\*\*")
UPGRADE_LEAD = "- **Before you upgrade:**"
SECURITY_LINE = ("- **Security update:** parts Requestarr is built from were updated to fix known security problems, "
                 "so update when you can.")


def parse_version(value):
    match = re.fullmatch(r"v?(\d+)\.(\d+)\.(\d+)", value.strip())
    if not match:
        raise ValueError("not a version: %r" % value)
    return tuple(int(x) for x in match.groups())


def text(version):
    """How people see a version: 0.7.0004 from 0.7 on, the old x.y.z before."""
    return ("%d.%d.%04d" if version[:2] >= FIRST_NUMBERED else "%d.%d.%d") % version


# ---- the counter ------------------------------------------------------------------------------------------------

def built(tag_names):
    """The numbers taken so far, from tag names (v0.7.0004)."""
    return {tuple(int(x) for x in m.groups()) for m in map(TAG.match, tag_names) if m}


def tagged_builds(tags):
    """{number: commit} for the numbered tags of {tag name: commit}."""
    return {v: sha for name, sha in tags.items() for v in built([name])}


def next_number(taken, shipped, major=False):
    """The next build's number: one more than the newest, a minor up once a build of the newest's minor has shipped
    (goals go on numbering dev while a build waits in staging, so the newest isn't always the one that ships)."""
    newest = max(taken) if taken else FIRST_NUMBERED + (EARLIER_BUILDS,)
    if major:
        return (newest[0] + 1, 0, 1)
    rolled = any(build[:2] == newest[:2] for build in shipped)
    return (newest[0], newest[1] + (1 if rolled else 0), newest[2] + 1)


def git(*args):
    return subprocess.run(["git", *args], capture_output=True, text=True, encoding="utf-8")


def remote_tags():
    """{tag name: commit} from origin; an annotated tag is read by the commit it points to."""
    listed = git("ls-remote", "--tags", "origin")
    if listed.returncode != 0:
        raise SystemExit("couldn't read origin's tags, so the next number isn't known: %s" % listed.stderr.strip())
    tags = {}
    for row in listed.stdout.splitlines():
        sha, _, ref = row.partition("\t")
        name = ref[len("refs/tags/"):]
        peeled = name.endswith("^{}")
        name = name[:-3] if peeled else name
        if peeled or name not in tags:
            tags[name] = sha
    return tags


def branch_builds(branch, before=None):
    """The numbered builds (from 0.7) in a branch's changelog on origin. A build is taken once its release commit is
    on qa or staging, before its image is published and tagged, so a publish that fails never frees its number.
    before: a commit whose own numbers don't count (a pull request checked while its branch has already moved to it)."""
    ref = "origin/" + branch
    if before and git("rev-parse", ref).stdout.strip() == git("rev-parse", before).stdout.strip():
        ref = ref + "^"
    shown = git("show", ref + ":CHANGELOG.md")
    return {v for v in parse(shown.stdout)[2] if v[:2] >= FIRST_NUMBERED} if shown.returncode == 0 else set()


def next_from_git(major=False, shipping=None, before=None):
    """shipping: a build about to ship, counted as shipped (what ship-it's dry run shows). Production has shipped
    what main's changelog numbers."""
    git("fetch", "-q", "origin", "qa", "staging", "main")
    tags = {name: sha for name, sha in remote_tags().items() if not before or sha != git("rev-parse", before).stdout.strip()}
    shipping = {shipping} if shipping else set()
    taken = built(tags) | branch_builds("qa", before) | branch_builds("staging", before) | shipping
    return next_number(taken, branch_builds("main") | shipping, major)


def numbers_on(tags, sha):
    """The numbers tagged on exactly this commit."""
    return {version for version, tagged in tagged_builds(tags).items() if tagged == sha}


def label(stack, sha, tags, upcoming, released=None):
    """How a stack's build is labelled: its number when its commit has one (0.7.0005-qa), or the number a release
    commit carries (released: its changelog's top number when Unreleased is gone, as on qa after a staging promotion);
    otherwise the next number and the commit (0.7.0006-dev.4f2a9c1). A test publish always shows its commit."""
    own = numbers_on(tags, sha) or ({released} if released else set())
    if own and stack != "test":
        return "%s-%s" % (text(max(own)), stack)
    return "%s-%s.%s" % (text(upcoming), stack, sha[:7])


def released_number(sha):
    """The number a release commit carries: its changelog's top number, when no Unreleased is above it (from 0.7)."""
    shown = git("show", sha + ":CHANGELOG.md")
    top, pending = top_number(shown.stdout) if shown.returncode == 0 else (None, True)
    return top if top and not pending and top[:2] >= FIRST_NUMBERED else None


def is_ancestor(older, newer):
    """The commit older is newer or comes before it."""
    return git("merge-base", "--is-ancestor", older, newer).returncode == 0


def upgrade_lines(sha):
    """The "Before you upgrade" lines for admins under Unreleased in the commit's changelog."""
    shown = git("show", sha + ":CHANGELOG.md")
    if shown.returncode != 0:
        return set()
    return {row for row in (parse(shown.stdout)[1] or {}).get(ADMINS, []) if row.startswith(UPGRADE_LEAD)}


def new_upgrade(sha, tags):
    """A "Before you upgrade" line the newest numbered build before this commit didn't have: Unreleased keeps the
    line until the staging promotion, so only the goal that added it rolls the major up."""
    before = [(version, tagged) for version, tagged in tagged_builds(tags).items() if is_ancestor(tagged, sha)]
    previous = upgrade_lines(max(before)[1]) if before else set()
    return bool(upgrade_lines(sha) - previous)


def take_number(commit, major=False, dry_run=False):
    """A finished goal's number for its commit on dev: (number, whether it was taken now). The commit's own number when
    it has one, so running it again changes nothing; otherwise the next, tagged on origin."""
    git("fetch", "-q", "origin", "dev", "qa", "staging", "main")
    sha = git("rev-parse", "--verify", "-q", commit + "^{commit}").stdout.strip()
    if not sha:
        raise SystemExit("no commit %r" % commit)
    if not is_ancestor(sha, "origin/dev"):
        raise SystemExit("%s isn't on dev: only a finished goal's commit on dev takes a number" % sha[:7])
    tags = remote_tags()
    own = numbers_on(tags, sha)
    if own:
        return max(own), False
    later = sorted(version for version, tagged in tagged_builds(tags).items() if is_ancestor(sha, tagged))
    is_qa = sha == git("rev-parse", "origin/qa").stdout.strip()
    if later and not is_qa:
        raise SystemExit("%s comes before %s, already numbered: numbers only go up along dev" % (sha[:7], text(later[-1])))
    number = next_from_git()
    if major or (number[0] >= 1 and new_upgrade(sha, tags)):
        number = next_from_git(major=True)
    if not dry_run:
        record(number, sha)
    return number, True


def record(number, sha):
    """Tags the commit on origin; a push that fails takes the local tag away again, so nothing claims the number."""
    name = "v" + text(number)
    tagged = git("tag", name, sha)
    pushed = git("push", "-q", "origin", "refs/tags/" + name) if tagged.returncode == 0 else tagged
    if pushed.returncode != 0:
        git("tag", "-d", name)
        raise SystemExit("couldn't record %s on %s: %s" % (text(number), sha[:7], pushed.stderr.strip()))


# ---- published images ---------------------------------------------------------------------------------------------

IMAGE = "ghcr.io/requestarr/requestarr"


def top_number(changelog):
    """The newest numbered version of a changelog, and whether Unreleased is above it."""
    _, unreleased, sections = parse(changelog)
    return (max(sections) if sections else None), unreleased is not None


def same_commit(numbered, sha):
    return numbered == sha


def release_of(numbered, sha):
    """The commit is the numbered one, or the release commit the staging promotion made right on top of it."""
    return numbered == sha or git("rev-parse", sha + "^").stdout.strip() == numbered


def image_plan(event, branch, sha, changelog, tags, upcoming, test_tag="", built_from=same_commit):
    """What the Image workflow does: {"mode": "build" or "retag", "number", "label", "tags", "source", "record"}, or
    {"error": why}. Staging builds the image for the number on top of its changelog: the number the goal's dev commit
    was given (the release commit sits right on top of it), or a staging fix's new number (whose tag it records). Main
    never builds: it adds the release tags to the image staging published. built_from(numbered, sha): whether sha is
    the numbered commit or its release commit."""
    top, pending = top_number(changelog)
    tagged = tagged_builds(tags)
    if event == "push" and branch == "staging":
        if pending or top is None:
            return {"error": "staging's changelog has Unreleased on top: a build sent to testers has a number "
                             "(./release.sh promote-staging, or a fix into staging with the next number)"}
        if top[:2] < FIRST_NUMBERED:
            return {"error": "staging's top version is %s, from before build numbers: a build sent to testers takes "
                             "the next number (./release.sh next-version)" % text(top)}
        if top in tagged and not built_from(tagged[top], sha):
            return {"error": "%s was already published from another commit: a number is never reused" % text(top)}
        if top not in tagged and tagged and top < max(tagged):
            return {"error": "%s is below %s, already published: numbers only go up" % (text(top), text(max(tagged)))}
        number = text(top)
        return {"mode": "build", "number": number, "label": number, "source": "",
                "tags": "%s:beta,%s:%s" % (IMAGE, IMAGE, number), "record": "" if top in tagged else "v" + number}
    if event == "push" and branch == "main":
        if top is None or top not in tagged or not built_from(tagged[top], sha):
            return {"error": "main is only ever a build staging published (v<number> on this commit or before it): "
                             "./release.sh ship-it"}
        number = text(top)
        minor = "%d.%d" % top[:2]
        return {"mode": "retag", "number": number, "label": number, "source": "%s:%s" % (IMAGE, number),
                "tags": "%s:%s,%s:latest" % (IMAGE, minor, IMAGE), "record": ""}
    if event == "workflow_dispatch" and re.fullmatch(r"test-[a-z0-9.-]+", test_tag or ""):
        return {"mode": "build", "number": text(upcoming), "label": label("test", sha, tags, upcoming),
                "source": "", "tags": "%s:%s" % (IMAGE, test_tag), "record": ""}
    if event == "workflow_dispatch":
        return {"error": "a test tag starts with 'test-' and has only lowercase letters, digits, dots and dashes"}
    return {"error": "images are published from staging and main only (or a test tag by hand)"}


# ---- the two files ----------------------------------------------------------------------------------------------

def parse(document):
    """(text before the first version, Unreleased as {subheading: lines} or None, {version: section text})."""
    document = document.replace("\r\n", "\n")
    preamble, unreleased, sections = [], None, {}
    current = None
    for row in document.split("\n"):
        heading = HEADING.match(row)
        if heading and heading.group("unreleased"):
            unreleased, current = {}, None
        elif heading:
            current = parse_version(heading.group("version"))
            sections[current] = row + "\n"
        elif current is not None:
            sections[current] += row + "\n"
        elif unreleased is not None:
            add_unreleased_row(unreleased, row)
        else:
            preamble.append(row)
    sections = {v: s.rstrip("\n") + "\n\n" for v, s in sections.items()}
    return "\n".join(preamble).rstrip("\n") + "\n\n", unreleased, sections


def add_unreleased_row(unreleased, row):
    """A line under Unreleased; an indented one (a QA checklist item's "Checked by" line) belongs to the item above it,
    so the two move, merge and are released together."""
    if row.startswith("### "):
        unreleased.setdefault(row[4:].strip(), [])
    elif row.strip():
        last = list(unreleased)[-1] if unreleased else ""
        rows = unreleased.setdefault(last, [])
        if row.startswith(CONTINUATION) and rows:
            rows[-1] += "\n" + row.rstrip()
        else:
            rows.append(row.rstrip())


def ordered(unreleased):
    rank = {name: i for i, name in enumerate(SECTION_ORDER)}
    return sorted(((sub, rows) for sub, rows in unreleased.items() if rows), key=lambda item: rank.get(item[0], len(rank)))


def render_section(heading, unreleased):
    """A version's section: "## heading", then each part with its "### " subheading (the checklist has none)."""
    section = "## %s\n" % heading
    for sub, rows in ordered(unreleased or {}):
        if sub:
            section += "\n### %s\n" % sub
        section += "".join(row + "\n" for row in rows)
    return section + "\n"


def render(preamble, unreleased, sections):
    top = render_section(UNRELEASED, unreleased) if unreleased and ordered(unreleased) else ""
    body = top + "".join(sections[v] for v in sorted(sections, reverse=True))
    return (preamble + body).rstrip("\n") + "\n"


def lines_of(unreleased):
    return {row for rows in (unreleased or {}).values() for row in rows}


def released_lines(sections):
    """Every line of the numbered sections, an item with its indented lines as one, as Unreleased holds them."""
    units = set()
    for section in sections.values():
        rows = []
        for row in section.split("\n"):
            if row.startswith(CONTINUATION) and rows:
                rows[-1] += "\n" + row.rstrip()
            elif row.startswith("- "):
                rows.append(row.rstrip())
        units.update(rows)
    return units


def release(document, number, day, extra_admin_line=None, included_from=None):
    """Unreleased becomes the numbered section (heading only when nothing is under it). included_from: the oldest goal's
    build it includes, named in the heading when it isn't this one: "0.7.0011 (day), with 0.7.0005 to 0.7.0011"."""
    preamble, unreleased, sections = parse(document)
    if number in sections:
        raise ValueError("%s is already in this file" % text(number))
    unreleased = {sub: list(rows) for sub, rows in (unreleased or {}).items()}
    if extra_admin_line and extra_admin_line not in unreleased.get(ADMINS, []):
        unreleased.setdefault(ADMINS, []).append(extra_admin_line)
    heading = "%s (%s)" % (text(number), day)
    if included_from and included_from != number:
        heading += ", with %s to %s" % (text(included_from), text(number))
    sections[number] = render_section(heading, unreleased)
    return render(preamble, None, sections)


def first_line(section):
    return section.split("\n", 1)[0]


def rewrite_on_lower(version, lower, upper):
    """A changelog section the owner approved rewriting (APPROVED_REWRITES), reading differently under the same
    heading on two branches. Rewrites go into dev, so until the promotions carry the new words up, a merge-back keeps
    the lower branch's."""
    return version in APPROVED_REWRITES and lower != upper and first_line(lower) == first_line(upper)


def merge(ours, theirs, base=None):
    """Every numbered section of both, highest first (one both have must read the same, or be an approved rewrite,
    which keeps ours); Unreleased keeps the lines of both, ours first, except those the other side has since released
    (an older build with the same words doesn't count) and, given their common base, those one side rewrote or removed
    since it. Ours is the branch merged into, the lower one."""
    preamble, mine, own = parse(ours)
    _, other, others = parse(theirs)
    gone = set()
    if base is not None:
        mine_rows, other_rows = lines_of(mine), lines_of(other)
        gone = {row for row in lines_of(parse(base)[1]) if row not in mine_rows or row not in other_rows}
    for version in own.keys() & others.keys():
        if own[version].rstrip() != others[version].rstrip() and not rewrite_on_lower(version, own[version], others[version]):
            raise ValueError("%s reads differently on the two branches: merge it by hand" % text(version))
    sections = {**others, **own}
    unreleased = {}
    for side, newer in ((mine or {}, newly_released(own, others)), (other or {}, newly_released(others, own))):
        done = released_lines(newer) | gone
        for sub, rows in side.items():
            kept = unreleased.setdefault(sub, [])
            kept.extend(row for row in rows if row not in done and row not in kept)
    return render(preamble, unreleased, sections)


def newly_released(known, merged):
    """The numbered sections of `merged` that `known` doesn't have yet."""
    return {v: s for v, s in merged.items() if v not in known}


def upgrade_needs_admins(document):
    _, unreleased, _ = parse(document)
    return any(row.startswith(UPGRADE_LEAD) for row in (unreleased or {}).get(ADMINS, []))


# ---- a pull request's rules ---------------------------------------------------------------------------------------

def tested_versions():
    """The versions testers have (staging's changelog), or None when staging can't be read."""
    git("fetch", "-q", "origin", "staging")
    shown = git("show", "origin/staging:CHANGELOG.md")
    return set(parse(shown.stdout)[2]) if shown.returncode == 0 else None


def rewritten(old, version, section, allowed):
    """An approved rewrite: the owner approved that version, and its heading is as it was."""
    return version in allowed and version in old and first_line(old[version]) == first_line(section)


def numbered_changes(name, base, head, tested, rewrites=frozenset()):
    """A numbered section never changes, except that one newer than every version testers have may be folded into
    Unreleased (removed): it never reached them, and Unreleased says what changed since the last build they got.
    A changelog section the owner approved rewriting (rewrites) may change its lines, never its heading."""
    old, new = parse(base)[2], parse(head)[2]
    allowed = rewrites if name == "CHANGELOG.md" else frozenset()
    if any(old.get(v) != s and not rewritten(old, v, s, allowed) for v, s in new.items()):
        return ["%s: a numbered version changed; add lines under '## Unreleased' instead" % name]
    dropped = sorted(allowed - set(new))
    if dropped:
        return ["%s: an approved rewrite keeps its version: %s" % (name, ", ".join(map(text, dropped)))]
    folded = set(old) - set(new)
    if folded and (tested is None or min(folded) <= max(tested, default=(0, 0, 0))):
        return ["%s: only versions testers never had can be folded into Unreleased; %s reached them" % (name, ", ".join(map(text, sorted(folded))))]
    return []


def repeated_leads(unreleased):
    """Unreleased says where things ended up: one line per thing in each part, rewritten when that thing changes again."""
    problems = []
    for sub, rows in (unreleased or {}).items():
        leads = [match.group(1).rstrip(":").lower() for match in map(LEAD.match, rows) if match]
        problems += ["CHANGELOG.md: '%s' leads two lines under Unreleased%s; rewrite the one line to where it ended up" % (lead, " (%s)" % sub if sub else "")
                     for lead in sorted({lead for lead in leads if leads.count(lead) > 1})]
    return problems


def check_change(base, head, no_changelog, tested=None, rewrites=frozenset()):
    """Into dev, or a fix into qa: numbered sections untouched (a version testers never had may be folded into
    Unreleased; a changelog section the owner approved rewriting may change); the changelog gains Unreleased lines,
    one per thing, or the pull request says it has none."""
    problems = []
    for name in FILES:
        problems += numbered_changes(name, base[name], head[name], tested, rewrites)
    problems += repeated_leads(parse(head["CHANGELOG.md"])[1])
    added = lines_of(parse(head["CHANGELOG.md"])[1]) - lines_of(parse(base["CHANGELOG.md"])[1])
    if no_changelog and added:
        problems.append("the description says 'No changelog:', but CHANGELOG.md adds lines under Unreleased")
    elif not no_changelog and not added:
        problems.append("add what changed for users or admins under '## Unreleased' in CHANGELOG.md, or, when nothing "
                        "changes for either, a line in the pull request's description: 'No changelog: <why>'")
    return problems


def check_numbered(base, head, number, no_changelog, released):
    """A release (Unreleased renamed) or a fix into staging (a new section): exactly one new numbered section, the next
    number, and no Unreleased left."""
    problems = []
    for name in FILES:
        _, unreleased, sections = parse(head[name])
        old = parse(base[name])[2]
        if unreleased is not None:
            problems.append("%s: a build sent to testers has no Unreleased section" % name)
        if set(sections) - set(old) != {number} or any(sections.get(v) != s for v, s in old.items()):
            problems.append("%s: the only change is a new section for the next build, %s" % (name, text(number)))
    if released:
        problems += check_release_body(base, head, number)
    elif not no_changelog and not released_lines({number: parse(head["CHANGELOG.md"])[2].get(number, "")}):
        problems.append("a fix into staging says what changed in its section, or its description says 'No changelog: <why>'")
    return problems


def check_release_body(base, head, number):
    """A release keeps Unreleased word for word (the security line may be added)."""
    problems = []
    for name in FILES:
        sections = parse(head[name])[2]
        if number not in sections:
            continue
        heading = re.match(r"## \S+ \((\d{4}-\d{2}-\d{2})\)(?:, with (\S+) to )?", sections[number])
        day = heading.group(1) if heading else ""
        since = parse_version(heading.group(2)) if heading and heading.group(2) else None
        expected = (release(base[name], number, day, included_from=since),
                    release(base[name], number, day, SECURITY_LINE, since))
        if head[name].replace("\r\n", "\n") not in expected:
            problems.append("%s: the new section is Unreleased as it was, renamed %s (YYYY-MM-DD), the changelog's "
                            "with the goals' builds it includes" % (name, text(number)))
    return problems


def check_merge_back(base, upper, head):
    """Nothing lost: every numbered section of both branches, as it reads there (an approved rewrite as the lower
    branch has it); Unreleased only lines from either."""
    problems = []
    for name in FILES:
        _, unreleased, sections = parse(head[name])
        lower = parse(base[name])[2]
        for side in (base, upper):
            for version, section in parse(side[name])[2].items():
                rewritten_below = version in lower and rewrite_on_lower(version, lower[version], section)
                if sections.get(version) != (lower[version] if rewritten_below else section):
                    problems.append("%s: %s is missing or changed; a merge-back keeps every version" % (name, text(version)))
        known = lines_of(parse(base[name])[1]) | lines_of(parse(upper[name])[1])
        if lines_of(unreleased) - known or lines_of(unreleased) & released_lines(newly_released(parse(base[name])[2], sections)):
            problems.append("%s: Unreleased keeps only the lines of both branches that no version has yet" % name)
    return sorted(set(problems))


def check(kind, base, head, upper=None, number=None, no_changelog=False, tested=None, rewrites=frozenset()):
    if kind == "change":
        return check_change(base, head, no_changelog, tested, rewrites)
    if kind in ("release", "staging-fix"):
        return check_numbered(base, head, number, no_changelog, kind == "release")
    if kind == "merge-back":
        return check_merge_back(base, upper, head)
    raise ValueError("unknown kind %r" % kind)


# ---- self-test --------------------------------------------------------------------------------------------------

def self_test():
    log = lambda body: "# Changelog\n\nIntro.\n\n" + body
    ticks = lambda body: "# QA checklist\n\nIntro.\n\n" + body
    old = "## 0.6.0 (2026-11-13)\n\n### For users\n- **Cards:** clearer.\n\n## 0.5.6 (2026-11-13)\n"
    old_list = "## 0.6.0 (2026-11-13)\n- **Cards:** look. You should see them.\n\n## 0.5.6 (2026-11-13)\n"
    base = {"CHANGELOG.md": log(old), "QA-CHECKLIST.md": ticks(old_list)}
    pending = "## Unreleased\n\n### For users\n- **Numbers:** builds have one.\n\n"
    change = {"CHANGELOG.md": log(pending + old), "QA-CHECKLIST.md": ticks("## Unreleased\n- **Numbers:** look. You should see one.\n\n" + old_list)}
    first = (0, 7, 4)
    released = {name: release(change[name], first, "2026-11-20") for name in FILES}
    staged_fix = {"CHANGELOG.md": released["CHANGELOG.md"].replace("## 0.7.0004", "## 0.7.0005 (2026-11-21)\n\n### For admins\n- **Fix:** it works.\n\n## 0.7.0004"),
                  "QA-CHECKLIST.md": released["QA-CHECKLIST.md"].replace("## 0.7.0004", "## 0.7.0005 (2026-11-21)\n\n## 0.7.0004")}
    folded = {"CHANGELOG.md": log("## Unreleased\n\n### For users\n- **Cards:** clearer, and numbered.\n\n## 0.5.6 (2026-11-13)\n"),
              "QA-CHECKLIST.md": ticks("## Unreleased\n- **Cards:** look. You should see them numbered.\n\n## 0.5.6 (2026-11-13)\n")}
    newer_dev = log("## Unreleased\n\n### For users\n- **Numbers:** builds have one.\n- **Later:** after QA.\n\n" + old)
    reworded = {**base, "CHANGELOG.md": base["CHANGELOG.md"].replace("clearer", "better")}
    back = merge(newer_dev, released["CHANGELOG.md"])
    approved = {name: document.replace("0.7.0004", "0.7.0010") for name, document in released.items()}
    rewritten_dev = {**approved, "CHANGELOG.md": approved["CHANGELOG.md"].replace("builds have one", "every build has one")}
    tags = ["v0.3.1", "v0.7.0004", "v0.7.0005", "test-org-move"]
    cases = [
        (next_number(set(), set()) == (0, 7, 4), "the first build under build numbers is 0.7.0004 (three were sent before)"),
        (next_number(built(tags), set()) == (0, 7, 6), "every build sent takes the next number"),
        (next_number(built(tags), {(0, 7, 5)}) == (0, 8, 6), "once the newest build ships, the next one is a minor up"),
        (next_number({(0, 7, 4), (0, 8, 5)}, {(0, 7, 4)}) == (0, 8, 6), "an older minor shipping doesn't move the minor again"),
        (next_number({(0, 7, 11), (0, 7, 12), (0, 7, 13)}, {(0, 7, 11)}) == (0, 8, 14),
         "goals numbered while a build waited in staging don't stop the minor rolling up once it ships"),
        (next_number(built(tags), set(), major=True) == (1, 0, 1), "a major: minor to 0, the count starts again"),
        (next_number({(1, 0, 1)}, set()) == (1, 0, 2), "after a major the count goes on from there"),
        (text((0, 7, 4)) == "0.7.0004" and text((0, 6, 0)) == "0.6.0" and text((0, 7, 12345)) == "0.7.12345",
         "people see four digits from 0.7 on, the old numbers as they were"),
        (check("change", base, change) == [], "a normal pull request: lines under Unreleased"),
        (check("change", base, base, no_changelog=True) == [], "a pull request nobody would notice says so instead"),
        (bool(check("change", base, base)), "no lines and nothing said fails"),
        (bool(check("change", base, change, no_changelog=True)), "'No changelog' with lines fails"),
        (bool(check("change", base, {**change, "CHANGELOG.md": change["CHANGELOG.md"].replace("clearer", "better")})),
         "changing a numbered version fails"),
        (check("change", base, reworded, no_changelog=True, rewrites={(0, 6, 0)}) == [],
         "a rewrite the owner approved changes that version's lines"),
        (bool(check("change", base, reworded, no_changelog=True, rewrites={(0, 5, 6)})),
         "an approved rewrite of one version doesn't open another"),
        (bool(check("change", base, {**base, "CHANGELOG.md": base["CHANGELOG.md"].replace("(2026-11-13)\n\n### For users", "(2026-11-14)\n\n### For users")},
                    no_changelog=True, rewrites={(0, 6, 0)})), "an approved rewrite keeps the version's heading"),
        ("every build has one" in merge(rewritten_dev["CHANGELOG.md"], approved["CHANGELOG.md"]),
         "a merge-back keeps the lower branch's words for a rewrite the owner approved"),
        (check("merge-back", rewritten_dev, rewritten_dev, approved) == [], "and its check passes"),
        (bool(check("merge-back", rewritten_dev, approved, approved)), "a merge-back that loses the rewrite fails"),
        (bool(check("change", base, {**base, "QA-CHECKLIST.md": base["QA-CHECKLIST.md"].replace("look", "see")}, no_changelog=True,
                    rewrites={(0, 6, 0)})), "nor does it open the QA checklist's numbered version"),
        (check("change", change, {**change, "CHANGELOG.md": newer_dev}) == [], "a fix into QA adds to QA's Unreleased"),
        (check("change", base, folded, tested={(0, 5, 6)}) == [], "a version testers never had folds into Unreleased"),
        (bool(check("change", base, folded, tested={(0, 6, 0)})), "a version testers have can't be folded"),
        (bool(check("change", base, folded)), "nor any version when staging can't be read"),
        (bool(check("change", base, {**change, "CHANGELOG.md": log(pending + "- **Numbers:** again.\n\n" + old)})),
         "two lines with the same lead under Unreleased fail: the one line is rewritten"),
        ("## 0.7.0004 (2026-11-20)\n\n### For users\n- **Numbers:** builds have one.\n\n## 0.6.0" in released["CHANGELOG.md"]
         and "## Unreleased" not in released["CHANGELOG.md"], "the staging promotion turns Unreleased into the number"),
        ("## 0.7.0004 (2026-11-20)\n- **Numbers:**" in released["QA-CHECKLIST.md"], "the checklist's Unreleased too"),
        ("## 0.7.0004 (2026-11-20)\n\n## 0.6.0" in release(base["CHANGELOG.md"], first, "2026-11-20"),
         "a build with nothing new is its heading only"),
        (SECURITY_LINE in release(change["CHANGELOG.md"], first, "d", SECURITY_LINE).split("## 0.6.0")[0],
         "a security fix in the build tells admins"),
        (check("release", change, released, number=first) == [], "the release pull request passes"),
        (bool(check("release", change, released, number=(0, 7, 5))), "a release with the wrong number fails"),
        (bool(check("release", change, {**released, "CHANGELOG.md": released["CHANGELOG.md"].replace("builds have one", "x")},
                    number=first)), "a release that rewrites the lines fails"),
        (check("staging-fix", released, staged_fix, number=(0, 7, 5)) == [], "a fix into staging takes the next number"),
        (bool(check("staging-fix", released, staged_fix, number=(0, 7, 6))), "a staging fix with another number fails"),
        (bool(check("staging-fix", released, change, number=(0, 7, 5))), "a staging fix under Unreleased fails"),
        ("## Unreleased\n\n### For users\n- **Later:** after QA.\n\n## 0.7.0004" in back, "a merge-back keeps only new lines in Unreleased"),
        (check("merge-back", {**base, "CHANGELOG.md": newer_dev}, {**released, "CHANGELOG.md": back}, released) == [],
         "the merge-back passes its check"),
        (bool(check("merge-back", base, base, released)), "a merge-back that loses a version fails"),
        (merge(released["CHANGELOG.md"], staged_fix["CHANGELOG.md"]) == staged_fix["CHANGELOG.md"], "a staging fix merges down whole"),
        ("- **Cards:** clearer." in merge(log("## Unreleased\n\n### For users\n- **Cards:** clearer.\n\n" + old), released["CHANGELOG.md"]).split("## 0.7.0004")[0],
         "a merge-back keeps an Unreleased line that only an older build has the same words as"),
        (upgrade_needs_admins(log("## Unreleased\n\n### For admins\n- **Before you upgrade:** move it.\n\n")), "an upgrade admins must act on"),
        (render(*parse(change["CHANGELOG.md"])) == change["CHANGELOG.md"], "a file reads back as it was"),
    ]
    with_goals = release(change["CHANGELOG.md"], (0, 7, 11), "2026-12-01", included_from=(0, 7, 5))
    cases += [
        ("## 0.7.0011 (2026-12-01), with 0.7.0005 to 0.7.0011\n" in with_goals,
         "the staging heading names the goals' builds it includes"),
        ("## 0.7.0004 (2026-11-20)\n" in release(change["CHANGELOG.md"], first, "2026-11-20", included_from=first),
         "a build of one goal names only itself"),
        (check("release", change, {"CHANGELOG.md": with_goals, "QA-CHECKLIST.md": release(change["QA-CHECKLIST.md"], (0, 7, 11), "2026-12-01")},
               number=(0, 7, 11)) == [],
         "a release heading with the goals' builds passes"),
        (label("dev", "abc1234567", {"v0.7.0005": "abc1234567"}, (0, 7, 6)) == "0.7.0005-dev",
         "a numbered dev commit shows its number"),
        (label("qa", "abc1234567", {"v0.7.0005": "abc1234567"}, (0, 7, 6)) == "0.7.0005-qa", "and QA the same number"),
        (label("dev", "def5678901", {"v0.7.0005": "abc1234567"}, (0, 7, 6)) == "0.7.0006-dev.def5678",
         "a dev build between goals shows the next number and its commit"),
        (label("qa", "def5678901", {"v0.7.0005": "abc1234567"}, (0, 7, 6), (0, 7, 5)) == "0.7.0005-qa",
         "QA on the staging promotion's release commit shows the number it carries"),
    ]
    staging = image_plan("push", "staging", "abc1234", released["CHANGELOG.md"], {}, first)
    rerun = image_plan("push", "staging", "abc1234", released["CHANGELOG.md"], {"v0.7.0004": "abc1234"}, (0, 7, 5))
    ship = image_plan("push", "main", "abc1234", released["CHANGELOG.md"], {"v0.7.0004": "abc1234"}, (0, 8, 5))
    cases += [
        (staging.get("tags") == IMAGE + ":beta," + IMAGE + ":0.7.0004" and staging["record"] == "v0.7.0004"
         and staging["label"] == "0.7.0004", "staging publishes the number and records its tag"),
        (rerun.get("mode") == "build" and rerun["record"] == "", "a re-run of a staging publish keeps its number"),
        ("error" in image_plan("push", "staging", "abc1234", change["CHANGELOG.md"], {}, first), "staging never builds Unreleased"),
        ("error" in image_plan("push", "staging", "def5678", released["CHANGELOG.md"], {"v0.7.0004": "abc1234"}, (0, 7, 5)),
         "a number is never published twice"),
        (image_plan("push", "staging", "def5678", staged_fix["CHANGELOG.md"], {}, first).get("record") == "v0.7.0005",
         "after a publish that failed, the fix's number publishes: a number is taken by its release commit, not its tag"),
        ("error" in image_plan("push", "staging", "def5678", released["CHANGELOG.md"], {"v0.7.0005": "abc1234"}, (0, 7, 6)),
         "numbers only go up"),
        ("error" in image_plan("push", "staging", "def5678", base["CHANGELOG.md"], {}, first),
         "staging never publishes a number from before build numbers"),
        (ship.get("mode") == "retag" and ship["source"] == IMAGE + ":0.7.0004" and ship["tags"] == IMAGE + ":0.7," + IMAGE + ":latest",
         "ship it retags the image staging published, without building"),
        ("error" in image_plan("push", "main", "def5678", released["CHANGELOG.md"], {"v0.7.0004": "abc1234"}, (0, 8, 5)),
         "main only ships a commit staging published"),
        (image_plan("workflow_dispatch", "x", "abc1234567", "", {}, first, "test-a").get("label") == "0.7.0004-test.abc1234",
         "a test publish is labelled, not numbered"),
    ]
    # A goal's dev commit dev1 numbered 0.7.0004; the release commit rel sits right on top of it, on qa and staging.
    after = lambda numbered, sha: (numbered, sha) in {("dev1", "rel"), ("dev1", "dev1")}
    carried = image_plan("push", "staging", "rel", released["CHANGELOG.md"], {"v0.7.0004": "dev1"}, (0, 7, 5), built_from=after)
    cases += [
        (carried.get("mode") == "build" and carried["number"] == "0.7.0004" and carried["record"] == "",
         "staging publishes the number the goal's dev commit took, and adds no tag"),
        ("error" in image_plan("push", "staging", "later", released["CHANGELOG.md"], {"v0.7.0004": "dev1"}, (0, 7, 5), built_from=after),
         "a later commit with a number already published isn't built again under it"),
        (image_plan("push", "main", "rel", released["CHANGELOG.md"], {"v0.7.0004": "dev1"}, (0, 8, 5), built_from=after).get("mode") == "retag",
         "ship it retags the build staging published from the numbered commit"),
        ("error" in image_plan("push", "main", "later", released["CHANGELOG.md"], {"v0.7.0004": "dev1"}, (0, 8, 5), built_from=after),
         "main retags only staging's own build"),
    ]
    checklist_base = "# QA checklist\n\n## Unreleased\n- **Old:** Do it. You should see it.\n  - Checked automatically: the live bug reports check.\n- **Kept:** Do it. You should see it.\n  - Checked automatically: the live bug reports check.\n"
    checklist_fix = checklist_base.replace("- **Old:** Do it. You should see it.\n  - Checked automatically: the live bug reports check.\n",
                                           "- **New:** Do it. You should see it.\n  - Checked by the owner, needs human eyes: how it looks.\n")
    checklist_dev = checklist_base + "- **Dev's:** Do it. You should see it.\n  - Checked automatically: the live passing check.\n"
    merged_checklist = merge(checklist_dev, checklist_fix, checklist_base)
    cases += [
        ("- **Old:**" not in merged_checklist and "- **New:** Do it. You should see it.\n  - Checked by the owner" in merged_checklist,
         "a merge-back keeps the fix's rewrite of an item, not the old one"),
        (merged_checklist.count("  - Checked automatically: the live bug reports check.") == 1 and "- **Dev's:**" in merged_checklist,
         "every item keeps its own Checked by line, even one another item has word for word"),
        (merge(checklist_base, checklist_base).count("Checked automatically: the live bug reports check.") == 2,
         "two items with the same Checked by line both keep it"),
    ]
    try:
        merge(change["CHANGELOG.md"], change["CHANGELOG.md"].replace("clearer", "changed"))
        cases.append((False, "a numbered version that reads differently isn't merged"))
    except ValueError:
        cases.append((True, "a numbered version that reads differently isn't merged"))

    failed = [name for passed, name in cases if not passed]
    if failed:
        print("SELF-TEST FAILED: " + "; ".join(failed))
        return 1
    print("Build numbers self-test passed (%d cases)." % len(cases))
    return 0


# ---- command line -----------------------------------------------------------------------------------------------

def read_dir(path):
    found = {}
    for name in FILES:
        with open(os.path.join(path, name), encoding="utf-8") as handle:
            found[name] = handle.read()
    return found


def write_dir(path, documents):
    for name, document in documents.items():
        with open(os.path.join(path, name), "w", encoding="utf-8", newline="\n") as handle:
            handle.write(document)


def main(argv):
    args = argv[1:]
    if args == ["--self-test"]:
        return self_test()
    if args[:1] == ["next"]:
        changelog = args[args.index("--changelog") + 1] if "--changelog" in args else None
        upgrade = False
        if changelog:
            with open(changelog, encoding="utf-8") as handle:
                upgrade = upgrade_needs_admins(handle.read())
        shipping = parse_version(args[args.index("--shipped") + 1]) if "--shipped" in args else None
        before = args[args.index("--before") + 1] if "--before" in args else None
        upcoming = next_from_git(shipping=shipping, before=before)
        if "--major" in args or (upgrade and upcoming[0] >= 1):
            upcoming = next_from_git(major=True, before=before)
        print(text(upcoming))
        return 0
    if len(args) >= 2 and args[0] == "number" and set(args[2:]) <= {"--major", "--dry-run"}:
        number, taken = take_number(args[1], "--major" in args, "--dry-run" in args)
        if taken and "--dry-run" in args:
            print("Dry run: %s would take %s; nothing tagged." % (args[1], text(number)), file=sys.stderr)
        elif not taken:
            print("%s already has %s." % (args[1], text(number)), file=sys.stderr)
        print(text(number))
        return 0
    if len(args) in (4, 5) and args[0] == "image":
        with open("CHANGELOG.md", encoding="utf-8") as handle:
            plan = image_plan(args[1], args[2], args[3], handle.read(), remote_tags(), next_from_git(),
                              args[4] if len(args) == 5 else "", release_of)
        for key, value in plan.items():
            print("%s=%s" % (key, value))
        return 1 if "error" in plan else 0
    if len(args) == 3 and args[0] == "label":
        sha = git("rev-parse", "--verify", "-q", args[2] + "^{commit}").stdout.strip() or args[2]
        print(label(args[1], sha, remote_tags(), next_from_git(), released_number(sha)))
        return 0
    if len(args) >= 4 and args[0] == "release":
        security = SECURITY_LINE if "--security" in args else None
        since = parse_version(args[args.index("--from") + 1]) if "--from" in args else None
        documents = read_dir(args[1])
        number = parse_version(args[2])
        write_dir(args[1], {"CHANGELOG.md": release(documents["CHANGELOG.md"], number, args[3], security, since),
                            "QA-CHECKLIST.md": release(documents["QA-CHECKLIST.md"], number, args[3])})
        return 0
    if len(args) in (4, 5) and args[0] == "merge":
        ours, theirs = read_dir(args[1]), read_dir(args[2])
        base = read_dir(args[4]) if len(args) == 5 else None
        write_dir(args[3], {name: merge(ours[name], theirs[name], base[name] if base else None) for name in FILES})
        return 0
    if len(args) >= 4 and args[0] == "check":
        flags = args[4:]
        upper = read_dir(flags.pop(0)) if flags and not flags[0].startswith("--") else None
        number = parse_version(flags[flags.index("--number") + 1]) if "--number" in flags else None
        tested = tested_versions() if args[1] == "change" else None
        rewrites = frozenset(parse_version(flags[i + 1]) for i, flag in enumerate(flags) if flag == "--rewrite")
        problems = check(args[1], read_dir(args[2]), read_dir(args[3]), upper, number, "--no-changelog" in flags, tested,
                         rewrites)
        for problem in problems:
            print(("::error::" if os.environ.get("GITHUB_ACTIONS") else "") + problem)
        return 1 if problems else 0
    print(__doc__)
    return 2


if __name__ == "__main__":
    sys.exit(main(sys.argv))
