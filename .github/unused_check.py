#!/usr/bin/env python3
"""Unused public and internal members in Requestarr's own C# code (docs/development.md#clean-code). CI's Backend job
runs it; run it locally with `python3 .github/unused_check.py` (after `dotnet tool restore`).

An analyzer looks at one project at a time, so it can't see a public member nobody uses. Roslynator's command line
(`roslynator find-symbol --unused`, Apache-2.0) searches the whole solution. Scope: the code Requestarr wrote, the same
files `.editorconfig`'s "Requestarr additions" holds to the clean-code rules; inherited Servarr code is left as it is.

Set aside, because something other than code reaches them: constructors (dependency injection builds the services,
records their own), controllers (routing) and migrations (the migration runner). What's left fails the build unless
.github/unused-symbols.txt lists it with the reason it stays (written to a database column, sent in an API answer,
read from a file). A listed symbol that's no longer found is reported, so the list doesn't keep stale lines.

    python3 .github/unused_check.py            # runs Roslynator
    python3 .github/unused_check.py --self-test
"""
import os
import re
import subprocess
import sys

HERE = os.path.dirname(os.path.abspath(__file__))
REPO = os.path.dirname(HERE)
BASELINE = os.path.join(HERE, "unused-symbols.txt")

# Requestarr's own code: .editorconfig's "Requestarr additions", relative to src/.
INCLUDE = ["**/Requestarr/**", "NzbDrone.Core/Datastore/Migration/*.cs", "Requestarr.Http/Qa/**", "Requestarr.Http/Portal/**", "Requestarr.Http/Webhooks/**",
           "Requestarr.Http/Frontend/Mappers/VersionLogo.cs", "Requestarr.Http/Frontend/Mappers/VersionLogoMapper.cs",
           "Requestarr.Http/Frontend/Mappers/WebIcons.cs", "NzbDrone.Common/Exceptions/RequestarrStartupException.cs"]
TEST_PROJECTS = ["Requestarr.Core.Test", "Requestarr.Api.V1.Test", "Requestarr.Common.Test", "Requestarr.Host.Test",
                 "Requestarr.Libraries.Test", "Requestarr.Mono.Test", "Requestarr.Update.Test", "Requestarr.Windows.Test",
                 "Requestarr.Integration.Test", "Requestarr.Automation.Test", "Requestarr.Benchmark.Test",
                 "Requestarr.Test.Common", "Requestarr.Test.Dummy"]
KINDS = ("class", "method", "property", "field", "const", "enum", "interface", "struct", "event", "indexer", "delegate")
FINDING = re.compile(r"^\s+(%s)\s+(\S.*)$" % "|".join(KINDS))


def findings(output):
    """[(kind, symbol)] from Roslynator's normal-verbosity list."""
    return [(m.group(1), m.group(2).strip()) for m in map(FINDING.match, output.splitlines()) if m]


def reached_otherwise(kind, symbol):
    """Constructors, controllers and migrations: injection, routing and the migration runner reach them."""
    call = re.match(r"(?P<type>.*)\.(?P<name>[^.(<]+)(?:<[^>]*>)?\(", symbol)
    if kind == "method" and call and re.sub(r"<.*>$", "", call.group("type").split(".")[-1]) == call.group("name"):
        return True
    owner = call.group("type") if call else symbol
    return owner.split(".")[-1].endswith("Controller") or ".Datastore.Migration." in symbol


def baseline(text):
    """{symbol: reason} from the list: one symbol a line, "  # the reason it stays" after it."""
    entries = {}
    for line in text.splitlines():
        if line.strip() and not line.startswith("#"):
            symbol, _, reason = line.partition("  # ")
            entries[symbol.strip()] = reason.strip()
    return entries


def compare(found, kept):
    """(new findings, listed symbols no longer found, listed symbols without a reason)."""
    symbols = [s for k, s in found if not reached_otherwise(k, s)]
    return [s for s in symbols if s not in kept], sorted(set(kept) - set(symbols)), sorted(s for s, r in kept.items() if not r)


def run_roslynator():
    command = ["dotnet", "roslynator", "find-symbol", os.path.join("src", "Requestarr.sln"), "--unused", "--visibility", "public", "internal",
               "--ignored-projects"] + TEST_PROJECTS + ["--include"] + INCLUDE + ["-v", "n"]
    result = subprocess.run(command, cwd=REPO, capture_output=True, text=True)
    if result.returncode != 0 or not re.search(r"\d+ symbols? found", result.stdout):
        sys.exit("Roslynator didn't run (exit %d):\n%s%s" % (result.returncode, result.stdout[-2000:], result.stderr[-2000:]))
    return result.stdout


def main(argv):
    if argv[1:] == ["--self-test"]:
        return self_test()
    with open(BASELINE, encoding="utf-8") as handle:
        kept = baseline(handle.read())
    new, stale, unexplained = compare(findings(run_roslynator()), kept)
    for symbol in new:
        print("unused: %s (remove it, or list it in .github/unused-symbols.txt with the reason it stays)" % symbol)
    for symbol in stale:
        print("no longer unused: %s (remove its line from .github/unused-symbols.txt)" % symbol)
    for symbol in unexplained:
        print("no reason given: %s" % symbol)
    if new or stale or unexplained:
        print("%d unused, %d stale and %d unexplained line(s): see docs/development.md#clean-code." % (len(new), len(stale), len(unexplained)))
        return 1
    print("No unused public or internal members in Requestarr's code beyond the %d listed with their reasons." % len(kept))
    return 0


SAMPLE = """Analyze solution 'src/Requestarr.sln'
    method   Requestarr.Core.Access.AccessSettings.AccessSettings(IConfigService config)
    method   Requestarr.Core.Requests.LookupOutcome<T>.LookupOutcome(T Value, string Error)
    class    Requestarr.Http.Qa.QaChecklistController
    class    NzbDrone.Core.Datastore.Migration.qa_automated_results
    property Requestarr.Core.Requests.Request.Isbn
    method   Requestarr.Core.Requests.RequestCards.Outcome(ServiceResult result)
    method   Requestarr.Core.Qa.QaRunner.Use(IFooController controller)

2 symbols found
"""


def self_test():
    kept = baseline("# header\nRequestarr.Core.Requests.Request.Isbn  # a database column\nRequestarr.Core.Gone.Old  # was used\nRequestarr.Core.X.Y\n")
    new, stale, unexplained = compare(findings(SAMPLE), kept)
    cases = [
        ("seven findings read", len(findings(SAMPLE)) == 7),
        ("constructors, generic ones too, a controller and a migration set aside", new[:1] == ["Requestarr.Core.Requests.RequestCards.Outcome(ServiceResult result)"]),
        ("a controller only as a parameter type isn't set aside", new[1:] == ["Requestarr.Core.Qa.QaRunner.Use(IFooController controller)"]),
        ("a listed symbol no longer found is stale", "Requestarr.Core.Gone.Old" in stale),
        ("a listed symbol needs its reason", unexplained == ["Requestarr.Core.X.Y"]),
        ("a listed finding with its reason passes", "Requestarr.Core.Requests.Request.Isbn" not in new),
    ]
    wrong = [name for name, ok in cases if not ok]
    for name in wrong:
        print("unused_check self-test: wrong for %s" % name, file=sys.stderr)
    print("unused_check self-test: %d of %d cases right" % (len(cases) - len(wrong), len(cases)))
    return 1 if wrong else 0


if __name__ == "__main__":
    sys.exit(main(sys.argv))
