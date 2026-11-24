#!/usr/bin/env python3
"""The changelog as entries (docs/development.md#changelog): one small file per change under changelog/entries, the
builds under changelog/builds.txt, and CHANGELOG.md generated from them, never edited by hand.

An entry, changelog/entries/<id>.txt, one "name: value" field a line:
    audience: users | admins
    kind: new | fixed | changed
    build: 0.7.0011            (left out until the staging promotion fills it in)
    replaces: <id>, <id>       (earlier entries this one reworks, undoes or replaces; optional)
    line: **Lead:** what changed, for a reader who never had what it replaces
    line_if_seen: ...          (optional: what changed, for a reader who already had a replaced entry)
A plain "name: value" file, not YAML or JSON: nothing to escape, a line reads exactly as written, and both this script
and Requestarr read it in a few lines with no library.

changelog/builds.txt lists every build sent to testers, "version | day | notes": "with 0.7.0005 to 0.7.0011" names the
goals' builds a staging build includes; "flat" shows a build from before kinds as it was written, not grouped.

What readers are shown (fold): an entry another entry replaces is left out for readers who never got it, and the one
replacing it shows alone, as New when the replaced one was New; readers who got it see the replacing entry's
line_if_seen. CHANGELOG.md is written for readers who got every released build, as Requestarr's feeds are for theirs.

    python3 .github/changelog_entries.py write [<repo>]     regenerates CHANGELOG.md from the entries
"""
import os
import re
import sys

AUDIENCES = {"users": "For users", "admins": "For admins"}
KINDS = {"new": "New", "fixed": "Fixed", "changed": "Changed"}
FIELDS = ("audience", "kind", "build", "replaces", "line", "line_if_seen")
ID = re.compile(r"^[a-z0-9][a-z0-9.-]*$")
VERSION = re.compile(r"^\d+\.\d+\.\d+$")
UNRELEASED = "Unreleased"
FLAT = "flat"
INCLUDES = re.compile(r"^with (\d+\.\d+\.\d+) to (\d+\.\d+\.\d+)$")
# Versions from 0.7 on show their build with four digits.
FIRST_BUILD_NUMBERED = (0, 7)


def version_of(value):
    return tuple(int(part) for part in value.split("."))


def shown(version):
    major, minor, build = version
    return "%d.%d.%s" % (major, minor, ("%04d" if (major, minor) >= FIRST_BUILD_NUMBERED else "%d") % build)


class Entry:
    def __init__(self, entry_id, fields):
        self.id = entry_id
        self.audience = fields.get("audience", "")
        self.kind = fields.get("kind", "")
        self.build = version_of(fields["build"]) if VERSION.match(fields.get("build", "")) else None
        self.build_text = fields.get("build", "")
        self.replaces = [r.strip() for r in fields.get("replaces", "").split(",") if r.strip()]
        self.line = fields.get("line", "")
        self.line_if_seen = fields.get("line_if_seen", "")
        self.unknown = [name for name in fields if name not in FIELDS]

    def text(self):
        """The entry file's contents, its fields in their usual order."""
        values = {"audience": self.audience, "kind": self.kind, "build": shown(self.build) if self.build else "",
                  "replaces": ", ".join(self.replaces), "line": self.line, "line_if_seen": self.line_if_seen}
        return "".join("%s: %s\n" % (name, values[name]) for name in FIELDS if values[name])


class Build:
    def __init__(self, version, day, flat=False, included_from=None):
        self.version, self.day, self.flat, self.included_from = version, day, flat, included_from

    def includes(self):
        """The goals' builds this one includes ("with 0.7.0005 to 0.7.0011"), or None."""
        if self.included_from and self.included_from != self.version:
            return "with %s to %s" % (shown(self.included_from), shown(self.version))
        return None

    def heading(self):
        heading, includes = "%s (%s)" % (shown(self.version), self.day), self.includes()
        return "%s, %s" % (heading, includes) if includes else heading

    def row(self):
        notes = [note for note in (self.includes(), FLAT if self.flat else None) if note]
        return " | ".join([shown(self.version), self.day] + notes)


def parse_entry(entry_id, text):
    fields = {}
    for row in text.replace("\r\n", "\n").split("\n"):
        if row.strip():
            name, _, value = row.partition(":")
            fields[name.strip()] = value.strip()
    return Entry(entry_id, fields)


def parse_builds(text):
    """[(line number, Build or None, the row)]: None for a row that isn't one."""
    rows = []
    for number, row in enumerate(text.replace("\r\n", "\n").split("\n"), 1):
        if not row.strip() or row.startswith("#"):
            continue
        parts = [p.strip() for p in row.split("|")]
        build = None
        if len(parts) >= 2 and VERSION.match(parts[0]) and re.fullmatch(r"\d{4}-\d{2}-\d{2}", parts[1]):
            build = Build(version_of(parts[0]), parts[1])
            for note in parts[2:]:
                includes = INCLUDES.match(note)
                if note == FLAT:
                    build.flat = True
                elif includes:
                    build.included_from = version_of(includes.group(1))
                else:
                    build = None
                    break
        rows.append((number, build, row))
    return rows


