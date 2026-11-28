#!/usr/bin/env python3
"""The automated checks before a promotion to QA (the owner's process, docs/development.md#qa-checklist): the full test
suite, every live Discord check and the automated checklist items, on dev at the commit to be promoted. QA gets a build
only once all of it passed; QA's checklist shows only the owner's items, and nothing here touches the QA server.

    python3 docker/qa_checks.py run [--commit SHA] [--out DIR] [--only suite|web|live]
    python3 docker/qa_checks.py record [--out DIR] [--fail "Item name=what is wrong"]...
    python3 docker/qa_checks.py verify SHA
    python3 docker/qa_checks.py summary SHA
    python3 docker/qa_checks.py --self-test

run: the checkout must be at the commit (the checks are built from it). It has GitHub run the full suite on the commit
     (the Build workflow by hand, on dev or the commit's build number tag; a run of it that passed or is still going is
     used) and builds the solution. Then, all at the same time: the web app checks bring the dev stack up at that commit
     with sign-in required and a throwaway login (headless Edge or Chrome, light and dark theme), seed the requests they
     act on ("Automated check: ...") and afterwards remove those and everything the checks did there, audit entries
     included, then reset the dev stack for the checks that need it just reset; and every live check runs on the dev
     server, in LIVE_LANES side by side. At the end it resets the dev stack and runs the read-back of the running stack.
     Results, read-backs and screenshots go in DIR (default _qa-checks/<short commit>).
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
import contextlib
import hashlib
import json
import os
import platform
import re
import secrets
import shutil
import sqlite3
import subprocess
import sys
import tempfile
import threading
import time
from concurrent.futures import ThreadPoolExecutor
import urllib.error
import urllib.parse
import urllib.request

HERE = os.path.dirname(os.path.abspath(__file__))
REPO = os.path.dirname(HERE)
TESTS = os.path.join(REPO, "_tests", "net10.0", "Requestarr.Core.Test.dll")
WEB_FIXTURE = "QaWebChecksFixture"

# Web checks run on a Requestarr just set up (the run's second one, beside the dev stack), not on the owner's dev data,
# in this order: Post History posts the whole server's imported history, and there only the test member's made-up
# history is imported; then the import from Requestrr imports the recorded channels (copied in for it) and lists its
# history posting in System, Tasks.
FRESH_WEB = ["post_history", "import_preview"]
RECORDINGS = os.path.join(REPO, "src", "Requestarr.Core.Test", "Imports", "Recorded")
HISTORY_SEEDED = 30
DISCORD_API = "https://discord.com/api/v10"
ACCOUNT = "qa-checks"
# Each stack's port setting in secrets.env and its default, as docker/dev.ps1 and docker/dev.sh have them.
PORTS = {"qa": ("REQUESTARR_PORT", "29797"), "dev": ("DEV_REQUESTARR_PORT", "39797")}

# A second Requestarr for the checks that save connections in the web app (dev's come from its provisioning file and
# can't be changed there, and any provisioning file locks them): the dev image with no provisioning file, on the dev
# stack's network so it reaches the test Sonarr and Radarr, its data inside the container, removed with it. Its API key
# and sign-in come from the environment (Requestarr:Auth), so nobody signs in to it.
THROWAWAY = "requestarr-dev-throwaway"
THROWAWAY_PORT = "39798"
DEV_NETWORK = "requestarr-dev_default"
THROWAWAY_TRIES = 60
THROWAWAY_WAIT_SECONDS = 2


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
    "web app paging": Check("web", "paging", "web-app"),
    "web app live updates": Check("web", "live_updates", "web-app"),
    "web app requestrr import": Check("web", "import_preview", "web-app"),
    "live portal actions": Check("live", "LivePortalActionsFixture", "admin", ("[access]", "[request]", "[requests]", "[removal]")),
    "live member renames": Check("live", "LiveMemberRenamesFixture", "admin", ("[stored]", "[history]", "[search]", "[channel]", "[portal]")),
    "live shared library": Check("live", "LiveRemovalFixture", "admin", ("[shared]",)),
    "live deleting turned off": Check("live", "LiveRemovalFixture", "admin", ("[by-hand]",)),
    "live history removal": Check("live", "LiveRemovalFixture", "admin", ("[history]",)),
    "live update cards": Check("live", "LiveAnnouncementsFixture", "admin", ("[qa]",)),
    "live Read changes": Check("live", "LiveWelcomeFixture", "admin", ("[cards]",)),
    "live access requests": Check("live", "LiveWelcomeFixture", "access", ("[spam]", "[block]", "[unblock]")),
    "live checklist channel": Check("live", "LiveQaFixture", "other", ("[channels]", "[checklist]")),
    "live passing": Check("live", "LiveQaFixture", "other", ("[pass]", "[complete]")),
    "live failing": Check("live", "LiveQaFixture", "other", ("[fail]",)),
    "live carry-over": Check("live", "LiveQaFixture", "other", ("[promotion]",)),
    "live bug reports": Check("live", "LiveQaFixture", "other", ("[reports]", "[issues]")),
    "live report limit": Check("live", "LiveQaFixture", "other", ("[limit]",)),
    "live routing by content": Check("live", "LiveRoutingFixture", "requests", ("[route]", "[paused]")),
    "live series fixes": Check("live", "LiveSeriesFixFixture", "requests", ("[fix]",)),
    "live import alerts": Check("live", "LiveImportAlertsFixture", "requests", ("[import]", "[later]")),
    "web app connection windows": Check("web", "connection_windows", "web-app"),
    "web app server cards": Check("web", "server_cards", "web-app"),
    "web app post history": Check("web", "post_history", "web-app"),
    "live request messages": Check("live", "LiveRequestMessageFixture", "requests", ("[all seasons]",)),
    "live imported history": Check("live", "LiveImportHistoryFixture", "requests", ("[import]", "[feed]", "[member]", "[history]", "[admin history]", "[others]")),
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

# The full suite's workflow, and the line its tests end with (.github/run_tests.py).
SUITE_WORKFLOW = "build.yml"
SUITE_TOTALS = re.compile(r"Tests \(full\): (\d+) failed, (\d+) passed, (\d+) skipped")
VERSION_TAG = re.compile(r"v\d+\.\d+\.\d{4,}")
# How long GitHub may take to list a run it was asked for, and how often a call to it is tried again: the suite's result is
# read at the end of a long run, which a dropped connection or a log not ready yet mustn't throw away.
RUN_LISTED_SECONDS = 120
GH_TRIES = 10
GH_WAIT_SECONDS = 30

# Every live check, in lanes that run side by side, alongside the web checks (the owner's rule: whatever can run in
# parallel does, docs/development.md#qa-checklist). A lane's checks run one after another. Each lane keeps apart from the
# others and from the dev stack: every name Requestarr gives a channel, group or role in the dev server takes the lane's
# prefix (QA_CHECKS_LANE, "lane3-requestarr-admin"), and it has a test Sonarr and Radarr of its own (compose.yaml's,
# under the compose project requestarr-dev-lane3, on ports of its own). What's left is the one test member, whose
# nickname and roles every lane sees. Balanced by how long each check took with all lanes running (seconds).
LIVE_LANES = [
    # Version words finds the lane's admin and announcements channels the QA check's setup made.
    ["LiveQaFixture", "LiveVersionWordsFixture", "LiveMemberRenamesFixture"],  # 260, 10, 35 (after the web checks)
    ["LiveWelcomeFixture", "LiveAnnouncementsFixture"],  # 265, 115
    ["LiveDevHelpersFixture", "LiveRemovalFixture"],  # about 100, 135
    ["LiveRequestMessageFixture", "LiveAdminRequestsFixture"],  # about 90, 120
    ["LiveAdminConsoleFixture", "LiveAvailabilityFeedFixture"],  # about 80, 145
    ["LiveBackendFixture", "LiveRoutingFixture", "LiveSeriesFixFixture", "LiveDiscordFixture"],  # 50, 30, 20, 20
    ["LiveImportAlertsFixture", "LiveImportHistoryFixture", "LivePortalActionsFixture"],  # 55, 50, 75
]
LIVE = [fixture for lane in LIVE_LANES for fixture in lane]
# The checks that use no test Sonarr or Radarr (a lane of only these gets none), and those that add titles to them, so
# they start with them empty (some checks leave their titles there on purpose).
NO_BACKENDS = {"LiveQaFixture", "LiveMemberRenamesFixture", "LiveVersionWordsFixture"}
EMPTY_FIRST = {"LiveBackendFixture", "LiveAdminConsoleFixture", "LiveAdminRequestsFixture", "LiveAvailabilityFeedFixture",
               "LiveDevHelpersFixture", "LiveRequestMessageFixture", "LiveRemovalFixture", "LivePortalActionsFixture",
               "LiveRoutingFixture", "LiveSeriesFixFixture", "LiveImportAlertsFixture", "LiveImportHistoryFixture"}
LANE_PREFIX = "lane%d-"
LANE_PROJECT = "requestarr-dev-lane%d"
# Lane N's test Radarr and Sonarr are on these ports plus N (the dev stack's are 37878 and 38989).
LANE_PORTS = (37880, 38990)
TEST_BACKENDS = ["sonarr-test", "radarr-test", "test-backends-init"]
TEST_BACKEND_VOLUMES = ["sonarr-test-config", "radarr-test-config", "test-media"]
COMPOSE = os.path.join(HERE, "compose.yaml")
# Discord takes one sign-in of a bot per 5 seconds, and sign-ins that come faster are refused and tried again: started
# together, the lanes' checks and the dev stack's bot kept each other out for over a minute. So the checks that sign the
# bot in start this far apart, across every lane. These three use a stand-in for Discord.
SIGN_IN_APART_SECONDS = 8
NO_SIGN_IN = {"LiveBackendFixture", "LiveRoutingFixture", "LiveSeriesFixFixture"}
# And at most this many hold the bot at once: each sets the server up when it starts, and with more Discord's limits on
# one server's channels and messages slowed them past their timeouts.
MAX_BOTS = 5
# Checks that wait for the web checks to end: renaming the test member makes the dev stack's Requestarr, there for the
# web checks, rename them in its own data, which the web checks show.
AFTER_WEB = {"LiveMemberRenamesFixture"}
# The web checks that need a Requestarr just set up (FRESH_WEB) run on a second one beside the dev stack, at the same time
# as the others: compose.yaml's stack under the project of the lane after the live checks', with that lane's prefix on
# its Discord names, the dev bot, and a port of its own.
FRESH_LANE = len(LIVE_LANES) + 1
FRESH_PORT = "39799"
# What its log says once its bot has set up the server, looked for every few seconds for up to 3 minutes.
BOT_READY = "Discord bot ready in server"
BOT_READY_TRIES = 60
BOT_READY_WAIT_SECONDS = 3
# The read-back reads the running dev stack, so it runs last, once it's reset.
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
        # Parts run at the same time: this part's own seconds, not the list's last.
        seconds = time.monotonic() - start
        timings.append((name, seconds))
        print("[qa-checks] %s: %.0fs" % (name, seconds), flush=True)


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


def run_web(out, env, password):
    """The web checks on the owner's dev data, on the dev stack already up at the commit with the checks' login."""
    web_dir = os.path.join(out, "web")
    web_env = web_env_for(env, web_dir, password, stack_url(env, "dev"), "dev")
    series = None
    try:
        # The marked requests the checks act on, one of them in the test Sonarr; removed again with everything the
        # checks did, pass or fail.
        series = add_check_series(env)
        print("[qa-checks] seeded %d requests for the web checks" % edit_dev_database(lambda con: seed_check_rows(con, series)), flush=True)
        if series:
            web_env["QA_CHECKS_SONARR_SERIES"] = series["title"]
        web_env.update(start_throwaway())
        fresh = "".join("&Name!=" + name for name in FRESH_WEB)
        dotnet_test("FullyQualifiedName~" + WEB_FIXTURE + fresh, web_env, os.path.join(web_dir, "web.log"))
    finally:
        remove_throwaway()
        try:
            clean_check_entries()
        finally:
            remove_check_series(env, series)


def run_web_fresh(out, env):
    """The web checks that need a Requestarr just set up, on the second one (fresh_env): up with the checks' login, the
    test member known from a made-up import with HISTORY_SEEDED requests of their own, then the checks. It goes with its
    data when they end."""
    password = secrets.token_urlsafe(24)
    fresh = fresh_env(env, password)
    container = LANE_PROJECT % FRESH_LANE + "-requestarr-1"
    try:
        # Down first: one an earlier run left would keep its data.
        lane_compose(fresh, "down", "-v")
        lane_compose(fresh, "up", "-d", "--wait")
        wait_bot_ready(container)
        member = test_member(env)
        edit_dev_database(lambda con: seed_history_rows(con, member), container)
        print("[qa-checks] the test member imported with %d requests of their own" % HISTORY_SEEDED, flush=True)
        web_dir = os.path.join(out, "web")
        web_env = dict(web_env_for(env, web_dir, password, "http://localhost:" + FRESH_PORT, "dev-lane%d" % FRESH_LANE),
                       QA_CHECKS_LANE=fresh["QA_CHECKS_LANE"], QA_CHECKS_TEST_MEMBER=member["username"], QA_CHECKS_HISTORY_SEEDED=str(HISTORY_SEEDED))
        for name in FRESH_WEB:
            # The recordings only once Post History is done: their import adds imported history of its own.
            if name == "import_preview":
                copy_recordings_in(container)
            dotnet_test("FullyQualifiedName~%s.%s" % (WEB_FIXTURE, name), web_env, os.path.join(web_dir, "web-fresh-%s.log" % name))
    finally:
        subprocess.run(lane_compose_args(fresh, "down", "-v"), env=lane_compose_env(fresh), capture_output=True)


def fresh_env(env, password):
    """The second Requestarr's compose variables: the dev stack's image, provisioning file and bot, a lane's prefix on its
    Discord names (a dev stack takes it from REQUESTARR_DISCORD_NAME_PREFIX), its own port, test Sonarr and Radarr and
    data, and the checks' login."""
    lane = lane_env(FRESH_LANE, env)
    return dict(lane_compose_env(lane), STACK_REQUESTARR_PORT=FRESH_PORT, STACK_DISCORD_NAME_PREFIX=lane["QA_CHECKS_LANE"],
                STACK_DISCORD_BOT_TOKEN=env["DEV_DISCORD_BOT_TOKEN"], STACK_DISCORD_GUILD_ID=env["DEV_DISCORD_GUILD_ID"],
                STACK_DISCORD_APPLICATION_ID=env.get("DEV_DISCORD_APPLICATION_ID", ""), STACK_DISCORD_ADMIN_ROLE_ID=env.get("DEV_DISCORD_ADMIN_ROLE_ID", ""),
                STACK_AUTH_REQUIRED="enabled", STACK_PROVISIONING_FILE=DEV_PROVISIONING, **login_env(password))


