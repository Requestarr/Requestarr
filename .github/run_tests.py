#!/usr/bin/env python3
"""Runs the tests GitHub's Build workflow runs (docs/development.md#continuous-integration), after building them.

    python3 .github/run_tests.py own     # a pull request into dev: Requestarr's own tests (its Core folders, the API)
    python3 .github/run_tests.py full    # a promotion, a run by hand, a Dependabot update: every test project, on Linux
    python3 .github/run_tests.py --self-test

The Core tests take most of the time, so they run as several test processes side by side, each in its own copy of the
test folder (Core's tests share a database file there). A new Core folder is Requestarr's own until it's listed as inherited.
"""
import os
import re
import shutil
import subprocess
import sys
from concurrent.futures import ThreadPoolExecutor

REPO = os.path.dirname(os.path.dirname(os.path.abspath(__file__)))
BUILT = os.path.join(REPO, "_tests", "net10.0")
# Beside BUILT: some tests find the source by how deep their folder is, and skip themselves when they can't.
COPY = os.path.join(REPO, "_tests", "core-%d")

# Tests only a person or another platform runs.
ALWAYS = "Category!=ManualTest&Category!=IntegrationTest&Category!=AutomationTest&Category!=WINDOWS"
CORE = "Requestarr.Core.Test.dll"
API = "Requestarr.Api.V1.Test.dll"
INHERITED_ASSEMBLIES = ["Requestarr.Common.Test.dll", "Requestarr.Host.Test.dll", "Requestarr.Mono.Test.dll"]

# Core's folders of Servarr's own tests; Datastore also has Requestarr's, named for the migrations they test.
INHERITED_FOLDERS = ["Caching", "Configuration", "HealthChecks", "Housekeeping", "Http", "Instrumentation", "Localization",
                     "Messaging", "ThingiProvider"]
DATASTORE = "Datastore"
OWN_IN_DATASTORE = ["MigrationFixture", "InitialSchemaFixture"]
# Requestarr's slowest Core folders, split so the side-by-side runs take about as long as each other; every other folder
# is in one more run.
SLOW_FOLDERS = [["Requests", "Admin"], ["Welcome", "Discord", "Removals"]]

COUNTS = re.compile(r"Failed:\s+(\d+), Passed:\s+(\d+), Skipped:\s+(\d+)")


def named(part):
    return "FullyQualifiedName~" + part


def not_named(part):
    return "FullyQualifiedName!~" + part


def folder(name):
    return "Requestarr.Core.Test.%s." % name


def any_of(filters):
    return "(%s)" % "|".join(filters)


def all_of(filters):
    return "(%s)" % "&".join(filters)


def core_runs():
    """The filters of Core's runs: (Requestarr's own, split up; the inherited folders). Together they are every Core test
    once."""
    inherited = any_of([named(folder(f)) for f in INHERITED_FOLDERS]
                       + [all_of([named(folder(DATASTORE))] + [not_named(f) for f in OWN_IN_DATASTORE])])
    others = all_of([not_named(folder(f)) for f in INHERITED_FOLDERS + sum(SLOW_FOLDERS, [])]
                    + [any_of([not_named(folder(DATASTORE))] + [named(f) for f in OWN_IN_DATASTORE])])
    return [any_of([named(folder(f)) for f in group]) for group in SLOW_FOLDERS] + [others], inherited


def runs(suite):
    """(assembly, filter) for each test process."""
    own, inherited = core_runs()
    found = [(CORE, "%s&%s" % (ALWAYS, part)) for part in own] + [(API, ALWAYS)]
    if suite == "full":
        found += [(CORE, "%s&%s" % (ALWAYS, inherited))] + [(assembly, ALWAYS) for assembly in INHERITED_ASSEMBLIES]
    return found


def run_one(number, assembly, test_filter):
    tests = BUILT
    if assembly == CORE:
        tests = COPY % number
        shutil.rmtree(tests, ignore_errors=True)
        shutil.copytree(BUILT, tests)
    result = subprocess.run(["dotnet", "test", assembly, "--filter", test_filter], cwd=tests, stdout=subprocess.PIPE,
                            stderr=subprocess.STDOUT, text=True)
    print(result.stdout, flush=True)
    return result.returncode, result.stdout


def totals(outputs):
    """(failed, passed, skipped) over every run's summary line."""
    found = [[int(n) for n in row] for output in outputs for row in COUNTS.findall(output)]
    return tuple(sum(row[i] for row in found) for i in range(3))


def ran(code, output):
    """dotnet test calls a filter that matches no test a success: a run must have passed something."""
    return code == 0 and totals([output])[1] > 0


def report(line):
    print(line)
    if os.environ.get("GITHUB_STEP_SUMMARY"):
        with open(os.environ["GITHUB_STEP_SUMMARY"], "a", encoding="utf-8") as summary:
            summary.write(line + "\n")


def run(suite):
    planned = runs(suite)
    with ThreadPoolExecutor(os.cpu_count()) as pool:
        results = list(pool.map(lambda numbered: run_one(numbered[0], *numbered[1]), enumerate(planned)))
    report("Tests (%s): %d failed, %d passed, %d skipped" % ((suite,) + totals([output for _, output in results])))
    wrong = ["%s --filter \"%s\"" % planned[i] for i, result in enumerate(results) if not ran(*result)]
    for name in wrong:
        print("::error::Failed, or no test ran: " + name)
    return 1 if wrong else 0


def self_test():
    passed = "Passed!  - Failed:     0, Passed:   682, Skipped:    14, Total:   696 - Requestarr.Common.Test.dll"
    own = runs("own")
    full = runs("full")
    cases = [
        ("a pull request runs Core and the API only", {assembly for assembly, _ in own} == {CORE, API}),
        ("the full suite runs every test project", {assembly for assembly, _ in full} == {CORE, API, *INHERITED_ASSEMBLIES}),
        ("the full suite has every run a pull request has", full[:len(own)] == own),
        ("every run leaves out what a person runs", all(test_filter.startswith(ALWAYS) for _, test_filter in full)),
        ("the totals add up", totals([passed, passed.replace("Failed:     0", "Failed:     1")]) == (1, 1364, 28)),
        ("a run that passed", ran(0, passed)),
        ("a failed run isn't one", not ran(1, passed)),
        ("nor is a run whose filter matched nothing", not ran(0, "No test matches the given testcase filter")),
    ]
    wrong = [name for name, right in cases if not right]
    for name in wrong:
        print("run_tests self-test: wrong for " + name, file=sys.stderr)
    print("run_tests self-test: %d of %d cases right" % (len(cases) - len(wrong), len(cases)))
    return 1 if wrong else 0


def main(args):
    if args == ["--self-test"]:
        return self_test()
    if args in (["own"], ["full"]):
        return run(args[0])
    print(__doc__)
    return 2


if __name__ == "__main__":
    sys.exit(main(sys.argv[1:]))
