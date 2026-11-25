#!/usr/bin/env python3
"""What the code scan found (docs/development.md#code-scan). The Code scan workflow runs it after the scan of REF.

On dev each open finding gets a GitHub issue labelled "security", unless one for it is open already. On any other branch,
and on a promotion's pull request, an open finding fails the check. A finding dismissed on GitHub's code scanning page
isn't open anywhere. Issues are public and findings aren't, so an issue says only which rule and links to the finding.

  --self-test  checks the parts that decide, against built-in samples.

Needs the GitHub CLI (GH_TOKEN, REPO).
"""
import json
import os
import subprocess
import sys

DEV = "refs/heads/dev"
LABEL = "security"
MOST_ISSUES = "1000"


def gh(*args):
    return subprocess.run(("gh",) + args, check=True, text=True, stdout=subprocess.PIPE).stdout


# ---- deciding ---------------------------------------------------------------------------------------------------

def title_start(finding):
    return "Code scan finding %d:" % finding["number"]


def title(finding):
    return "%s %s" % (title_start(finding), finding["rule"]["description"])


def severity(finding):
    return finding["rule"].get("security_severity_level") or finding["rule"].get("severity") or "unknown"


def body(finding):
    return ("The code scan of dev found a problem: **%s** (severity: %s).\n\n"
            "Where it is, for maintainers: %s\n\n"
            "Fix it, or dismiss it there with the reason, then close this issue.\n"
            % (finding["rule"]["description"], severity(finding), finding["html_url"]))


def without_issue(findings, open_titles):
    return [f for f in findings if not any(t.startswith(title_start(f)) for t in open_titles)]


# ---- GitHub -----------------------------------------------------------------------------------------------------

def open_findings(repo, ref):
    pages = gh("api", "--paginate", "--slurp", "repos/%s/code-scanning/alerts?state=open&per_page=100&ref=%s" % (repo, ref))
    return [finding for page in json.loads(pages) for finding in page]


def open_issue_titles(repo):
    issues = gh("issue", "list", "--repo", repo, "--state", "open", "--label", LABEL, "--limit", MOST_ISSUES, "--json", "title")
    return [issue["title"] for issue in json.loads(issues)]


def open_issues(repo, findings):
    for finding in without_issue(findings, open_issue_titles(repo)):
        print(gh("issue", "create", "--repo", repo, "--label", LABEL, "--title", title(finding), "--body", body(finding)).strip())


def main():
    repo, ref = os.environ["REPO"], os.environ["REF"]
    findings = open_findings(repo, ref)
    print("Open findings on %s: %d" % (ref, len(findings)))
    for finding in findings:
        print("- %s (%s)" % (title(finding), severity(finding)))
    if ref == DEV:
        open_issues(repo, findings)
    elif findings:
        sys.exit("The code scan found a problem: fix it on dev, or dismiss it on GitHub's code scanning page.")


def self_test():
    log = {"number": 2, "html_url": "https://example.com/2",
           "rule": {"description": "Log entries created from user input", "security_severity_level": "medium"}}
    path = {"number": 27, "html_url": "https://example.com/27",
            "rule": {"description": "Unsafe path", "security_severity_level": None, "severity": "warning"}}
    assert title(log) == "Code scan finding 2: Log entries created from user input"
    assert without_issue([log, path], []) == [log, path]
    assert without_issue([log, path], [title(path)]) == [log], "finding 27's issue isn't finding 2's"
    assert without_issue([log, path], [title(log), "Code scan finding 27: reworded"]) == []
    assert severity(log) == "medium" and severity(path) == "warning"
    assert "https://example.com/2" in body(log)
    print("code_scan self-test passed")


if __name__ == "__main__":
    if "--self-test" in sys.argv[1:]:
        self_test()
    else:
        main()