def wait_bot_ready(container):
    """Until the container's Requestarr says its bot set up the server (as dev.ps1's check reads it)."""
    for _ in range(BOT_READY_TRIES):
        logs = subprocess.run(["docker", "logs", container], capture_output=True, text=True, encoding="utf-8", errors="replace")
        if BOT_READY in logs.stdout + logs.stderr:
            return
        time.sleep(BOT_READY_WAIT_SECONDS)
    sys.exit("%s's bot didn't set up the dev server." % container)


def copy_recordings_in(container):
    """The recorded Requestrr channels into a Requestarr's recordings folder (compose.yaml's REQUESTARR_IMPORT_RECORDINGS),
    which the Import page lists as a server of its own. They go with its data."""
    docker("cp", os.path.join(RECORDINGS, "."), container + ":" + DEV_RECORDINGS)
    docker("exec", "-u", "0", container, "chown", "-R", "1654:1654", DEV_RECORDINGS)


def login_env(password):
    return {"DEV_AUTH_REQUIRED": "enabled", "REQUESTARR_ADMIN_USERNAME": ACCOUNT, "REQUESTARR_ADMIN_PASSWORD": password}


def web_env_for(env, web_dir, password, url, which):
    os.makedirs(web_dir, exist_ok=True)
    return dict(env, QA_CHECKS_URL=url, QA_CHECKS_DIR=web_dir, QA_CHECKS_USERNAME=ACCOUNT,
                QA_CHECKS_PASSWORD=password, QA_CHECKS_VERSION=running_build(which))


