#!/usr/bin/env python3
"""The automated checks before a promotion to QA (the owner's process, docs/development.md#qa-checklist): the full test
suite, every live Discord check and the automated checklist items, on dev at the commit to be promoted. QA gets a build
only once all of it passed; QA's checklist shows only the owner's items, and nothing here touches the QA server.

    python3 docker/qa_checks.py run [--commit SHA] [--out DIR] [--only web|live]
    python3 docker/qa_checks.py record [--out DIR] [--fail "Item name=what is wrong"]...
    python3 docker/qa_checks.py verify SHA
    python3 docker/qa_checks.py summary SHA
    python3 docker/qa_checks.py --self-test

run: the checkout must be at the commit (the tests are built from it). It builds the solution and runs the full suite
     the way CI does, brings the dev stack up at that commit with sign-in required and a throwaway login for the web app
     checks (headless Edge or Chrome, light and dark theme), stops it and runs every live check on the dev server (the
     test Sonarr and Radarr emptied before each, so no check finds another's titles), resets the dev stack and runs the
     read-back of the running stack. Results, read-backs and screenshots go in DIR (default _qa-checks/<short commit>).
record: after the automation has looked at every screenshot against its item's Expect (one that doesn't match
     is --fail), records the results on the dev stack's checklist (the automation's server; it must run that commit) and
     marks the run reviewed. QA never takes them.
verify: whether the run for SHA is ready for the promotion (release.sh promote-qa): a full run that passed the suite,
     every live check and every item, reviewed. Exit 0 ready, 1 not (the reasons are printed), 3 no run for SHA.
summary: the run's results in Markdown, for the promotion pull request.

The checks each item may name are CHECKS below ("Checked automatically: the <name> check."); .github/checklist_check.py
refuses an item naming any other.
"""
import base64
import hashlib
import json
import os
import platform
import re
import secrets
import shutil
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
# Each stack's port setting in secrets.env and its default, as docker/dev.ps1 and docker/dev.sh have them.
PORTS = {"qa": ("REQUESTARR_PORT", "29797"), "dev": ("DEV_REQUESTARR_PORT", "39797")}


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
    "web app portal pages": Check("web", "portal_pages", "web-app"),
    "web app settings pages": Check("web", "settings_pages", "web-app"),
    "web app header search": Check("web", "header_search", "web-app"),
    "web app request views": Check("web", "request_views", "web-app"),
    "web app request actions": Check("web", "request_actions", "web-app"),
    "web app add member": Check("web", "add_members", "web-app"),
    "live portal actions": Check("live", "LivePortalActionsFixture", "admin"),
    "live member renames": Check("live", "LiveMemberRenamesFixture", "admin"),
    "live shared library": Check("live", "LiveRemovalFixture", "admin", ("[shared]",)),
    "live deleting turned off": Check("live", "LiveRemovalFixture", "admin", ("[by-hand]",)),
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

# An item (QA-CHECKLIST.md): "- **Name**", "  - Steps:" over "    1. ...", "  - Expect:" over "    - ...", then who checks it.
ITEM = re.compile(r"^- \*\*([^*]+)\*\*$")
STEP = re.compile(r"^    (\d+)\. (.+)$")
BULLET = re.compile(r"^    - (.+)$")
STEPS_LINE = "  - Steps:"
EXPECT_LINE = "  - Expect:"
CHECKED_LINE = "  - Checked "
CHECKED_BY = re.compile(r"^- Checked (?:automatically: the (?P<check>.+) check|by the owner, (?P<reason>needs human eyes|"
                        r"unsure of the desired behavior|couldn't verify): (?P<why>.+?))\.?$")
UNRELEASED = "## Unreleased"
EVIDENCE_LINES = 25

# CI's test assemblies and filter (.github/workflows/build.yml), with the platform's own category left out.
SUITE = ["Requestarr.Api.V1.Test.dll", "Requestarr.Common.Test.dll", "Requestarr.Core.Test.dll", "Requestarr.Host.Test.dll",
         "Requestarr.Libraries.Test.dll", "Requestarr.Mono.Test.dll", "Requestarr.Update.Test.dll"]
SUITE_FILTER = "Category!=ManualTest&Category!=IntegrationTest&Category!=AutomationTest"

