#!/usr/bin/env python3
"""Fails when a string users or admins read holds a fixed list of app names ("Sonarr and Radarr", "Sonarr or Radarr",
"Sonarr/Radarr"...). Such lists go stale the moment Lidarr or a second instance is added, and they're wrong when only
one app is connected: build the words from the connected instances, or say "every connection" or "the app that
downloads it" (the head rule "App names in wording", docs/development.md#app-names).

Covers the translations (en.json), the string literals in Requestarr's own C# code (comments, log lines and tests
left out: they explain the design to developers), the web app's string literals and JSX text, and the changelog's
Unreleased lines (posted in Discord). An intended case goes in .github/app-names-allowed.txt as
"path: the exact text  # why".

Usage: python3 .github/app_names_check.py [--self-test]"""
import json
import re
import sys
from pathlib import Path

ROOT = Path(__file__).resolve().parent.parent
ALLOWED = ROOT / ".github" / "app-names-allowed.txt"
APPS = r"(?:Sonarr|Radarr|Lidarr|Readarr|Mylar3?|LazyLibrarian)(?:'s)?"
LIST = re.compile(rf"\b{APPS}(?:\s*(?:,|/|&|\band\b|\bor\b)\s*(?:the\s+)?{APPS})+")

CSHARP_DIRS = ["src/NzbDrone.Core/Requestarr", "src/Requestarr.Http", "src/Requestarr.Api.V1"]
WEB_DIR = "frontend/src"
WEB_SUFFIXES = (".ts", ".tsx", ".js")
TRANSLATIONS = "src/NzbDrone.Core/Localization/Core/en.json"
CHANGELOG = "CHANGELOG.md"

# A line with words users read: a string, a template or JSX text.
QUOTES = re.compile(r"[\"'`]|>[^<>{}]*[A-Za-z]")
LOG_LINE = re.compile(r"\b(?:_logger|_log|logger|Logger)\.\w+\(|\bconsole\.\w+\(")


def code_part(line):
    """The line without a trailing // comment (a // inside a string, as in a URL, is kept)."""
    quote = None
    escaped = False
    for i, ch in enumerate(line):
        if quote:
            if escaped:
                escaped = False
            elif ch == "\\":
                escaped = True
            elif ch == quote:
                quote = None
        elif ch in "\"'`":
            quote = ch
        elif line.startswith("//", i):
            return line[:i]
    return line


def is_comment(line):
    stripped = line.strip()
    return stripped.startswith(("//", "/*", "*"))


def findings_in_code(text):
    for number, line in enumerate(text.splitlines(), 1):
        if is_comment(line) or LOG_LINE.search(line):
            continue
        code = code_part(line)
        if not QUOTES.search(code):
            continue
        # The whole line, not literal by literal: an interpolated string holds other strings ($"{a ?? "b"}"). Code
        # itself never spells a list in words, and a member access (Implementations.Sonarr) is skipped.
        for match in LIST.finditer(code):
            if match.start() == 0 or code[match.start() - 1] not in "._":
                yield number, match.group(0), code.strip()


def findings_in_translations(text):
    for key, value in json.loads(text).items():
        for match in LIST.finditer(value):
            yield key, match.group(0), value


def findings_in_unreleased(text):
    """The changelog's Unreleased lines: Discord posts them. Numbered versions are never edited, so they're left out."""
    inside = False
    for number, line in enumerate(text.splitlines(), 1):
        if line.startswith("## "):
            inside = line.strip() == "## Unreleased"
            continue
        if inside:
            for match in LIST.finditer(line):
                yield number, match.group(0), line.strip()


def load_allowed():
    allowed = set()
    if ALLOWED.exists():
        for raw in ALLOWED.read_text(encoding="utf-8").splitlines():
            entry = raw.split("  #", 1)[0].strip()
            if entry and not entry.startswith("#"):
                allowed.add(entry)
    return allowed


def code_files():
    """Requestarr's own C# files, then the web app's source files without its tests."""
    for directory in CSHARP_DIRS:
        yield from sorted((ROOT / directory).rglob("*.cs"))
    for path in sorted((ROOT / WEB_DIR).rglob("*")):
        if path.suffix in WEB_SUFFIXES and ".test." not in path.name:
            yield path


def read(rel):
    return (ROOT / rel).read_text(encoding="utf-8")


def scan():
    """Every finding as (path, line number or translation key, the listed names, the text they're in)."""
    found = []
    for path in code_files():
        rel = path.relative_to(ROOT).as_posix()
        found += [(rel, *finding) for finding in findings_in_code(read(rel))]
    found += [(TRANSLATIONS, *finding) for finding in findings_in_translations(read(TRANSLATIONS))]
    found += [(CHANGELOG, *finding) for finding in findings_in_unreleased(read(CHANGELOG))]
    return found


def allow_entry(finding):
    """The line in the allow file that would let this finding through."""
    path, _, words, _ = finding
    return f"{path}: {words}"


def main():
    allowed = load_allowed()
    found = scan()
    problems = [f for f in found if allow_entry(f) not in allowed]
    stale = sorted(allowed - {allow_entry(f) for f in found})

    for path, where, words, text in problems:
        print(f"{path}:{where}: \"{words}\" is a fixed list of app names in: {text[:140]}")
    for entry in stale:
        print(f"{ALLOWED.relative_to(ROOT).as_posix()}: \"{entry}\" is allowed but no longer found: remove the line")

    if problems or stale:
        print(f"{len(problems)} fixed list(s), {len(stale)} stale allow line(s): see docs/development.md#app-names.")
        return 1

    print(f"No fixed lists of app names in what users and admins read ({len(allowed)} allowed with a reason).")
    return 0


def self_test():
    cases = [
        ('"Every Sonarr and Radarr passed."', True),
        ('"Nothing in Sonarr or Radarr matches."', True),
        ('"Sonarr/Radarr"', True),
        ('"in Sonarr\'s or Radarr\'s list"', True),
        ('"Sonarr, Radarr or Lidarr instances"', True),
        ('$"{instance ?? "Sonarr or Radarr"} refused"', True),
        ('new[] { Implementations.Sonarr, Implementations.Radarr }', False),
        ('"a \\" b // Sonarr and Radarr"', True),
        ('"Open in Sonarr"', False),
        ('"Sonarr (Anime) failed: no answer"', False),
        ('"Every connection passed its test."', False),
    ]
    right = 0
    for literal, expected in cases:
        got = any(findings_in_code(f"var a = {literal};"))
        right += got == expected
        if got != expected:
            print(f"wrong: {literal} expected {expected}")
    comment = not any(findings_in_code("// Sonarr and Radarr explain the design"))
    logged = not any(findings_in_code('_logger.Warn("Sonarr and Radarr down");'))
    jsx = any(findings_in_code("<p>Add Sonarr or Radarr</p>"))
    translated = any(findings_in_translations('{"A": "Every Sonarr and Radarr"}'))
    sample = "\n".join(["## Unreleased", "- Sonarr or Radarr", "## 0.7.0001", "- Sonarr and Radarr"])
    changelog = [n for n, _, _ in findings_in_unreleased(sample)]
    extra = [comment, logged, jsx, translated, changelog == [2]]
    right += sum(extra)
    total = len(cases) + len(extra)
    print(f"app_names_check self-test: {right} of {total} cases right")
    return 0 if right == total else 1


if __name__ == "__main__":
    sys.exit(self_test() if sys.argv[1:2] == ["--self-test"] else main())