def web_results(web_dir):
    results = {}
    for name in os.listdir(web_dir):
        if name.endswith(".json"):
            with open(os.path.join(web_dir, name), encoding="utf-8") as handle:
                results[name[:-5]] = json.load(handle)
    return results


def start_throwaway():
    """The throwaway Requestarr up and answering: its address and API key for the checks."""
    remove_throwaway()
    key = secrets.token_hex(16)
    # Its own network carries the browser's port, so a check can cut it off from the dev stack's and back.
    docker("network", "create", THROWAWAY)
    docker("run", "-d", "--name", THROWAWAY, "--network", THROWAWAY, "-p", "127.0.0.1:%s:9797" % THROWAWAY_PORT,
           "-e", "TZ=Etc/UTC", "-e", "Requestarr__Auth__ApiKey=" + key, "-e", "Requestarr__Auth__Method=External", "requestarr:dev")
    docker("network", "connect", DEV_NETWORK, THROWAWAY)
    url = "http://localhost:" + THROWAWAY_PORT
    for _ in range(THROWAWAY_TRIES):
        try:
            with urllib.request.urlopen(url + "/ping", timeout=5):
                print("[qa-checks] throwaway Requestarr up at %s" % url, flush=True)
                return {"QA_CHECKS_THROWAWAY_URL": url}
        except OSError:
            time.sleep(THROWAWAY_WAIT_SECONDS)
    sys.exit("The throwaway Requestarr didn't start.")


def remove_throwaway():
    """The throwaway Requestarr and its data gone (nothing when there's none)."""
    subprocess.run(["docker", "rm", "-f", "-v", THROWAWAY], capture_output=True)
    subprocess.run(["docker", "network", "rm", THROWAWAY], capture_output=True)


def gh(*args):
    return subprocess.run(["gh"] + list(args), capture_output=True, text=True, encoding="utf-8", check=True, cwd=REPO).stdout


def gh_until(good, *args):
    """gh's answer once good(answer), tried again while gh fails or it isn't."""
    for _ in range(GH_TRIES):
        answer = subprocess.run(["gh"] + list(args), capture_output=True, text=True, encoding="utf-8", cwd=REPO)
        if answer.returncode == 0 and good(answer.stdout):
            return answer.stdout
        print("[qa-checks] gh %s: trying again. %s" % (" ".join(args), answer.stderr.strip()), flush=True)
        time.sleep(GH_WAIT_SECONDS)
    sys.exit("GitHub didn't answer: gh %s" % " ".join(args))


def suite_runs(commit):
    """The full suite's runs by hand on this commit, newest first."""
    return json.loads(gh("run", "list", "--workflow", SUITE_WORKFLOW, "--event", "workflow_dispatch", "--commit", commit,
                         "--json", "databaseId,status,conclusion"))


def usable_run(runs):
    """The newest run that passed or is still going: its number, or None."""
    return next((r["databaseId"] for r in runs if r["status"] != "completed" or r["conclusion"] == "success"), None)


def suite_ref(commit):
    """What GitHub can run the commit from: dev while it's dev's newest, otherwise the commit's build number tag."""
    git("fetch", "-q", "--tags", "origin", "dev")
    if git("rev-parse", "origin/dev") == commit:
        return "dev"
    numbered = [tag for tag in git("tag", "--points-at", commit).split() if VERSION_TAG.fullmatch(tag)]
    if not numbered:
        sys.exit("%s isn't dev's newest commit and has no build number, so GitHub can't run the full suite on it." % commit[:7])
    return numbered[0]


def start_suite(commit):
    """The full suite started on GitHub for this commit (or the run already there): the run's number."""
    earlier = suite_runs(commit)
    found = usable_run(earlier)
    if found:
        return found
    known = {r["databaseId"] for r in earlier}
    gh("workflow", "run", SUITE_WORKFLOW, "--ref", suite_ref(commit))
    deadline = time.monotonic() + RUN_LISTED_SECONDS
    while time.monotonic() < deadline:
        time.sleep(5)
        started = [r["databaseId"] for r in suite_runs(commit) if r["databaseId"] not in known]
        if started:
            return started[0]
    sys.exit("GitHub hasn't listed the full suite's run on %s: see the Build workflow's runs." % commit[:7])


def finish_suite(run_id, out):
    """Waits for the full suite's run: {passed, failed, skipped, ok, url}. Its output goes in suite.log."""
    view = ("run", "view", str(run_id))
    result = {"status": ""}
    while result["status"] != "completed":  # watch ends early when the connection drops
        subprocess.run(["gh", "run", "watch", str(run_id), "--interval", "30"], stdout=subprocess.DEVNULL, cwd=REPO)
        time.sleep(5)
        result = json.loads(gh_until(bool, *view, "--json", "status,conclusion,url"))
    passed = result["conclusion"] == "success"
    # A run that passed has its totals in the log; one that failed may have stopped before its tests.
    output = gh_until(SUITE_TOTALS.search if passed else bool, *view, "--log")
    with open(os.path.join(out, "suite.log"), "w", encoding="utf-8") as log:
        log.write(output)
    counts = suite_counts(output)
    counts.update(ok=passed and counts["failed"] == 0 and counts["passed"] > 0, url=result["url"])
    print("[qa-checks] full suite: %(passed)d passed, %(failed)d failed, %(skipped)d skipped (%(url)s)" % counts, flush=True)
    return counts


