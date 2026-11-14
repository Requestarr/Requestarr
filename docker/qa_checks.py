#!/usr/bin/env python3
"""Claude Code's QA checks: the QA checklist items a script can check, run on every promotion to QA, on the commit QA
receives, without touching the QA server (docs/development.md#qa-checklist).

    python3 docker/qa_checks.py run [--commit SHA] [--out DIR] [--only web|live]
    python3 docker/qa_checks.py record --stack qa|dev [--out DIR] [--fail "Item name=what is wrong"]...
    python3 docker/qa_checks.py --self-test

run: the checkout must be at the commit QA receives (the tests are built from it). It builds the solution, brings the
     dev stack up at that commit with sign-in required and a throwaway login, runs the web app checks in a real browser
     (headless Edge or Chrome, light and dark theme) against it, then stops it and runs the live Discord checks on the
     dev server (the bot and the test member, as every live check does), and finally resets the dev stack. Each item
     Claude Code checks gets its result, what the check read back and its screenshots, in DIR (default
     _qa-checks/<short commit>). Nothing here reads or changes the QA server, its data or its bot.
record: after Claude Code has looked at every screenshot against the item's "You should see" (a screenshot that
     doesn't match is recorded failing with --fail), posts each result to the stack's checklist: "Passed by Claude
     Code" with its evidence, or failing, which files a bug report like an admin's Failing. Only the stack running the
     commit that was checked takes the results.

The checks each item may name are CHECKS below ("Checked by Claude Code: the <name> check."); .github/checklist_check.py
refuses an item naming any other.
"""
import base64
import json
import os
import platform
import re
import secrets
import subprocess
import sys
import time
import urllib.error
import urllib.request

HERE = os.path.dirname(os.path.abspath(__file__))
REPO = os.path.dirname(HERE)
TESTS = os.path.join(REPO, "_tests", "net10.0", "Requestarr.Core.Test.dll")
WEB_FIXTURE = "QaWebChecksFixture"
ACCOUNT = "qa-checks"


class Check:
    """A web check is one test of QaWebChecksFixture (it writes its own result and screenshots). A live check is one
    of the live fixtures; its evidence is the read-back lines it logs under the given tags."""

    def __init__(self, kind, target, component, tags=()):
        self.kind, self.target, self.component, self.tags = kind, target, component, tags


CHECKS = {
    "web app status page": Check("web", "status_page", "web-app"),
    "web app header": Check("web", "header_logo", "web-app"),
    "web app sign-in and sign-out": Check("web", "sign_in_and_out", "web-app"),
    "web app general settings": Check("web", "general_settings", "web-app"),
    "web app sign-in page": Check("web", "sign_in_page", "web-app"),
    "web app loading screen": Check("web", "loading_screen", "web-app"),
    "web app project links": Check("web", "project_links", "web-app"),
    "live update cards": Check("live", "LiveAnnouncementsFixture", "admin", ("[qa]",)),
    "live Read changes": Check("live", "LiveWelcomeFixture", "admin", ("[cards]",)),
    "live access requests": Check("live", "LiveWelcomeFixture", "access", ("[spam]", "[block]", "[unblock]")),
    "live checklist channel": Check("live", "LiveQaFixture", "other", ("[channels]", "[checklist]")),
    "live passing": Check("live", "LiveQaFixture", "other", ("[pass]", "[complete]")),
    "live failing": Check("live", "LiveQaFixture", "other", ("[fail]",)),
    "live carry-over": Check("live", "LiveQaFixture", "other", ("[promotion]",)),
    "live bug reports": Check("live", "LiveQaFixture", "other", ("[reports]", "[issues]")),
    "live report limit": Check("live", "LiveQaFixture", "other", ("[limit]",)),
}

ITEM = re.compile(r"^- \*\*([^*:]+):\*\* (.+)$")
CHECKED_BY = re.compile(r"^- Checked by (?:Claude Code: the (?P<check>.+) check|the owner, (?P<reason>needs human eyes|"
                        r"unsure of the desired behavior|couldn't verify): (?P<why>.+?))\.?$")
UNRELEASED = "## Unreleased"
EVIDENCE_LINES = 25


def checklist_items(text):
    """The Unreleased items with who checks them: [{name, text, check}] (check None for the owner's)."""
    items, inside = [], False
    for line in text.splitlines():
        stripped = line.strip()
        if stripped.startswith("## "):
            inside = stripped == UNRELEASED
            continue
        item = ITEM.match(stripped)
        checked = CHECKED_BY.match(stripped)
        if inside and item:
            items.append({"name": item.group(1).strip(), "text": item.group(2).strip(), "check": None})
        elif inside and checked and items:
            items[-1]["check"] = checked.group("check")
    return items


