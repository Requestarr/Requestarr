#!/usr/bin/env bash
# The release flow (docs/development.md#release-flow): every pull request merges to dev; qa moves only on the
# owner's word ("promote to QA"), staging only on "QA passed", main only on "ship it"; a fix found in QA or staging goes
# into that branch and is merged back down (staging into qa, qa into dev) in the same task, so the branches never drift.
# Build numbers (docs/development.md#versions): every finished goal numbers its commit on dev (number); QA and staging
# carry that number when it's promoted to them; .github/build_numbers.py is the only thing that takes one.
#
#   ./release.sh drift [<dev ref> <qa ref> [<staging ref>]]
#         fails if qa has a commit dev doesn't, or staging one qa doesn't (default: origin/dev, origin/qa, origin/staging)
#   ./release.sh number [<commit on dev>] [--major "<the owner's words>"] [--dry-run]
#         a finished goal's build number: tags the commit (default: dev's newest) v<the next number> on GitHub and
#         prints it; a commit that has one prints it again. Only dev's commits, and never one older than a numbered one.
#   ./release.sh promote-qa "<who asked, and their words>" [--commit <numbered commit on dev>] [--dry-run]
#         moves qa to dev's newest numbered commit (or that one): a pull request that records who asked, the build
#         check, then a fast-forward (nothing newer, no merge commit). QA shows its number: 0.7.0005-qa. A commit without
#         a number isn't promoted: a goal's build is numbered when the goal finishes.
#         First the automated checks must have passed on dev at that commit (docker/qa_checks.py: the full suite, every
#         live check, its checklist items); without a run it runs them, and any failure stops the promotion.
#   ./release.sh promote-staging "<who asked, and their words>" [--owner-override "<the owner's words>"] [--dry-run]
#         carries QA's build number (the tag on qa's commit; no new number): a release commit turns Unreleased into
#         "## 0.7.0011 (date), with 0.7.0005 to 0.7.0011" (the goals' builds it includes) in CHANGELOG.md and
#         "## 0.7.0011 (date)" in QA-CHECKLIST.md; qa and staging move to it (a pull request each, recording who asked),
#         and qa is merged back into dev. Staging's push publishes the image as 0.7.0011. Only when the QA gate is clear
#         (qa-gate below), unless the owner overrides it in so many words. A major is taken when the build is numbered.
#   ./release.sh ship-it "<who asked, and their words>" [--dry-run]
#         production: main moves to staging's commit, and the Image workflow adds the release tags (0.7, latest) to
#         the image staging published from it. Nothing is rebuilt; the next build sent to testers is a minor up.
#         --dry-run (on every promotion) says what would move and which number comes out of it, and changes nothing.
#   ./release.sh next-version
#         the number the next finished goal takes, and how dev and QA are labelled now
#   ./release.sh qa-gate [--json]
#         is QA's test checklist complete, with no item failing and no bug report from QA still open? Read from the QA
#         stack on this machine (docker/qa-gate.py); exit 0 when clear. A staging hold (below) makes it blocked.
#   ./release.sh hold-staging "<the owner's words>"
#         the owner holds staging: the QA gate stays blocked and promote-staging refuses without --owner-override (which
#         lifts the hold once the promotion is done). Kept in .staging-hold on this machine, not in git.
#   ./release.sh lift-staging-hold
#         lifts it again
#   ./release.sh merge-back
#         after a fix merged into staging or qa (or a promotion to staging): pull requests that merge staging into qa
#         and qa into dev, each merged once the build check passes. CHANGELOG.md and QA-CHECKLIST.md keep every
#         numbered version of both; Unreleased keeps the lines no build has yet.
#   ./release.sh check-changelog <base> <head> <target branch> [<pull request head> [<pull request branch>]]
#         the changelog rules CI's Changelog job applies to a pull request (PR_BODY: its description)
#
# Needs git and Python 3, and the GitHub CLI (gh) for promote-*, ship-it and merge-back.
set -euo pipefail

here="$(cd "$(dirname "$0")" && pwd)"
files=(CHANGELOG.md QA-CHECKLIST.md)
# The owner's staging hold: their words, who recorded them and when (hold-staging).
hold="$here/.staging-hold"