def suite_counts(output):
    """The totals the full suite's log ends with; all 0 when its tests didn't get that far."""
    found = SUITE_TOTALS.search(output)
    failed, passed, skipped = map(int, found.groups()) if found else (0, 0, 0)
    return {"passed": passed, "failed": failed, "skipped": skipped}


def build_label(which, commit):
    """How a stack labels this commit's build: 0.7.0008-dev for a numbered commit, 0.7.0008-dev.4f2a9c1 for one between
    goals."""
    return subprocess.run([sys.executable, os.path.join(REPO, ".github", "build_numbers.py"), "label", which, commit],
                          capture_output=True, text=True, check=True, cwd=REPO).stdout.strip()


def live_env_for(out, env, commit):
    """The live checks run as the dev build of this commit, labelled the way the dev stack labels it."""
    return dict(env, LIVE_QA_STATE_DIR=os.path.join(out, "live", "state"), REQUESTARR_RELEASE_VERSION=build_label("dev", commit))


def run_one_live(fixture, out, live_env):
    log_path = os.path.join(out, "live", fixture + ".log")
    passed = dotnet_test("FullyQualifiedName~" + fixture, live_env, log_path)
    print("[qa-checks] %s: %s" % (fixture, "passed" if passed else "FAILED"), flush=True)
    with open(log_path, encoding="utf-8") as handle:
        return passed, handle.read()


class Bots:
    """Hands the lanes' threads turns with the bot: at most MAX_BOTS at once, signing in SIGN_IN_APART_SECONDS apart."""

    def __init__(self):
        self._held = threading.BoundedSemaphore(MAX_BOTS)
        self._sign_in = threading.Lock()
        self._last = float("-inf")

    @contextlib.contextmanager
    def turn(self):
        with self._held:
            with self._sign_in:
                time.sleep(max(0.0, self._last + SIGN_IN_APART_SECONDS - time.monotonic()))
                self._last = time.monotonic()
            yield


def run_live(out, env, commit, web_done):
    """Every lane at once: {fixture: (passed, log)}. web_done is set once the web checks have ended."""
    os.makedirs(os.path.join(out, "live"), exist_ok=True)
    live_env = live_env_for(out, env, commit)
    bots = Bots()
    with ThreadPoolExecutor(max_workers=len(LIVE_LANES)) as pool:
        lanes = [pool.submit(run_lane, number, fixtures, out, live_env, web_done, bots) for number, fixtures in enumerate(LIVE_LANES, 1)]
        return {fixture: result for lane in lanes for fixture, result in lane.result().items()}


def lane_env(number, live_env):
    """A lane's checks: its prefix on every Discord name, and its own test Sonarr and Radarr."""
    radarr, sonarr = (port + number for port in LANE_PORTS)
    return dict(live_env, QA_CHECKS_LANE=LANE_PREFIX % number, LIVE_COMPOSE_PROJECT=LANE_PROJECT % number,
                DEV_RADARR_TEST_PORT=str(radarr), DEV_SONARR_TEST_PORT=str(sonarr), LIVE_PYTHON=sys.executable)


def run_lane(number, fixtures, out, live_env, web_done, bots):
    """A lane's checks one after another. One its test Sonarr and Radarr failed for fails with Docker's words, and the
    lane goes on: the other lanes' results are kept either way."""
    env = lane_env(number, live_env)
    backends = not NO_BACKENDS.issuperset(fixtures)
    results = {}
    for index, fixture in enumerate(fixtures):
        if fixture in AFTER_WEB:
            web_done.wait()
        try:
            if backends and (index == 0 or fixture in EMPTY_FIRST):
                empty_lane_backends(env)
        except subprocess.CalledProcessError as error:
            results[fixture] = backends_failed(fixture, out, error)
            continue
        with contextlib.nullcontext() if fixture in NO_SIGN_IN else bots.turn():
            results[fixture] = run_one_live(fixture, out, env)
    if backends:
        # Gone with its volumes; a failure here only leaves containers for the next run's reset to replace.
        subprocess.run(lane_compose_args(env, "down", "-v"), env=lane_compose_env(env), capture_output=True)
    return results


def backends_failed(fixture, out, error):
    log = "The lane's test Sonarr and Radarr didn't start: %s\n%s" % (" ".join(error.cmd), error.stderr or "")
    with open(os.path.join(out, "live", fixture + ".log"), "w", encoding="utf-8") as handle:
        handle.write(log)
    print("[qa-checks] %s: FAILED (its test Sonarr and Radarr didn't start)" % fixture, flush=True)
    return False, log


def lane_compose_args(env, *args):
    """docker compose on a lane's test Sonarr and Radarr: compose.yaml's, under the lane's project."""
    return ["docker", "compose", "-p", env["LIVE_COMPOSE_PROJECT"], "-f", COMPOSE] + list(args)


def lane_compose_env(env):
    """compose.yaml's variables for a lane: its ports (its Requestarr is never started)."""
    return dict(env, REQUESTARR_STACK="dev", STACK_REQUESTARR_PORT="0",
                STACK_RADARR_TEST_PORT=env["DEV_RADARR_TEST_PORT"], STACK_SONARR_TEST_PORT=env["DEV_SONARR_TEST_PORT"])


def lane_compose(env, *args):
    subprocess.run(lane_compose_args(env, *args), env=lane_compose_env(env), check=True, capture_output=True, text=True)


def empty_lane_backends(env):
    """A lane's test Sonarr and Radarr started again from nothing, as dev.ps1 reset-test-backends does the stack's."""
    project = env["LIVE_COMPOSE_PROJECT"]
    lane_compose(env, "rm", "-sf", *TEST_BACKENDS)
    subprocess.run(["docker", "volume", "rm"] + ["%s_%s" % (project, volume) for volume in TEST_BACKEND_VOLUMES], capture_output=True)
    lane_compose(env, "up", "-d", "--wait", *TEST_BACKENDS)


def clean_lanes_in_discord(env):
    """What the lanes made in the dev server (every name with a lane's prefix) removed: before a run, in case an earlier
    one stopped halfway, and after it."""
    subprocess.run([sys.executable, os.path.join(HERE, "reset-discord.py"), "--lanes"], check=True,
                   env=dict(env, STACK_DISCORD_BOT_TOKEN=env["DEV_DISCORD_BOT_TOKEN"], STACK_DISCORD_GUILD_ID=env["DEV_DISCORD_GUILD_ID"]))


def reset_dev_after(env):
    """The dev server and stack back to a clean start after a run, the lanes' leftovers first."""
    try:
        clean_lanes_in_discord(env)
    finally:
        stack(["reset-dev"])


def run_all_web(out, env, commit, timings):
    """The dev stack up at the commit (which builds the image), then side by side: the web checks on the owner's dev data
    there, and those that need a Requestarr just set up on a second one."""
    password = secrets.token_urlsafe(24)
    timed("dev stack up at the commit, signed in", timings, lambda: stack(["up"], login_env(password), ref=commit))
    with ThreadPoolExecutor(max_workers=1) as pool:
        fresh = pool.submit(timed, "web app checks on a Requestarr just set up (the test member's made-up history)", timings,
                            lambda: run_web_fresh(out, env))
        timed("web app checks on the owner's data (its marked requests seeded and removed)", timings, lambda: run_web(out, env, password))
        fresh.result()
    return web_results(os.path.join(out, "web"))