def live_evidence(log, tags):
    """The lines a live check logged under the tags, which say what it read back from Discord."""
    lines = [line.strip() for line in log.splitlines() if any(line.strip().startswith(tag) for tag in tags)]
    return "\n".join(lines[:EVIDENCE_LINES]) or "The check logged nothing under %s." % ", ".join(tags)


def results_for(items, web, live):
    """Each Claude Code item's result from what the checks left: web {test: {passed, evidence, screenshots}} and live
    {fixture: (passed, log)}. A check that didn't run is a failure that says so."""
    results = []
    for item in [i for i in items if i["check"]]:
        check = CHECKS.get(item["check"])
        if check is None:
            passed, evidence, screenshots = False, "No such check: %s." % item["check"], []
        elif check.kind == "web":
            found = web.get(check.target, {"passed": False, "evidence": "The web check didn't run.", "screenshots": []})
            passed, evidence, screenshots = found["passed"], found["evidence"], found["screenshots"]
        else:
            ran, log = live.get(check.target, (False, ""))
            passed, screenshots = ran, []
            evidence = live_evidence(log, check.tags) if log else "The live check didn't run."
            if not ran:
                evidence = "The live check failed (its log is in the run's live folder).\n" + evidence
        results.append({"name": item["name"], "check": item["check"], "component": check.component if check else "other",
                        "passed": passed, "evidence": evidence, "screenshots": screenshots})
    return results


def secrets_env():
    """docker/secrets.env as environment variables for the checks (read here, never printed)."""
    values = {}
    with open(os.path.join(HERE, "secrets.env"), encoding="utf-8") as handle:
        for line in handle:
            match = re.match(r"^\s*([A-Z0-9_]+)=(.*)$", line.rstrip("\n"))
            if match:
                values[match.group(1)] = match.group(2)
    return values


def stack(command, env=None, ref=None):
    """docker/dev.ps1 (Windows) or docker/dev.sh on the dev stack."""
    if platform.system() == "Windows":
        args = ["powershell", "-NoProfile", "-ExecutionPolicy", "Bypass", "-File", os.path.join(HERE, "dev.ps1"), "-Stack", "dev"]
        args += ["-Ref", ref] if ref else []
    else:
        args = [os.path.join(HERE, "dev.sh"), "--stack", "dev"] + (["--ref", ref] if ref else [])
    subprocess.run(args + command, env=dict(os.environ, **(env or {})), check=True)


def running_build(which):
    """The build a stack's Requestarr runs (0.7.0005-qa.4f2a9c1), from its image's label: read, never changed."""
    return subprocess.run(["docker", "ps", "--filter", "label=com.docker.compose.project=requestarr-" + which,
                           "--filter", "label=com.docker.compose.service=requestarr", "--format", '{{.Label "org.opencontainers.image.version"}}'],
                          capture_output=True, text=True, check=True).stdout.strip()


def git(*args):
    return subprocess.run(["git", "-C", REPO] + list(args), capture_output=True, text=True, check=True).stdout.strip()


def timed(name, timings, body):
    start = time.monotonic()
    try:
        return body()
    finally:
        timings.append((name, time.monotonic() - start))
        print("[qa-checks] %s: %.0fs" % (name, timings[-1][1]), flush=True)


def dotnet_test(filter_, env, log_path):
    """One test run; its output goes to log_path. True when every test passed."""
    with open(log_path, "w", encoding="utf-8") as log:
        code = subprocess.run(["dotnet", "test", TESTS, "--filter", filter_, "--logger", "console;verbosity=detailed"],
                              env=env, stdout=log, stderr=subprocess.STDOUT, cwd=os.path.dirname(TESTS)).returncode
    return code == 0


def run_web(out, env, commit):
    password = secrets.token_urlsafe(24)
    login = {"DEV_AUTH_REQUIRED": "enabled", "REQUESTARR_ADMIN_USERNAME": ACCOUNT, "REQUESTARR_ADMIN_PASSWORD": password}
    stack(["up"], login, ref=commit)
    port = env.get("DEV_REQUESTARR_PORT") or "39797"
    web_env = dict(env, QA_CHECKS_URL="http://localhost:" + port, QA_CHECKS_DIR=os.path.join(out, "web"),
                   QA_CHECKS_USERNAME=ACCOUNT, QA_CHECKS_PASSWORD=password, QA_CHECKS_COMMIT=commit[:7],
                   QA_CHECKS_VERSION=running_build("dev"))
    os.makedirs(web_env["QA_CHECKS_DIR"], exist_ok=True)
    dotnet_test("FullyQualifiedName~" + WEB_FIXTURE, web_env, os.path.join(out, "web", "web.log"))
    results = {}
    for name in os.listdir(web_env["QA_CHECKS_DIR"]):
        if name.endswith(".json"):
            with open(os.path.join(web_env["QA_CHECKS_DIR"], name), encoding="utf-8") as handle:
                results[name[:-5]] = json.load(handle)
    return results