die() {
    echo "release.sh: $*" >&2
    exit 1
}

has_ref() {
    git rev-parse -q --verify "$1^{commit}" >/dev/null
}

# The first Python that really runs (a stub can exit 0 without running anything).
python() {
    local candidate
    for candidate in python3 python; do
        if [ "$(command "$candidate" -c 'print(42)' 2>/dev/null)" = 42 ]; then
            command "$candidate" "$@"
            return
        fi
    done
    die "Python 3 is needed for the changelog check"
}

numbers() {
    python "$here/.github/build_numbers.py" "$@"
}

fetch() {
    git fetch -q origin dev qa
    git fetch -q origin staging main 2>/dev/null || true
    git fetch -q --tags origin 2>/dev/null || true
}

# The build numbers among the tag names read in (v0.7.0005 gives 0.7.0005), lowest first. No match is not a failure:
# under pipefail a grep that finds nothing would otherwise end the script.
build_numbers_in() {
    { grep -E '^v[0-9]+\.[0-9]+\.[0-9]{4,}$' || true; } | sed 's/^v//' | sort -V
}

# The build number tagged on exactly this commit, or nothing.
number_of() {
    git tag --points-at "$1" | build_numbers_in | tail -1
}

# The oldest numbered goal build that the commit has and staging doesn't: where the staging heading's "with" starts.
first_new_build() {
    git tag --merged "$1" --no-merged origin/staging | build_numbers_in | head -1
}

# dev's newest goal build: numbered, on dev, and not on staging (a fix into staging comes down with its number).
newest_numbered() {
    local number
    number=$(git tag --merged origin/dev --no-merged origin/staging | build_numbers_in | tail -1)
    [ -n "$number" ] && git rev-parse "v$number^{commit}"
}

# Every numbered version in CHANGELOG.md at a commit, newest first.
all_versions() {
    git show "$1:CHANGELOG.md" 2>/dev/null | grep -E '^## [0-9]+\.[0-9]+\.[0-9]+' | sed -E 's/^## +//; s/ .*//' || true
}

# CHANGELOG.md and QA-CHECKLIST.md as they are at a commit, into a folder (an empty file where one is missing).
files_at() {
    local file
    mkdir -p "$2"
    for file in "${files[@]}"; do
        git show "$1:$file" >"$2/$file" 2>/dev/null || : >"$2/$file"
    done
}

# Fails if $2 (the branch above) has a commit $1 (the branch below) doesn't.
drift_pair() {
    local lower="$1" upper="$2" missing
    missing=$(git log --format='  %h %s' "$lower..$upper")
    if [ -n "$missing" ]; then
        echo "DRIFT: $upper has $(echo "$missing" | wc -l | tr -d ' ') commit(s) that $lower doesn't:"
        echo "$missing"
        return 1
    fi
    echo "No drift: every commit in $upper ($(git rev-parse --short "$upper")) is in $lower ($(git rev-parse --short "$lower"))."
}

drift() {
    local dev="${1:-}" qa="${2:-}" staging="${3:-}"
    if [ -z "$dev" ]; then
        fetch
        dev=origin/dev
        qa=origin/qa
        has_ref origin/staging && staging=origin/staging
    fi

    local failed=0
    drift_pair "$dev" "$qa" || failed=1
    if [ -n "$staging" ]; then
        drift_pair "$qa" "$staging" || failed=1
    fi
    if [ "$failed" = 1 ]; then
        echo "Merge the fix back down (./release.sh merge-back) so every branch below has every fix made above it."
    fi
    return "$failed"
}

# Where a branch sits in the flow; a merge-back goes from a higher one into a lower one.
rank() {
    case "$1" in
        dev) echo 1 ;;
        qa) echo 2 ;;
        staging) echo 3 ;;
        main) echo 4 ;;
        *) echo 0 ;;
    esac
}

below() {
    case "$1" in
        qa) echo dev ;;
        staging) echo qa ;;
        main) echo staging ;;
    esac
}