DEV_CONTAINER = "requestarr-dev-requestarr-1"
# dev.ps1's copy of the provisioning file as the commit it built has it.
DEV_PROVISIONING = os.path.join(HERE, ".stacks", "dev-provisioning.yml")
DEV_DATABASE = "/config/requestarr.db"
DEV_RECORDINGS = "/config/import-recordings"

# The made-up owner and title prefix of the requests seeded for the web checks (seed_check_rows), and a made-up person
# who asks for one title and follows another (the web app request views check filters their page).
SEED_OWNER = "Automated check"
SEED_FOLLOWER = "Automated check follower"
SEED_TITLE = "Automated check: "
SEED_PEOPLE = (SEED_OWNER, SEED_FOLLOWER)
# The request views check's requests by name (after SEED_TITLE): what the follower follows, their own, and the one in the
# test Sonarr. QaWebChecksFixture names them the same.
FOLLOWED = "followed"
FOLLOWERS_OWN = "asked by the follower"
IN_SONARR = "open in Sonarr"

# The series the request views check opens in the dev stack's test Sonarr (Severance on TheTVDB, which the live checks
# request too), added through Sonarr's API for the web checks and deleted after them. The provisioning file names that
# Sonarr by its address in the stack; its port on this PC is the stack's (docker/dev.ps1).
CHECK_SERIES_TVDB = 371980
TEST_SONARR_ADDRESS = "http://sonarr-test:8989"
TEST_SONARR_ROOT = "/data/tv"
TEST_SONARR_PROFILE = "HD-1080p"
DEFAULT_SONARR_TEST_PORT = "38989"

# A made-up server among the ones Requestarr remembers (the Config table's discordknownservers, one list per bot): the
# bot isn't in it, so it stands for a server that removed the bot (the web app server cards check).
MADE_UP_SERVER = {"id": "900000000000300001", "name": "Automated check server", "iconUrl": None}

# What the checks write in the dev stack's audit log: everything the web checks do, signed in as ACCOUNT, and anything
# about the made-up "Automated check" titles and people. The owner's own entries (their sign-in, Discord, the bot) and
# the QA checklist's results stay.
CHECK_ENTRIES = "Actor = ? OR Actor LIKE ? OR Detail LIKE '%Automated check%'"


def docker(*args):
    subprocess.run(["docker"] + list(args), check=True, capture_output=True)


def edit_dev_database(edit, container=DEV_CONTAINER):
    """Runs EDIT(connection) on a dev Requestarr's database (the dev stack's, or the run's second one), Requestarr stopped
    for it and always started again, and waits until it's healthy; returns what EDIT returned. Nothing when there's no
    such Requestarr (a reset removed it)."""
    if subprocess.run(["docker", "inspect", container], capture_output=True).returncode != 0:
        print("[qa-checks] no %s: its data left as it is" % container, flush=True)
        return None
    with tempfile.TemporaryDirectory() as folder:
        local = os.path.join(folder, "requestarr.db")
        docker("stop", container)
        try:
            copy_database_out(local, container)
            con = sqlite3.connect(local)
            try:
                done = edit(con)
                con.commit()
                con.execute("pragma journal_mode=delete")
            finally:
                con.close()
            copy_database_in(local, folder, container)
        finally:
            docker("start", container)
        docker("exec", "-u", "0", container, "sh", "-c", "chown 1654:1654 %s* && chmod 644 %s*" % (DEV_DATABASE, DEV_DATABASE))
        docker("restart", container)
    wait_healthy(container)
    return done


def wait_healthy(container, tries=120):
    for _ in range(tries):
        state = subprocess.run(["docker", "inspect", "-f", "{{.State.Health.Status}}", container], capture_output=True, text=True).stdout.strip()
        if state == "healthy":
            return
        time.sleep(2)
    sys.exit("%s didn't come back healthy after its data was edited" % container)


def seed_check_rows(con, series=None):
    """The requests the web checks act on, titled "Automated check: ...", owned by made-up people known only from an
    import (so the bot never contacts them): one waiting to be approved (it goes to the test Radarr), one waiting for
    the live updates check to deny from outside the page, three history rows for select mode, one for a row's Delete
    record, and for the request views check a denied one the made-up follower follows, the follower's own (not in the
    library) and, given SERIES (add_check_series), one in the test Sonarr. How many were added."""
    now = time.strftime("%Y-%m-%d %H:%M:%SZ", time.gmtime())
    owner, follower = (con.execute("INSERT INTO RequestarrUsers (DisplayName, Status, Added, StatusChanged) VALUES (?, 3, ?, ?)",
                                   (name, now, now)).lastrowid for name in SEED_PEOPLE)
    radarr = con.execute("SELECT Id FROM Backends WHERE Implementation = 'Radarr' ORDER BY Id LIMIT 1").fetchone()
    radarr = radarr[0] if radarr else None
    rows = [(owner, "approve", 0, 10386, 1999, radarr), (owner, "live update", 0, 105, 1985, radarr)]
    rows += [(owner, "history", 7, None, 2001 + n, None) for n in range(3)] + [(owner, "delete one", 7, None, 2010, None)]
    rows += [(owner, FOLLOWED, 2, None, 2015, None), (follower, FOLLOWERS_OWN, 7, None, 2016, None)]
    for person, name, status, tmdb, year, backend in rows:
        con.execute("INSERT INTO Requests (OwnerUserId, ContentType, Title, Year, TmdbId, Status, BackendId, MonitorFutureSeasons, Requested, Progress) "
                    "VALUES (?, 1, ?, ?, ?, ?, ?, 0, ?, 0)", (person, SEED_TITLE + name, year, tmdb, status, backend, now))
    followed = con.execute("SELECT Id FROM Requests WHERE Title = ?", (SEED_TITLE + FOLLOWED,)).fetchone()[0]
    con.execute("INSERT INTO RequestSubscribers (RequestId, UserId, Added) VALUES (?, ?, ?)", (followed, follower, now))
    known = known_servers(con)
    if known:
        set_known_servers(con, known + [dict(MADE_UP_SERVER, bot=known[0]["bot"])])
    return len(rows) + seed_sonarr_request(con, owner, series, now)


def seed_sonarr_request(con, owner, series, now):
    """The owner's request for SERIES as one sent to the test Sonarr is once it's there: in that Sonarr's library under
    its ID, with its page there known (what Open in Sonarr opens). How many were added: none without the series or that
    Sonarr."""
    backend = con.execute("SELECT Id FROM Backends WHERE Implementation = 'Sonarr' AND Settings LIKE ? ORDER BY Id LIMIT 1",
                          ("%" + TEST_SONARR_ADDRESS + "%",)).fetchone()
    if not series or not backend:
        return 0
    media = json.dumps({"overview": series["overview"], "titleSlug": series["titleSlug"], "checked": now})
    con.execute("INSERT INTO Requests (OwnerUserId, ContentType, Title, Year, TvdbId, Status, BackendId, ExternalItemId, Seasons, MonitorFutureSeasons, "
                "Requested, Progress, Media) VALUES (?, 2, ?, ?, ?, 5, ?, ?, '[1]', 0, ?, 0, ?)",
                (owner, SEED_TITLE + IN_SONARR, series["year"], series["tvdbId"], backend[0], series["id"], now, media))
    return 1