def run_live(out, env):
    stack(["reset-dev", "--stopped"])
    os.makedirs(os.path.join(out, "live"), exist_ok=True)
    live_env = dict(env, LIVE_QA_STATE_DIR=os.path.join(out, "live", "state"))
    results = {}
    for fixture in sorted({c.target for c in CHECKS.values() if c.kind == "live"}):
        log_path = os.path.join(out, "live", fixture + ".log")
        passed = dotnet_test("FullyQualifiedName~" + fixture, live_env, log_path)
        with open(log_path, encoding="utf-8") as handle:
            results[fixture] = (passed, handle.read())
        print("[qa-checks] %s: %s" % (fixture, "passed" if passed else "FAILED"), flush=True)
    return results


def run(args):
    commit = git("rev-parse", option(args, "--commit") or "origin/qa")
    if git("rev-parse", "HEAD") != commit:
        sys.exit("The checkout is at %s, not %s: the checks are built from the checkout. Run git switch --detach %s first."
                 % (git("rev-parse", "--short", "HEAD"), commit[:7], commit[:7]))
    out = os.path.abspath(option(args, "--out") or os.path.join(REPO, "_qa-checks", commit[:7]))
    only = option(args, "--only")
    os.makedirs(out, exist_ok=True)
    env = dict(os.environ, **secrets_env())
    timings, web, live = [], {}, {}
    with open(os.path.join(REPO, "QA-CHECKLIST.md"), encoding="utf-8") as handle:
        items = checklist_items(handle.read())
    try:
        timed("build", timings, lambda: subprocess.run(["dotnet", "build", os.path.join(REPO, "src", "Requestarr.sln"), "-v", "q", "-nologo"], check=True))
        if only in (None, "web"):
            web = timed("web app checks (dev stack up at the commit, signed in)", timings, lambda: run_web(out, env, commit))
        if only in (None, "live"):
            live = timed("live Discord checks (dev server, bot and test member)", timings, lambda: run_live(out, env))
    finally:
        timed("dev stack reset", timings, lambda: stack(["reset-dev"]))
    results = results_for(items, web, live)
    total = sum(t for _, t in timings)
    with open(os.path.join(out, "results.json"), "w", encoding="utf-8") as handle:
        json.dump({"commit": commit, "results": results, "timings": timings, "seconds": round(total)}, handle, indent=2)
    report(results, out, total)


def report(results, out, total):
    for r in results:
        shots = ", ".join(os.path.basename(s) for s in r["screenshots"]) or "no screenshots"
        print("%s  %-32s the %s check (%s)" % ("PASS" if r["passed"] else "FAIL", r["name"], r["check"], shots))
    passed = sum(1 for r in results if r["passed"])
    print("\n%d of %d of Claude Code's items passed in %.0f minutes. Results: %s" % (passed, len(results), total / 60, os.path.join(out, "results.json")))
    print("Look at every screenshot against its item's \"You should see\", then record them on the stack the commit went to:")
    print('  python3 docker/qa_checks.py record --stack qa [--fail "Item name=what is wrong"]')


def api(url, key, path, body=None):
    data = json.dumps(body).encode() if body is not None else None
    request = urllib.request.Request(url + path, data=data, method="POST" if data else "GET",
                                     headers={"X-Api-Key": key, "Content-Type": "application/json", "Accept": "application/json"})
    with urllib.request.urlopen(request, timeout=60) as response:
        return json.load(response)


def encoded(path):
    with open(path, "rb") as handle:
        return base64.b64encode(handle.read()).decode()


def payload(result):
    shots = [{"name": os.path.basename(path), "data": encoded(path)} for path in result["screenshots"]]
    return {"passed": result["passed"], "evidence": result["evidence"], "component": result["component"], "screenshots": shots}


def overrides(args):
    """--fail "Item name=what is wrong": Claude Code's own look at a screenshot found it doesn't match."""
    found = {}
    for i, arg in enumerate(args):
        if arg == "--fail" and i + 1 < len(args) and "=" in args[i + 1]:
            name, why = args[i + 1].split("=", 1)
            found[name.strip()] = why.strip()
    return found


def apply_overrides(results, failed):
    unknown = set(failed) - {r["name"] for r in results}
    if unknown:
        sys.exit("--fail names no item Claude Code checked: %s" % ", ".join(sorted(unknown)))
    for r in results:
        if r["name"] in failed:
            r["passed"] = False
            r["evidence"] = "The screenshots don't match what the item says you should see: %s\n%s" % (failed[r["name"]], r["evidence"])
    return results