check_changelog() {
    local base="$1" head="$2" target="$3" pr_head="${4:-$2}" pr_branch="${5:-}" work kind="" upper="" flags=() lower
    lower=$(below "$target")
    work=$(mktemp -d)
    files_at "$head" "$work/head"

    # The words: two sections, plain English, no placeholders; the checklist's items (each checker says what it checks).
    python "$here/.github/changelog_check.py" "$work/head/CHANGELOG.md" &&
        python "$here/.github/checklist_check.py" "$work/head/QA-CHECKLIST.md" "$work/head/CHANGELOG.md" || { rm -rf "$work"; return 1; }

    if [ "$(rank "${pr_branch%%/*}")" -gt "$(rank "$target")" ] || [[ "$pr_branch" == merge-back/* ]]; then
        # A merge-back brings fixes down: nothing of either branch may be lost.
        kind=merge-back
        upper="$pr_head"
        git rev-parse -q --verify "$pr_head^2" >/dev/null && upper="$pr_head^2"
        files_at "$upper" "$work/upper"
        files_at "$base" "$work/base"
    elif [[ "$pr_branch" == release/* ]]; then
        # The staging promotion's release commit, on top of qa: Unreleased becomes the next number.
        kind=release
        has_ref origin/qa && git merge-base --is-ancestor "$pr_head^" origin/qa \
            || { echo "::error::A release commit sits on top of qa as it is (./release.sh promote-staging makes it)."; rm -rf "$work"; return 1; }
        files_at "$pr_head^" "$work/base"
    elif [ -n "$lower" ] && has_ref "origin/$lower" && git merge-base --is-ancestor "$pr_head" "origin/$lower"; then
        echo "A promotion of commits already on $lower: their changelog was checked there."
        rm -rf "$work"
        return 0
    else
        case "$target" in
            dev | qa) kind=change ;;
            staging) kind=staging-fix ;;
            main) echo "::error::main moves only by a promotion of staging's build (./release.sh ship-it)."; rm -rf "$work"; return 1 ;;
            *) rm -rf "$work"; return 0 ;;
        esac
        files_at "$base" "$work/base"
    fi

    # A pull request that changes nothing users or admins see says so in its description (a dependency update is one).
    if [[ "$pr_branch" == dependabot/* ]] || printf '%s\n' "${PR_BODY:-}" | grep -qE '^No changelog: .{5,}'; then
        flags+=(--no-changelog)
    fi
    # Numbered versions never change, unless the owner approved rewriting one changelog section, recorded in the
    # description with his words: 'Rewrite of a numbered version: 0.7.0010, approved by the owner on 2026-11-23: "..."'.
    if [ "$kind" = change ]; then
        local rewrite
        while read -r rewrite; do
            [ -n "$rewrite" ] && flags+=(--rewrite "$rewrite")
        done < <(printf '%s\n' "${PR_BODY:-}" | sed -nE 's/^Rewrite of a numbered version: ([0-9]+\.[0-9]+\.[0-9]{4,}), approved by the owner on [0-9]{4}-[0-9]{2}-[0-9]{2}: ".{5,}"\r?$/\1/p')
    fi
    # A release carries the number of the build QA ran (the tag on the commit it sits on). A fix into staging takes the
    # next number as it was before this pull request (its branches may already have moved to it); a major when the
    # owner's word is recorded, or from 1.0 a "Before you upgrade" line asks for one.
    if [ "$kind" = release ]; then
        local carried
        carried=$(number_of "$pr_head^")
        [ -n "$carried" ] || { echo "::error::A release commit sits on a numbered build (./release.sh number, then promote-qa)."; rm -rf "$work"; return 1; }
        flags+=(--number "$carried")
    elif [ "$kind" = staging-fix ]; then
        local major=()
        printf '%s\n' "${PR_BODY:-}" | grep -qE '^Major roll-up: .+' && major=(--major)
        flags+=(--number "$(numbers next --before "$pr_head" --changelog "$work/base/CHANGELOG.md" "${major[@]}")")
    fi
    local status=0
    numbers check "$kind" "$work/base" "$work/head" ${upper:+"$work/upper"} "${flags[@]}" || status=1
    rm -rf "$work"
    [ "$status" = 0 ] || { echo "::error::See docs/development.md#changelog and #versions."; return 1; }
    echo "CHANGELOG.md and QA-CHECKLIST.md follow the rules for a $kind into $target."
}

# Waits for a pull request's checks, then requires the build check (what the branch rules require) to have passed.
# gh's --required doesn't see checks that repository rulesets require, so build is looked up by name.
wait_for_build() {
    sleep 15
    gh pr checks "$1" --watch --interval 30 >/dev/null 2>&1 || true
    local state
    state=$(gh pr checks "$1" --json name,state --jq '[.[] | select(.name == "build")][0].state')
    echo "build: ${state:-not reported}"
    [ "$state" = "SUCCESS" ]
}

# can_move <branch> <commit>: the branch can fast-forward to the commit.
can_move() {
    [ "$(git rev-parse "origin/$1")" != "$2" ] || die "$1 is already at $(git rev-parse --short "$2")"
    git merge-base --is-ancestor "origin/$1" "$2" \
        || die "$1 has commits that $(git rev-parse --short "$2") doesn't (drift: ./release.sh drift), so $1 can't move to it"
}

# open_pull_request <branch> <base> <title> <body>: prints its address.
# The body goes in a file: a promotion's lists every commit moving, too long for a command line on Windows.
open_pull_request() {
    local body
    body=$(mktemp)
    printf '%s' "$4" > "$body"
    gh pr create --base "$2" --head "$1" --title "$3" --body-file "$body"
    rm -f "$body"
}

# fast_forward <pull request> <commit> <branch>: once build passes, the branch moves to exactly that commit (GitHub
# marks the pull request merged).
fast_forward() {
    echo "Waiting for the build check on $1…"
    wait_for_build "$1" || die "the build check didn't pass; $3 is unchanged"
    git push origin "$2:refs/heads/$3"
    sleep 5
    echo "Pull request: $1 ($(gh pr view "$1" --json state --jq .state))"
}

# The options every promotion takes: sets dry, override, major and commit.
read_options() {
    dry=0 override="" major=""
    while [ $# -gt 0 ]; do
        case "$1" in
            --dry-run) dry=1 ;;
            --owner-override) override="${2:-}"; [ -n "$override" ] || die "--owner-override needs the owner's words"; shift ;;
            --major) major="${2:-}"; [ -n "$major" ] || die "--major needs the owner's words"; shift ;;
            --commit) commit="${2:-}"; [ -n "$commit" ] || die "--commit needs a commit"; shift ;;
            *) die "unknown option $1" ;;
        esac
        shift
    done
}

promote_qa() {
    local who="${1:-}" dry override major commit="" target short branch commits label
    [ -n "$who" ] || die 'promote-qa "<who asked, and their words>" [--commit <numbered commit on dev>] [--dry-run]'
    shift
    read_options "$@"
    fetch
    if [ -n "$commit" ]; then
        target=$(git rev-parse -q --verify "$commit^{commit}") || die "no commit '$commit'"
    else
        target=$(newest_numbered) || die "dev has no numbered build yet: a finished goal numbers its commit (./release.sh number)"
    fi
    git merge-base --is-ancestor "$target" origin/dev || die "$commit isn't on dev: only dev's commits are promoted to QA"
    [ -n "$(number_of "$target")" ] \
        || die "dev @ $(git rev-parse --short "$target") has no build number, so it isn't promoted: QA gets a finished goal's numbered build (./release.sh number $(git rev-parse --short "$target") once its goal is done)"
    can_move qa "$target"
    short=$(git rev-parse --short "$target")
    label=$(numbers label qa "$target")
    commits=$(git log --format='- %h %s' "origin/qa..$target")

    echo "Promoting dev @ $short to QA; QA shows it as $label. Commits moving to qa:"
    echo "$commits"
    # The owner's process: the automated checks first, on dev at this commit; QA gets only a build that passed them all.
    checks_passed "$target" || return 1
    if [ "$dry" = 1 ]; then
        echo "Dry run: nothing pushed, no pull request opened; qa stays at $(git rev-parse --short origin/qa)."
        return 0
    fi
    branch="promote/qa-$short"
    git push -q origin "$target:refs/heads/$branch"
    local url evidence
    evidence=$(python docker/qa_checks.py summary "$target")
    url=$(open_pull_request "$branch" qa "Promote dev @ $short to QA" "$(printf 'Moves `qa` to `dev` @ `%s`: exactly these commits, nothing newer. Merged as a fast-forward of `qa` once the build check passes. QA shows it as %s.\n\nRequested by: %s\n\n### The automated checks on dev @ %s (all passed)\n%s\n\n### Commits\n%s\n' "$short" "$label" "$who" "$short" "$evidence" "$commits")")
    echo "$url"
    fast_forward "$url" "$target" qa
    git push -q origin --delete "$branch" || true
    # up keeps QA's data: its checklist results carry over (a reworded item starts untested) and its bug reports stay.
    echo "QA is now at $short. Next: .\\docker\\dev.ps1 up (or docker/dev.sh up) so the QA stack runs qa: its checklist lists only the owner's items."
}

# Whether the automated checks passed on dev at this commit (docker/qa_checks.py verify): the full suite, every live check
# and its checklist items, from one full run. Without a run, a promotion runs them first (the checkout at the commit); a
# dry run says so. Any failure stops the promotion until it's fixed on dev.
checks_passed() {
    local target="$1" short status=0
    short=$(git rev-parse --short "$target")
    python docker/qa_checks.py verify "$target" || status=$?
    [ "$status" = 3 ] || return "$status"
    if [ "$dry" = 1 ]; then
        echo "Dry run: no run of the automated checks on $short yet; the promotion runs them first (python3 docker/qa_checks.py run --commit $short) and goes on only if every check passed."
        return 0
    fi
    echo "Running the automated checks on $short first (about 35 minutes)…"
    python docker/qa_checks.py run --commit "$target"
    python docker/qa_checks.py verify "$target"
}

# A security fix in something Requestarr ships, merged since staging's last build: the build's For admins says so.
security_fix_since() {
    local target="$1" oid
    for oid in $(gh pr list --state merged --label security --label "for admins" --limit 100 --json mergeCommit --jq '.[].mergeCommit.oid' 2>/dev/null); do
        if has_ref "$oid" && git merge-base --is-ancestor "$oid" "$target" && ! git merge-base --is-ancestor "$oid" origin/staging; then
            echo --security
            return
        fi
    done
}

# The QA gate's verdict for the promotion record, or a stop: only with QA's test checklist complete and no bug report
# from QA open, or on the owner's explicit word.
qa_gate_record() {
    local verdict
    if [ -n "$override" ]; then
        echo "Owner override: $override (the QA gate was not checked or not clear)"
        [ ! -f "$hold" ] || echo "Staging was held: $(cat "$hold")"
    elif [ -f "$hold" ] && [ "$dry" != 1 ]; then
        die "staging is held by the owner: $(cat "$hold"). Only the owner's word moves it: promote-staging \"<who>\" --owner-override \"<the owner's words>\""
    elif verdict=$("$here/docker/dev.sh" qa-gate --json); then
        printf '%s' "$verdict" | python -c 'import json,sys; v=json.load(sys.stdin); print("QA checklist: complete for %s (%d items), the last item passed by %s at %s; no bug report from QA open." % (", ".join(v["versions"]), v["total"], v["completedBy"], v["completedAt"]))'
    elif [ "$dry" = 1 ]; then
        echo "QA gate: blocked (a real promotion stops here unless the owner overrides it)."
    else
        "$here/docker/dev.sh" qa-gate >&2 || true
        die "promotion to staging is blocked: QA's test checklist isn't complete, an item is failing, or a bug report from QA is still open. Only the owner can override it: promote-staging \"<who>\" --owner-override \"<the owner's words>\""
    fi
    if [ -z "$override" ] && [ -f "$hold" ]; then
        echo "Staging hold: $(cat "$hold") (a real promotion stops here unless the owner overrides it)."
    fi
}

promote_staging() {
    local who="${1:-}" dry override major commit="" target short number first gate security work day versions branch
    [ -n "$who" ] || die 'promote-staging "<who asked, and their words>" [--owner-override "<words>"] [--dry-run]'
    shift
    read_options "$@"
    [ -z "$major" ] || die "a major is taken when the build is numbered: ./release.sh number <commit> --major \"<the owner's words>\""
    fetch
    target=$(git rev-parse origin/qa)
    short=$(git rev-parse --short "$target")
    number=$(number_of "$target")
    [ -n "$number" ] \
        || die "qa @ $short has no build number, so there's no number to carry: QA runs a goal's numbered build (promote-qa), and a fix into QA is numbered on dev after its merge-back (./release.sh number $short)"
    first=$(first_new_build "$target")
    can_move staging "$target"
    gate=$(qa_gate_record)

    work=$(mktemp -d)
    files_at "$target" "$work/release"
    security=$(security_fix_since "$target")
    day=$(date -u +%Y-%m-%d)
    numbers release "$work/release" "$number" "$day" $security ${first:+--from "$first"}
    versions=$(comm -23 <(grep -E '^## [0-9]' "$work/release/CHANGELOG.md" | sed -E 's/^## +//; s/ .*//' | sort -u) \
        <(all_versions origin/staging | sort -u) | sort -rV | sed 's/^/- /')

    echo "Promoting QA (qa @ $short) to staging as build $number, the number QA ran, with the goals' builds ${first:-$number} to $number. Its changelog section:"
    sed -n "/^## $number /,/^## [0-9]/p" "$work/release/CHANGELOG.md" | sed '$d' | sed 's/^/    /'
    echo "Versions staging gets:"
    echo "$versions"
    echo "Published as ghcr.io/requestarr/requestarr:beta and :$number (v$number is already on the goal's dev commit). qa is merged back into dev."
    [ -z "$security" ] || echo "A security fix in something Requestarr ships is in this build: its For admins section says so."
    echo "$gate"
    if [ "$dry" = 1 ]; then
        rm -rf "$work"
        echo "Dry run: nothing pushed, no pull request opened; staging stays at $(git rev-parse --short origin/staging)."
        return 0
    fi

    # The release commit: qa as it is, with Unreleased turned into the number.
    branch="release/$number"
    git worktree add -q -b "$branch" "$work/tree" "$target"
    cp "$work/release/CHANGELOG.md" "$work/release/QA-CHECKLIST.md" "$work/tree/"
    git -C "$work/tree" add "${files[@]}"
    git -C "$work/tree" commit -q -m "Build $number: Unreleased becomes $number ($day) in the changelog and QA checklist"
    local release
    release=$(git -C "$work/tree" rev-parse HEAD)
    git -C "$work/tree" push -q origin "$branch"
    git worktree remove --force "$work/tree"

    local body into_qa into_staging
    body=$(printf 'Build %s, the number QA ran: QA (`qa` @ `%s`) with Unreleased turned into `## %s (%s)` in CHANGELOG.md and QA-CHECKLIST.md, with the goals'"'"' builds %s to %s. Merged as a fast-forward of `qa` and then `staging` once the build check passes; staging publishes it as %s.\n\nRequested by: %s\n\n%s\n\n### Versions staging gets\n%s\n' \
        "$number" "$short" "$number" "$day" "${first:-$number}" "$number" "$number" "$who" "$gate" "$versions")
    into_qa=$(open_pull_request "$branch" qa "Build $number: release commit on QA" "$body")
    into_staging=$(open_pull_request "$branch" staging "Promote build $number to staging" "$body")
    echo "$into_qa"
    echo "$into_staging"
    fast_forward "$into_qa" "$release" qa
    fast_forward "$into_staging" "$release" staging
    rm -f "$hold"
    git push -q origin --delete "$branch" || true
    rm -rf "$work"
    fetch
    merge_into qa dev
    echo "Staging is now build $number ($(git rev-parse --short origin/staging)). Next: the Image workflow publishes"
    echo "      ghcr.io/requestarr/requestarr:beta and :$number (gh run list --workflow image.yml);"
    echo "      .\\docker\\dev.ps1 up (or docker/dev.sh up) so QA drops what staging now has;"
    echo "      on the server: Container Manager, the requestarr-beta project, Action then Build (docs/setup-guide.md, Running the beta)."
}

ship_it() {
    local who="${1:-}" dry override major commit="" target short number tagged branch
    [ -n "$who" ] || die 'ship-it "<who asked, and their words>" [--dry-run]'
    shift
    read_options "$@"
    fetch
    target=$(git rev-parse origin/staging)
    short=$(git rev-parse --short "$target")
    number=$(all_versions "$target" | head -1)
    tagged=$(git rev-parse -q --verify "refs/tags/v$number^{commit}" || true)
    [ -n "$number" ] && [ -n "$tagged" ] && git merge-base --is-ancestor "$tagged" "$target" \
        || die "staging @ $short isn't a published build (no tag v$number on it or before it): only a build staging published ships"
    can_move main "$target"

    echo "Shipping build $number: main moves to staging @ $short."
    echo "The Image workflow adds ghcr.io/requestarr/requestarr:${number%.*} and :latest to :$number, the image staging"
    echo "published from this same commit. Nothing is rebuilt, so production runs the file the testers ran."
    echo "The next build sent to testers is a minor up: $(numbers next --shipped "$number")."
    if [ "$dry" = 1 ]; then
        echo "Dry run: nothing pushed, no pull request opened; main stays at $(git rev-parse --short origin/main)."
        return 0
    fi
    branch="promote/main-$number"
    git push -q origin "$target:refs/heads/$branch"
    local url
    url=$(open_pull_request "$branch" main "Ship build $number" "$(printf 'Moves `main` to `staging` @ `%s` (build %s), exactly, as a fast-forward once the build check passes. The Image workflow then tags the image staging published from this commit as %s and latest; nothing is rebuilt.\n\nRequested by: %s\n' "$short" "$number" "${number%.*}" "$who")")
    echo "$url"
    fast_forward "$url" "$target" main
    git push -q origin --delete "$branch" || true
    echo "Production is build $number. The Image workflow retags the image (gh run list --workflow image.yml)."
}

# resolve_merge_back <from> <to>: the merge of <from> into <to> on a branch merge-back/<from>-<commit> (made in a
# separate worktree, so the checkout here is untouched). CHANGELOG.md and QA-CHECKLIST.md are always rebuilt from both
# sides, never left to git: a plain merge could put lines still unreleased below into the build above. Prints the
# branch; fails, saying what to do, if anything else conflicts.
resolve_merge_back() {
    local from="$1" to="$2" branch work others
    branch="merge-back/$from-$(git rev-parse --short "origin/$from")"
    work=$(mktemp -d)
    git worktree add -q -b "$branch" "$work/tree" "origin/$to" >&2
    git -C "$work/tree" merge -q --no-commit --no-ff "origin/$from" >/dev/null 2>&1 || true
    others=$(git -C "$work/tree" diff --name-only --diff-filter=U | grep -vxE 'CHANGELOG.md|QA-CHECKLIST.md' || true)
    if [ -n "$others" ]; then
        git -C "$work/tree" merge --abort
        git worktree remove --force "$work/tree"
        git branch -q -D "$branch"
        cat >&2 <<EOF
$from and $to conflict in: $others. Resolve on a branch:
  git switch -c $branch origin/$to && git merge origin/$from
  CHANGELOG.md and QA-CHECKLIST.md: python3 .github/build_numbers.py merge <$to's files> <$from's files> <out> <their merge base's files>
  git commit, push, then: gh pr create --base $to --title "Merge $from back into $to"
EOF
        return 1
    fi
    files_at "origin/$to" "$work/ours"
    files_at "origin/$from" "$work/theirs"
    # Their common base: a line one side rewrote or removed since it is gone from the merge, not kept beside the new one.
    files_at "$(git merge-base "origin/$to" "origin/$from")" "$work/base"
    numbers merge "$work/ours" "$work/theirs" "$work/tree" "$work/base" >&2
    git -C "$work/tree" add "${files[@]}"
    git -C "$work/tree" commit -q -m "Merge $from back into $to" >&2
    git -C "$work/tree" push -q origin "$branch" >&2
    git worktree remove --force "$work/tree"
    rm -rf "$work"
    echo "$branch"
}

# merge_into <from> <to>: a pull request that merges branch <from> into <to>, merged once the build check passes.
merge_into() {
    local from="$1" to="$2" head url
    head=$(resolve_merge_back "$from" "$to") || return 1
    url=$(gh pr create --base "$to" --head "$head" --title "Merge $from back into $to" \
        --body "Merges \`$from\` into \`$to\`, so $to has every fix made in $from and the drift check passes. CHANGELOG.md and QA-CHECKLIST.md keep every numbered version of both; Unreleased keeps only the lines no build has yet.")
    echo "$url"
    sleep 10

    echo "Waiting for the build check…"
    wait_for_build "$url" || die "the build check didn't pass; nothing merged"
    # A merge commit (never squash): the branch below then contains the fix's own commits, which drift looks for.
    gh pr merge "$url" --merge --delete-branch
    git fetch -q origin "$to"
}

merge_back() {
    fetch
    local merged=0

    if has_ref origin/staging && ! git merge-base --is-ancestor origin/staging origin/qa; then
        merge_into staging qa || return 1
        merged=1
    fi
    if ! git merge-base --is-ancestor origin/qa origin/dev; then
        merge_into qa dev || return 1
        merged=1
    fi
    [ "$merged" = 1 ] || echo "Every fix is already on the branches below: nothing to merge back."
    drift
}

# The gate as the stack reads it, then the owner's hold, which blocks it whatever the checklist says.
qa_gate() {
    local status=0
    "$here/docker/dev.sh" qa-gate "$@" || status=$?
    if [ -f "$hold" ]; then
        # With --json the verdict is the only thing on standard output.
        echo "HELD: the owner holds staging: $(cat "$hold"). Don't promote until the owner says so (\"QA passed\", or lifts the hold)." >&2
        [ "$status" = 2 ] || status=1
    fi
    return "$status"
}

hold_staging() {
    [ -n "${1:-}" ] || die "hold-staging \"<the owner's words>\""
    printf '"%s" (recorded %s by %s)\n' "$1" "$(date -u +%Y-%m-%dT%H:%MZ)" "$(git config user.name)" > "$hold"
    echo "Staging held: $(cat "$hold")"
}

lift_staging_hold() {
    [ -f "$hold" ] || { echo "Staging isn't held."; return 0; }
    echo "Lifted the staging hold: $(cat "$hold")"
    rm -f "$hold"
}

next_version() {
    fetch
    local number
    number=$(numbers next)
    echo "The next finished goal (or a fix into staging) takes $number."
    echo "Dev shows $(numbers label dev "$(git rev-parse origin/dev)") and QA $(numbers label qa "$(git rev-parse origin/qa)")."
}

# A finished goal's number for its dev commit (default dev's newest): the tag on GitHub, taken once.
number_build() {
    local dry override major commit="" target
    if [ $# -gt 0 ] && [ "${1#--}" = "$1" ]; then
        commit="$1"
        shift
    fi
    read_options "$@"
    fetch
    target=$(git rev-parse -q --verify "${commit:-origin/dev}^{commit}") || die "no commit '$commit'"
    [ -z "$major" ] || echo "Major roll-up: $major" >&2
    numbers number "$target" ${major:+--major} $([ "$dry" = 1 ] && echo --dry-run)
}

case "${1:-}" in
    drift) shift; drift "$@" ;;
    check-changelog) shift; check_changelog "$@" ;;
    promote-qa) shift; promote_qa "$@" ;;
    promote-staging) shift; promote_staging "$@" ;;
    ship-it) shift; ship_it "$@" ;;
    next-version) next_version ;;
    number) shift; number_build "$@" ;;
    qa-gate) shift; qa_gate "$@" ;;
    hold-staging) shift; hold_staging "$@" ;;
    lift-staging-hold) lift_staging_hold ;;
    merge-back) merge_back ;;
    *)
        sed -n '2,44p' "$0"
        exit 1
        ;;
esac