# Every live check: these need the dev stack's bot stopped; the read-back reads the running stack, so it runs last.
LIVE = ["LiveDiscordFixture", "LiveBackendFixture", "LiveAdminConsoleFixture", "LiveAdminRequestsFixture",
        "LiveAvailabilityFeedFixture", "LiveDevHelpersFixture", "LiveRequestMessageFixture", "LiveRemovalFixture",
        "LiveAnnouncementsFixture", "LiveQaFixture", "LiveWelcomeFixture"]
LIVE_ON_RUNNING_STACK = "LiveReadBackFixture"


def item_key(name, text):
    """The item's key on the server (QaChecklist.WordingKey): its name in slug form and 8 hex digits of its wording's
    SHA-256, so a result lands on exactly the item whose words were checked."""
    wording = re.sub(r"\s+", " ", "%s: %s" % (name, text))
    slug = re.sub("[^a-z0-9]+", "-", name.lower()).strip("-")
    return "%s.%s" % (slug, hashlib.sha256(wording.encode("utf-8")).hexdigest()[:8])


def item_text(steps, expect):
    """An item's wording as the server keeps it (QaChecklist.Wording): its steps numbered, then what to expect."""
    return "\n".join(["Steps:"] + ["%d. %s" % (n, s) for n, s in enumerate(steps, 1)] + ["Expect:"] + ["- " + e for e in expect])


def checklist_items(text):
    """The Unreleased items with who checks them: [{name, text, key, check}] (check None for the owner's)."""
    items, inside, part = [], False, None
    for line in text.splitlines():
        line = line.rstrip()
        if line.startswith("## "):
            inside = line == UNRELEASED
            continue
        item = ITEM.match(line)
        if inside and item:
            items.append({"name": item.group(1).strip(), "steps": [], "expect": [], "check": None})
            part = None
        elif inside and items:
            part = item_line(items[-1], line, part)
    for item in items:
        item["text"] = item_text(item.pop("steps"), item.pop("expect"))
        item["key"] = item_key(item["name"], item["text"])
    return items


def item_line(item, line, part):
    """Reads one of an item's lines into it; returns the part (steps or expect) the next lines belong to."""
    checked = CHECKED_BY.match(line.strip())
    if line.startswith(CHECKED_LINE) and checked:
        item["check"] = checked.group("check")
        return part
    if line in (STEPS_LINE, EXPECT_LINE):
        return "steps" if line == STEPS_LINE else "expect"
    step, bullet = STEP.match(line), BULLET.match(line)
    if part == "steps" and step:
        item["steps"].append(step.group(2).strip())
    elif part == "expect" and bullet:
        item["expect"].append(bullet.group(1).strip())
    return part


def live_result(ran, log, tags):
    """A live check passes only when its test ran and passed and it logged read-backs under the item's tags."""
    lines = [line.strip() for line in log.splitlines() if any(line.strip().startswith(tag) for tag in tags)]
    if not log:
        return False, "The live check didn't run."
    if not lines:
        return False, "The live check logged nothing under %s (skipped, or stopped before it got there)." % ", ".join(tags)
    evidence = "\n".join(lines[:EVIDENCE_LINES])
    if not ran:
        return False, "The live check failed (its log is in the run's live folder).\n" + evidence
    return True, "Passed on the dev server; the check read back %d line(s):\n%s" % (len(lines), evidence)


def results_for(items, web, live):
    """Each automated item's result from what the checks left: web {test: {passed, evidence, screenshots}} and live
    {fixture: (passed, log)}. A check that didn't run is a failure that says so."""
    results = []
    for item in [i for i in items if i["check"]]:
        check = CHECKS.get(item["check"])
        screenshots = []
        if check is None:
            passed, evidence = False, "No such check: %s." % item["check"]
        elif check.kind == "web":
            found = web.get(check.target, {"passed": False, "evidence": "The web check didn't run.", "screenshots": []})
            passed, evidence, screenshots = found["passed"], found["evidence"], found["screenshots"]
        else:
            passed, evidence = live_result(*live.get(check.target, (False, "")), check.tags)
        results.append({"name": item["name"], "key": item["key"], "check": item["check"], "kind": check.kind if check else None,
                        "component": check.component if check else "other", "passed": passed, "evidence": evidence, "screenshots": screenshots})
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
    """The build a stack's Requestarr runs (0.7.0005-qa), from its image's label: read, never changed."""
    return subprocess.run(["docker", "ps", "--filter", "label=com.docker.compose.project=requestarr-" + which,
                           "--filter", "label=com.docker.compose.service=requestarr", "--format", '{{.Label "org.opencontainers.image.version"}}'],
                          capture_output=True, text=True, check=True).stdout.strip()