def sonarr_api(env):
    """The dev stack's test Sonarr's API, as this PC reaches it, and its key."""
    return "http://localhost:%s/api/v3/" % env.get("DEV_SONARR_TEST_PORT", DEFAULT_SONARR_TEST_PORT), env.get("SONARR_TEST_API_KEY", "")


def add_check_series(env):
    """CHECK_SERIES_TVDB in the dev stack's test Sonarr: its Sonarr ID, slug, title, year and overview, and whether this
    run added it (one there already is used as it is). None, with why, when that Sonarr can't be reached or can't look
    the series up; the check that opens it then fails, saying so."""
    url, key = sonarr_api(env)
    try:
        found = [s for s in api(url, key, "series?tvdbId=%d" % CHECK_SERIES_TVDB) if s.get("tvdbId") == CHECK_SERIES_TVDB]
        series, added = (found[0], False) if found else (add_series(url, key), True)
    except (OSError, ValueError, LookupError) as error:
        print("[qa-checks] the test Sonarr couldn't add the series the checks open: %s" % error, flush=True)
        return None
    print("[qa-checks] %s in the test Sonarr for the web checks" % series["title"], flush=True)
    return {"id": series["id"], "titleSlug": series["titleSlug"], "title": series["title"], "year": series.get("year"),
            "overview": series.get("overview"), "tvdbId": CHECK_SERIES_TVDB, "added": added}


def add_series(url, key):
    """The series looked up in Sonarr and added there with nothing monitored and nothing searched for."""
    looked_up = api(url, key, "series/lookup?term=tvdb:%d" % CHECK_SERIES_TVDB)[0]
    profiles = api(url, key, "qualityprofile")
    profile = next((p for p in profiles if p["name"] == TEST_SONARR_PROFILE), profiles[0])
    return api(url, key, "series", dict(looked_up, qualityProfileId=profile["id"], rootFolderPath=TEST_SONARR_ROOT, monitored=False,
                                        seasonFolder=True, monitorNewItems="none",
                                        addOptions={"monitor": "none", "searchForMissingEpisodes": False, "searchForCutoffUnmetEpisodes": False}))


def remove_check_series(env, series):
    """The series add_check_series added deleted from the test Sonarr again (one that was there already stays). The run
    resets the dev stack right after the web checks, which empties that Sonarr anyway; this keeps a series with no request
    out of the dev stack's library meanwhile, and when the run stops before its reset."""
    if not series or not series["added"]:
        return
    url, key = sonarr_api(env)
    request = urllib.request.Request("%sseries/%d?deleteFiles=true" % (url, series["id"]), method="DELETE", headers={"X-Api-Key": key})
    try:
        with urllib.request.urlopen(request, timeout=60):
            print("[qa-checks] %s deleted from the test Sonarr again" % series["title"], flush=True)
    except OSError as error:
        print("[qa-checks] %s stays in the test Sonarr until the dev stack's reset: %s" % (series["title"], error), flush=True)


def known_servers(con):
    row = con.execute("SELECT Value FROM Config WHERE Key = 'discordknownservers'").fetchone()
    return json.loads(row[0]) if row and row[0] else []


def set_known_servers(con, servers):
    con.execute("UPDATE Config SET Value = ? WHERE Key = 'discordknownservers'", (json.dumps(servers),))


def test_member(env):
    """The first of DISCORD_TEST_MEMBERS as the dev server knows them: their Discord ID and username (read, never
    changed)."""
    username = env["DISCORD_TEST_MEMBERS"].split(",")[0].strip()
    request = urllib.request.Request("%s/guilds/%s/members/search?query=%s&limit=10" % (DISCORD_API, env["DEV_DISCORD_GUILD_ID"], urllib.parse.quote(username)),
                                     headers={"Authorization": "Bot " + env["DEV_DISCORD_BOT_TOKEN"], "User-Agent": "DiscordBot (https://github.com/Requestarr/Requestarr, 1.0)"})
    with urllib.request.urlopen(request, timeout=30) as response:
        found = [m["user"] for m in json.load(response) if m["user"]["username"] == username]
    if not found:
        sys.exit("The test member %s isn't in the dev server." % username)
    return {"id": found[0]["id"], "username": username}


def seed_history_rows(con, member):
    """The test member known only from an import, as Requestrr's history makes people, with HISTORY_SEEDED old requests
    of their own (marked "Automated check: ..."), so Post History has theirs to post and nobody else's."""
    now = time.strftime("%Y-%m-%d %H:%M:%SZ", time.gmtime())
    user = con.execute("INSERT INTO RequestarrUsers (DisplayName, Status, Added, StatusChanged, ImportedFrom) VALUES (?, 3, ?, ?, 'Requestrr')",
                       (member["username"], now, now)).lastrowid
    con.execute("INSERT INTO UserIdentities (UserId, Provider, ExternalId, Username, DisplayName, Linked) VALUES (?, 'Discord', ?, ?, ?, ?)",
                (user, member["id"], member["username"], member["username"], now))
    for n in range(HISTORY_SEEDED):
        con.execute("INSERT INTO Requests (OwnerUserId, ContentType, Title, Year, Status, MonitorFutureSeasons, Requested, Progress, Imported) "
                    "VALUES (?, 1, ?, ?, 7, 0, ?, 0, 1)", (user, SEED_TITLE + "post history %d" % (n + 1), 1990 + n, now))


def remove_check_data(con):
    """What the checks made in the dev stack's data, the owner's own left as it is: the seeded requests and the made-up
    people, what was done to them (follows, removals with their keeps and prompts), removals the checks' sign-in started,
    and the audit entries the checks wrote. How many audit entries went."""
    people = [r[0] for r in con.execute("SELECT Id FROM RequestarrUsers WHERE DisplayName IN (%s) AND ImportedFrom IS NULL" % marks(SEED_PEOPLE), SEED_PEOPLE)]
    requests = [r[0] for r in con.execute("SELECT Id FROM Requests WHERE Title LIKE ?", (SEED_TITLE + "%",))]
    removals = [r[0] for r in con.execute("SELECT Id FROM RemovalProposals WHERE RequestId IN (%s) OR InitiatedByName LIKE ?" % marks(requests),
                                          requests + [ACCOUNT + "%"])]
    for table in ("RemovalFlags", "RemovalPrompts"):
        con.execute("DELETE FROM %s WHERE ProposalId IN (%s)" % (table, marks(removals)), removals)
    con.execute("DELETE FROM RemovalProposals WHERE Id IN (%s)" % marks(removals), removals)
    con.execute("DELETE FROM RequestSubscribers WHERE RequestId IN (%s) OR UserId IN (%s)" % (marks(requests), marks(people)), requests + people)
    con.execute("DELETE FROM Requests WHERE Id IN (%s)" % marks(requests), requests)
    for table in ("UserMediaAccess", "UserIdentities"):
        con.execute("DELETE FROM %s WHERE UserId IN (%s)" % (table, marks(people)), people)
    con.execute("DELETE FROM RequestarrUsers WHERE Id IN (%s)" % marks(people), people)
    set_known_servers(con, [s for s in known_servers(con) if s["id"] != MADE_UP_SERVER["id"]])
    return con.execute("DELETE FROM AuditEntries WHERE " + CHECK_ENTRIES, ("web:" + ACCOUNT, "web:" + ACCOUNT + " %")).rowcount