class Changelog:
    def __init__(self, entries, builds, header):
        self.entries, self.builds, self.header = entries, builds, header

    @staticmethod
    def load(root):
        folder = os.path.join(root, "changelog")
        entries = {}
        entries_dir = os.path.join(folder, "entries")
        for name in sorted(os.listdir(entries_dir)) if os.path.isdir(entries_dir) else []:
            if name.endswith(".txt"):
                with open(os.path.join(entries_dir, name), encoding="utf-8") as handle:
                    entries[name[:-4]] = parse_entry(name[:-4], handle.read())
        builds = []
        if os.path.exists(os.path.join(folder, "builds.txt")):
            with open(os.path.join(folder, "builds.txt"), encoding="utf-8") as handle:
                builds = [b for _, b, _ in parse_builds(handle.read()) if b]
        header = ""
        if os.path.exists(os.path.join(folder, "header.md")):
            with open(os.path.join(folder, "header.md"), encoding="utf-8") as handle:
                header = handle.read()
        return Changelog(entries, builds, header)

    def save(self, root):
        """Writes the entries and builds back (the staging promotion fills in builds), and CHANGELOG.md from them."""
        folder = os.path.join(root, "changelog")
        os.makedirs(os.path.join(folder, "entries"), exist_ok=True)
        for entry in self.entries.values():
            with open(os.path.join(folder, "entries", entry.id + ".txt"), "w", encoding="utf-8", newline="\n") as handle:
                handle.write(entry.text())
        with open(os.path.join(folder, "builds.txt"), encoding="utf-8") as handle:
            comments = [row for row in handle.read().replace("\r\n", "\n").split("\n") if row.startswith("#")]
        rows = comments + [b.row() for b in sorted(self.builds, key=lambda b: b.version, reverse=True)]
        with open(os.path.join(folder, "builds.txt"), "w", encoding="utf-8", newline="\n") as handle:
            handle.write("\n".join(rows) + "\n")
        write_markdown(root, self.render())

    def released(self):
        return {e.id for e in self.entries.values() if e.build}

    def render(self):
        """CHANGELOG.md: Unreleased, then every build newest first, each entry as readers of every released build see it."""
        lines = fold(self.entries, sorted(self.entries.values(), key=lambda e: e.id), self.released())
        out = self.header.replace("\r\n", "\n").rstrip("\n") + "\n\n"
        unreleased = [shown_entry for shown_entry in lines if shown_entry[0].build is None]
        if unreleased:
            out += render_version(UNRELEASED, unreleased, flat=False)
        for build in sorted(self.builds, key=lambda b: b.version, reverse=True):
            out += render_version(build.heading(), [s for s in lines if s[0].build == build.version], build.flat)
        return out.rstrip("\n") + "\n"


def ancestors(entry, entries):
    """Every entry this one replaces, through the whole chain (a loop is reported by the check, not followed)."""
    seen, todo = [], list(entry.replaces)
    while todo:
        entry_id = todo.pop(0)
        if entry_id in seen or entry_id not in entries:
            continue
        seen.append(entry_id)
        todo.extend(entries[entry_id].replaces)
    return seen


def fold(entries, available, posted):
    """[(entry, text, kind)] shown to readers who already got the entries in `posted`, of those `available` to them."""
    replaced = {r for e in available for r in e.replaces}
    shown_entries = []
    for entry in available:
        if entry.id in replaced and entry.id not in posted:
            continue
        before = ancestors(entry, entries)
        if any(b in posted for b in before):
            shown_entries.append((entry, entry.line_if_seen or entry.line, entry.kind))
        else:
            was_new = any(entries[b].kind == "new" for b in before)
            shown_entries.append((entry, entry.line, "new" if was_new else entry.kind))
    return shown_entries


def render_version(heading, shown_entries, flat):
    out = "## %s\n\n" % heading
    for audience, title in AUDIENCES.items():
        mine = [s for s in shown_entries if s[0].audience == audience]
        if not mine:
            continue
        out += "### %s\n" % title
        if flat:
            out += "".join("- %s\n" % text for _, text, _ in mine)
        else:
            for kind, kind_title in KINDS.items():
                rows = [text for _, text, k in mine if k == kind]
                if rows:
                    out += "\n#### %s\n" % kind_title + "".join("- %s\n" % text for text in rows)
        out += "\n"
    return out


def write_markdown(root, text):
    with open(os.path.join(root, "CHANGELOG.md"), "w", encoding="utf-8", newline="\n") as handle:
        handle.write(text)


def release(root, number, day, included_from=None, security_entry=None):
    """The staging promotion: every entry without a build gets this one, the build is listed, and CHANGELOG.md is
    written again. security_entry: (id, line) of the line telling admins a security fix is in this build."""
    changelog = Changelog.load(root)
    if any(b.version == number for b in changelog.builds):
        raise ValueError("%s is already in the changelog" % shown(number))
    if security_entry and security_entry[0] not in changelog.entries:
        changelog.entries[security_entry[0]] = Entry(security_entry[0], {"audience": "admins", "kind": "fixed", "line": security_entry[1]})
    for entry in changelog.entries.values():
        if entry.build is None:
            entry.build = number
    changelog.builds.append(Build(number, day, included_from=included_from))
    changelog.save(root)


def main(argv):
    if argv[1:2] == ["write"]:
        root = argv[2] if len(argv) > 2 else os.path.dirname(os.path.dirname(os.path.abspath(__file__)))
        write_markdown(root, Changelog.load(root).render())
        print("CHANGELOG.md written from %s" % os.path.join(root, "changelog"))
        return 0
    print(__doc__)
    return 2


if __name__ == "__main__":
    sys.exit(main(sys.argv))