def stack_url(env, which):
    setting, default = PORTS[which]
    return "http://localhost:" + (env.get(setting) or default)


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
    """One test run; its output goes to log_path. True when it passed and none of its tests was skipped (a live check
    skips itself when a key it needs is missing, which dotnet test counts as success)."""
    with open(log_path, "w", encoding="utf-8") as log:
        code = subprocess.run(["dotnet", "test", TESTS, "--filter", filter_, "--logger", "console;verbosity=detailed"],
                              env=env, stdout=log, stderr=subprocess.STDOUT, cwd=os.path.dirname(TESTS)).returncode
    with open(log_path, encoding="utf-8", errors="replace") as log:
        return code == 0 and ran_all(log.read())


def ran_all(output):
    """Whether dotnet test's summary says at least one test passed and none was skipped. The detailed summary puts each
    count on its own line ("Passed: 1") and leaves out a count of none; the short one is "Passed: 1, Skipped: 0"."""
    summary = output[output.rfind("Total tests:"):] if "Total tests:" in output else output
    passed = re.findall(r"Passed:\s+(\d+)", summary)
    skipped = re.findall(r"Skipped:\s+(\d+)", summary)
    return bool(passed) and int(passed[-1]) > 0 and (not skipped or int(skipped[-1]) == 0)


def run_web(out, env, commit):
    password = secrets.token_urlsafe(24)
    login = {"DEV_AUTH_REQUIRED": "enabled", "REQUESTARR_ADMIN_USERNAME": ACCOUNT, "REQUESTARR_ADMIN_PASSWORD": password}
    stack(["up"], login, ref=commit)
    web_dir = os.path.join(out, "web")
    web_env = dict(env, QA_CHECKS_URL=stack_url(env, "dev"), QA_CHECKS_DIR=web_dir,
                   QA_CHECKS_USERNAME=ACCOUNT, QA_CHECKS_PASSWORD=password,
                   QA_CHECKS_VERSION=running_build("dev"))
    os.makedirs(web_dir, exist_ok=True)
    dotnet_test("FullyQualifiedName~" + WEB_FIXTURE, web_env, os.path.join(web_dir, "web.log"))
    results = {}
    for name in os.listdir(web_dir):
        if name.endswith(".json"):
            with open(os.path.join(web_dir, name), encoding="utf-8") as handle:
                results[name[:-5]] = json.load(handle)
    return results


def run_suite(out, env):
    """The full suite as CI runs it: {passed, failed, skipped, ok}."""
    platform_category = "LINUX" if platform.system() == "Windows" else "WINDOWS"
    log_path = os.path.join(out, "suite.log")
    with open(log_path, "w", encoding="utf-8") as log:
        code = subprocess.run(["dotnet", "test"] + SUITE + ["--filter:%s&Category!=%s" % (SUITE_FILTER, platform_category)],
                              env=env, stdout=log, stderr=subprocess.STDOUT, cwd=os.path.dirname(TESTS)).returncode
    with open(log_path, encoding="utf-8", errors="replace") as log:
        counts = suite_counts(log.read())
    counts["ok"] = code == 0 and counts["failed"] == 0 and counts["passed"] > 0
    print("[qa-checks] full suite: %(passed)d passed, %(failed)d failed, %(skipped)d skipped" % counts, flush=True)
    return counts


def suite_counts(output):
    """The totals of every assembly's summary line ("Passed!  - Failed: 0, Passed: 682, Skipped: 14, ...")."""
    counts = {"passed": 0, "failed": 0, "skipped": 0}
    for failed, passed, skipped in re.findall(r"Failed:\s+(\d+), Passed:\s+(\d+), Skipped:\s+(\d+)", output):
        counts["failed"] += int(failed)
        counts["passed"] += int(passed)
        counts["skipped"] += int(skipped)
    return counts