def marks(ids):
    return ",".join("?" * len(ids)) or "NULL"


def clean_check_entries():
    """The checks' data removed from the dev stack (the head's rule: test data never stays in the owner's dev data)."""
    removed = edit_dev_database(remove_check_data)
    if removed is not None:
        print("[qa-checks] removed what the checks made on dev, %d audit entries with it" % removed, flush=True)
    return removed


def copy_database_out(local, container):
    """A stopped Requestarr's database to LOCAL, with its write-ahead log when it has one."""
    docker("cp", container + ":" + DEV_DATABASE, local)
    for suffix in ("-wal", "-shm"):
        try:
            docker("cp", container + ":" + DEV_DATABASE + suffix, local + suffix)
        except subprocess.CalledProcessError:
            pass  # no write-ahead log: everything is in the database file


def copy_database_in(local, folder, container):
    """LOCAL back as a Requestarr's database, its old write-ahead log emptied so it isn't replayed over the edit."""
    empty = os.path.join(folder, "empty")
    open(empty, "w").close()
    docker("cp", local, container + ":" + DEV_DATABASE)
    for suffix in ("-wal", "-shm"):
        docker("cp", empty, container + ":" + DEV_DATABASE + suffix)


def run(args):
    commit = git("rev-parse", option(args, "--commit") or "origin/qa")
    if git("rev-parse", "HEAD") != commit:
        sys.exit("The checkout is at %s, not %s: the checks are built from the checkout. Run git switch --detach %s first."
                 % (git("rev-parse", "--short", "HEAD"), commit[:7], commit[:7]))
    out = os.path.abspath(option(args, "--out") or os.path.join(REPO, "_qa-checks", commit[:7]))
    only = option(args, "--only")
    timings, web, live, suite = [], {}, {}, None
    started = time.monotonic()
    if only == "suite":
        os.makedirs(out, exist_ok=True)
        return 0 if timed("full suite", timings, lambda: finish_suite(start_suite(commit), out))["ok"] else 1
    # A run starts empty, so nothing an earlier run left can stand in for a check that didn't run this time.
    shutil.rmtree(out, ignore_errors=True)
    os.makedirs(out, exist_ok=True)
    suite_run = start_suite(commit) if only is None else None
    env = dict(os.environ, **secrets_env())
    if only in (None, "live", "web") and not env.get("DISCORD_TEST_MEMBERS"):
        sys.exit("The live checks press buttons as a test member: set DISCORD_TEST_MEMBERS (in your shell or docker/secrets.env) first.")
    with open(os.path.join(REPO, "QA-CHECKLIST.md"), encoding="utf-8") as handle:
        items = checklist_items(handle.read())
    try:
        timed("build", timings, lambda: subprocess.run(["dotnet", "build", os.path.join(REPO, "src", "Requestarr.sln"), "-v", "q", "-nologo"], check=True))
        clean_lanes_in_discord(env)
        web_done = threading.Event()
        with ThreadPoolExecutor(max_workers=2) as pool:
            live_part = None
            if only in (None, "live"):
                live_part = pool.submit(timed, "live Discord checks (dev server, bot and test member, in %d lanes)" % len(LIVE_LANES),
                                        timings, lambda: run_live(out, env, commit, web_done))
            try:
                web = run_all_web(out, env, commit, timings) if only in (None, "web") else {}
            finally:
                web_done.set()
            live = live_part.result() if live_part else {}
    finally:
        timed("dev stack reset", timings, lambda: reset_dev_after(env))
    if only is None:
        live[LIVE_ON_RUNNING_STACK] = timed("read-back of the running dev stack", timings,
                                            lambda: run_one_live(LIVE_ON_RUNNING_STACK, out, live_env_for(out, env, commit)))
        suite = timed("full suite (the rest of its run on GitHub)", timings, lambda: finish_suite(suite_run, out))
    results = results_for(items, web, live)
    # The parts overlap, so the run's time is the clock's, not the sum of theirs.
    total = time.monotonic() - started
    with open(os.path.join(out, "results.json"), "w", encoding="utf-8") as handle:
        json.dump({"commit": commit, "only": only, "suite": suite, "live": {f: passed for f, (passed, _) in live.items()},
                   "results": results, "reviewed": False, "timings": timings, "seconds": round(total)}, handle, indent=2)
    report(results, out, total)
    return 0


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
    if running != build_label(which, run_data["commit"]):
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
    lines = ["- **Full suite:** %(passed)d passed, %(failed)d failed, %(skipped)d skipped (%(url)s)." % suite,
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


SUITE_OUTPUT = """Tests	Test	2026-11-24T06:08:59Z Passed!  - Failed:     0, Passed:   682, Skipped:    14, Total:   696 - Requestarr.Common.Test.dll
Tests	Test	2026-11-24T06:08:59Z Tests (full): 1 failed, 683 passed, 14 skipped"""


def a_ready_run():
    return {"commit": "abc", "only": None, "suite": {"passed": 683, "failed": 0, "skipped": 14, "ok": True, "url": "the run"},
            "live": {f: True for f in LIVE + [LIVE_ON_RUNNING_STACK]}, "reviewed": True, "seconds": 1800,
            "results": [{"name": "Build number", "check": "web app status page", "passed": True, "evidence": "Version shown 0.7.0005-dev.abc\nmore"}]}


# The tables seed_check_rows and remove_check_data touch, with the columns they use.
TEST_SCHEMA = """
CREATE TABLE RequestarrUsers (Id INTEGER PRIMARY KEY, DisplayName TEXT, Status INTEGER, Added TEXT, StatusChanged TEXT, ImportedFrom TEXT);
CREATE TABLE Backends (Id INTEGER PRIMARY KEY, Implementation TEXT, Settings TEXT);
CREATE TABLE Requests (Id INTEGER PRIMARY KEY, OwnerUserId INTEGER, ContentType INTEGER, Title TEXT, Year INTEGER, TmdbId INTEGER,
                       TvdbId INTEGER, Status INTEGER, BackendId INTEGER, ExternalItemId INTEGER, Seasons TEXT, MonitorFutureSeasons INTEGER,
                       Requested TEXT, Progress INTEGER, Media TEXT);
CREATE TABLE RemovalProposals (Id INTEGER PRIMARY KEY, RequestId INTEGER, InitiatedByName TEXT);
CREATE TABLE RemovalFlags (Id INTEGER PRIMARY KEY, ProposalId INTEGER);
CREATE TABLE RemovalPrompts (Id INTEGER PRIMARY KEY, ProposalId INTEGER);
CREATE TABLE RequestSubscribers (Id INTEGER PRIMARY KEY, RequestId INTEGER, UserId INTEGER, Added TEXT);
CREATE TABLE UserMediaAccess (Id INTEGER PRIMARY KEY, UserId INTEGER);
CREATE TABLE UserIdentities (Id INTEGER PRIMARY KEY, UserId INTEGER);
CREATE TABLE AuditEntries (Id INTEGER PRIMARY KEY, Actor TEXT, Detail TEXT);
CREATE TABLE Config (Id INTEGER PRIMARY KEY, Key TEXT, Value TEXT);
"""


def seeded_and_cleaned(series=None):
    """A database with the owner's own person, request, removal and audit entry, seeded for the checks (with SERIES in the
    test Sonarr), acted on as the checks do, then cleaned: how many were seeded and how many audit entries went, what's
    left in each table, the remembered servers before and after, and what the request views check reads (who follows
    what, and the request in the test Sonarr)."""
    con = sqlite3.connect(":memory:")
    con.executescript(TEST_SCHEMA)
    con.execute("INSERT INTO Backends (Implementation) VALUES ('Radarr')")
    con.execute("INSERT INTO Backends (Implementation, Settings) VALUES ('Sonarr', ?)", (json.dumps({"url": TEST_SONARR_ADDRESS}),))
    con.execute("INSERT INTO RequestarrUsers (DisplayName, ImportedFrom) VALUES ('Owner', '#media-requests')")
    con.execute("INSERT INTO Requests (OwnerUserId, Title) VALUES (1, 'Heat')")
    con.execute("INSERT INTO RemovalProposals (RequestId, InitiatedByName) VALUES (1, 'owner (web app)')")
    con.execute("INSERT INTO AuditEntries (Actor, Detail) VALUES ('web:owner', 'Heat dropped')")
    con.execute("INSERT INTO Config (Key, Value) VALUES ('discordknownservers', ?)", (json.dumps([{"bot": "42", "id": "7", "name": "Original", "iconUrl": None}]),))
    seeded = seed_check_rows(con, series)
    with_made_up = [k["id"] for k in known_servers(con)]
    read = {"follows": con.execute("SELECT f.DisplayName, r.Title, r.Status FROM RequestSubscribers s JOIN RequestarrUsers f ON f.Id = s.UserId "
                                   "JOIN Requests r ON r.Id = s.RequestId").fetchall(),
            "in sonarr": con.execute("SELECT BackendId, ExternalItemId, TvdbId, json_extract(Media, '$.titleSlug') FROM Requests WHERE Title = ?",
                                     (SEED_TITLE + IN_SONARR,)).fetchall()}
    con.execute("INSERT INTO RemovalProposals (RequestId, InitiatedByName) VALUES (2, 'qa-checks (web app)')")
    con.execute("INSERT INTO RemovalFlags (ProposalId) VALUES (2)")
    con.execute("INSERT INTO RequestSubscribers (RequestId, UserId) VALUES (3, 2)")
    con.execute("INSERT INTO AuditEntries (Actor, Detail) VALUES ('web:qa-checks', 'Automated check: history (2001) dropped')")
    removed = remove_check_data(con)
    left = {t: con.execute("SELECT COUNT(*) FROM %s" % t).fetchone()[0]
            for t in ("RequestarrUsers", "Requests", "RemovalProposals", "RemovalFlags", "RequestSubscribers", "AuditEntries")}
    return seeded, removed, left, (with_made_up, [k["id"] for k in known_servers(con)]), read


def self_test():
    ready = a_ready_run()
    items = checklist_items(SAMPLE)
    web = {"status_page": {"passed": True, "evidence": "Version 0.7.0005-qa.abc1234", "screenshots": ["s/status-light.jpg"]}}
    live = {"LiveQaFixture": (True, "noise\n[pass] the owner presses Passes\n[fail] not this one\n")}
    results = {r["name"]: r for r in results_for(items, web, live)}
    failed = apply_overrides([dict(results["Build number"])], {"Build number": "the logo is on black"})[0]
    lanes = [lane_env(n, {}) for n in range(1, len(LIVE_LANES) + 1)]
    lane_ports = {lane[port] for lane in lanes for port in ("DEV_RADARR_TEST_PORT", "DEV_SONARR_TEST_PORT")}
    with_series = seeded_and_cleaned({"id": 17, "titleSlug": "severance", "title": "Severance", "year": 2022, "overview": "Work.",
                                      "tvdbId": CHECK_SERIES_TVDB, "added": True})
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
        ("a suite that never got to its tests", suite_counts("The build failed") == {"passed": 0, "failed": 0, "skipped": 0}),
        ("a passed run of the suite is used again", usable_run([{"databaseId": 2, "status": "completed", "conclusion": "failure"},
                                                                {"databaseId": 1, "status": "completed", "conclusion": "success"}]) == 1),
        ("a run still going is waited for", usable_run([{"databaseId": 3, "status": "in_progress", "conclusion": ""}]) == 3),
        ("a failed run isn't used again", usable_run([{"databaseId": 2, "status": "completed", "conclusion": "failure"}]) is None),
        ("a ready run", not_ready(ready, "abc") == []),
        ("a failed item refuses", any("\"Build number\" failed" in r for r in not_ready(dict(ready, results=[dict(ready["results"][0], passed=False)]), "abc"))),
        ("a failed live check refuses", any("LiveQaFixture failed" in r for r in not_ready(dict(ready, live=dict(ready["live"], LiveQaFixture=False)), "abc"))),
        ("a partial run refuses", any("partial" in r for r in not_ready(dict(ready, only="web"), "abc"))),
        ("a failed suite refuses", any("suite" in r for r in not_ready(dict(ready, suite=dict(ready["suite"], ok=False)), "abc"))),
        ("screenshots not looked at refuses", any("screenshots" in r for r in not_ready(dict(ready, reviewed=False), "abc"))),
        ("another commit's run refuses", any("not def" in r for r in not_ready(ready, "def"))),
        ("every live check has read-backs to show", all(c.tags for c in CHECKS.values() if c.kind == "live")),
        ("every live check an item names runs", all(c.target in LIVE + [LIVE_ON_RUNNING_STACK] for c in CHECKS.values() if c.kind == "live")),
        ("each live check in one lane", len(LIVE) == len(set(LIVE)) and LIVE_ON_RUNNING_STACK not in LIVE),
        ("the lanes' lists name only lane checks", NO_BACKENDS | EMPTY_FIRST | NO_SIGN_IN | AFTER_WEB <= set(LIVE) and not NO_BACKENDS & EMPTY_FIRST),
        ("each lane its own names, project and ports", lanes[2]["QA_CHECKS_LANE"] == "lane3-" and lanes[2]["LIVE_COMPOSE_PROJECT"] == "requestarr-dev-lane3"
         and len(lane_ports | {"37878", "38989"}) == 2 * len(LIVE_LANES) + 2),
        ("the checks' requests seeded", seeded_and_cleaned()[0] == 8),
        ("the made-up follower follows the denied one", seeded_and_cleaned()[4]["follows"] == [(SEED_FOLLOWER, SEED_TITLE + FOLLOWED, 2)]),
        ("no request in the test Sonarr without its series", seeded_and_cleaned()[4]["in sonarr"] == []),
        ("with it, one in that Sonarr's library with its page known",
         with_series[0] == 9 and with_series[4]["in sonarr"] == [(2, 17, CHECK_SERIES_TVDB, "severance")]),
        ("a made-up server remembered for the checks, then forgotten", seeded_and_cleaned()[3] == (["7", MADE_UP_SERVER["id"]], ["7"])),
        ("and everything the checks did removed, the owner's own kept",
         with_series[1:3] == (1, {"RequestarrUsers": 1, "Requests": 1, "RemovalProposals": 1, "RemovalFlags": 0, "RequestSubscribers": 0, "AuditEntries": 1})),
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
        return run(argv[2:])
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