def record(args):
    which = option(args, "--stack")
    if which not in ("qa", "dev"):
        sys.exit("record --stack qa|dev")
    env = secrets_env()
    port = env.get("REQUESTARR_PORT" if which == "qa" else "DEV_REQUESTARR_PORT") or ("29797" if which == "qa" else "39797")
    url, key = "http://localhost:" + port, env["REQUESTARR_API_KEY"]
    out = option(args, "--out") or latest_run()
    with open(os.path.join(out, "results.json"), encoding="utf-8") as handle:
        run_data = json.load(handle)
    running = running_build(which)
    if not running.endswith("." + run_data["commit"][:7]):
        sys.exit("The %s stack runs %s, not the commit that was checked (%s): nothing recorded." % (which, running, run_data["commit"][:7]))
    on_stack = {i["name"]: i for i in api(url, key, "/api/v1/qa/checklist")["checklist"]["items"] if i.get("checkedBy") == "Claude Code"}
    for result in apply_overrides(run_data["results"], overrides(args)):
        item = on_stack.pop(result["name"], None)
        if item is None:
            print("SKIP  %s: not one of Claude Code's items on the %s checklist" % (result["name"], which))
            continue
        try:
            saved = api(url, key, "/api/v1/qa/checklist/%s/result" % item["key"], payload(result))
            print("%s  %s: %s by %s" % ("PASS" if result["passed"] else "FAIL", result["name"], saved["state"], saved["by"]))
        except urllib.error.HTTPError as error:
            print("ERROR %s: HTTP %d %s" % (result["name"], error.code, error.read().decode(errors="replace")))
    for name in on_stack:
        print("MISSING %s: Claude Code's item on the %s checklist had no result in this run" % (name, which))


def latest_run():
    root = os.path.join(REPO, "_qa-checks")
    runs = sorted((os.path.join(root, d) for d in os.listdir(root)), key=os.path.getmtime) if os.path.isdir(root) else []
    if not runs:
        sys.exit("No run yet: python3 docker/qa_checks.py run first.")
    return runs[-1]


def option(args, name):
    return args[args.index(name) + 1] if name in args and args.index(name) + 1 < len(args) else None


SAMPLE = """# QA checklist

## Unreleased
- **Build number:** Open System, then Status. You should see the number.
  - Checked by Claude Code: the web app status page check.
- **Bot picture:** Open the bot's profile. You should see the picture.
  - Checked by the owner, needs human eyes: the profile popout is only in the Discord app.
- **Passing an item:** Press Passes. You should see it passed.
  - Checked by Claude Code: the live passing check.
- **Gone:** Do it. You should see it.
  - Checked by Claude Code: the retired check.

## 0.6.0 (2026-11-01)
- **Old:** Do it. You should see it.
"""


def self_test():
    items = checklist_items(SAMPLE)
    web = {"status_page": {"passed": True, "evidence": "Version 0.7.0005-qa.abc1234", "screenshots": ["s/status-light.jpg"]}}
    live = {"LiveQaFixture": (True, "noise\n[pass] the owner presses Passes\n[fail] not this one\n")}
    results = {r["name"]: r for r in results_for(items, web, live)}
    failed = apply_overrides([dict(results["Build number"])], {"Build number": "the logo is on black"})[0]
    expected = [
        ("only Unreleased items, in order", [i["name"] for i in items] == ["Build number", "Bot picture", "Passing an item", "Gone"]),
        ("the owner's item has no check", items[1]["check"] is None and "Bot picture" not in results),
        ("a web check's own result", results["Build number"]["passed"] and results["Build number"]["screenshots"] == ["s/status-light.jpg"]),
        ("a live check's tagged lines", results["Passing an item"]["evidence"] == "[pass] the owner presses Passes"),
        ("an unknown check fails", not results["Gone"]["passed"] and "No such check" in results["Gone"]["evidence"]),
        ("a check that didn't run fails", not results_for(items, {}, {})[0]["passed"]),
        ("--fail overrides a pass", not failed["passed"] and failed["evidence"].startswith("The screenshots don't match")),
        ("--fail parsed", overrides(["--fail", "A=b c", "--stack", "qa"]) == {"A": "b c"}),
    ]
    wrong = [name for name, ok in expected if not ok]
    for name in wrong:
        print("qa_checks self-test: wrong for %s" % name, file=sys.stderr)
    print("qa_checks self-test: %d of %d cases right" % (len(expected) - len(wrong), len(expected)))
    return 1 if wrong else 0


def main(argv):
    if argv[1:2] == ["--self-test"]:
        return self_test()
    if argv[1:2] == ["run"]:
        run(argv[2:])
        return 0
    if argv[1:2] == ["record"]:
        record(argv[2:])
        return 0
    print(__doc__)
    return 2


if __name__ == "__main__":
    sys.exit(main(sys.argv))