def live_env_for(out, env, commit):
    """The live checks run as the dev build of this commit, labelled the way the dev stack labels it."""
    label = subprocess.run([sys.executable, os.path.join(REPO, ".github", "build_numbers.py"), "label", "dev", commit],
                           capture_output=True, text=True, check=True, cwd=REPO).stdout.strip()
    return dict(env, LIVE_QA_STATE_DIR=os.path.join(out, "live", "state"), REQUESTARR_RELEASE_VERSION=label)


def run_one_live(fixture, out, live_env):
    log_path = os.path.join(out, "live", fixture + ".log")
    passed = dotnet_test("FullyQualifiedName~" + fixture, live_env, log_path)
    print("[qa-checks] %s: %s" % (fixture, "passed" if passed else "FAILED"), flush=True)
    with open(log_path, encoding="utf-8") as handle:
        return passed, handle.read()


def run_live(out, env, commit, fixtures):
    stack(["reset-dev", "--stopped"])
    os.makedirs(os.path.join(out, "live"), exist_ok=True)
    live_env = live_env_for(out, env, commit)
    results = {}
    for fixture in fixtures:
        # Some checks leave their titles in the test Sonarr and Radarr (on purpose); each starts with them empty.
        stack(["reset-test-backends"])
        results[fixture] = run_one_live(fixture, out, live_env)
    return results


def run(args):
    commit = git("rev-parse", option(args, "--commit") or "origin/qa")
    if git("rev-parse", "HEAD") != commit:
        sys.exit("The checkout is at %s, not %s: the checks are built from the checkout. Run git switch --detach %s first."
                 % (git("rev-parse", "--short", "HEAD"), commit[:7], commit[:7]))
    out = os.path.abspath(option(args, "--out") or os.path.join(REPO, "_qa-checks", commit[:7]))
    only = option(args, "--only")
    # A run starts empty, so nothing an earlier run left can stand in for a check that didn't run this time.
    shutil.rmtree(out, ignore_errors=True)
    os.makedirs(out, exist_ok=True)
    env = dict(os.environ, **secrets_env())
    if only in (None, "live") and not env.get("DISCORD_TEST_MEMBERS"):
        sys.exit("The live checks press buttons as a test member: set DISCORD_TEST_MEMBERS (in your shell or docker/secrets.env) first.")
    timings, web, live, suite = [], {}, {}, None
    with open(os.path.join(REPO, "QA-CHECKLIST.md"), encoding="utf-8") as handle:
        items = checklist_items(handle.read())
    try:
        timed("build", timings, lambda: subprocess.run(["dotnet", "build", os.path.join(REPO, "src", "Requestarr.sln"), "-v", "q", "-nologo"], check=True))
        if only is None:
            suite = timed("full suite", timings, lambda: run_suite(out, env))
        if only in (None, "web"):
            web = timed("web app checks (dev stack up at the commit, signed in)", timings, lambda: run_web(out, env, commit))
        if only in (None, "live"):
            fixtures = LIVE if only is None else sorted({c.target for c in CHECKS.values() if c.kind == "live"})
            live = timed("live Discord checks (dev server, bot and test member)", timings, lambda: run_live(out, env, commit, fixtures))
    finally:
        timed("dev stack reset", timings, lambda: stack(["reset-dev"]))
    if only is None:
        live[LIVE_ON_RUNNING_STACK] = timed("read-back of the running dev stack", timings,
                                            lambda: run_one_live(LIVE_ON_RUNNING_STACK, out, live_env_for(out, env, commit)))
    results = results_for(items, web, live)
    total = sum(t for _, t in timings)
    with open(os.path.join(out, "results.json"), "w", encoding="utf-8") as handle:
        json.dump({"commit": commit, "only": only, "suite": suite, "live": {f: passed for f, (passed, _) in live.items()},
                   "results": results, "reviewed": False, "timings": timings, "seconds": round(total)}, handle, indent=2)
    report(results, out, total)


def report(results, out, total):
    for r in results:
        shots = ", ".join(os.path.basename(s) for s in r["screenshots"]) or "no screenshots"
        print("%s  %-32s the %s check (%s)" % ("PASS" if r["passed"] else "FAIL", r["name"], r["check"], shots))
    passed = sum(1 for r in results if r["passed"])
    print("\n%d of %d of the automated items passed in %.0f minutes. Results: %s" % (passed, len(results), total / 60, os.path.join(out, "results.json")))
    print("Look at every screenshot against its item's Expect, then record them on dev (QA never takes them):")
    print('  python3 docker/qa_checks.py record [--fail "Item name=what is wrong"]')


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
    """--fail "Item name=what is wrong": The automation's own look at a screenshot found it doesn't match."""
    found = {}
    for i, arg in enumerate(args):
        if arg != "--fail":
            continue
        name, _, why = (args[i + 1] if i + 1 < len(args) else "").partition("=")
        if not name.strip() or not why.strip():
            sys.exit('--fail takes "Item name=what is wrong" (got %r): nothing recorded.' % (args[i + 1] if i + 1 < len(args) else ""))
        found[name.strip()] = why.strip()
    return found


def apply_overrides(results, failed):
    unknown = set(failed) - {r["name"] for r in results}
    if unknown:
        sys.exit("--fail names no item the automation checked: %s" % ", ".join(sorted(unknown)))
    for r in results:
        if r["name"] in failed:
            r["passed"] = False
            r["evidence"] = "The screenshots don't match what the item says you should see: %s\n%s" % (failed[r["name"]], r["evidence"])
    return results


def record(args):
    which = option(args, "--stack") or "dev"
    if which != "dev":
        sys.exit("QA's checklist is the owner's: the automated results are recorded on dev only.")
    env = secrets_env()
    url, key = stack_url(env, which), env["REQUESTARR_API_KEY"]
    out = option(args, "--out") or latest_run()
    with open(os.path.join(out, "results.json"), encoding="utf-8") as handle:
        run_data = json.load(handle)
    running = running_build(which)
    if not running.endswith("." + run_data["commit"][:7]):
        sys.exit("The %s stack runs %s, not the commit that was checked (%s): nothing recorded." % (which, running, run_data["commit"][:7]))
    failed = overrides(args)
    mark_reviewed(out, run_data, failed)
    on_stack = {i["key"]: i for i in api(url, key, "/api/v1/qa/checklist")["checklist"]["items"] if i.get("checkedBy") == "automation"}
    for result in apply_overrides(run_data["results"], failed):
        item = on_stack.pop(result["key"], None)
        if run_data.get("only") and result["kind"] != run_data["only"]:
            print("SKIP  %s: its %s check wasn't part of this run (--only %s)" % (result["name"], result["kind"], run_data["only"]))
            continue
        if item is None:
            print("SKIP  %s: not one of the automated items on the %s checklist, in these words" % (result["name"], which))
            continue
        try:
            saved = api(url, key, "/api/v1/qa/checklist/%s/result" % item["key"], payload(result))
            print("%s  %s: %s by %s" % ("PASS" if result["passed"] else "FAIL", result["name"], saved["state"], saved["by"]))
        except urllib.error.HTTPError as error:
            print("ERROR %s: HTTP %d %s" % (result["name"], error.code, error.read().decode(errors="replace")))
    for item in on_stack.values():
        print("MISSING %s (%s): the automated item on the %s checklist had no result in this run" % (item["name"], item["version"], which))


def mark_reviewed(out, run_data, failed):
    """The automation has looked at the screenshots: the run says so, with any it found don't match, for verify."""
    run_data["results"] = apply_overrides(run_data["results"], failed)
    run_data["reviewed"] = True
    with open(os.path.join(out, "results.json"), "w", encoding="utf-8") as handle:
        json.dump(run_data, handle, indent=2)


def load_run(commit):
    path = os.path.join(REPO, "_qa-checks", commit[:7], "results.json")
    if not os.path.exists(path):
        return None
    with open(path, encoding="utf-8") as handle:
        return json.load(handle)


def not_ready(run_data, commit):
    """Why a run can't let this commit go to QA (nothing when it can)."""
    reasons = []
    if run_data["commit"] != commit:
        reasons.append("the run was for %s, not %s" % (run_data["commit"][:7], commit[:7]))
    if run_data.get("only"):
        reasons.append("the run was partial (--only %s): it left out the full suite and the other checks" % run_data["only"])
    suite = run_data.get("suite")
    if not suite or not suite.get("ok"):
        reasons.append("the full suite didn't pass" + (": %(failed)d failed" % suite if suite else " (it didn't run)"))
    reasons += ["the live check %s failed" % f for f, passed in sorted((run_data.get("live") or {}).items()) if not passed]
    missing = [f for f in LIVE + [LIVE_ON_RUNNING_STACK] if f not in (run_data.get("live") or {})]
    if missing and not run_data.get("only"):
        reasons.append("live checks that didn't run: " + ", ".join(missing))
    reasons += ["the item \"%s\" failed (the %s check)" % (r["name"], r["check"]) for r in run_data["results"] if not r["passed"]]
    if not run_data.get("reviewed"):
        reasons.append("nobody looked at the screenshots yet (record, with --fail for any that don't match)")
    return reasons


def verify(args):
    commit = git("rev-parse", args[0] if args else "origin/dev")
    run_data = load_run(commit)
    if run_data is None:
        print("No run of the automated checks for %s." % commit[:7])
        return 3
    reasons = not_ready(run_data, commit)
    for reason in reasons:
        print("Not ready for QA: %s." % reason)
    if reasons:
        print("Refused: QA gets %s only once every check passed on dev. Fix it on dev and run the checks again." % commit[:7])
        return 1
    print("The automated checks passed on dev @ %s: the full suite, %d live checks and %d checklist items." % (commit[:7], len(run_data["live"]), len(run_data["results"])))
    return 0


def summary_text(run_data):
    suite = run_data["suite"]
    live = run_data["live"]
    lines = ["- **Full suite:** %(passed)d passed, %(failed)d failed, %(skipped)d skipped." % suite,
             "- **Live checks on the dev server:** %d of %d passed (%s)." % (sum(live.values()), len(live), ", ".join(sorted(live))),
             "- **Checklist items:** %d of %d passed, screenshots looked at:" % (sum(1 for r in run_data["results"] if r["passed"]), len(run_data["results"]))]
    lines += ["  - %s: %s" % (r["name"], r["evidence"].strip().splitlines()[0]) for r in run_data["results"]]
    lines.append("- **Time:** %d minutes. Screenshots and the full read-backs are in the promotion's reply." % round(run_data["seconds"] / 60))
    return "\n".join(lines)


def summary(args):
    commit = git("rev-parse", args[0] if args else "origin/dev")
    run_data = load_run(commit)
    if run_data is None:
        sys.exit("No run of the automated checks for %s." % commit[:7])
    print(summary_text(run_data))
    return 0


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
- **Build number**
  - Steps:
    1. Open System, then Status.
  - Expect:
    - The number.
  - Checked automatically: the web app status page check.
- **Bot picture**
  - Steps:
    1. Open the bot's profile.
  - Expect:
    - The picture.
  - Checked by the owner, needs human eyes: the profile popout is only in the Discord app.
- **Passing an item**
  - Steps:
    1. Press Passes.
    2. Press it again.
  - Expect:
    - It passed.
  - Checked automatically: the live passing check.
- **Gone**
  - Steps:
    1. Do it.
  - Expect:
    - It.
  - Checked automatically: the retired check.

## 0.6.0 (2026-11-01)
- **Old**
  - Steps:
    1. Do it.
  - Expect:
    - It.
"""


# An item's words and the key the server gave them (November 14, 2026): the two must agree.
READ_CHANGES = "Press Read changes on the update card. You should see the full list of what changed since the last beta, only to you."


def refuses(call):
    try:
        call()
    except SystemExit:
        return True
    return False


SUITE_OUTPUT = """Passed!  - Failed:     0, Passed:   682, Skipped:    14, Total:   696, Duration: 32 s - Requestarr.Common.Test.dll
Failed!  - Failed:     1, Passed:     1, Skipped:     0, Total:     2, Duration: 1 s - Requestarr.Host.Test.dll"""


def a_ready_run():
    return {"commit": "abc", "only": None, "suite": {"passed": 683, "failed": 0, "skipped": 14, "ok": True},
            "live": {f: True for f in LIVE + [LIVE_ON_RUNNING_STACK]}, "reviewed": True, "seconds": 1800,
            "results": [{"name": "Build number", "check": "web app status page", "passed": True, "evidence": "Version shown 0.7.0005-dev.abc\nmore"}]}


def self_test():
    ready = a_ready_run()
    items = checklist_items(SAMPLE)
    web = {"status_page": {"passed": True, "evidence": "Version 0.7.0005-qa.abc1234", "screenshots": ["s/status-light.jpg"]}}
    live = {"LiveQaFixture": (True, "noise\n[pass] the owner presses Passes\n[fail] not this one\n")}
    results = {r["name"]: r for r in results_for(items, web, live)}
    failed = apply_overrides([dict(results["Build number"])], {"Build number": "the logo is on black"})[0]
    expected = [
        ("only Unreleased items, in order", [i["name"] for i in items] == ["Build number", "Bot picture", "Passing an item", "Gone"]),
        ("the owner's item has no check", items[1]["check"] is None and "Bot picture" not in results),
        ("an item's wording, as the server keeps it", items[2]["text"] == "Steps:\n1. Press Passes.\n2. Press it again.\nExpect:\n- It passed."),
        ("a web check's own result", results["Build number"]["passed"] and results["Build number"]["screenshots"] == ["s/status-light.jpg"]),
        ("a live check's tagged lines", results["Passing an item"]["evidence"] == "Passed on the dev server; the check read back 1 line(s):\n[pass] the owner presses Passes"),
        ("an unknown check fails", not results["Gone"]["passed"] and "No such check" in results["Gone"]["evidence"]),
        ("a check that didn't run fails", not results_for(items, {}, {})[0]["passed"]),
        ("--fail overrides a pass", not failed["passed"] and failed["evidence"].startswith("The screenshots don't match")),
        ("--fail parsed", overrides(["--fail", "A=b c", "--stack", "qa"]) == {"A": "b c"}),
        ("--fail without its reason refuses", refuses(lambda: overrides(["--fail", "A: b c"]))),
        ("the server's key for an item's words", item_key("Read changes", READ_CHANGES) == "read-changes.412ba92d"),
        ("a live check without its read-backs fails", not live_result(True, "Passed: 1, Skipped: 0\n[other] line", ("[pass]",))[0]),
        ("a skipped test isn't a pass", not ran_all("Passed!  - Failed:     0, Passed:     0, Skipped:     1, Total:     1")),
        ("a passed test is", ran_all("Passed!  - Failed:     0, Passed:     1, Skipped:     0, Total:     1")),
        ("the detailed summary's pass", ran_all("Total tests: 1\n     Passed: 1\n Total time: 1.9 Minutes")),
        ("the detailed summary's skip", not ran_all("Total tests: 1\n     Skipped: 1\n Total time: 1 Minutes")),
        ("the suite's totals", suite_counts(SUITE_OUTPUT) == {"passed": 683, "failed": 1, "skipped": 14}),
        ("a ready run", not_ready(ready, "abc") == []),
        ("a failed item refuses", any("\"Build number\" failed" in r for r in not_ready(dict(ready, results=[dict(ready["results"][0], passed=False)]), "abc"))),
        ("a failed live check refuses", any("LiveQaFixture failed" in r for r in not_ready(dict(ready, live=dict(ready["live"], LiveQaFixture=False)), "abc"))),
        ("a partial run refuses", any("partial" in r for r in not_ready(dict(ready, only="web"), "abc"))),
        ("a failed suite refuses", any("suite" in r for r in not_ready(dict(ready, suite=dict(ready["suite"], ok=False)), "abc"))),
        ("screenshots not looked at refuses", any("screenshots" in r for r in not_ready(dict(ready, reviewed=False), "abc"))),
        ("another commit's run refuses", any("not def" in r for r in not_ready(ready, "def"))),
        ("the summary for the pull request", "- **Full suite:** 683 passed" in summary_text(ready) and "  - Build number: Version shown" in summary_text(ready)),
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
    if argv[1:2] == ["verify"]:
        return verify(argv[2:])
    if argv[1:2] == ["summary"]:
        return summary(argv[2:])
    print(__doc__)
    return 2


if __name__ == "__main__":
    sys.exit(main(sys.argv))
